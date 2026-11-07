using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Named, exportable concert snapshots. They are LOCAL configuration backups,
/// never network messages or automated native FFXIV START actions. Restoration
/// creates another saved playlist rather than overwriting an existing profile.
/// </summary>
public sealed partial class MainWindow
{
    private string snapshotNameInput = "My Concert";
    private string selectedSnapshotId = "";
    private string snapshotStatus = "Save a concert setup, then restore it whenever you need it.";
    private bool suppressSnapshotSync;
    private bool snapshotRestorePending;
    private bool snapshotDeletePending;

    private ConcertSnapshot CaptureConcertSnapshot(string name)
    {
        name = (name ?? "").Trim();
        var profile = SavedPlaylistLibrary.GetActive(cfg);
        var files = cfg.Playlist.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var snapshot = new ConcertSnapshot
        {
            Name = name,
            Role = cfg.EnsembleMode,
            PlaylistName = profile.Name,
            PlaylistFiles = files,
            SelectedMidiPath = files.FirstOrDefault(path =>
                string.Equals(path, loaded, StringComparison.OrdinalIgnoreCase)) ?? "",
            SmartPlaylist = ConcertSnapshotFormat.Copy(GetActiveSmartPlaylist()),
            Options = new ConcertSnapshotOptions
            {
                PlaybackSpeed = cfg.PlaybackSpeed,
                GlobalTranspose = cfg.GlobalTranspose,
                RepairOctaves = cfg.RepairOctaves,
                ChordStrategy = cfg.ChordStrategy,
                MinimumGapMs = cfg.MinimumGapMs,
                ArpeggioSpacingMs = cfg.ArpeggioSpacingMs,
                KeyHoldMs = cfg.KeyHoldMs,
                AutoNext = cfg.AutoNext,
                AutoDetectMidiTrackInstruments = cfg.AutoDetectMidiTrackInstruments,
                AutoReadMidiTrackOctaves = cfg.AutoReadMidiTrackOctaves,
                AutoSwitchInstrumentBeforePlay = cfg.AutoSwitchInstrumentBeforePlay,
                GuitarToneOverridesEnabled = cfg.GuitarToneOverridesEnabled,
                KeepPartyAssignmentsAcrossSongs = cfg.KeepPartyAssignmentsAcrossSongs,
                GameEnsembleEnabled = cfg.GameEnsembleEnabled,
                NativeLeaderSyncEnabled = cfg.NativeLeaderSyncEnabled,
                NativeEnsembleAutoAccept = cfg.NativeEnsembleAutoAccept,
                FollowConductorAssignments = cfg.FollowConductorAssignments,
                ConcertAutoPrepareNext = cfg.ConcertAutoPrepareNext
            }
        };
        if (cfg.EnsembleLineupsByPlaylistId.TryGetValue(profile.Id, out var lineup) && lineup != null)
            snapshot.Lineup = ConcertSnapshotFormat.Copy(lineup);
        foreach (string path in files)
        {
            var one = new ConcertSnapshotSong();
            if (cfg.InstrumentsByFile.TryGetValue(path, out int instrument)) one.Instrument = instrument;
            if (cfg.PartyInstrumentsByFile.TryGetValue(path, out var partyInstruments))
                one.PartyInstruments = new(partyInstruments, StringComparer.OrdinalIgnoreCase);
            if (cfg.PartyAssignmentsByFile.TryGetValue(path, out var parts))
                one.PartyTracks = new(parts, StringComparer.OrdinalIgnoreCase);
            if (cfg.OctaveShiftsByFile.TryGetValue(path, out var octaves))
                one.Octaves = new(octaves, StringComparer.OrdinalIgnoreCase);
            if (cfg.ManualOctaveLaneOverridesByFile.TryGetValue(path, out var manual))
                one.ManualOctaveOverrides = manual.ToList();
            if (cfg.EnsemblePartsByFile.TryGetValue(path, out var lanesSaved))
                one.EnabledLanes = lanesSaved.ToList();
            if (cfg.GuitarTonesByFile.TryGetValue(path, out var tones))
                one.GuitarTones = new(tones, StringComparer.OrdinalIgnoreCase);
            if (cfg.SoulweaverPresetsByFile.TryGetValue(path, out var soulweaver) && soulweaver != null)
                one.Soulweaver = ConcertSnapshotFormat.Copy(soulweaver);
            if (cfg.ConcertSongCuesByKey.TryGetValue(ConcertSetlist.EntryKey(profile.Id, path), out var cue) && cue != null)
                one.Cue = ConcertSnapshotFormat.Copy(cue);
            snapshot.Songs[path] = one;
        }
        // Snapshot is immutable from this point unless explicitly replaced by the user.
        ConcertSnapshotFormat.Validate(snapshot);
        return snapshot;
    }

