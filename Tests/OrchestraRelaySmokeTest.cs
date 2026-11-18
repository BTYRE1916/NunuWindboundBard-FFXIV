using NunuWindboundBard.Core;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

if (args.Length != 2) throw new ArgumentException("Supply the web socket relay address and a test MIDI path.");
string relay = args[0];
string midiPath = args[1];
// Exercise the production certificate factory before involving the relay.
using (var certificate = RelayBandConnection.CreateHostCertificate())
using (var signer = certificate.GetECDsaPrivateKey())
using (var verifier = certificate.GetECDsaPublicKey())
{
    Require(signer is not null && verifier is not null, "host certificate has a signing key");
    byte[] challenge = RandomNumberGenerator.GetBytes(32);
    byte[] signature = signer!.SignData(challenge, HashAlgorithmName.SHA256);
    Require(verifier!.VerifyData(challenge, signature, HashAlgorithmName.SHA256), "certificate signs after import");

    if (OperatingSystem.IsWindows())
    {
        Require(signer is ECDsaCng, "Windows host certificate uses CNG");
        using var key = ((ECDsaCng)signer).Key;
        Require(!key.IsEphemeral && !key.IsMachineKey && !string.IsNullOrEmpty(key.KeyName),
            "Schannel can use a named current-user key");
        using var reopened = CngKey.Open(key.KeyName!, key.Provider!);
        using var anotherSigner = new ECDsaCng(reopened);
        Require(verifier.VerifyData(challenge, anotherSigner.SignData(challenge, HashAlgorithmName.SHA256),
            HashAlgorithmName.SHA256), "the Windows key can be reopened for signing");
    }
}

using var host = new MultiDeviceEnsemble();
host.HostBand(relay, "Orchestra smoke");
await Until(() => host.IsHosting && host.RelayJoinCode.Length > 0, "host could not open room");
string joinCode = host.RelayJoinCode;
var invite = RelayBandInvite.Decode(joinCode);
Require(invite.Stage == "Orchestra smoke", "invite retains the band name");

await RejectJoin(invite with { Certificate = new string('0', 64) }, "wrong conductor certificate", "Conductor certificate did not match");
await RejectJoin(invite with { Secret = new string('0', 24) }, "wrong band credential", "Failed at Checking conductor band credentials");
await RejectJoin(invite with { Room = new string('0', 48) }, "wrong room", "HTTP 404");

