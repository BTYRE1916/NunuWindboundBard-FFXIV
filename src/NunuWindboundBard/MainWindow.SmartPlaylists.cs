using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>Smart playlist UI. No MIDI callbacks or ensemble timing are changed here.</summary>
public sealed partial class MainWindow
{
    private SmartPlaylistState GetActiveSmartPlaylist()
    {
        cfg.SmartPlaylistsById ??= new();
        return SmartPlaylistManager.Get(cfg.SmartPlaylistsById, SavedPlaylistLibrary.GetActive(cfg).Id);
    }

    private void MovePlaylistSong(int from, int to)
    {
        try
        {
            SmartPlaylistManager.Move(cfg.Playlist, from, to);
            activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, loaded, StringComparison.OrdinalIgnoreCase));
            PersistActivePlaylist();
            playlistMessage = "Playlist order saved. No MIDI playback settings changed.";
        }
        catch (Exception ex) { playlistMessage = "Reorder failed: " + ex.Message; }
    }

    private void RemovePlaylistSong(int index)
    {
        if (index < 0 || index >= cfg.Playlist.Count) return;
        string path = cfg.Playlist[index];
        cfg.Playlist.RemoveAt(index);
        SmartPlaylistManager.PruneQueue(GetActiveSmartPlaylist(), cfg.Playlist);
        activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, loaded, StringComparison.OrdinalIgnoreCase));
        PersistActivePlaylist();
        playlistMessage = $"Removed {Path.GetFileName(path)} from this playlist. The MIDI on disk was NOT deleted.";
    }

    private void SetPlaylistFavorite(string path)
    {
        SmartPlaylistManager.ToggleFavorite(GetActiveSmartPlaylist(), path);
        cfg.Save();
    }

    private void QueuePlaylistSong(string path, bool first)
    {
        try
        {
            SmartPlaylistManager.AddPlayNext(GetActiveSmartPlaylist(), cfg.Playlist, path, first);
            cfg.Save();
            playlistMessage = $"Queued {Path.GetFileName(path)} {(first ? "to play next" : "after existing requests")}.";
        }
        catch (Exception ex) { playlistMessage = "Queue error: " + ex.Message; }
    }

    /// <summary>ImGui's native drag payload; uses indices, never serialises paths or modifies files.</summary>
    private unsafe void DrawSmartPlaylistControls()
    {
        var smart = GetActiveSmartPlaylist();
        ImGui.TextColored(new Vector4(.97f, .60f, .85f, 1f), Loc.T($"SMART PLAYLIST MANAGER — {cfg.Playlist.Count} songs"));
        ImGui.TextDisabled(Loc.T("Drag a song onto another to reorder it. Right-click for favourites, Play Next and sorting."));
        if (cfg.EnsembleMode != (int)EnsembleRole.Solo)
            ImGui.TextDisabled(Loc.T("In Ensemble mode the queue is for planning; performers still use the conductor's cue."));

        bool shuffle = smart.Shuffle;
        if (ImGui.Checkbox(Loc.T("Shuffle##smart"), ref shuffle))
        {
            smart.Shuffle = shuffle;
            SmartPlaylistManager.ResetShuffle(smart);
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.BeginCombo(Loc.T("Repeat##smart"), Loc.T(smart.Repeat.ToString())))
        {
            foreach (PlaylistRepeatMode mode in Enum.GetValues<PlaylistRepeatMode>())
            {
                if (ImGui.Selectable(Loc.T(mode.ToString()), smart.Repeat == mode))
                {
                    smart.Repeat = mode;
                    SmartPlaylistManager.ResetShuffle(smart);
                    cfg.Save();
                }
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Play Next requests take priority; Repeat One is bypassed by manual Next."));

        ImGui.SetNextItemWidth(-1);
        ImGui.InputText(Loc.T("Search names, paths or tags##SmartSearch"), ref playlistFilter, 256);
        ImGui.SetNextItemWidth(220);
        ImGui.InputText(Loc.T("Filter tags##SmartTagFilter"), ref playlistTagFilter, 80);
        ImGui.SameLine();
        ImGui.Checkbox(Loc.T("Favourites only"), ref playlistFavoritesOnly);

        int matches = 0;
        ImGui.BeginChild("##SmartPlaylistScroll", new Vector2(0, 250), true);
        for (int i = 0; i < cfg.Playlist.Count; i++)
        {
            string path = cfg.Playlist[i];
            if (!SmartPlaylistManager.Matches(smart, path, playlistFilter, playlistTagFilter, playlistFavoritesOnly))
                continue;
            matches++;
            bool fav = SmartPlaylistManager.IsFavorite(smart, path);
            string label = $"{(fav ? "★" : "·")} {i + 1:0000}  {Path.GetFileName(path)}##smartSong{i}";
            if (ImGui.Selectable(Loc.T(label), activePlaylist == i))
                SelectPlaylistSong(path);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T(path + (smart.Tags.TryGetValue(path, out var ts) ? "\nTags: " + string.Join(", ", ts) : "")));

            if (ImGui.BeginPopupContextItem($"SmartContext{i}"))
            {
                if (ImGui.MenuItem(Loc.T(fav ? "Unfavourite" : "Add to favourites"))) SetPlaylistFavorite(path);
                if (ImGui.MenuItem(Loc.T("Play NEXT (top of queue)"))) QueuePlaylistSong(path, true);
                if (ImGui.MenuItem(Loc.T("Add to end of Play Next queue"))) QueuePlaylistSong(path, false);
                if (ImGui.MenuItem(Loc.T("Move one place up"), "", false, i > 0)) MovePlaylistSong(i, i - 1);
                if (ImGui.MenuItem(Loc.T("Move one place down"), "", false, i + 1 < cfg.Playlist.Count)) MovePlaylistSong(i, i + 1);
                if (ImGui.MenuItem(Loc.T("Remove from playlist (keep file)"))) RemovePlaylistSong(i);
                ImGui.EndPopup();
            }
            if (ImGui.BeginDragDropSource())
            {
                int from = i;
                ImGui.SetDragDropPayload("WINDBOUND_PLAYLIST", BitConverter.GetBytes(from), ImGuiCond.Once);
                ImGui.TextUnformatted(Loc.T($"Move: {Path.GetFileName(path)}"));
                ImGui.EndDragDropSource();
            }
            if (ImGui.BeginDragDropTarget())
            {
                var payload = ImGui.AcceptDragDropPayload("WINDBOUND_PLAYLIST");
                if (!payload.IsNull && payload.IsDelivery() && payload.DataSize == sizeof(int))
                {
                    int from = *(int*)payload.Data;
                    if (from != i) MovePlaylistSong(from, i);
                }
                ImGui.EndDragDropTarget();
            }
        }
        ImGui.EndChild();
        ImGui.TextDisabled(Loc.T($"Showing {matches} of {cfg.Playlist.Count} songs • saved order unchanged by Play Next."));

        if (activePlaylist >= 0 && activePlaylist < cfg.Playlist.Count)
        {
            string selected = cfg.Playlist[activePlaylist];
            if (!string.Equals(playlistTagsTarget, selected, StringComparison.OrdinalIgnoreCase))
            {
                playlistTagsTarget = selected;
                playlistTagsInput = string.Join(", ", SmartPlaylistManager.GetTags(smart, selected));
            }
            ImGui.TextUnformatted(Loc.T($"Selected: {Path.GetFileName(selected)}"));
            if (ImGui.SmallButton(Loc.T(SmartPlaylistManager.IsFavorite(smart, selected) ? "★ Unfavourite" : "☆ Favourite")))
                SetPlaylistFavorite(selected);
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Play Next"))) QueuePlaylistSong(selected, true);
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Move Up"))) MovePlaylistSong(activePlaylist, Math.Max(0, activePlaylist - 1));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Move Down"))) MovePlaylistSong(activePlaylist, Math.Min(cfg.Playlist.Count - 1, activePlaylist + 1));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Remove selected"))) RemovePlaylistSong(activePlaylist);
            ImGui.SetNextItemWidth(370);
            ImGui.InputText(Loc.T("Tags (comma-separated)##tags"), ref playlistTagsInput, 256);
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Save tags")))
            {
                SmartPlaylistManager.SetTags(smart, selected, playlistTagsInput);
                cfg.Save();
                playlistMessage = "Tags saved for this playlist entry.";
            }
        }

        if (ImGui.TreeNode(Loc.T($"Play Next queue ({smart.PlayNext.Count})##SmartQueue")))
        {
            ImGui.TextDisabled(Loc.T("The queue is separate from your saved playlist and survives restarts."));
            string? toRemove = null;
            int index = -1;
            foreach (string path in smart.PlayNext.ToArray())
            {
                index++;
                ImGui.TextUnformatted(Loc.T($"{index + 1:00}. {Path.GetFileName(path)}"));
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.T($"Remove##Queue{index}"))) { toRemove = path; break; }
            }
            if (toRemove != null) { SmartPlaylistManager.ConsumeQueueItem(smart, toRemove); cfg.Save(); }
            if (smart.PlayNext.Count > 0 && ImGui.SmallButton(Loc.T("Clear Play Next queue")))
            {
                smart.PlayNext.Clear(); cfg.Save();
            }
            ImGui.TreePop();
        }
        if (ImGui.TreeNode(Loc.T($"Recently played ({smart.Recent.Count})##SmartRecent")))
        {
            foreach (var entry in smart.Recent.Take(20))
            {
                if (ImGui.SmallButton(Loc.T($"Load##recent{entry.PlayedUtc.Ticks}"))) SelectPlaylistSong(entry.Path);
                ImGui.SameLine();
                ImGui.TextUnformatted(Loc.T($"{entry.PlayedUtc.ToLocalTime():g} — {Path.GetFileName(entry.Path)}"));
            }
            ImGui.TreePop();
        }

        if (ImGui.SmallButton(Loc.T("Scan for missing files")))
        {
            missingFileCandidates = cfg.Playlist.Where(p => string.IsNullOrWhiteSpace(p) || !File.Exists(p)).ToList();
            playlistMessage = missingFileCandidates.Count == 0 ? "All playlist MIDI paths exist." :
                $"Found {missingFileCandidates.Count} missing song(s). Review before removing entries.";
        }
        if (missingFileCandidates.Count > 0)
        {
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T($"Review removing {missingFileCandidates.Count} entries...")))
                ImGui.OpenPopup("##ConfirmMissingRemoval");
        }
        if (ImGui.BeginPopup("##ConfirmMissingRemoval"))
        {
            ImGui.TextWrapped(Loc.T($"Remove {missingFileCandidates.Count} missing entries from this playlist?\nNo MIDI files will be deleted from disk."));
            foreach (var name in missingFileCandidates.Take(8)) ImGui.TextDisabled(Loc.T(Path.GetFileName(name)));
            if (missingFileCandidates.Count > 8) ImGui.TextDisabled(Loc.T("...and more"));
            if (ImGui.Button(Loc.T("Remove missing playlist entries")))
            {
                int removed = cfg.Playlist.RemoveAll(p => missingFileCandidates.Contains(p, StringComparer.OrdinalIgnoreCase) && !File.Exists(p));
                SmartPlaylistManager.PruneQueue(smart, cfg.Playlist);
                activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, loaded, StringComparison.OrdinalIgnoreCase));
                missingFileCandidates.Clear();
                PersistActivePlaylist();
                playlistMessage = $"Removed {removed} missing playlist entries; no files were deleted.";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Cancel"))) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }
    }
}