    private void SaveConcertSnapshot()
    {
        try
        {
            cfg.ConcertSnapshots ??= new();
            if (cfg.ConcertSnapshots.Count >= ConcertSnapshotFormat.MaxSnapshots)
                throw new InvalidOperationException("Snapshot library is full (30). Export and delete an old snapshot first.");
            var snap = CaptureConcertSnapshot(snapshotNameInput);
            cfg.ConcertSnapshots.Add(snap);
            selectedSnapshotId = snap.Id;
            cfg.Save();
            snapshotStatus = $"Saved '{snap.Name}' — {snap.PlaylistFiles.Count} MIDI(s), " +
                $"{snap.Lineup?.PerformerIdsBySlot.Count ?? 0} performer slots. Safe to export as JSON.";
        }
        catch (Exception ex) { snapshotStatus = "Could not save snapshot: " + ex.Message; }
    }

    private ConcertSnapshot? SelectedConcertSnapshot() =>
        cfg.ConcertSnapshots?.FirstOrDefault(x => x is not null && x.Id == selectedSnapshotId);

    private void ValidateSnapshotRestorePreconditions(ConcertSnapshot snapshot)
    {
        ConcertSnapshotFormat.Validate(snapshot);
        if (plugin.Player.IsPlaying || plugin.Player.IsStopPending || plugin.Player.IsRewindPending ||
            plugin.Player.IsEnsemblePlayback || plugin.Instruments.InProgress || pendingNativeUnequip ||
            nativeFollowerFile is not null || nativeFollowerTracks is not null ||
            nativeFollowerInstrument is not null || songFlow.HasPending ||
            pendingInstrumentSong is not null || plugin.LiveMidi.NotesHeld > 0 ||
            preparingConductor is not null || preparingFollower is not null ||
            (concertPhase is ConcertPhase.File or ConcertPhase.Tracks or ConcertPhase.Instruments or ConcertPhase.Health) ||
            (concertRetryPhase != ConcertRetryPhase.Idle))
            throw new InvalidOperationException("Stop music and finish/cancel ensemble preparation before restoring a concert.");
        if (cfg.SavedPlaylists.Count >= 100)
            throw new InvalidOperationException("Saved playlist library is full (100). Delete an unused profile first.");
        if ((cfg.ConcertSnapshots?.Count ?? 0) >= ConcertSnapshotFormat.MaxSnapshots)
            throw new InvalidOperationException("Snapshot library is full. Export/remove an old snapshot before restoring; an automatic safety backup is required.");
    }

