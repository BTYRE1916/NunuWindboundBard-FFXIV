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
/// Concert Director controls only preparation and monitoring. It NEVER sends
/// a musical start timer. Every player still starts from FFXIV native Ensemble.
/// The conductor uses existing file/track/instrument commands and acknowledgements.
/// </summary>
public sealed partial class MainWindow
{
    private enum ConcertPhase { Idle, File, Tracks, Instruments, Health, Ready, Failed }
    private enum ConcertRetryPhase { Idle, File, Tracks, Instruments }
    private ConcertRetryPhase concertRetryPhase;
    private ulong concertRetryTarget;
    private sealed record StagePeer(ConcertStageSnapshot Snapshot, long ReceivedQpc);
    private ConcertPhase concertPhase;
    private readonly Dictionary<ulong, StagePeer> concertPeers = new();
    private string concertNonce = "";
    private string concertHash = "";
    private string concertStatus = "Stage Manager is idle. Prepare the orchestra before a concert.";
    private long concertPhaseQpc;
    private long concertProbeQpc;
    private bool concertMonitorActive;
    private string concertLastError = "";

    private bool ConcertIsConductor => NativeSyncEnabled &&
        (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor;

    private static double SecondsSince(long qpc) => qpc <= 0 ? double.PositiveInfinity :
        (Stopwatch.GetTimestamp() - qpc) / (double)Stopwatch.Frequency;

    /// <summary>Invalidate old readiness when the conductor selects another MIDI.
    /// Already prepared clients must be checked against the NEW file hash.</summary>
    private void ConcertOnMidiChanged()
    {
        if (!ConcertIsConductor) return;
        if (!string.Equals(concertHash, nativeLoadedSha, StringComparison.OrdinalIgnoreCase))
        {
            concertHash = nativeLoadedSha;
            concertNonce = "";
            concertPeers.Clear();
            concertPhase = ConcertPhase.Idle;
            concertRetryPhase = ConcertRetryPhase.Idle;
            concertRetryTarget = 0;
            concertMonitorActive = false;
            concertStatus = "New MIDI selected; prepare the orchestra for this song.";
        }
    }

    private void ConcertSetPhase(ConcertPhase phase, string message)
    {
        concertPhase = phase;
        concertPhaseQpc = Stopwatch.GetTimestamp();
        concertStatus = message;
    }

    private void ConcertCancelPreparation(string message)
    {
        concertPhase = ConcertPhase.Idle;
        concertRetryPhase = ConcertRetryPhase.Idle;
        concertRetryTarget = 0;
        concertMonitorActive = false;
        concertNonce = "";
        concertPeers.Clear();
        concertStatus = message;
    }

    private void ConcertPrepareOrchestra(bool alreadySentFile = false)
    {
        try
        {
            if (!ConcertIsConductor)
                throw new InvalidOperationException("Native Game Ensemble and Conductor must be enabled.");
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
                plugin.Instruments.InProgress)
                throw new InvalidOperationException("Stop playback and finish the current switch before preparing.");
            if (song is null || string.IsNullOrWhiteSpace(loaded) || !File.Exists(loaded))
                throw new InvalidOperationException("Select a MIDI in the Ensemble playlist first.");
            if (NativeRoster().Count < 2)
                throw new InvalidOperationException("Join the same FFXIV party with the other Bards.");
            concertHash = GamePartyCueProtocol.GetHash(loaded);
            concertProbeQpc = 0;
            concertPeers.Clear();
            concertLastError = "";
            concertMonitorActive = true;
            if (!alreadySentFile) BroadcastNativeFile();
            if (nativeLeaderFile is null || nativeLeaderFile.Failed)
                throw new InvalidOperationException(nativeFileSyncStatus);
            ConcertSetPhase(ConcertPhase.File, "Synchronising the selected MIDI across the party...");
        }
        catch (Exception ex) { ConcertFail("Concert preparation: " + ex.Message); }
    }

    private void ConcertFail(string reason)
    {
        concertLastError = reason;
        ConcertSetPhase(ConcertPhase.Failed, reason);
    }

