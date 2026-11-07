using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Conductor ARM/DISARM for native FFXIV ensemble parties. Reuses the existing
/// verified party-chat identity transport and reports an acknowledgement per
/// follower; no note-on, keyboard injection or START is ever triggered.
/// </summary>
public sealed partial class MainWindow
{
    private sealed class PartyArmBatch
    {
        public required string Nonce;
        public required bool RequestedArm;
        public required HashSet<ulong> ExpectedFollowers;
        public readonly Dictionary<ulong, int> Acknowledged = new();
        public readonly Dictionary<ulong, string> Rejected = new();
        public long StartedQpc;
        public bool TimedOut;
        public bool Complete => ExpectedFollowers.All(id =>
            Acknowledged.ContainsKey(id) || Rejected.ContainsKey(id));
    }

    private sealed class IncomingPartyArm
    {
        public required GamePartyCue Cue;
        public long StartedQpc;
        public long LastReplyQpc;
        public int AckDelayMs;
        public int? ResultCode;
        public string Failure = "";
    }

    private PartyArmBatch? partyArmBatch;
    private IncomingPartyArm? pendingPartyArm;
    private readonly HashSet<string> seenPartyArmNonces = new(StringComparer.Ordinal);
    private string nativeArmStatus = "Party ARM not requested.";