    private void ApplyConcertSnapshot(ConcertSnapshot snapshot)
    {
        // The new profile is created first so neither source playlist nor the
        // previous Solo/Ensemble active library will be destroyed on restore.
        var target = SavedPlaylistLibrary.NewProfile(cfg, snapshot.PlaylistName + " (restored)",
            snapshot.Role == 0 ? 0 : 1, copyCurrent: false);
        target.Files = snapshot.PlaylistFiles.ToList();
        SavedPlaylistLibrary.Select(cfg, target.Id);
        cfg.EnsembleMode = snapshot.Role;

        var options = snapshot.Options;
        cfg.PlaybackSpeed = options.PlaybackSpeed;
        cfg.GlobalTranspose = options.GlobalTranspose;
        cfg.RepairOctaves = options.RepairOctaves;
        cfg.ChordStrategy = options.ChordStrategy;
        cfg.MinimumGapMs = options.MinimumGapMs;
        cfg.ArpeggioSpacingMs = options.ArpeggioSpacingMs;
        cfg.KeyHoldMs = options.KeyHoldMs;
        cfg.AutoNext = options.AutoNext;
        cfg.AutoDetectMidiTrackInstruments = options.AutoDetectMidiTrackInstruments;
        cfg.AutoReadMidiTrackOctaves = options.AutoReadMidiTrackOctaves;
        cfg.AutoSwitchInstrumentBeforePlay = options.AutoSwitchInstrumentBeforePlay;
        cfg.GuitarToneOverridesEnabled = options.GuitarToneOverridesEnabled;
        cfg.KeepPartyAssignmentsAcrossSongs = options.KeepPartyAssignmentsAcrossSongs;
        cfg.GameEnsembleEnabled = options.GameEnsembleEnabled;
        cfg.NativeLeaderSyncEnabled = options.NativeLeaderSyncEnabled;
        cfg.NativeEnsembleAutoAccept = options.NativeEnsembleAutoAccept;
        cfg.FollowConductorAssignments = options.FollowConductorAssignments;
        cfg.ConcertAutoPrepareNext = options.ConcertAutoPrepareNext;
        cfg.GameEnsembleOneClick = false; // never reinstate deprecated UTC chat START
        cfg.MultiDeviceEnabled = false; // never reconnect old TCP hosts on restore
        outputArmed = false;             // user must ARM again for the new concert
        waitingForEnsembleCue = false;
        // Deliberately preserve the current language, MIDI controller, network
        // credentials, audio preferences, output ARM and follower session consent.

        var smart = ConcertSnapshotFormat.Copy(snapshot.SmartPlaylist);
        smart.Recent.Clear(); // history belongs to this client, not a transferred concert
        smart.ShuffleRemaining.Clear();
        smart.ShuffleCycleStarted = false;
        cfg.SmartPlaylistsById[target.Id] = smart;
        if (snapshot.Lineup is not null)
            cfg.EnsembleLineupsByPlaylistId[target.Id] = ConcertSnapshotFormat.Copy(snapshot.Lineup);

        foreach (var (path, one) in snapshot.Songs)
        {
            // These legacy settings are stored per MIDI path globally; importing
            // restores them intentionally, but the preceding automatic snapshot
            // backs up the original values for later recovery.
            if (one.Instrument.HasValue) cfg.InstrumentsByFile[path] = one.Instrument.Value;
            else cfg.InstrumentsByFile.Remove(path);
            RestoreMap(cfg.PartyInstrumentsByFile, path, one.PartyInstruments);
            RestoreMap(cfg.PartyAssignmentsByFile, path, one.PartyTracks);
            RestoreMap(cfg.OctaveShiftsByFile, path, one.Octaves);
            RestoreMap(cfg.GuitarTonesByFile, path, one.GuitarTones);
            if (one.ManualOctaveOverrides.Count > 0)
                cfg.ManualOctaveLaneOverridesByFile[path] = one.ManualOctaveOverrides.ToList();
            else cfg.ManualOctaveLaneOverridesByFile.Remove(path);
            if (one.EnabledLanes.Count > 0)
                cfg.EnsemblePartsByFile[path] = one.EnabledLanes.ToList();
            else cfg.EnsemblePartsByFile.Remove(path);
            if (one.Soulweaver is not null)
                cfg.SoulweaverPresetsByFile[path] = ConcertSnapshotFormat.Copy(one.Soulweaver);
            else cfg.SoulweaverPresetsByFile.Remove(path);
            if (one.Cue is not null)
                cfg.ConcertSongCuesByKey[ConcertSetlist.EntryKey(target.Id, path)] = ConcertSnapshotFormat.Copy(one.Cue);
        }
        cfg.LastFile = snapshot.SelectedMidiPath;
        cfg.Save();

        // Clear in-memory conductor state; no party STOP/START, instrument SET or
        // file selection message is broadcast by restoring a snapshot.
        ConcertCancelPreparation("Snapshot restored. Prepare the orchestra before native START.");
        ConcertCancelSetlist("Snapshot restored. Next-song automation awaits a fresh performance.");
        plugin.Network.Stop();
        plugin.LiveMidi.Panic();
        plugin.AudioPreview.Stop("Concert snapshot changed. Audio preview stopped.");
        CancelOneClickInstrumentPreparation();
        CancelNativeLeaderSync("Snapshot restored locally; no party commands were sent.");
        gameStartGate.Cancel("Concert snapshot restored; new native Ensemble START required.");
        ClearLoadedMidiForPlaylistChange();
        playlistRenameInput = target.Name;
        snapshotNameInput = snapshot.Name;

        // Load only the local MIDI, suppressing the conductor's automatic party
        // file cue. The user's next PREPARE ENTIRE ORCHESTRA sends the changes.
        if (!string.IsNullOrWhiteSpace(snapshot.SelectedMidiPath) && File.Exists(snapshot.SelectedMidiPath))
        {
            suppressSnapshotSync = true;
            try
            {
                if (!Load(snapshot.SelectedMidiPath))
                    snapshotStatus = "Snapshot restored; selected MIDI needs manual loading: " + status;
            }
            finally { suppressSnapshotSync = false; }
        }
        if (string.IsNullOrWhiteSpace(loaded) && !string.IsNullOrWhiteSpace(snapshot.SelectedMidiPath))
            fileInput = snapshot.SelectedMidiPath; // missing path visible for repair; don't auto-load
        cfg.Save();
    }

