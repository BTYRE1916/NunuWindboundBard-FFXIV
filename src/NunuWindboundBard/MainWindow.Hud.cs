using System;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Small, UI-only bridge for the performance HUD. No duplicated MIDI scheduler or
/// direct native note calls: all actions reuse the proven MainWindow paths.
/// </summary>
public sealed partial class MainWindow
{
    internal bool HudOutputArmed => outputArmed;
    internal bool HudLiveMidiActive => HudIsSolo && cfg.LiveMidiInputEnabled && plugin.LiveMidi.Connected &&
        outputArmed && !plugin.Player.IsPlaying;
    internal bool HudHasMidi => !HudLiveMidiActive && song is not null && arranged is not null;
    internal string HudSongName => HudLiveMidiActive
            ? "Live MIDI: " + plugin.LiveMidi.ConnectedName
            : string.IsNullOrEmpty(loaded) ? "No MIDI loaded" : Path.GetFileNameWithoutExtension(loaded);
    internal string HudPlaylistName => SavedPlaylistLibrary.GetActive(cfg).Name;
    internal string HudRole => ((EnsembleRole)cfg.EnsembleMode).ToString();
    internal int HudPlaylistIndex => activePlaylist;
    internal int HudPlaylistCount => cfg.Playlist.Count;
    internal int HudTrackCount => lanes.Count(l => l.Include);
    internal double HudDurationMs => HudLiveMidiActive ? 0 : song?.DurationMs ?? 0;
    internal string HudInstrumentName => plugin.Instruments.CurrentName;
    internal string HudNextSongName => cfg.Playlist.Count == 0
        ? "No songs in playlist"
        : SmartPlaylistManager.PeekNext(GetActiveSmartPlaylist(), cfg.Playlist, activePlaylist);

    internal int HudReadyFollowers => preparingConductor?.ReadyFollowers.Count ?? 0;
    internal int HudExpectedFollowers => preparingConductor?.ExpectedFollowers.Count ?? 0;

    internal string HudPlaybackStatus
    {
        get
        {
            if (plugin.Player.IsEnsemblePlayback && plugin.Player.EnsembleCountInSeconds > 0)
                return $"Ensemble starts in {plugin.Player.EnsembleCountInSeconds:0.0}s";
            if (plugin.Player.IsPlaying)
                return plugin.Player.IsPaused ? "Paused" : outputArmed ? "Playing in FFXIV" : "Preview playing";
            if (HudIsSolo && cfg.LiveMidiInputEnabled && plugin.LiveMidi.Connected && outputArmed &&
                plugin.IsGameOutputSafe()) return "LIVE MIDI Piano • Ready";
            if (plugin.Player.IsStopPending || plugin.Player.IsRewindPending) return "Stopping / rewinding...";
            if (preparingConductor is not null || preparingFollower is not null)
                return "Preparing ensemble instruments...";
            if (pendingInstrumentSong is not null || plugin.Instruments.InProgress)
                return "Switching instrument...";
            if (song is null) return "Load a MIDI in the full player";
            if (!outputArmed) return "Output not ARMed";
            if (!plugin.IsGameOutputSafe()) return "Open expanded 37-note Performance";
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
                return cfg.GameEnsembleEnabled
                    ? "Auto-ready for FFXIV Ensemble" : "Waiting for conductor";
            return "Ready";
        }
    }

    internal string HudDetailStatus => (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo
        ? status : ensembleStatus;

    internal string HudPlayCaption => (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor
        ? cfg.GameEnsembleEnabled ? "START ENSEMBLE" : "PLAY ENSEMBLE" : "PLAY";

    internal bool HudIsFollower => (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower;
    internal bool HudIsSolo => (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo;

    internal void HudSetOutputArmed(bool armed)
    {
        if (outputArmed == armed) return;
        outputArmed = armed;
        if (!armed)
        {
            plugin.LiveMidi.Panic();
            if (preparingConductor is not null || preparingFollower is not null)
            {
                // If an ensemble is preparing, propagate cancellation rather than
                // leaving the rest of the party expecting this client to play.
                StopPlayback();
            }
            else
            {
                // Match the existing Songs & Playback ARM checkbox semantics.
                CancelPendingInstrumentStart();
                if (plugin.Player.IsPlaying) plugin.Player.Stop();
            }
        }
    }

    internal void HudStartOrCue()
    {
        // Followers must not accidentally start their own playback and ruin sync.
        if (HudIsFollower)
        {
            status = "Follower: wait for your conductor's ensemble cue.";
            return;
        }
        if (HudIsSolo) StartSong();
        else CueEnsemble();  // Native game Ready Check; legacy local/TCP only when explicitly selected.
    }

    internal void HudPauseResume()
    {
        if (HudIsSolo) plugin.Player.TogglePause();
        else status = "Ensembles: conductor should STOP, then cue all Bards again to keep sync.";
    }

    internal void HudNext()
    {
        if (HudIsSolo) NextSong();
        else ensembleStatus = "Ensemble: conductor must select the next MIDI and cue the party together.";
    }

    internal void HudStop() => StopPlayback();
    internal void HudResetToStart() => ResetCurrentMidiToStart();
}
