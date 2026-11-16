using System.Diagnostics;
using System.Security.Cryptography;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;

namespace NunuWindboundBard.Core;

internal sealed partial class ShortcutBarRuntime
{
    private const long PartyMacroAgeMs = 10_000;
    private const int RecentPartyMacroLimit = 64;
    private readonly Dictionary<string, long> recentPartyMacros = new(StringComparer.Ordinal);
    private MacroParty? permittedMacroParty;
    private long partyMacroPermissionAt;
    private long nextPartyMacroCheck;

    private readonly record struct MacroParty(ulong Local, ulong Leader, string RosterTag);

    internal bool AcceptsPartyMacros => permittedMacroParty.HasValue;
    internal bool CanAcceptPartyMacros => GetFollowerMacroParty().HasValue;

    internal void SetPartyMacroPermission(bool enabled)
    {
        permittedMacroParty = enabled ? GetFollowerMacroParty() : null;
        partyMacroPermissionAt = Stopwatch.GetTimestamp();
        nextPartyMacroCheck = partyMacroPermissionAt;
    }

    private void RefreshPartyMacroPermission()
    {
        if (!AcceptsPartyMacros) return;
        long now = Stopwatch.GetTimestamp();
        if (now < nextPartyMacroCheck) return;
        nextPartyMacroCheck = now + Stopwatch.Frequency / 4;
        if (permittedMacroParty != GetFollowerMacroParty())
        {
            SetPartyMacroPermission(false);
            status = "Party macro permission ended because the party, leader or Ensemble settings changed.";
        }
    }

    private MacroParty? GetFollowerMacroParty() =>
        plugin.Config.EnsembleMode == (int)EnsembleRole.Follower &&
        GetMacroParty() is { } party && party.Local != party.Leader ? party : null;

    private MacroParty? GetMacroParty()
    {
        var party = Plugin.Party;
        if (!Plugin.ClientState.IsLoggedIn || !plugin.Config.GameEnsembleEnabled ||
            !plugin.Config.NativeLeaderSyncEnabled ||
            party.Length < 2 || party.PartyLeaderIndex >= (uint)party.Length)
            return null;

        ulong leader = party[(int)party.PartyLeaderIndex]?.ContentId ?? 0;
        ulong local = PartyRoster.GetLocalContentId(Plugin.Objects, party);
        var roster = party.Select(member => member?.ContentId ?? 0).ToArray();
        if (leader == 0 || local == 0 || !roster.Contains(local) ||
            roster.Length != party.Length || roster.Contains(0UL))
            return null;

        return new MacroParty(local, leader, OneClickEnsembleCue.RosterTag(roster));
    }

    private unsafe void BroadcastPartyMacro(int slot)
    {
        if (plugin.Config.EnsembleMode != (int)EnsembleRole.Conductor ||
            GetMacroParty() is not { } party || party.Local != party.Leader)
            throw new InvalidOperationException("Select Conductor with Native Ensemble and Follow Party Leader enabled, and make this bard the FFXIV party leader.");

        var shell = RaptureShellModule.Instance();
        if (shell == null) throw new InvalidOperationException("FFXIV macros are unavailable.");
        var macro = FindMacro(slot);
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        string message = GamePartyCueProtocol.PartyMacro(nonce, party.RosterTag,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), slot);
        if (!plugin.PartyChat.TryBroadcast(message, out string reason))
            throw new InvalidOperationException(reason);

        shell->ExecuteMacro(macro);
        status = $"Party macro slot {slot % 100:00} ({(slot < 100 ? "Individual" : "Shared")}) submitted to FFXIV party chat.";
    }

    internal void ReceivePartyMacro(PartyCueEnvelope envelope)
    {
        var cue = envelope.Cue;
        if (!AcceptsPartyMacros || cue.Kind != GamePartyCueKind.PartyMacro ||
            envelope.ReceivedAt <= partyMacroPermissionAt)
            return;

        if (GetFollowerMacroParty() is not { } party || party != permittedMacroParty)
        {
            SetPartyMacroPermission(false);
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (envelope.SenderContentId != party.Leader ||
            !string.Equals(cue.RosterTag, party.RosterTag, StringComparison.Ordinal) ||
            cue.MacroSlot is < 0 or >= 200 ||
            cue.StartUnixMs < now - PartyMacroAgeMs || cue.StartUnixMs > now + PartyMacroAgeMs ||
            !CanRun())
            return;

        foreach (var previous in recentPartyMacros.Where(entry => entry.Value < now).ToArray())
            recentPartyMacros.Remove(previous.Key);
        if (recentPartyMacros.Count >= RecentPartyMacroLimit ||
            !recentPartyMacros.TryAdd(cue.Nonce, cue.StartUnixMs + PartyMacroAgeMs))
            return;

        try
        {
            ExecuteMacro(cue.MacroSlot);
            status = $"Party macro slot {cue.MacroSlot % 100:00} ({(cue.MacroSlot < 100 ? "Individual" : "Shared")}) submitted locally.";
        }
        catch (Exception error)
        {
            status = "Party macro skipped: " + error.Message;
            Plugin.Log.Warning(status);
        }
    }
}