    private void DrawNativePartyArmControls()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.65f, .95f, .78f, 1f), Loc.T("ONE-CLICK PARTY OUTPUT ARM"));
        if (ImGui.Button(Loc.T("ARM ALL PARTY BARDS##conductorArm"))) ConductorArmParty(true);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("DISARM ALL##conductorDisarm"))) ConductorArmParty(false);
        ImGui.TextDisabled(Loc.T("Sets ARM on this conductor and all opted-in active Followers. No instrument change or song START."));
        ImGui.TextWrapped(Loc.T(nativeArmStatus));
        if (partyArmBatch is { } batch)
        {
            var missing = batch.ExpectedFollowers.Where(id =>
                !batch.Acknowledged.ContainsKey(id) && !batch.Rejected.ContainsKey(id)).ToList();
            if (missing.Count > 0)
                ImGui.TextDisabled(Loc.T("Waiting for: ") + string.Join(", ", missing.Select(PartyMemberName)));
            foreach (var failure in batch.Rejected)
                ImGui.TextColored(new Vector4(1f, .65f, .38f, 1f),
                    PartyMemberName(failure.Key) + ": " + Loc.T(failure.Value));
        }
    }

    private void DrawNativeFollowerArmConsent()
    {
        bool allowed = cfg.AllowLeaderArmCommands;
        if (ImGui.Checkbox(Loc.T("Allow my party leader to ARM this Bard automatically##allowPartyArm"), ref allowed))
        {
            cfg.AllowLeaderArmCommands = allowed;
            cfg.Save();
        }
        ImGui.TextDisabled(Loc.T("Enabled by default for native Ensemble followers. ARM itself is never saved across restarts."));
    }

    private void ConductorArmParty(bool arm)
    {
        try
        {
            if (!NativeSyncEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor)
                throw new InvalidOperationException("Select native Ensemble Conductor and enable leader sync first.");
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("STOP the current performance before changing the party ARM state.");
            if (arm && (song is null || arranged is null || string.IsNullOrWhiteSpace(loaded) || !File.Exists(loaded)))
                throw new InvalidOperationException("Load a MIDI before ARMing the ensemble.");
            if (songFlow.HasPending || pendingNativeUnequip || plugin.Instruments.InProgress)
                throw new InvalidOperationException("Finish the current song/instrument transition before using ARM ALL.");
            string hash = !string.IsNullOrWhiteSpace(loaded) && File.Exists(loaded)
                ? GamePartyCueProtocol.GetHash(loaded) : "0000000000000000";
            string nonce = NewNativeNonce();
            var session = CreateLeaderSession(nonce);
            string rosterTag = OneClickEnsembleCue.RosterTag(NativeRoster());
            string message = GamePartyCueProtocol.NativeArmParty(nonce, hash, rosterTag, arm);
            plugin.PartyChat.Enabled = true;
            if (!plugin.PartyChat.TryBroadcast(message, out string problem))
                throw new InvalidOperationException(problem);
            partyArmBatch = new PartyArmBatch
            {
                Nonce = nonce,
                RequestedArm = arm,
                ExpectedFollowers = session.Expected,
                StartedQpc = Stopwatch.GetTimestamp()
            };
            // Own player is part of this one-click operation. ARM is session-only;
            // never enable any player MIDI scheduler, direct note-on or group START.
            HudSetOutputArmed(arm);
            nativeArmStatus = (arm ? "ARM" : "DISARM") +
                $" submitted to {session.Expected.Count} follower(s). Waiting for their acknowledgements...";
        }
        catch (Exception ex)
        {
            nativeArmStatus = (arm ? "ARM ALL" : "DISARM ALL") + " failed: " + ex.Message;
        }
    }

    private void ReceiveNativePartyArm(GamePartyCue cue)
    {
        // The transport already verified the FFXIV party-leader identity.
        // This also enforces an exact party roster before any ARM state changes.
        if (!NativeSyncEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) return;
        if (!string.Equals(cue.RosterTag, OneClickEnsembleCue.RosterTag(NativeRoster()),
            StringComparison.OrdinalIgnoreCase))
        {
            nativeArmStatus = "Ignored party ARM: conductor's roster no longer matches mine.";
            plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeArmFail(cue.Nonce, "ROSTER"), out _);
            return;
        }
        if (!seenPartyArmNonces.Add(cue.Nonce)) return;
        if (seenPartyArmNonces.Count > 256) seenPartyArmNonces.Clear();
        // Stagger up to eight party clients so they do not all submit a chat ACK
        // on the same frame (FFXIV may throttle simultaneous /p messages).
        int slot = NativeTargetSlot(PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party));
        pendingPartyArm = new IncomingPartyArm
        {
            Cue = cue, StartedQpc = Stopwatch.GetTimestamp(),
            AckDelayMs = Math.Clamp((slot - 1) * 330, 0, 4900)
        };
        nativeArmStatus = cue.ArmRequested ? "Leader requested ARM; validating my MIDI and track assignments..." :
            "Leader requested DISARM...";
    }

    private void OnNativeArmReply(GamePartyCue cue, ulong senderId)
    {
        if (!NativeSyncEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor ||
            partyArmBatch is not { } batch || cue.Nonce != batch.Nonce ||
            !batch.ExpectedFollowers.Contains(senderId)) return;
        if (batch.Acknowledged.ContainsKey(senderId) || batch.Rejected.ContainsKey(senderId)) return;
        if (cue.Kind == GamePartyCueKind.NativeArmFailed)
            batch.Rejected[senderId] = "Follower rejected ARM: " + cue.PackedInstruments;
        else if (cue.Kind == GamePartyCueKind.NativeArmReady &&
                 ((batch.RequestedArm && (cue.ArmReplyCode == 1 || cue.ArmReplyCode == 2 || cue.ArmReplyCode == 3)) ||
                  (!batch.RequestedArm && cue.ArmReplyCode == 0)))
            batch.Acknowledged[senderId] = cue.ArmReplyCode;
        else return;
        UpdateNativeArmStatus(batch);
    }

    private void UpdateNativeArmStatus(PartyArmBatch batch)
    {
        int missing = batch.ExpectedFollowers.Count - batch.Acknowledged.Count - batch.Rejected.Count;
        int silent = batch.Acknowledged.Values.Count(x => x == 2);
        int waitingKeyboard = batch.Acknowledged.Values.Count(x => x == 3);
        string action = batch.RequestedArm ? "ARM" : "DISARM";
        nativeArmStatus = $"{action}: {batch.Acknowledged.Count}/{batch.ExpectedFollowers.Count} " +
            $"acknowledged; {batch.Rejected.Count} rejected; {missing} awaiting reply.";
        if (silent > 0) nativeArmStatus += $" {silent} silent Bard(s) skipped.";
        if (waitingKeyboard > 0)
            nativeArmStatus += $" {waitingKeyboard} ARMed but still need the 37-key keyboard.";
        if (batch.Complete && batch.Rejected.Count == 0)
            nativeArmStatus += " All responding Bards prepared; START is still a separate native FFXIV action.";
        if (batch.TimedOut && missing > 0)
            nativeArmStatus += " TIMEOUT: check the missing client's Follower mode and receiver diagnostics.";
    }

    /// <summary>Called on Framework.Update, after incoming party messages and MIDI loading.</summary>
    public void PollNativePartyArming()
    {
        if (!NativeSyncEnabled)
        {
            pendingPartyArm = null;
            partyArmBatch = null;
            return;
        }
        var now = Stopwatch.GetTimestamp();
        if (partyArmBatch is { } batch && !batch.Complete && !batch.TimedOut &&
            (now - batch.StartedQpc) / (double)Stopwatch.Frequency > 25)
        {
            batch.TimedOut = true;
            UpdateNativeArmStatus(batch);
        }
        if (pendingPartyArm is not { } pending) return;
        double seconds = (now - pending.StartedQpc) / (double)Stopwatch.Frequency;
        var cue = pending.Cue;
        bool loading = nativeFollowerFile is not null;
        bool busy = songFlow.HasPending || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                    pendingNativeUnequip || (loading && !cue.ArmRequested);
        var check = NativePartyArmPlan.Evaluate(cue.ArmRequested,
            NativeSyncEnabled && (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower,
            cfg.AllowLeaderArmCommands, plugin.Player.IsPlaying, busy, loading,
            cue.Hash, nativeLoadedSha, song is not null && arranged is not null,
            lanes.Count(x => x.Include));
        if (check.Decision == PartyArmDecision.Wait && seconds <= 25) return;
        if (check.Decision == PartyArmDecision.Wait)
            check = new PartyArmEvaluation(PartyArmDecision.Reject, "TIMEOUT");
        if (seconds < pending.AckDelayMs / 1000.0) return;
        if (pending.LastReplyQpc != 0 &&
            (now - pending.LastReplyQpc) / (double)Stopwatch.Frequency < 1) return;
        pending.LastReplyQpc = now;
        try
        {
            if (check.Decision == PartyArmDecision.Reject)
            {
                pending.Failure = check.Reason;
                if (plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeArmFail(cue.Nonce,
                    pending.Failure), out _))
                {
                    nativeArmStatus = "Conductor ARM rejected: " + pending.Failure;
                    pendingPartyArm = null;
                }
                return;
            }
            // Apply once; repeat only the ACK on transport failures.
            if (!pending.ResultCode.HasValue)
            {
                int replyCode;
                if (check.Decision == PartyArmDecision.Armed)
                {
                    HudSetOutputArmed(true);
                    replyCode = plugin.IsGameOutputSafe() && !plugin.Instruments.InProgress ? 1 : 3;
                }
                else if (check.Decision == PartyArmDecision.Silent)
                {
                    HudSetOutputArmed(false); // no playable local tracks
                    replyCode = 2;
                }
                else
                {
                    HudSetOutputArmed(false);
                    replyCode = 0;
                }
                pending.ResultCode = replyCode;
            }
            if (plugin.PartyChat.TryReply(GamePartyCueProtocol.NativeArmAck(cue.Nonce,
                pending.ResultCode.Value), out string outcome))
            {
                nativeArmStatus = pending.ResultCode.Value switch
                {
                    0 => "Conductor DISARM applied.",
                    1 => "Conductor ARM applied; 37-key Performance ready.",
                    2 => "Conductor ARM received; this Bard has no assigned tracks (silent).",
                    _ => "Conductor ARM applied; open expanded 37-key Performance before START."
                };
                pendingPartyArm = null;
            }
            else nativeArmStatus = "Output state updated, awaiting ACK delivery: " + outcome;
        }
        catch (Exception ex)
        {
            nativeArmStatus = "Could not apply conductor ARM: " + ex.Message;
            pendingPartyArm = null; // leave no partial delayed change
        }
    }

    private void CancelNativePartyArming(string reason)
    {
        pendingPartyArm = null;
        partyArmBatch = null;
        nativeArmStatus = reason;
    }
}