var followers = Enumerable.Range(0, 8).Select(_ => new MultiDeviceEnsemble()).ToArray();
try
{
    for (int i = 0; i < followers.Length; i++)
        followers[i].JoinBand(joinCode, $"Bard {i + 2}", (ulong)(i + 2));
    await Until(() => host.Performers.Count == 8 && followers.All(bard => bard.HasClockSync),
        "all eight followers must pair and synchronize clocks");
    var assignments = Enumerable.Range(0, 9).ToDictionary(i => $"{i}:0", i => (ulong)(i + 1));
    var arrangement = new RemoteArrangementSettings(false, 0, 0, 0, 0, new());
    Require(host.PublishSession(midiPath, arrangement, assignments), "new MIDI session published");

    for (int i = 0; i < followers.Length; i++)
    {
        var bard = followers[i];
        var session = await WaitNotice(bard, NetworkNoticeKind.Session);
        Require(session.Assignments?.GetValueOrDefault($"{i + 1}:0") == (ulong)(i + 2),
            $"bard {i + 2} receives assigned part");
        Require(session.Midi?.AsSpan().SequenceEqual(File.ReadAllBytes(midiPath)) == true, "MIDI bytes match");
        bard.SendReady(session.Revision, session.Hash, true, "Ready");
    }
    await Until(() => host.CanCue(out _), "host receives ready status for all eight followers");
    Require(host.Performers.All(p => p.SupportsBandSetup), "all updated followers advertise band setup");
    var instruments = Enumerable.Range(0, followers.Length).ToDictionary(i => (ulong)(i + 2), i => i + 1);
    long revisionBeforeRetry = host.SessionRevision;
    Require(host.RetryBandMember(2), "targeted retry accepted");
    var retry = await WaitNotice(followers[0], NetworkNoticeKind.Session);
    Require(retry.SetupSequence > 0 && retry.Revision == revisionBeforeRetry &&
        retry.Midi!.SequenceEqual(File.ReadAllBytes(midiPath)), "targeted retry preserves the MIDI and revision");
    Require(host.Performers.Count(p => p.Ready) == 7, "only the retried bard loses readiness");
    followers[0].SendReady(retry.Revision, retry.Hash, true, "Ready", retry.SetupSequence);
    await Until(() => host.CanCue(out _), "retry acknowledged");
    foreach (var action in new[] { BandSetupAction.Prepare, BandSetupAction.Disarm, BandSetupAction.SetInstruments,
                                   BandSetupAction.Unequip, BandSetupAction.Arm })
    {
        Require(host.SendBandSetup(action, instruments) == followers.Length, "setup reaches all eight followers");
        Require(!host.CanCue(out _), "previous readiness invalid after setup");
        for (int i = 0; i < followers.Length; i++)
        {
            var bard = followers[i];
            var setup = await WaitNotice(bard, NetworkNoticeKind.BandSetup);
            Require(setup.Setup == action && setup.Instrument == i + 1 && setup.Revision == host.SessionRevision &&
                setup.Hash == host.SharedSongHash && setup.ConnectionId == bard.RelayFollowerConnectionId,
                "command targeted to this bard, connection and song");
            bard.SendReady(setup.Revision, setup.Hash, true, "stale", setup.SetupSequence - 1);
            // Keep one stale ACK outstanding while the other followers report ready.
            if (i != followers.Length - 1) bard.SendReady(setup.Revision, setup.Hash, true, "Ready", setup.SetupSequence);
            else
            {
                await Until(() => host.Performers.Count(p => p.Ready) == followers.Length - 1, "seven current ACKs");
                Require(!host.CanCue(out _), "old ACK cannot release the last follower");
                bard.SendReady(setup.Revision, setup.Hash, true, "Ready", setup.SetupSequence);
            }
        }
        await Until(() => host.CanCue(out _), "post-setup readiness");
    }
    Require(host.PublishSession(midiPath, arrangement, assignments, resend: true) && host.SessionRevision > revisionBeforeRetry,
        "manual MIDI and track sync sends again even when the file is unchanged");
    foreach (var bard in followers)
    {
        var fresh = await WaitNotice(bard, NetworkNoticeKind.Session);
        Require(fresh.SetupSequence == 0, "new revision clears setup acknowledgements");
        bard.SendReady(fresh.Revision, fresh.Hash, true, "Ready");
    }
    await Until(() => host.CanCue(out _), "all eight acknowledge fresh sync");
    Console.WriteLine("PASS: hosted prepare, instruments, ARM/DISARM, unequip, targeted retry, resend and stale readiness.");
    double start = MultiDeviceEnsemble.NowMs() + 5000;
    host.BroadcastStart(start, 1000);
    long startSequence = 0;
    NetworkNotice? unacknowledged = null;
    for (int i = 0; i < followers.Length; i++)
    {
        var bard = followers[i];
        var cue = await WaitNotice(bard, NetworkNoticeKind.Cue);
        if (startSequence == 0) startSequence = cue.StartSequence;
        Require(cue.StartSequence == startSequence && cue.StartSequence > 0 &&
            cue.ConnectionId == bard.RelayFollowerConnectionId &&
            cue.HostStartMs == start && bard.TryConvertHostTimeToLocalQpc(cue.HostStartMs, out _),
            "each follower receives the same correlated downbeat on its own connection");
        // Simulate one lost acknowledgement and one follower refusing to arm.
        if (i == 4) unacknowledged = cue;
        else bard.SendStartAck(cue.StartSequence, cue.Revision, cue.Hash, i != 6,
            i == 6 ? "Performance keyboard closed" : "Playback scheduled");
    }
    await Until(() => host.Performers.Count(p => p.StartOutcome.StartsWith("ARMED:")) == 6 &&
        host.Performers.Count(p => p.StartOutcome.StartsWith("FAILED:")) == 1,
        "conductor receives seven per-Bard START results");
    Require(host.Performers.Single(p => p.ContentId == 8).StartOutcome.Contains("Performance keyboard closed"),
        "per-follower rejection is visible to conductor");
    var repeated = await WaitNotice(followers[4], NetworkNoticeKind.Cue);
    Require(repeated.StartSequence == startSequence && repeated.HostStartMs == start &&
        repeated.Revision == unacknowledged!.Revision, "unconfirmed follower gets same START again, not a new downbeat");
    followers[4].SendStartAck(repeated.StartSequence, repeated.Revision, repeated.Hash, true, "Playback scheduled");
    await Until(() => host.Performers.Count(p => p.StartOutcome.StartsWith("ARMED:")) == 7,
        "lost START result is recovered");
    Require(host.RelayReport.Contains($"START #{startSequence} retransmitted"),
        "relay diagnostics include the targeted START retry");
    Console.WriteLine("PASS: eight-follower broadcast, per-Bard acceptance/failure, missing-ACK retry and shared downbeat.");
    host.BroadcastStop();
    foreach (var bard in followers) await WaitNotice(bard, NetworkNoticeKind.Stop);
    foreach (var bard in followers)
    {
        var reference = Regex.Match(bard.RelayReport, @"relay ref ([A-F0-9]{12})");
        Require(reference.Success && host.RelayReport.Contains(reference.Value), "both clients record the same relay reference");
        CheckPrivate(bard.RelayReport, invite);
    }
    followers[0].Stop();
    await Until(() => host.Performers.Count == 7 && host.RelayReport.Contains("Failed at Connected band traffic"),
        "conductor retains a live follower drop");
    Require(followers[0].RelayReport.Contains("Connection stopped on this client."), "local leave recorded");
    Require(followers.Skip(1).All(bard => bard.IsConnected), "other followers remain connected");
    host.Stop();
    await Until(() => followers.All(bard => !bard.IsConnected), "closing room disconnects every follower");
    Require(followers.Skip(1).All(bard => bard.RelayReport.Contains("Failed at Connected band traffic")),
        "established disconnects retain the failing phase after cleanup");
    CheckPrivate(host.RelayReport, invite);
    try
    {
        await RelayBandConnection.JoinBandAsync(invite, new CancellationTokenSource(TimeSpan.FromSeconds(4)).Token);
        throw new InvalidOperationException("Closed code unexpectedly opened a relay connection.");
    }
    catch (System.Net.WebSockets.WebSocketException) { }
    Console.WriteLine("PASS: host and eight followers, MIDI and assignments, ready/start/stop, invite and room closure.");
}
finally
{
    foreach (var bard in followers) bard.Dispose();
}

