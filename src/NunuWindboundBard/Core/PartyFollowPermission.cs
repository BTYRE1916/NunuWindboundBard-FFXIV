namespace NunuWindboundBard.Core;

internal readonly record struct PartyFollowScope(ulong Local, ulong Leader, string RosterTag);

internal sealed class PartyFollowPermission
{
    private const long CueWindowMs = 15_000;
    private const int RecentCueLimit = 64;
    private readonly Dictionary<string, long> recent = new(StringComparer.Ordinal);
    private long grantedAt;

    internal PartyFollowScope? Scope { get; private set; }

    internal void Set(PartyFollowScope? scope, long timestamp)
    {
        Scope = scope;
        grantedAt = timestamp;
    }

    internal bool TryAccept(GamePartyCue cue, ulong sender, long receivedAt, PartyFollowScope current,
        uint territory, long nowUnixMs)
    {
        if (Scope != current || receivedAt <= grantedAt ||
            cue.Kind != GamePartyCueKind.PartyFollow || sender != current.Leader ||
            cue.RosterTag != current.RosterTag || cue.FormationTerritory != territory ||
            cue.StartUnixMs < nowUnixMs - CueWindowMs || cue.StartUnixMs > nowUnixMs + CueWindowMs)
            return false;

        foreach (var expired in recent.Where(pair => pair.Value < nowUnixMs).ToArray())
            recent.Remove(expired.Key);
        return recent.Count < RecentCueLimit && recent.TryAdd(cue.Nonce, cue.StartUnixMs + CueWindowMs);
    }
}
