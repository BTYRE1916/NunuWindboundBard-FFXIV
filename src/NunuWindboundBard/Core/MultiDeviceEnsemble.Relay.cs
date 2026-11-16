using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Windbound.OrchestraRelay;

namespace NunuWindboundBard.Core;

public sealed partial class MultiDeviceEnsemble
{
    private string relayJoinCode = "";

    public string RelayJoinCode { get { lock (gate) return relayJoinCode; } }

    public void HostBand(string relayAddress, string stage)
    {
        Uri relay = RelayBandConnection.ReadRelayAddress(relayAddress);
        string secret = NewSessionCode();
        ValidateStageAndCode(stage, secret);
        Stop();
        var lifetime = new CancellationTokenSource();
        lock (gate)
        {
            cancel = lifetime;
            joining = true;
            revision = 0;
            status = "Opening a private band on the relay…";
        }
        _ = Task.Run(() => HostBandLoopAsync(relay, stage, secret, lifetime.Token));
    }

    public void JoinBand(string joinCode, string character, ulong contentId)
    {
        var invite = RelayBandInvite.Decode(joinCode);
        ValidateStageAndCode(invite.Stage, invite.Secret);
        if (contentId == 0 || string.IsNullOrWhiteSpace(character))
            throw new InvalidOperationException("Log in to FFXIV before joining a band.");
        Stop();
        var lifetime = new CancellationTokenSource();
        lock (gate)
        {
            cancel = lifetime;
            joining = true;
            status = "Joining the conductor through the relay…";
        }
        _ = JoinLoopAsync(ct => RelayBandConnection.JoinBandAsync(invite, ct),
            invite.Stage, invite.Secret, character, contentId, lifetime.Token);
    }

    private async Task HostBandLoopAsync(Uri relay, string stage, string secret, CancellationToken ct)
    {
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var certificate = RelayBandConnection.CreateHostCertificate();
            using var control = await RelayBandConnection.OpenAsync(relay, RelayProtocol.HostPath, null,
                connectTimeout.Token).ConfigureAwait(false);
            string room = await RelayBandConnection.ReadTokenAsync(control, connectTimeout.Token).ConfigureAwait(false);
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

            while (!ct.IsCancellationRequested)
            {
                string key = await RelayBandConnection.ReadTokenAsync(control, ct).ConfigureAwait(false);
                _ = AttachBandPeerAsync(relay, key, certificate, stage, secret, ct);
            }
        }
        catch (Exception error)
        {
            if (ct.IsCancellationRequested) return;
            string message = error is OperationCanceledException ? "Relay connection timed out." :
                "Relay session ended: " + error.Message;
            lock (gate)
            {
                if (cancel is null || cancel.Token != ct) return;
                Stop();
                status = message;
                notices.Enqueue(new(NetworkNoticeKind.Disconnected, message));
            }
        }
    }

    private async Task AttachBandPeerAsync(Uri relay, string key, X509Certificate2 certificate,
        string stage, string secret, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            Stream stream = await RelayBandConnection.AcceptBandPeerAsync(relay, key, certificate, timeout.Token)
                .ConfigureAwait(false);
            await HostPeerAsync(stream, stage, secret, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or WebSocketException or
            OperationCanceledException or System.Security.Authentication.AuthenticationException or ObjectDisposedException)
        {
            if (!ct.IsCancellationRequested)
                notices.Enqueue(new(NetworkNoticeKind.Error, "A relay connection could not pair with the conductor."));
        }
    }
}