    private void ConcertRequestStageStatus()
    {
        try
        {
            if (!ConcertIsConductor || song is null || string.IsNullOrWhiteSpace(loaded))
                throw new InvalidOperationException("Load a MIDI and choose Conductor first.");
            if (SecondsSince(concertProbeQpc) < 3)
                throw new InvalidOperationException("Wait three seconds before sending another status request.");
            concertNonce = NewNativeNonce();
            concertHash = GamePartyCueProtocol.GetHash(loaded);
            concertPeers.Clear();
            plugin.PartyChat.Enabled = true;
            if (!plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.StageProbe(concertNonce, concertHash), out string reason))
                throw new InvalidOperationException(reason);
            concertProbeQpc = Stopwatch.GetTimestamp();
            concertMonitorActive = true;
            concertStatus = "Status request sent. Waiting for party members to report their MIDI, instrument, tracks and keyboard.";
        }
        catch (Exception ex) { concertStatus = "Cannot refresh stage: " + ex.Message; }
    }

    private ConcertStageSnapshot ConcertGetLocalSnapshot(string requestedHash)
    {
        var flags = ConcertStageFlags.None;
        if (NativeSyncEnabled) flags |= ConcertStageFlags.NativeEnabled;
        if (!string.IsNullOrEmpty(nativeLoadedSha) &&
            string.Equals(nativeLoadedSha, requestedHash, StringComparison.OrdinalIgnoreCase))
            flags |= ConcertStageFlags.MidiMatches;
        if (lanes.Any(x => x.Include)) flags |= ConcertStageFlags.TracksAssigned;
        if (outputArmed) flags |= ConcertStageFlags.OutputArmed;
        if (plugin.IsGameOutputSafe()) flags |= ConcertStageFlags.Keyboard37;
        if (plugin.Instruments.InProgress) flags |= ConcertStageFlags.InstrumentSwitching;
        if (songFlow.HasPending || pendingNativeUnequip || nativeFollowerFile is not null ||
            nativeFollowerTracks is not null || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
            flags |= ConcertStageFlags.Busy;
        if (plugin.Player.IsPlaying) flags |= ConcertStageFlags.Playing;
        return new ConcertStageSnapshot(string.IsNullOrEmpty(nativeLoadedSha) ? "0000000000000000" : nativeLoadedSha,
            flags, plugin.Instruments.CurrentId, lanes.Count(x => x.Include));
    }

    private void ReplyConcertStageStatus(GamePartyCue probe)
    {
        if (!NativeSyncEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) return;
        try
        {
            var snapshot = ConcertGetLocalSnapshot(probe.Hash);
            plugin.PartyChat.TryReply(GamePartyCueProtocol.StageReport(probe.Nonce, snapshot.MidiHash,
                (int)snapshot.Flags, snapshot.CurrentInstrument, snapshot.EnabledLanes), out _);
        }
        catch (Exception ex) { concertStatus = "Stage status reply failed: " + ex.Message; }
    }

    private void ReceiveConcertStageReport(GamePartyCue cue, ulong senderId)
    {
        if (!ConcertIsConductor || cue.Nonce != concertNonce ||
            !NativeRoster().Contains(senderId)) return;
        concertPeers[senderId] = new StagePeer(
            new ConcertStageSnapshot(cue.Hash, (ConcertStageFlags)cue.StageFlags,
                cue.StageInstrument, cue.StageLanes), Stopwatch.GetTimestamp());
    }

    private ConcertStageAssessment ConcertAssessMember(ulong id)
    {
        bool isLocal = id == PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        ConcertStageSnapshot snapshot;
        bool stale;
        if (isLocal)
        {
            snapshot = ConcertGetLocalSnapshot(concertHash);
            stale = false;
        }
        else if (concertPeers.TryGetValue(id, out var peer))
        {
            snapshot = peer.Snapshot;
            stale = SecondsSince(peer.ReceivedQpc) > 32;
        }
        else { return new ConcertStageAssessment(false, "No status report received"); }
        int instrument = GetPartyInstrumentChoices().GetValueOrDefault(id);
        int? expectedLanes = ConcertStageHealth.ExpectedLaneCount(partyAssignments,
            lanes.Select(l => l.Key).ToArray(), id);
        return ConcertStageHealth.Assess(snapshot, concertHash, instrument, expectedLanes, stale);
    }

    private bool ConcertAllReady()
    {
        var members = NativeRoster();
        return members.Count > 1 && members.All(id => ConcertAssessMember(id).Ready);
    }

    public void PollConcertDirector()
    {
        if (!ConcertIsConductor)
        {
            if (concertPhase != ConcertPhase.Idle) ConcertCancelPreparation("Concert Director is not in Conductor mode.");
            return;
        }
        if (concertRetryPhase != ConcertRetryPhase.Idle)
        {
            if (SecondsSince(concertPhaseQpc) > 55)
            { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail("Single-Bard retry timed out."); return; }
            switch (concertRetryPhase)
            {
                case ConcertRetryPhase.File:
                    if (nativeLeaderFile?.Failed == true) { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail(nativeFileSyncStatus); return; }
                    if (nativeLeaderFile?.Complete != true) return;
                    SetNativePartyTracks(concertRetryTarget);
                    if (nativeLeaderTracks is null || nativeLeaderTracks.Failed)
                    { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail(nativeTracksSyncStatus); return; }
                    concertRetryPhase = ConcertRetryPhase.Tracks;
                    concertPhaseQpc = Stopwatch.GetTimestamp();
                    break;
                case ConcertRetryPhase.Tracks:
                    if (nativeLeaderTracks?.Failed == true) { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail(nativeTracksSyncStatus); return; }
                    if (nativeLeaderTracks?.Complete != true) return;
                    SetNativePartyInstruments(concertRetryTarget);
                    if (nativeLeaderInstruments is null || nativeLeaderInstruments.Failed)
                    { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail(nativeInstrumentSyncStatus); return; }
                    concertRetryPhase = ConcertRetryPhase.Instruments;
                    concertPhaseQpc = Stopwatch.GetTimestamp();
                    break;
                case ConcertRetryPhase.Instruments:
                    if (nativeLeaderInstruments?.Failed == true) { concertRetryPhase = ConcertRetryPhase.Idle; ConcertFail(nativeInstrumentSyncStatus); return; }
                    if (nativeLeaderInstruments?.Complete != true) return;
                    concertRetryPhase = ConcertRetryPhase.Idle;
                    concertProbeQpc = 0;
                    ConcertRequestStageStatus();
                    ConcertSetPhase(ConcertPhase.Health, "Retry complete. Rechecking stage readiness...");
                    break;
            }
            return;
        }
        if (concertPhase is ConcertPhase.File or ConcertPhase.Tracks or ConcertPhase.Instruments)
        {
            if (SecondsSince(concertPhaseQpc) > 55)
            { ConcertFail("Stage preparation timed out. Inspect the missing Bard, then retry."); return; }
            switch (concertPhase)
            {
                case ConcertPhase.File:
                    if (nativeLeaderFile?.Failed == true) { ConcertFail(nativeFileSyncStatus); return; }
                    if (nativeLeaderFile?.Complete != true) return;
                    SetNativePartyTracks();
                    if (nativeLeaderTracks is null || nativeLeaderTracks.Failed)
                    { ConcertFail(nativeTracksSyncStatus); return; }
                    ConcertSetPhase(ConcertPhase.Tracks, "MIDI loaded. Applying conductor track assignments...");
                    break;
                case ConcertPhase.Tracks:
                    if (nativeLeaderTracks?.Failed == true) { ConcertFail(nativeTracksSyncStatus); return; }
                    if (nativeLeaderTracks?.Complete != true) return;
                    SetNativePartyInstruments();
                    if (nativeLeaderInstruments is null || nativeLeaderInstruments.Failed)
                    { ConcertFail(nativeInstrumentSyncStatus); return; }
                    ConcertSetPhase(ConcertPhase.Instruments, "Tracks assigned. Automatically equipping the orchestra...");
                    break;
                case ConcertPhase.Instruments:
                    if (nativeLeaderInstruments?.Failed == true) { ConcertFail(nativeInstrumentSyncStatus); return; }
                    if (nativeLeaderInstruments?.Complete != true) return;
                    ConcertRequestStageStatus();
                    ConcertSetPhase(ConcertPhase.Health, "Instruments confirmed. Checking the complete orchestra...");
                    break;
            }
        }
        else if (concertPhase == ConcertPhase.Health)
        {
            if (ConcertAllReady())
            {
                ConcertSetPhase(ConcertPhase.Ready,
                    "All reported Bards READY. Press FFXIV native START ENSEMBLE when ready to perform.");
            }
            else if (SecondsSince(concertPhaseQpc) > 24)
                ConcertFail("Stage check incomplete. Refresh Status or retry the missing performer.");
        }
        // Poll every 24s only during an active stage-monitoring session, not during playback.
        // Rate limiting avoids flooding FFXIV party chat when 8 accounts are running.
        if (concertMonitorActive && (concertPhase is ConcertPhase.Ready or ConcertPhase.Failed) &&
            !plugin.Player.IsPlaying && SecondsSince(concertProbeQpc) > 24)
            ConcertRequestStageStatus();
    }

    private void DrawConcertDirector()
    {
        if (!cfg.GameEnsembleEnabled || cfg.GameEnsembleOneClick || !cfg.NativeLeaderSyncEnabled) return;
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.98f,.60f,.84f,1f), Loc.T("CONCERT DIRECTOR — ORCHESTRA STAGE MANAGER"));
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor)
        {
            ImGui.TextDisabled(Loc.T("Follower: status requests from your conductor are answered automatically."));
            return;
        }
        if (ImGui.Button(Loc.T("PREPARE ENTIRE ORCHESTRA"))) ConcertPrepareOrchestra();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("ARM ALL BARDS##directorArm"))) ConductorArmParty(true);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Refresh Stage Status"))) ConcertRequestStageStatus();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Cancel Preparation"))) ConcertCancelPreparation("Preparation cancelled. No music was started.");
        ImGui.TextWrapped(Loc.T(concertStatus));
        ImGui.TextWrapped(Loc.T("Party ARM: " + nativeArmStatus));
        string desired = !string.IsNullOrEmpty(nativeLoadedSha) ? nativeLoadedSha : concertHash;
        if (string.IsNullOrWhiteSpace(desired))
        { ImGui.TextDisabled(Loc.T("Load an Ensemble MIDI first.")); return; }
        if (string.IsNullOrEmpty(concertHash)) concertHash = desired;
        var roster = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        int ready = roster.Count(m => ConcertAssessMember(m.ContentId).Ready);
        ImGui.TextColored(ready == roster.Count && roster.Count > 0
            ? new Vector4(.45f,.93f,.65f,1f) : new Vector4(1f,.77f,.41f,1f),
            Loc.T($"Stage READY: {ready}/{roster.Count}"));
        ImGui.BeginChild("##concertStageRoster", new Vector2(-1, Math.Min(270, 39 * roster.Count + 10)), true);
        foreach (var member in roster)
        {
            var health = ConcertAssessMember(member.ContentId);
            ImGui.TextColored(health.Ready ? new Vector4(.45f,.93f,.65f,1f) : new Vector4(1f,.77f,.41f,1f),
                (health.Ready ? "✓ " : "! ") + member.Name + " — " + Loc.T(health.Message));
            if (!health.Ready && !member.IsLocal)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.T("Retry##stageRetry") + member.ContentId))
                    ConcertRetryMember(member.ContentId);
            }
        }
        ImGui.EndChild();
        if (ImGui.SmallButton(Loc.T("Copy Stage Report")))
        {
            string report = "Windbound Concert Director | " + DateTimeOffset.UtcNow.ToString("O") + "\n" +
                "MIDI: " + Path.GetFileName(loaded) + "\n" +
                string.Join("\n", roster.Select(m => m.Name + ": " + ConcertAssessMember(m.ContentId).Message));
            ImGui.SetClipboardText(report);
        }
        ImGui.TextDisabled(Loc.T("Stage reports are received via FFXIV party chat; the native Ready Check alone starts music."));
        DrawConcertSetlist();
    }

    private void ConcertRetryMember(ulong targetId)
    {
        if (!ConcertIsConductor) return;
        try
        {
            if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
                throw new InvalidOperationException("Stop the current ensemble before retrying a performer.");
            if (concertRetryPhase != ConcertRetryPhase.Idle)
                throw new InvalidOperationException("Another Bard's retry is in progress.");
            // Only the selected roster slot will apply the new file/track/instrument cues.
            // Other party clients discard these messages without changing their state.
            concertPhase = ConcertPhase.Idle;
            concertRetryTarget = targetId;
            BroadcastNativeFile(targetId);
            if (nativeLeaderFile is null || nativeLeaderFile.Failed)
                throw new InvalidOperationException(nativeFileSyncStatus);
            concertRetryPhase = ConcertRetryPhase.File;
            concertPhaseQpc = Stopwatch.GetTimestamp();
            concertMonitorActive = true;
            concertStatus = "Retrying only " + PartyMemberName(targetId) + ": MIDI → tracks → instrument → stage check.";
        }
        catch (Exception ex)
        {
            concertRetryPhase = ConcertRetryPhase.Idle;
            ConcertFail("Single-Bard retry failed: " + ex.Message);
        }
    }
}
