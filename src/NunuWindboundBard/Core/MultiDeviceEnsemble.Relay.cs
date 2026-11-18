using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windbound.OrchestraRelay;

namespace NunuWindboundBard.Core;

public sealed partial class MultiDeviceEnsemble
{
    private string relayJoinCode = "";

    private readonly RelayDiagnostics relayDiagnostics = new();
    private RelayTrace? activeRelay;
    public string RelayReport => relayDiagnostics.Report;

    public string RelayJoinCode { get { lock (gate) return relayJoinCode; } }

    public void HostBand(string relayAddress, string stage)
    {
        var trace = relayDiagnostics.Begin("Conductor");
        Uri relay = RelayBandConnection.ReadRelayAddress(relayAddress);
        string secret = NewSessionCode();
        ValidateStageAndCode(stage, secret);
        Stop();
        var lifetime = new CancellationTokenSource();
        lock (gate)
        {
            cancel = lifetime;
            activeRelay = trace;
            joining = true;
            revision = 0;
            status = "Opening a private band on the relay…";
        }
        _ = Task.Run(() => HostBandLoopAsync(relay, stage, secret, lifetime.Token, trace));
    }

    public void JoinBand(string joinCode, string character, ulong contentId)
    {
        var trace = relayDiagnostics.Begin("Follower");
        RelayBandInvite invite;
        try { invite = RelayBandInvite.Decode(joinCode); }
        catch (ArgumentException) { trace.Note("Join code format rejected locally."); throw; }
        ValidateStageAndCode(invite.Stage, invite.Secret);
        if (contentId == 0 || string.IsNullOrWhiteSpace(character))
            throw new InvalidOperationException("Log in to FFXIV before joining a band.");
        Stop();
        var lifetime = new CancellationTokenSource();
        lock (gate)
        {
            cancel = lifetime;
            activeRelay = trace;
            joining = true;
            status = "Joining the conductor through the relay…";
        }
        _ = JoinLoopAsync(ct => RelayBandConnection.JoinBandAsync(invite, ct, trace),
            invite.Stage, invite.Secret, character, contentId, lifetime.Token, trace);
    }

    private async Task HostBandLoopAsync(Uri relay, string stage, string secret, CancellationToken ct, RelayTrace trace)
    {
        CancellationToken deadline = default;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline = connectTimeout.Token;
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            trace.Step("Creating conductor certificate");
            using var certificate = RelayBandConnection.CreateHostCertificate();
            trace.Step("Opening room /v1/host");
            using var control = await RelayBandConnection.OpenAsync(relay, RelayProtocol.HostPath, null,
                connectTimeout.Token, trace).ConfigureAwait(false);
            trace.Step("Waiting for room confirmation");
            string room = await RelayBandConnection.ReadTokenAsync(control, connectTimeout.Token).ConfigureAwait(false);
            connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            var invite = new RelayBandInvite(relay.AbsoluteUri, room, stage, secret,
                certificate.GetCertHashString(HashAlgorithmName.SHA256));
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                if (cancel is null || cancel.Token != ct) return;
                relayJoinCode = invite.Encode();
                joining = false;
                hosting = true;
                status = "Private band hosted. Copy the join code for your bards.";
                notices.Enqueue(new(NetworkNoticeKind.Connected, status));
            }

            trace.Step("Room open, listening for followers");
            while (!ct.IsCancellationRequested)
            {
                string key = await RelayBandConnection.ReadTokenAsync(control, ct).ConfigureAwait(false);
                _ = AttachBandPeerAsync(relay, key, certificate, stage, secret, ct);
            }
        }
        catch (Exception error)
        {
            if (ct.IsCancellationRequested) return;
            string message = "Relay session ended. " + trace.Failed(error, deadline);
            lock (gate)
            {
                if (cancel is null || cancel.Token != ct) return;
                activeRelay = null;
                Stop();
                status = message;
                notices.Enqueue(new(NetworkNoticeKind.Disconnected, message));
            }
        }
    }

    private async Task AttachBandPeerAsync(Uri relay, string key, X509Certificate2 certificate,
        string stage, string secret, CancellationToken ct)
    {
        var trace = relayDiagnostics.Begin("Conductor peer");
        CancellationToken deadline = default;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline = timeout.Token;
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            Stream stream = await RelayBandConnection.AcceptBandPeerAsync(relay, key, certificate, timeout.Token, trace)
                .ConfigureAwait(false);
            timeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await HostPeerAsync(stream, stage, secret, ct, trace).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (!ct.IsCancellationRequested)
                notices.Enqueue(new(NetworkNoticeKind.Error, "Relay pairing failed. " + trace.Failed(error, deadline)));
        }
    }
}
