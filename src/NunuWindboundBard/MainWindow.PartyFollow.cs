using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private readonly PartyFollowPermission partyFollowPermission = new();
    private long nextPartyFollowCheck;
    private string partyFollowStatus = "";

    private PartyFollowScope? GetPartyFollowScope()
    {
        var party = Plugin.Party;
        if (!NativeSyncEnabled || !Plugin.ClientState.IsLoggedIn ||
            party.Length < 2 || party.PartyLeaderIndex >= (uint)party.Length)
            return null;

        ulong leader = party[(int)party.PartyLeaderIndex]?.ContentId ?? 0;
        ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, party);
        if (leader == 0 || me == 0 || ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor) != (me == leader))
            return null;

        var roster = NativeRoster();
        if (roster.Count != party.Length || !roster.Contains(leader) || !roster.Contains(me)) return null;
        return new PartyFollowScope(me, leader, OneClickEnsembleCue.RosterTag(roster));
    }

    private void DrawPartyFollowControls(EnsembleRole role)
    {
        if (role is not (EnsembleRole.Conductor or EnsembleRole.Follower)) return;
        var scope = GetPartyFollowScope();
        if (role == EnsembleRole.Follower)
        {
            bool allow = scope.HasValue && partyFollowPermission.Scope == scope;
            ImGui.BeginDisabled(scope is null);
            if (ImGui.Checkbox(Loc.T("Accept follow requests from this party leader"), ref allow))
            {
                partyFollowPermission.Set(allow ? scope : null, Stopwatch.GetTimestamp());
                partyFollowStatus = "";
            }
            ImGui.EndDisabled();
        }
        else
        {
            ImGui.BeginDisabled(scope is null);
            if (ImGui.Button(Loc.T("Follow Party Leader")) && scope is { } party)
                SendPartyFollow(party);
            ImGui.EndDisabled();
        }

        if (scope is null)
            ImGui.TextWrapped(Loc.T("Join a party and enable leader sync. Set the FFXIV party leader as Conductor and the other bards as Followers."));
        else if (role == EnsembleRole.Conductor)
            ImGui.TextWrapped(Loc.T("Ask opted-in followers in this area to follow you using FFXIV's Follow command."));
        if (partyFollowStatus.Length > 0) ImGui.TextWrapped(Loc.T(partyFollowStatus));
    }

    private void RefreshPartyFollowPermission()
    {
        if (partyFollowPermission.Scope is not { } scope) return;
        long now = Stopwatch.GetTimestamp();
        if (now < nextPartyFollowCheck) return;
        nextPartyFollowCheck = now + Stopwatch.Frequency / 4;
        if (GetPartyFollowScope() != scope)
        {
            partyFollowPermission.Set(null, now);
            partyFollowStatus = "Follow permission ended because the party, leader, or Ensemble role changed.";
        }
    }

    private bool CanFollowParty(out string reason)
    {
        var conditions = Plugin.Conditions;
        if (StageTerritory == 0 || conditions[ConditionFlag.Unconscious] ||
            conditions[ConditionFlag.BetweenAreas] || conditions[ConditionFlag.BetweenAreas51] ||
            conditions[ConditionFlag.WatchingCutscene] || conditions[ConditionFlag.WatchingCutscene78] ||
            conditions[ConditionFlag.OccupiedInCutSceneEvent])
        {
            reason = "Follow is unavailable while the game is loading, in a cutscene, or the bard is unconscious.";
            return false;
        }
        return StageSafe(out reason);
    }

    private void SendPartyFollow(PartyFollowScope party)
    {
        if (!CanFollowParty(out partyFollowStatus)) return;
        string message = GamePartyCueProtocol.PartyFollow(NewNativeNonce(), party.RosterTag, StageTerritory,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (plugin.PartyChat.TryBroadcast(message, out partyFollowStatus))
            partyFollowStatus = "Follow request submitted to FFXIV party chat.";
    }

    private void ReceivePartyFollow(PartyCueEnvelope envelope)
    {
        if (partyFollowPermission.Scope is null || GetPartyFollowScope() is not { } scope ||
            !partyFollowPermission.TryAccept(envelope.Cue, envelope.SenderContentId, envelope.ReceivedAt,
                scope, StageTerritory, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()))
            return;

        if (!CanFollowParty(out partyFollowStatus)) return;
        if (stageNavigator.Active || stageFacing.Active || soloStageWalking || soloStageFacingPending)
        {
            partyFollowStatus = "Finish or stop stage positioning before following the party leader.";
            return;
        }
        var party = Plugin.Party;
        var leader = party[(int)party.PartyLeaderIndex];
        if (leader?.ContentId != scope.Leader ||
            leader.GameObject is not { IsTargetable: true } target)
        {
            partyFollowStatus = "Move near the party leader, then ask them to send Follow again.";
            return;
        }

        try
        {
            SubmitNativeFollow(target);
            partyFollowStatus = $"Submitted /follow for {leader.Name.TextValue}.";
        }
        catch (Exception error)
        {
            partyFollowStatus = "Follow skipped: " + error.Message;
            Plugin.Log.Warning(partyFollowStatus);
        }
    }

    private static unsafe void SubmitNativeFollow(IGameObject leader)
    {
        var ui = UIModule.Instance();
        if (ui == null) throw new InvalidOperationException("FFXIV chat is unavailable.");
        var targets = Plugin.Targets;
        // Like selecting Follow in the game, target the party leader first.
        // Keep the leader selected so the native command can resolve its target.
        ulong leaderObjectId = leader.GameObjectId;
        if (targets.Target?.GameObjectId != leaderObjectId) targets.Target = leader;
        if (targets.Target?.GameObjectId != leaderObjectId)
            throw new InvalidOperationException("FFXIV could not target the party leader.");
        var command = Utf8String.FromString("/follow");
        if (command == null) throw new InvalidOperationException("FFXIV could not allocate the Follow command.");
        try { ui->ProcessChatBoxEntry(command); }
        finally { command->Dtor(true); }
    }
}
