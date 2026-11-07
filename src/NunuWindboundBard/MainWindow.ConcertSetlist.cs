using System;
using System.Diagnostics;
using System.IO;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private bool concertNativeSongStarted;
    private string concertNativeSongPath = "";
    private long concertNativeStartQpc;
    private bool concertNativeHadParts;
    private double concertNativeSongDurationMs;
    private string concertSetlistStatus = "The conductor remains in control of every FFXIV Ensemble START.";

    private ConcertSongCue ConcertCurrentCue(string path)
    {
        string key = ConcertSetlist.EntryKey(SavedPlaylistLibrary.GetActive(cfg).Id, path);
        if (!cfg.ConcertSongCuesByKey.TryGetValue(key, out var cue) || cue is null)
            cfg.ConcertSongCuesByKey[key] = cue = new ConcertSongCue();
        return cue;
    }

    private void ConcertOnNativeStarted()
    {
        if (!ConcertIsConductor || song is null || string.IsNullOrWhiteSpace(loaded)) return;
        concertNativeSongPath = loaded;
        concertNativeSongStarted = true;
        concertNativeStartQpc = Stopwatch.GetTimestamp();
        concertNativeHadParts = arranged?.Notes.Count > 0;
        concertNativeSongDurationMs = song.DurationMs / Math.Max(.1f, cfg.PlaybackSpeed);
        concertSetlistStatus = "Ensemble is playing; the next song will be prepared after the music finishes.";
    }

    private void ConcertCancelSetlist(string message)
    {
        concertNativeSongStarted = false;
        concertNativeSongPath = "";
        concertSetlistStatus = message;
    }

    public void PollConcertSetlist()
    {
        if (!ConcertIsConductor || !concertNativeSongStarted) return;
        if (!cfg.ConcertAutoPrepareNext) return;
        if (song is null || string.IsNullOrWhiteSpace(concertNativeSongPath) ||
            !string.Equals(concertNativeSongPath, loaded, StringComparison.OrdinalIgnoreCase))
        { concertNativeSongStarted = false; return; }
        if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending)
            return;
        double elapsedMs = (Stopwatch.GetTimestamp() - concertNativeStartQpc) * 1000.0 / Stopwatch.Frequency;
        if (concertNativeHadParts && !plugin.Player.CompletedSuccessfully)
        { concertNativeSongStarted = false; return; } // manually stopped or failed, never advance
        var cue = ConcertCurrentCue(concertNativeSongPath);
        if (cue.HoldAfter)
        {
            concertNativeSongStarted = false;
            concertSetlistStatus = "Audience break: this song is marked HOLD. Press Prepare Next Song when ready.";
            return;
        }
        if (elapsedMs < concertNativeSongDurationMs + cue.BreakAfterSeconds * 1000.0)
            return;
        concertNativeSongStarted = false;
        ConcertPrepareNextSong();
    }

    private void ConcertPrepareNextSong()
    {
        try
        {
            if (!ConcertIsConductor || plugin.Player.IsPlaying || plugin.Player.IsStopPending ||
                plugin.Player.IsRewindPending || cfg.Playlist.Count == 0)
                throw new InvalidOperationException("Wait for the Ensemble performance to finish first.");
            string? next = ConcertSetlist.PickNext(GetActiveSmartPlaylist(), cfg.Playlist,
                activePlaylist, File.Exists);
            if (string.IsNullOrEmpty(next))
            {
                concertSetlistStatus = "End of concert setlist. No further playable MIDI found.";
                return;
            }
            // Load() automatically sends the leader's native MIDI-selection cue;
            // the existing Mono/Windows/Linux follower path finds the identical hash.
            if (!Load(next)) throw new InvalidOperationException("Could not load the next MIDI: " + status);
            SmartPlaylistManager.ConsumeQueueItem(GetActiveSmartPlaylist(), next);
            cfg.Save();
            ConcertPrepareOrchestra(alreadySentFile: true);
            concertSetlistStatus = "Preparing next piece: " + Path.GetFileNameWithoutExtension(next) +
                ". The conductor must still press native START ENSEMBLE.";
        }
        catch (Exception ex) { concertSetlistStatus = "Next song preparation stopped: " + ex.Message; }
    }

    private void DrawConcertSetlist()
    {
        if (!ConcertIsConductor) return;
        if (!ImGui.TreeNode(Loc.T("Concert Setlist Automation"))) return;
        bool auto = cfg.ConcertAutoPrepareNext;
        if (ImGui.Checkbox(Loc.T("Automatically prepare next Ensemble song (never auto-start)"), ref auto))
        {
            cfg.ConcertAutoPrepareNext = auto;
            cfg.Save();
        }
        ImGui.TextDisabled(Loc.T("MIDI, tracks and instruments are staged after the piece finishes. Press native START for every new song."));
        if (song is not null && !string.IsNullOrWhiteSpace(loaded))
        {
            var cue = ConcertCurrentCue(loaded);
            ImGui.TextUnformatted(Loc.T("Concert notes for this song"));
            string intro = cue.Announcement;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText(Loc.T("Introduction / audience announcement##concertIntro"), ref intro, 180))
            { cue.Announcement = intro; cfg.Save(); }
            int breakSeconds = cue.BreakAfterSeconds;
            if (ImGui.SliderInt(Loc.T("Break after song (seconds)"), ref breakSeconds, 0, 120))
            { cue.BreakAfterSeconds = breakSeconds; cfg.Save(); }
            bool hold = cue.HoldAfter;
            if (ImGui.Checkbox(Loc.T("HOLD after this song until I prepare next"), ref hold))
            { cue.HoldAfter = hold; cfg.Save(); }
            bool encore = cue.Encore;
            if (ImGui.Checkbox(Loc.T("Mark this song as an Encore"), ref encore))
            { cue.Encore = encore; cfg.Save(); }
            if (!string.IsNullOrWhiteSpace(cue.Announcement))
                ImGui.TextWrapped(Loc.T("Audience cue: ") + cue.Announcement);
        }
        ImGui.TextDisabled(Loc.T("Next: ") +
            SmartPlaylistManager.PeekNext(GetActiveSmartPlaylist(), cfg.Playlist, activePlaylist));
        if (ImGui.Button(Loc.T("PREPARE NEXT SONG NOW")))
        {
            concertNativeSongStarted = false;
            ConcertPrepareNextSong();
        }
        ImGui.TextWrapped(Loc.T(concertSetlistStatus));
        ImGui.TreePop();
    }
}
