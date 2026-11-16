using NunuWindboundBard.Core;
using System.Net;
using System.Net.Sockets;

if (args.Length != 2) throw new ArgumentException("Supply the web socket relay address and a test MIDI path.");
string relay = args[0];
string midiPath = args[1];
using var host = new MultiDeviceEnsemble();
host.HostBand(relay, "Orchestra smoke");
await Until(() => host.IsHosting && host.RelayJoinCode.Length > 0, "host could not open room");
string joinCode = host.RelayJoinCode;
var invite = RelayBandInvite.Decode(joinCode);
Require(invite.Stage == "Orchestra smoke", "invite retains the band name");

await RejectJoin(invite with { Certificate = new string('0', 64) }, "wrong conductor certificate");
await RejectJoin(invite with { Secret = new string('0', 24) }, "wrong band credential");
await RejectJoin(invite with { Room = new string('0', 48) }, "wrong room");

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
    double start = MultiDeviceEnsemble.NowMs() + 5000;
    host.BroadcastStart(start, 1000);
    foreach (var bard in followers)
    {
        var cue = await WaitNotice(bard, NetworkNoticeKind.Cue);
        Require(cue.HostStartMs == start && bard.TryConvertHostTimeToLocalQpc(cue.HostStartMs, out _),
            "each follower receives and converts the same scheduled cue");
    }
    host.BroadcastStop();
    foreach (var bard in followers) await WaitNotice(bard, NetworkNoticeKind.Stop);
    host.Stop();
    await Until(() => followers.All(bard => !bard.IsConnected), "closing room disconnects every follower");
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

static async Task RejectJoin(RelayBandInvite invite, string label)
{
    using var bard = new MultiDeviceEnsemble();
    bard.JoinBand(invite.Encode(), "Bad invite", 999);
    await Until(() => !bard.IsJoining, label + " did not finish");
    Require(!bard.IsConnected, label + " must be rejected");
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