    private static void RestoreMap<T>(Dictionary<string, Dictionary<string, T>> target,
        string path, Dictionary<string, T> source)
    {
        if (source.Count > 0) target[path] = new Dictionary<string, T>(source, StringComparer.OrdinalIgnoreCase);
        else target.Remove(path);
    }

    private void RestoreConcertSnapshot(ConcertSnapshot snapshot)
    {
        try
        {
            ValidateSnapshotRestorePreconditions(snapshot);
            // Retain an automatic, persistent undo point before touching ANY
            // current playlist or per-MIDI overrides. It can also be exported.
            var safety = CaptureConcertSnapshot("Before restore — " + DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
            cfg.ConcertSnapshots.Add(safety);
            cfg.Save();
            ApplyConcertSnapshot(ConcertSnapshotFormat.Copy(snapshot));
            snapshotStatus = $"Restored '{snapshot.Name}' into a NEW playlist. " +
                $"Saved an undo snapshot. Missing MIDI files: {ConcertSnapshotFormat.MissingFileCount(snapshot)}. " +
                "Output remains unarmed; press Prepare Entire Orchestra before native START.";
        }
        catch (Exception ex) { snapshotStatus = "Restore failed: " + ex.Message; }
    }

    private void ExportConcertSnapshot(ConcertSnapshot snapshot)
    {
        var safeName = string.Concat(snapshot.Name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        fileDialogs.SaveFileDialog(Loc.T("Export concert snapshot"), ".json", safeName + ".json", ".json",
            (bool ok, string path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                    File.WriteAllText(path, ConcertSnapshotFormat.ExportToJson(snapshot));
                    snapshotStatus = "Snapshot exported: " + path;
                }
                catch (Exception ex) { snapshotStatus = "Snapshot export failed: " + ex.Message; }
            }, GetFileDialogStartFolder());
    }

    private void ImportConcertSnapshot()
    {
        fileDialogs.OpenFileDialog(Loc.T("Import concert snapshot"), ".json",
            (bool ok, List<string> paths) =>
            {
                if (!ok || paths.Count == 0) return;
                try
                {
                    cfg.ConcertSnapshots ??= new();
                    if (cfg.ConcertSnapshots.Count >= ConcertSnapshotFormat.MaxSnapshots)
                        throw new InvalidOperationException("Snapshot library is full (30).");
                    if (new FileInfo(paths[0]).Length > ConcertSnapshotFormat.MaxJsonBytes)
                        throw new InvalidDataException("Snapshot exceeds the 8 MB import limit.");
                    var imported = ConcertSnapshotFormat.ImportFromJson(File.ReadAllText(paths[0]));
                    cfg.ConcertSnapshots.Add(imported);
                    selectedSnapshotId = imported.Id;
                    cfg.Save();
                    snapshotStatus = $"Imported '{imported.Name}'. Review it before choosing Restore.";
                }
                catch (Exception ex) { snapshotStatus = "Snapshot import failed: " + ex.Message; }
            }, 1, GetFileDialogStartFolder());
    }

    private void DrawConcertSnapshots()
    {
        cfg.ConcertSnapshots ??= new();
        ImGui.TextColored(new Vector4(.98f, .61f, .85f, 1f), Loc.T("CONCERT SNAPSHOTS & RESTORE"));
        ImGui.TextWrapped(Loc.T("Save entire concert setups as named snapshots. Restore a snapshot to a NEW Solo/Ensemble playlist. No MIDI files are copied or altered."));
        ImGui.SetNextItemWidth(320);
        ImGui.InputText(Loc.T("Snapshot name##concertSnapshotName"), ref snapshotNameInput, 80);
        if (ImGui.Button(Loc.T("SAVE CURRENT CONCERT SNAPSHOT"))) SaveConcertSnapshot();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Import snapshot JSON..."))) ImportConcertSnapshot();
        ImGui.TextDisabled(Loc.T("Saved snapshots include instrument choices, track assignments, octaves, setlist, lineup, playback settings and concert cues."));
        ImGui.Separator();
        if (cfg.ConcertSnapshots.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("No snapshots saved yet. Give your current concert a name and press Save."));
            ImGui.TextWrapped(Loc.T(snapshotStatus));
            return;
        }
        var picked = SelectedConcertSnapshot();
        if (picked is null)
        {
            picked = cfg.ConcertSnapshots.LastOrDefault();
            selectedSnapshotId = picked?.Id ?? "";
        }
        if (ImGui.BeginCombo(Loc.T("Saved concert"), picked is null ? "(none)" : picked.Name))
        {
            foreach (var entry in cfg.ConcertSnapshots.Where(s => s is not null).ToList())
                if (ImGui.Selectable(entry.Name + " — " + entry.CreatedUtc.ToLocalTime().ToString("g") + "##" + entry.Id,
                        entry.Id == selectedSnapshotId))
                    selectedSnapshotId = entry.Id;
            ImGui.EndCombo();
        }
        picked = SelectedConcertSnapshot();
        if (picked is null) return;
        ImGui.TextUnformatted($"{(picked.Role == 0 ? Loc.T("Solo") : picked.Role == 1 ? Loc.T("Conductor") : Loc.T("Follower"))} · " +
            $"{picked.PlaylistFiles.Count} MIDI(s) · {picked.Lineup?.PerformerIdsBySlot.Count ?? 0} performer slot(s) · " +
            $"{picked.Options.PlaybackSpeed:0.00}x");
        ImGui.TextDisabled(Loc.T("Playlist:") + " " + picked.PlaylistName);
        ImGui.TextDisabled(Loc.T("Selected MIDI:") + " " + (string.IsNullOrEmpty(picked.SelectedMidiPath)
            ? Loc.T("None") : Path.GetFileName(picked.SelectedMidiPath)));
        ImGui.TextDisabled(Loc.T("Missing MIDI files:") + " " + ConcertSnapshotFormat.MissingFileCount(picked));
        if (ImGui.TreeNode(Loc.T("Preview snapshot contents")))
        {
            ImGui.TextUnformatted(Loc.T("Saved MIDI files:"));
            ImGui.BeginChild("##snapshotList", new Vector2(0, Math.Min(220, 26 * picked.PlaylistFiles.Count + 15)), true);
            foreach (var path in picked.PlaylistFiles.Take(80))
            {
                ImGui.TextColored(File.Exists(path) ? new Vector4(.5f,.92f,.67f,1) : new Vector4(1f,.72f,.37f,1),
                    (File.Exists(path) ? "✓ " : "! ") + Path.GetFileName(path));
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
            }
            if (picked.PlaylistFiles.Count > 80) ImGui.TextDisabled(Loc.T("Additional songs omitted from preview."));
            ImGui.EndChild();
            ImGui.TextDisabled($"{picked.Songs.Count} " + Loc.T("song configurations") +
                $" · {picked.Lineup?.PerformerIdsBySlot.Count ?? 0} " + Loc.T("performer slots"));
            ImGui.TextDisabled(Loc.T("Restore changes shared per-MIDI overrides; an automatic Before restore snapshot protects your previous settings."));
            ImGui.TreePop();
        }
        if (ImGui.Button(Loc.T("RESTORE SELECTED SNAPSHOT...")))
        {
            snapshotRestorePending = true;
            ImGui.OpenPopup("##confirmRestoreSnapshot");
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Export snapshot JSON..."))) ExportConcertSnapshot(picked);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Delete snapshot...")))
        {
            snapshotDeletePending = true;
            ImGui.OpenPopup("##confirmDeleteSnapshot");
        }
        if (ImGui.BeginPopup("##confirmRestoreSnapshot"))
        {
            ImGui.TextWrapped(Loc.T("Create a NEW playlist and restore this concert's settings? Existing playlists and snapshots are preserved."));
            ImGui.TextWrapped(Loc.T("This changes the local plugin's saved per-MIDI overrides. No notes are played, no instruments equipped and no party commands sent."));
            if (ImGui.Button(Loc.T("CONFIRM RESTORE")) && snapshotRestorePending)
            {
                snapshotRestorePending = false;
                RestoreConcertSnapshot(picked);
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Cancel"))) { snapshotRestorePending = false; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("##confirmDeleteSnapshot"))
        {
            ImGui.TextWrapped(Loc.T("Delete this saved snapshot? Your current playlist and MIDI files will not be deleted."));
            if (ImGui.Button(Loc.T("CONFIRM DELETE")) && snapshotDeletePending)
            {
                cfg.ConcertSnapshots.RemoveAll(s => s.Id == picked.Id);
                selectedSnapshotId = "";
                snapshotDeletePending = false;
                cfg.Save();
                snapshotStatus = "Snapshot deleted; MIDI files and playlists were not changed.";
                ImGui.CloseCurrentPopup();
            }
            ImGui.SameLine();
            if (ImGui.Button(Loc.T("Cancel"))) { snapshotDeletePending = false; ImGui.CloseCurrentPopup(); }
            ImGui.EndPopup();
        }
        ImGui.TextWrapped(Loc.T(snapshotStatus));
        ImGui.TextDisabled(Loc.T("ARM and follower consent are never restored. Only the conductor's native FFXIV Ready Check can start ensemble music."));
    }
}