// The existing direct connection also uses the shared stream protocol.
using var directHost = new MultiDeviceEnsemble();
using var directFollower = new MultiDeviceEnsemble();
int port;
using (var listener = new TcpListener(IPAddress.Loopback, 0))
{
    listener.Start();
    port = ((IPEndPoint)listener.LocalEndpoint).Port;
}
string directCode = MultiDeviceEnsemble.NewSessionCode();
directHost.Host(port, "Legacy direct", directCode);
directFollower.Join("127.0.0.1", port, "Legacy direct", directCode, "Direct Bard", 2);
await Until(() => directFollower.HasClockSync && directHost.Performers.Count == 1, "direct transport still works");
Console.WriteLine("PASS: existing direct private-network connection and clock sync.");

// An accepted TCP connection that never upgrades must be recorded as a setup timeout.
using (var stalled = new TcpListener(IPAddress.Loopback, 0))
using (var timedOut = new MultiDeviceEnsemble())
{
    stalled.Start();
    int stalledPort = ((IPEndPoint)stalled.LocalEndpoint).Port;
    timedOut.HostBand($"ws://127.0.0.1:{stalledPort}", "Timeout test");
    await Until(() => !timedOut.IsJoining, "host timeout did not finish");
    Require(timedOut.RelayReport.Contains("Failed at Opening room /v1/host") &&
        timedOut.RelayReport.Contains("connection deadline elapsed"), "timeout phase and cause retained");
    Require(!timedOut.RelayReport.Contains("Connection stopped on this client"), "timeout misreported as local stop");
}

// Open a real room whose conductor never answers the relay's attachment request.
using (var lifetime = new CancellationTokenSource(TimeSpan.FromSeconds(20)))
using (var silentHost = await RelayBandConnection.OpenAsync(new Uri(relay), "/v1/host", null, lifetime.Token))
{
    string silentRoom = await RelayBandConnection.ReadTokenAsync(silentHost, lifetime.Token);
    await RejectJoin(invite with { Room = silentRoom }, "attachment deadline", "conductor did not attach within 10 seconds");
}

var journal = new RelayDiagnostics();
var oldest = journal.Begin("Oldest");
oldest.Step("initial record");
for (int i = 0; i < 150; i++) journal.Begin("Recent").Step("record");
Require(!journal.Report.Contains("initial record") && journal.Report.Split('\n').Length == 121, "bounded report");
Console.WriteLine("PASS: HTTP failure, TLS pin mismatch, credential rejection, deadline, connected drop, local stop, correlation and report privacy.");

static void CheckPrivate(string report, RelayBandInvite invite)
{
    foreach (string field in new[] { invite.Encode(), invite.Room, invite.Secret, invite.Certificate, invite.Stage })
        Require(!report.Contains(field, StringComparison.Ordinal), "report leaked invitation data");
}

static async Task RejectJoin(RelayBandInvite invite, string label, string expected)
{
    using var bard = new MultiDeviceEnsemble();
    bard.JoinBand(invite.Encode(), "Bad invite", 999);
    await Until(() => !bard.IsJoining, label + " did not finish");
    Require(!bard.IsConnected, label + " must be rejected");
    Require(bard.RelayReport.Contains(expected), label + " should report " + expected + "\n" + bard.RelayReport);
    CheckPrivate(bard.RelayReport, invite);
}

static async Task<NetworkNotice> WaitNotice(MultiDeviceEnsemble device, NetworkNoticeKind kind)
{
    NetworkNotice? match = null;
    await Until(() =>
    {
        while (device.TryDequeue(out var notice))
            if (notice.Kind == kind) { match = notice; return true; }
        return false;
    }, $"expected {kind} notice");
    return match!;
}

static async Task Until(Func<bool> condition, string message)
{
    for (int i = 0; i < 300; i++)
    {
        if (condition()) return;
        await Task.Delay(50);
    }
    throw new InvalidOperationException(message);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}
