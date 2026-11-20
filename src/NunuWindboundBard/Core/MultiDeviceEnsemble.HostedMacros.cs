namespace NunuWindboundBard.Core;

public sealed partial class MultiDeviceEnsemble
{
    private const double MacroLifetimeMs = 10_000;

    internal bool CanAllowHostedMacros
    {
        get { lock (gate) return activeRelay is not null && hostPeer?.SupportsHostedMacros == true; }
    }

    internal bool HostedMacrosAllowed
    {
        get { lock (gate) return activeRelay is not null && hostPeer?.AllowsHostedMacros == true; }
    }

    internal bool SetHostedMacroPermission(int connectionId, bool allow)
    {
        lock (gate)
        {
            if (activeRelay is null || hostPeer is not { SupportsHostedMacros: true } peer ||
                peer.Id != connectionId) return false;
            if (peer.AllowsHostedMacros == allow) return true;
            peer.AllowsHostedMacros = allow;
            peer.Send(new Wire { Type = "MacroPermission", Ready = allow, MacroEpoch = ++peer.MacroEpoch });
            return true;
        }
    }

    internal (int Sent, int Connected) BroadcastHostedMacro(int slot)
    {
        if (slot is < 0 or >= 200) throw new ArgumentOutOfRangeException(nameof(slot));
        lock (gate)
        {
            if (!hosting || activeRelay is null)
                throw new InvalidOperationException("Host a band before sending a band macro.");
            int sent = 0;
            double now = NowMs();
            var recipients = peers.Values;
            foreach (var peer in recipients)
            {
                if (!peer.SupportsHostedMacros || !peer.AllowsHostedMacros) continue;
                var cue = new Wire { Type = "SavedMacro", MacroEpoch = peer.MacroEpoch,
                    MacroSequence = ++peer.MacroSequence, MacroSlot = slot, HostSendMs = now };
                if (peer.Send(cue)) sent++;
            }
            return (sent, recipients.Count);
        }
    }

    // Only the authenticated follower can change its own permission. Re-enabling
    // advances the epoch so a cue already queued before revocation stays invalid.
    private void ReceiveMacroPermission(Peer peer, Wire message)
    {
        lock (gate)
        {
            if (!peer.SupportsHostedMacros || message.MacroEpoch <= peer.MacroEpoch) return;
            peer.MacroEpoch = message.MacroEpoch;
            peer.AllowsHostedMacros = message.Ready;
        }
    }

    private void ReceiveHostedMacro(Peer peer, Wire message, double receivedMs)
    {
        lock (gate)
        {
            if (hostPeer != peer || !peer.AllowsHostedMacros || message.MacroEpoch != peer.MacroEpoch ||
                message.MacroSlot is not int slot || slot is < 0 or >= 200 ||
                message.MacroSequence <= peer.MacroSequence || !double.IsFinite(message.HostSendMs)) return;

            double localSendMs = hostClockOffsetMs is double offset ? message.HostSendMs - offset : receivedMs;
            double expiry = Math.Min(localSendMs, receivedMs) + MacroLifetimeMs;
            if (receivedMs >= expiry) return;
            peer.MacroSequence = message.MacroSequence;
            notices.Enqueue(new(NetworkNoticeKind.HostedMacro, ConnectionId: peer.Id)
            {
                SavedMacro = new(slot, message.MacroSequence, message.MacroEpoch, expiry)
            });
        }
    }

    // Called on the game thread immediately before submitting the native slot.
    internal bool TryConsumeHostedMacro(NetworkNotice notice, out int slot)
    {
        slot = -1;
        lock (gate)
        {
            if (notice.SavedMacro is not { } cue || hostPeer is not { AllowsHostedMacros: true } peer ||
                peer.Id != notice.ConnectionId || peer.MacroEpoch != cue.Epoch ||
                cue.Sequence <= peer.LastConsumedMacro || NowMs() >= cue.ExpiresMs) return false;
            peer.LastConsumedMacro = cue.Sequence;
            slot = cue.Slot;
            return true;
        }
    }
}
