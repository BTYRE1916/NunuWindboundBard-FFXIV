using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private string gameEnsembleStatus = "Native Ensemble mode: load MIDI, ARM, choose tracks; conductor starts the game's Ready Check.";
    private string expectedPartyMidiHash = "";
    private readonly GameEnsembleStartGate gameStartGate = new();
    // Remember a recently expanded 37-key UI: the native Ready Check dialog
    // can momentarily hide the Performance addon while the Bard is still performing.
    private long lastExpandedPerformanceQpc;

    /// <summary>One deterministic native ensemble path for both same-PC and
    /// different-PC parties. No UTC/party-chat START signal is involved.</summary>
    private void DrawGameEnsembleControls(EnsembleRole role)
    {
        bool game = cfg.GameEnsembleEnabled;
        if (ImGui.Checkbox(Loc.T("Use FFXIV native Ensemble (MidiBard-style; same PC or cross-PC)"), ref game))
        {
            CancelOneClickInstrumentPreparation();
            cfg.GameEnsembleEnabled = game;
            if (!game) CancelNativeLeaderSync("Game Ensemble mode disabled.");
            cfg.GameEnsembleOneClick = false; // Retire unreliable timed party-chat START.
            if (game)
            {
                plugin.Network.Stop();
                cfg.MultiDeviceEnabled = false;
                CancelPendingInstrumentStart();
                if (plugin.Player.IsPlaying) plugin.Player.Stop();
                gameEnsembleStatus = "Native mode selected. Prepare instruments and MIDI parts; conductor starts the FFXIV Ready Check.";
            }
            else gameStartGate.Cancel("Native Ensemble mode disabled.");
            waitingForEnsembleCue = false;
            cfg.Save();
        }
        if (!game) return;

        ImGui.TextColored(plugin.GameEnsemble.Available
            ? new Vector4(.43f, .95f, .69f, 1f)
            : new Vector4(1f, .62f, .42f, 1f),
            Loc.T(plugin.GameEnsemble.Available ? "FFXIV native Ensemble START listener: ONLINE"
                : "Native Ensemble listener unavailable (game patch / signature mismatch)"));

        bool autoAccept = cfg.NativeEnsembleAutoAccept;
        if (ImGui.Checkbox(Loc.T("Automatically accept FFXIV Ensemble Ready Check on this Bard"), ref autoAccept))
        {
            cfg.NativeEnsembleAutoAccept = autoAccept;
            cfg.Save();
        }
        ImGui.TextWrapped(Loc.T("Auto-Accept is ON by default for every new installation, once a Bard selects " +
            "Follower or Conductor and ARMs output. The leader can auto-load each player's matching MIDI, " +
            "set instruments, and start everyone with the game's native Ready Check. " +
            "No Tailscale or separate party-chat PLAY timer."));
        DrawNativeLeaderSync(role);

        if (role == EnsembleRole.Conductor)
        {
            if (ImGui.Button(Loc.T("START ENSEMBLE — FFXIV Ready Check"))) CueEnsemble();
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Check conductor readiness")))
            {
                var preflight = GetGameEnsembleReadiness(out var why);
                gameEnsembleStatus = $"{preflight}: {why}";
            }
        }
        else if (role == EnsembleRole.Follower)
        {
            ImGui.TextColored(new Vector4(.55f, .95f, .82f, 1f),
                Loc.T("FOLLOWER: No Play or Confirm click. Wait for FFXIV's native Ready Check."));
        }

        if (ImGui.TreeNode(Loc.T("Native Ensemble diagnostics / manual fallback")))
        {
            ImGui.TextWrapped(Loc.T(plugin.GameEnsemble.Diagnostics));
            ImGui.TextWrapped(Loc.T("Local START gate: " + gameStartGate.LastOutcome));
            if (ImGui.SmallButton(Loc.T("Manually confirm my Ready Check")))
                plugin.GameEnsemble.TryConfirmReadyCheck(role == EnsembleRole.Follower, out gameEnsembleStatus);
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Copy native diagnostics")))
                ImGui.SetClipboardText($"Role={role}; ARMed={outputArmed}; full Performance={plugin.IsGameOutputSafe()}\n" +
                    plugin.GameEnsemble.Diagnostics + "\n" + gameStartGate.LastOutcome + "\n" +
                    plugin.PartyChat.Diagnostics + "\n" + gameEnsembleStatus);
            ImGui.TreePop();
        }
        ImGui.TextWrapped(Loc.T(gameEnsembleStatus));
    }

    /// <summary>Called on Framework.Update before checking for the native
    /// FFXIV START. Only enrolled, ready and ARMed clients auto-confirm.</summary>
    public void PollNativeReadyChecks()
    {
        // During the game's modal Ready Check, PerformanceModeWide can
        // temporarily disappear. MidiBard auto-confirms based on being IN
        // Performance mode, not on the expanded keyboard window being visible.
        // Requiring the full UI at this moment was one of our earlier mistakes.
        long now = Stopwatch.GetTimestamp();
        if (plugin.IsExpandedKeyboard) lastExpandedPerformanceQpc = now;
        bool recentlyWide = lastExpandedPerformanceQpc != 0 &&
            (now - lastExpandedPerformanceQpc) < Stopwatch.Frequency * 10;
        bool nativeSyncReady = NativeLeaderSyncIsReady(out _);
        bool participating = recentlyWide && cfg.GameEnsembleEnabled && !cfg.GameEnsembleOneClick &&
            cfg.NativeEnsembleAutoAccept && (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo &&
            song is not null && arranged is not null && nativeSyncReady &&
            (arranged.Notes.Count == 0 || outputArmed) &&
            !plugin.Player.IsPlaying && !plugin.Player.IsStopPending && !plugin.Player.IsRewindPending;
        plugin.GameEnsemble.PumpReadyChecks(participating, plugin.IsPerforming);
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor &&
            (plugin.GameEnsemble.IsPreparing ||
             plugin.GameEnsemble.ReadyCheckStatus.StartsWith("Conductor confirmed", StringComparison.Ordinal)))
            gameEnsembleStatus = plugin.GameEnsemble.ReadyCheckStatus;
    }

    private void BroadcastPartySong()
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor ||
            !cfg.GameEnsembleEnabled || !cfg.GamePartyChatEnabled)
        { gameEnsembleStatus = "Enable Game Ensemble and party chat sync as Conductor first."; return; }
        if (activePlaylist < 0 || activePlaylist >= cfg.Playlist.Count ||
            string.IsNullOrWhiteSpace(loaded))
        { gameEnsembleStatus = "Choose a song from the current Ensemble playlist first."; return; }
        try
        {
            if (!string.Equals(Path.GetFullPath(loaded), Path.GetFullPath(cfg.Playlist[activePlaylist]),
                StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Choose a song from the active Ensemble playlist first.");
            string cmd = GamePartyCueProtocol.Song(activePlaylist, loaded);
            plugin.PartyChat.TryBroadcast(cmd, out gameEnsembleStatus);
        }
        catch (Exception ex) { gameEnsembleStatus = "Could not share song selection: " + ex.Message; }
    }

    private void BroadcastPartyStop()
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor) return;
        if (NativeSyncEnabled)
        {
            StopPlayback(); // sends STOP+UNEQUIP to all, including conductor
            gameEnsembleStatus = nativeStopStatus;
        }
        else plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.Stop, out gameEnsembleStatus);
    }

    /// <summary>Called on framework thread even while plugin UI is closed or player has alt-tabbed.</summary>
    public void PollGamePartyChat()
    {
        for (int i = 0; i < 12 && plugin.PartyChat.TryDequeue(out var envelope); i++)
        {
            var cue = envelope.Cue;
            if (!cfg.GameEnsembleEnabled || !(cfg.GamePartyChatEnabled || cfg.NativeLeaderSyncEnabled)) continue;
            if (cue.Kind is GamePartyCueKind.Ready or GamePartyCueKind.Failed or
                GamePartyCueKind.NativeFileReady or GamePartyCueKind.NativeFileFailed or
                GamePartyCueKind.NativeTracksReady or GamePartyCueKind.NativeTracksFailed or
                GamePartyCueKind.StageReport or GamePartyCueKind.NativeArmReady or GamePartyCueKind.NativeArmFailed or
                GamePartyCueKind.FormationPosition or GamePartyCueKind.FormationArrived or GamePartyCueKind.FormationFailed)
            {
                if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
                {
                    if (cue.Kind is GamePartyCueKind.FormationPosition or GamePartyCueKind.FormationArrived or GamePartyCueKind.FormationFailed)
                    {
                        HandleStageReply(cue, envelope.SenderContentId);
                        continue;
                    }
                    OnNativeLeaderReply(cue, envelope.SenderContentId);
                    OnNativeArmReply(cue, envelope.SenderContentId);
                    if (cue.Kind == GamePartyCueKind.StageReport)
                        ReceiveConcertStageReport(cue, envelope.SenderContentId);
                    OnPartyInstrumentReply(cue, envelope.SenderContentId); // legacy safe no-op in native mode
                }
                continue;
            }
            if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower) continue;
            if (cue.Kind == GamePartyCueKind.ShowmasterStart)
            { ReceiveShowmasterStart(cue); continue; }
            if (cue.Kind == GamePartyCueKind.ShowmasterStop)
            { ReceiveShowmasterStop(cue); continue; }
            if (cue.Kind == GamePartyCueKind.FormationCapture)
            { ReceiveStageCapture(cue); continue; }
            if (cue.Kind == GamePartyCueKind.FormationMove)
            { ReceiveStageMove(cue); continue; }
            if (cue.Kind == GamePartyCueKind.FormationStop)
            { ReceiveStageStop(cue); continue; }
            if (cue.Kind == GamePartyCueKind.NativeArmAll)
            {
                ReceiveNativePartyArm(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.StageProbe)
            {
                ReplyConcertStageStatus(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.NativeFile)
            {
                ReceiveNativeFile(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.NativeSetInstruments)
            {
                ReceiveNativeInstruments(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.NativeSetTracks)
            {
                ReceiveNativeTracks(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.NativeStopUnequip)
            {
                ReceiveNativeStopAndUnequip(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.Prepare)
            {
                ReceivePartyInstrumentPreparation(cue);
                continue;
            }
            if (cue.Kind == GamePartyCueKind.Stop)
            {
                CancelOneClickInstrumentPreparation();
                plugin.Player.Stop();
                songFlow.Cancel();
                gameEnsembleStatus = "FFXIV party leader requested STOP.";
                oneClickStatus = "Conductor STOP received.";
                continue;
            }
            if (cue.Kind == GamePartyCueKind.Start)
            {
                // Ignore obsolete chat-timed PLAY. Only FFXIV native START may begin music.
                continue;
            }
            if (cue.Kind != GamePartyCueKind.Song || !cfg.GamePartyChatEnabled) continue;
            if (cue.PlaylistIndex < 0 || cue.PlaylistIndex >= cfg.Playlist.Count)
            {
                gameEnsembleStatus = $"Party leader selected song {cue.PlaylistIndex + 1}, but my Ensemble playlist only has {cfg.Playlist.Count} entries.";
                continue;
            }
            string path = cfg.Playlist[cue.PlaylistIndex];
            if (!File.Exists(path))
            { gameEnsembleStatus = $"Song {cue.PlaylistIndex + 1} is missing locally: {Path.GetFileName(path)}"; continue; }
            try
            {
                string localHash = GamePartyCueProtocol.GetHash(path);
                if (!string.Equals(localHash, cue.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    gameEnsembleStatus = $"Song {cue.PlaylistIndex + 1} differs from conductor's MIDI (SHA-256 prefix mismatch). Sync the files first.";
                    continue;
                }
                expectedPartyMidiHash = cue.Hash;
                LoadFromUser(path); // transition queue handles any in-flight note releases
                gameEnsembleStatus = $"Received matching song {cue.PlaylistIndex + 1}: {Path.GetFileName(path)}. " +
                    (cfg.GameEnsembleOneClick ? "ARM and wait for conductor PLAY." : "Check tracks and confirm FFXIV Ready Check.");
            }
            catch (Exception ex) { gameEnsembleStatus = "Could not load party song: " + ex.Message; }
        }
    }

    /// <summary>Unlike our local/TCP bus, FFXIV Game Ensemble itself is the cue.
    /// Both roles are permitted to start when the server tells their client to.
    /// Only brief UI transitions are retried; missing songs/ARMs are not silently hidden.</summary>
    private GameEnsembleReadiness GetGameEnsembleReadiness(out string reason)
    {
        if (!cfg.GameEnsembleEnabled || (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo)
        { reason = "Enable Game Ensemble and choose Conductor or Follower."; return GameEnsembleReadiness.PermanentBlock; }
        if (!cfg.GameEnsembleOneClick && !plugin.GameEnsemble.Available)
        { reason = plugin.GameEnsemble.Status; return GameEnsembleReadiness.PermanentBlock; }
        if (song is null || arranged is null || string.IsNullOrWhiteSpace(loaded))
        { reason = "Load the same prepared MIDI on both clients before accepting Ready Check."; return GameEnsembleReadiness.PermanentBlock; }
        if (!NativeLeaderSyncIsReady(out reason))
            return GameEnsembleReadiness.PermanentBlock;
        // Native ensemble starts from the game's server event, not old party
        // playlist chat state. A previously received song hash cannot block it.
        if (plugin.Player.IsPlaying && !plugin.Player.IsStopPending)
        { reason = "A song is already playing. Use STOP before triggering another Ready Check."; return GameEnsembleReadiness.PermanentBlock; }
        if (songFlow.HasPending || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
        { reason = "the previous song's stop/reset/next-song transition"; return GameEnsembleReadiness.TemporaryBlock; }
        if (arranged.Notes.Count == 0)
        { reason = "No tracks assigned locally. This Bard will conduct silently."; return GameEnsembleReadiness.Silent; }
        if (!outputArmed)
        { reason = "Enable ARM on Songs & Playback for every Bard with assigned notes."; return GameEnsembleReadiness.PermanentBlock; }
        // In native mode the instrument that is ACTUALLY equipped is what the
        // game will play. Prefer that to a stale remembered instrument preset.
        // Players may prepare a different tone/instrument before the Ready Check.
        if (!plugin.IsGameOutputSafe())
        { reason = "the expanded 37-note Performance keyboard to become ready"; return GameEnsembleReadiness.TemporaryBlock; }
        reason = $"{Path.GetFileName(loaded)} — {arranged.Notes.Count:N0} prepared notes; {lanes.Count(l => l.Include)} part(s); ARMed and Performance ready.";
        return GameEnsembleReadiness.Ready;
    }

    /// <summary>Called on the FFXIV framework thread even while UI is hidden or user has alt-tabbed.</summary>
    public void PollGameEnsemble()
    {
        // Only the game's native START can launch a native ensemble performance.
        if (plugin.GameEnsemble.TryConsumeStart(out var nativeSignal))
        {
            if (plugin.Player.IsPlaying)
                plugin.Player.Stop();
            gameStartGate.Capture(nativeSignal);
            gameEnsembleStatus = gameStartGate.LastOutcome;
        }
        if (!gameStartGate.HasPending) return;
        GameEnsembleReadiness readiness;
        string reason;
        try { readiness = GetGameEnsembleReadiness(out reason); }
        catch (Exception ex)
        {
            readiness = GameEnsembleReadiness.PermanentBlock;
            reason = "Preflight error: " + ex.Message;
        }
        var decision = gameStartGate.Evaluate(readiness, reason, Stopwatch.GetTimestamp(), Stopwatch.Frequency);
        if (decision.Kind == GameEnsembleDecisionKind.Waiting)
        {
            gameEnsembleStatus = decision.Reason;
            return;
        }
        if (decision.Kind == GameEnsembleDecisionKind.Failed || decision.Kind == GameEnsembleDecisionKind.Silent)
        {
            gameEnsembleStatus = decision.Reason;
            if (decision.Kind == GameEnsembleDecisionKind.Silent)
                ConcertOnNativeStarted(); // silent conductors also know when setlist music begins
            return;
        }
        if (decision.Kind != GameEnsembleDecisionKind.Play) return;

        try
        {
            // Anchor to this CLIENT's native event timestamp. QPC is never sent
            // across the network or compared between the two PCs.
            StartSongCore(decision.StartQpc);
            ConcertOnNativeStarted();
            gameEnsembleStatus = decision.Reason + " Native playback started: " + Path.GetFileName(loaded) + ".";
            ensembleStatus = gameEnsembleStatus;
        }
        catch (Exception ex)
        {
            gameEnsembleStatus = "FFXIV Ensemble START received but local MIDI start failed: " + ex.Message;
        }
    }

}
