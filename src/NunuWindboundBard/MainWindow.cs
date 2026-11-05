using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Windowing;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration cfg;
    private readonly FileDialogManager fileDialogs = new();
    private const string MidiFilter = "MIDI files{.mid,.midi}";
    private MidiSong? song;
    private BardArrangement? arranged;
    private List<MidiLanePlan> lanes = new();
    private Dictionary<MidiLaneKey, int> laneNoteCounts = new();
    private string loaded = "";
    private string fileInput;
    private string status = "Welcome to Windbound Bard. Load a MIDI or the bundled calibration file.";
    private bool outputArmed; // intentionally never persisted
    private readonly SongFlowCoordinator songFlow = new();
    private int activePlaylist = -1;
    private bool showPianoRoll = true;
    private string playlistFilter = "";
    private string playlistNameInput = "";
    private string playlistRenameInput = "";
    private bool copyPlaylistOnCreate;
    private string playlistMessage = "";
    private readonly Dictionary<MidiLaneKey, ulong> partyAssignments = new();
    private long lastPartyPollTicks;
    private string assignmentStatus = "Conductor has not published party assignments.";
    private bool partyAssignmentsReceivedForCurrentMidi;
    private bool waitingForEnsembleCue; // never persisted; manually arm every session
    private long currentSongHash;
    private string networkCode = ""; // never persisted
    private string networkHost = "";
    private string networkStatus = "Not connected. Multi-Device is opt-in.";
    private long networkRevision;
    private string networkSongHash = "";
    private RemoteArrangementSettings? networkArrangement;
    private long lastNetworkReadyQpc;
    private string ensembleStatus = "Solo / not connected.";
    private sealed record PendingInstrumentSong(string Path, int InstrumentId, long StartedQpc);
    private PendingInstrumentSong? pendingInstrumentSong;

    public MainWindow(Plugin plugin) : base("Windbound Bard | Nunu's MIDI Studio###NunuWindboundBard")
    {
        this.plugin = plugin;
        cfg = plugin.Config;
        fileInput = cfg.LastFile;
        networkHost = cfg.MultiDeviceHostAddress;
        playlistRenameInput = SavedPlaylistLibrary.GetActive(cfg).Name;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 410), MaximumSize = new Vector2(1400, 1300)
        };
    }

    public void Dispose() => fileDialogs.Reset();
    public void DrawFileDialogs() => fileDialogs.Draw();

    public override void Draw()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), "NUNU'S WINDBOUND BARD   /windbound");
        ImGui.SameLine();
        ImGui.TextDisabled("FFXIV • C3–C6 • Dalamud 15 • Chords + Multi-Device + Soulweaver");
        ImGui.Separator();
        ImGui.TextWrapped(status);
        ImGui.TextWrapped(plugin.Player.Status);
        if (!string.IsNullOrWhiteSpace(plugin.Player.GuitarWarning))
            ImGui.TextColored(new Vector4(1f, .75f, .38f, 1f), plugin.Player.GuitarWarning);
        ImGui.TextColored(plugin.IsGameOutputSafe() ? new Vector4(.49f, .95f, .55f, 1f) : new Vector4(1f, .75f, .38f, 1f),
            plugin.IsGameOutputSafe() ? "Performance: FULL 37-KEY READY" :
            plugin.IsPerforming ? "Open expanded 37-note Performance keyboard for direct output" : "Performance mode not detected (preview still works)");

        if (ImGui.BeginTabBar("WindboundTabs"))
        {
            if (ImGui.BeginTabItem("Songs & Playback")) { DrawPlayer(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Ensemble")) { DrawEnsemble(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("FFXIV Optimiser")) { DrawOptimizer(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Soulweaver Studio")) { DrawSoulweaver(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Instruments")) { DrawInstruments(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Guitar Tones")) { DrawGuitarTones(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("Direct Output & Test")) { DrawKeyMapping(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem("About / Setup")) { DrawHelp(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void DrawPlayer()
    {
        ImGui.TextUnformatted("Load a Standard MIDI File (.mid / .midi), format 0 or 1:");
        ImGui.SetNextItemWidth(-235);
        ImGui.InputText("##MidiPath", ref fileInput, 1024);
        ImGui.SameLine();
        if (ImGui.Button("Load typed path")) LoadFromUser(fileInput);
        ImGui.SameLine();
        if (ImGui.Button("Browse MIDI...")) BrowseMidiToLoad();
        if (ImGui.Button("Load sample"))
        {
            string sample = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "Bard-37-Note-Calibration.mid");
            if (File.Exists(sample)) LoadFromUser(sample);
            else status = "Sample is included in the source ZIP under Samples. Enter its full path to load it.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Load chord test"))
        {
            string sample = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "Bard-Chord-Callback-Test.mid");
            if (File.Exists(sample)) LoadFromUser(sample);
            else status = "Chord test is included in Samples. Enter its full path to load it.";
        }
        ImGui.SameLine();
        if (ImGui.Button("Add MIDI files...")) BrowseMidiToAdd();
        ImGui.SameLine();
        if (ImGui.Button("Add MIDI folder...")) BrowseMidiFolder();
        ImGui.TextDisabled("Or use the typed path above:");
        ImGui.SameLine();
        if (ImGui.SmallButton("Add typed file")) AddToPlaylist(fileInput);
        ImGui.SameLine();
        if (ImGui.SmallButton("Add typed folder")) AddFolder(fileInput);
        bool subfolders = cfg.IncludeMidiSubfolders;
        if (ImGui.Checkbox("Include subfolders when importing MIDI folders", ref subfolders))
        {
            cfg.IncludeMidiSubfolders = subfolders;
            cfg.Save();
        }

        ImGui.Separator();
        DrawSavedPlaylistControls();
        ImGui.Separator();
        ImGui.TextUnformatted($"Playlist ({cfg.Playlist.Count} songs — scroll to see them all):");
        ImGui.SetNextItemWidth(-1);
        ImGui.InputText("Search playlist##PlaylistFilter", ref playlistFilter, 256);
        int matches = 0;
        ImGui.BeginChild("##FullPlaylistScroll", new Vector2(0, 225), true);
        for (int i = 0; i < cfg.Playlist.Count; i++)
        {
            string path = cfg.Playlist[i];
            string name = Path.GetFileName(path);
            if (!string.IsNullOrWhiteSpace(playlistFilter) &&
                !name.Contains(playlistFilter, StringComparison.OrdinalIgnoreCase) &&
                !path.Contains(playlistFilter, StringComparison.OrdinalIgnoreCase)) continue;
            matches++;
            if (ImGui.Selectable($"{i + 1:0000}. {name}##song{i}", activePlaylist == i))
            {
                SelectPlaylistSong(path);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(path);
        }
        ImGui.EndChild();
        if (!string.IsNullOrEmpty(playlistFilter)) ImGui.TextDisabled($"{matches} matching entries (out of {cfg.Playlist.Count})");
        if (ImGui.Button("Remove selected") && activePlaylist >= 0 && activePlaylist < cfg.Playlist.Count)
        {
            cfg.Playlist.RemoveAt(activePlaylist);
            activePlaylist = -1;
            PersistActivePlaylist();
        }
        ImGui.SameLine();
        bool autoNext = cfg.AutoNext;
        if (ImGui.Checkbox("Auto next", ref autoNext)) { cfg.AutoNext = autoNext; cfg.Save(); }
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo)
            ImGui.TextDisabled("Auto next is suspended in Ensemble mode; cue each song together.");

        ImGui.Separator();
        if (song is not null && arranged is not null)
        {
            ImGui.TextUnformatted($"Loaded: {Path.GetFileName(loaded)}  |  {song.Tracks.Count} tracks  |  {song.InitialBpm:0.#} BPM  |  {song.DurationMs / 1000:0.0}s");
            ImGui.TextUnformatted($"Bard arrangement: {arranged.Report.FinalNotes:N0} notes  |  {arranged.Report.ChordGroups:N0} chord groups  |  {arranged.Report.TimingDrops:N0} dense-event drops");
        }
        else ImGui.TextDisabled("No song loaded.");

        if (song is not null)
        {
            ImGui.TextUnformatted($"Instrument for this MIDI: {InstrumentCatalog.Display(GetInstrumentChoice())}  |  Equipped: {plugin.Instruments.CurrentName}");
            ImGui.TextDisabled("Change the instrument in the Instruments tab. Track octave controls are below.");
            DrawTracksOnMainPage();
        }

        bool arm = outputArmed;
        if (ImGui.Checkbox("ARM: Direct FFXIV Performance notes (no typing into chat)", ref arm))
        {
            outputArmed = arm;
            if (!arm)
            {
                CancelPendingInstrumentStart();
                if (plugin.Player.IsPlaying) plugin.Player.Stop();
            }
        }
        ImGui.TextDisabled(outputArmed ? "Armed: direct game callbacks. Alt-tab and chat are NOT keyboard output targets." : "Safe preview mode: no keys sent to the game.");

        if (ImGui.Button("Play")) StartSong();
        ImGui.SameLine();
        if (ImGui.Button(plugin.Player.IsPaused ? "Resume" : "Pause")) plugin.Player.TogglePause();
        ImGui.SameLine();
        if (ImGui.Button("STOP")) StopPlayback();
        ImGui.SameLine();
        if (ImGui.Button("Next")) NextSong();

        if (ImGui.TreeNode("Song switching diagnostics"))
        {
            string details = "Loaded MIDI: " + (string.IsNullOrWhiteSpace(loaded) ? "(none)" : loaded) + "\n" +
                $"Playlist entry: {activePlaylist + 1}/{cfg.Playlist.Count}; Solo: {cfg.EnsembleMode == (int)EnsembleRole.Solo}; Auto Next: {cfg.AutoNext}\n" +
                $"Playing: {plugin.Player.IsPlaying}; Stop pending: {plugin.Player.IsStopPending}; Last completed: {plugin.Player.CompletedSuccessfully}\n" +
                $"Queued action: {songFlow.PendingDescription}\n" +
                $"Instrument: {plugin.Instruments.CurrentName}; Requested: {(pendingInstrumentSong is null ? "None" : InstrumentCatalog.Display(pendingInstrumentSong.InstrumentId))}\n" +
                $"Output armed: {outputArmed}; Performance ready: {plugin.IsGameOutputSafe()}\n" +
                $"Playback: {plugin.Player.Status}\n" +
                $"Instrument switch: {plugin.Instruments.Status}\n" +
                $"Windbound: {status}";
            ImGui.TextWrapped(details);
            if (ImGui.Button("Copy song switching diagnostics")) ImGui.SetClipboardText(details);
            ImGui.TreePop();
        }

        float speed = cfg.PlaybackSpeed;
        if (ImGui.SliderFloat("Playback speed", ref speed, .5f, 2f, "%.2fx"))
        {
            cfg.PlaybackSpeed = speed; cfg.Save();
        }
        if (song != null)
        {
            float fraction = (float)Math.Clamp(plugin.Player.PositionMs / Math.Max(1, song.DurationMs), 0, 1);
            ImGui.ProgressBar(fraction, new Vector2(-1, 0));
            ImGui.TextUnformatted($"{plugin.Player.NotesPlayed:N0} notes played / {arranged?.Notes.Count ?? 0:N0}");
            if (ImGui.Checkbox("Show piano roll", ref showPianoRoll)) { }
            if (showPianoRoll) DrawRoll();
        }
    }

    private void PersistActivePlaylist()
    {
        SavedPlaylistLibrary.SnapshotActive(cfg);
        cfg.Save();
    }

    // These transitions run on the FFXIV framework thread, even if the ImGui
    // window is closed or FFXIV is in the background. Never advance from Draw().
    public void PollSongTransitions()
    {
        SongFlowAction action = songFlow.Poll(plugin.Player.IsPlaying,
            plugin.Player.CompletedSuccessfully, cfg.AutoNext,
            (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo);
        switch (action.Kind)
        {
            case SongFlowActionKind.Next:
                AdvancePlaylist();
                break;
            case SongFlowActionKind.SelectSong:
                if (Load(action.Path) && action.PlayAfterLoad)
                    StartSong();
                break;
            case SongFlowActionKind.StartLoaded:
                StartSong();
                break;
        }
    }

    /// <summary>Manual file load: switch seamlessly if a solo is in progress.</summary>
    private void LoadFromUser(string path)
    {
        if (plugin.Player.IsPlaying)
        {
            songFlow.QueueSelection(path, (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo);
            CancelPendingInstrumentStart();
            plugin.Player.Stop();
            status = "Stopping previous notes; selected MIDI will load when the player is ready.";
        }
        else
        {
            songFlow.Cancel();
            Load(path);
        }
    }

    private void SelectPlaylistSong(string path) => LoadFromUser(path);

    private void ClearLoadedMidiForPlaylistChange()
    {
        CancelPendingInstrumentStart();
        if (plugin.Player.IsPlaying) plugin.Player.Stop();
        song = null;
        arranged = null;
        lanes.Clear();
        laneNoteCounts.Clear();
        partyAssignments.Clear();
        partyAssignmentsReceivedForCurrentMidi = false;
        networkArrangement = null;
        networkRevision = 0;
        networkSongHash = "";
        loaded = "";
        currentSongHash = 0;
        fileInput = "";
        activePlaylist = -1;
        waitingForEnsembleCue = false;
        songFlow.Cancel();
        status = "Select a MIDI from the current saved playlist.";
    }

    private void SwitchSavedPlaylist(string id)
    {
        try
        {
            if (plugin.Player.IsPlaying || pendingInstrumentSong is not null)
                throw new InvalidOperationException("STOP playback before switching playlists.");
            var profile = cfg.SavedPlaylists.First(x => x.Id == id);
            if (profile.Purpose != SavedPlaylistLibrary.PurposeForRole(cfg.EnsembleMode))
                throw new InvalidOperationException("Change Solo/Ensemble mode to open this playlist.");
            SavedPlaylistLibrary.Select(cfg, id);
            ClearLoadedMidiForPlaylistChange();
            cfg.Save();
            playlistRenameInput = profile.Name;
            playlistMessage = $"Opened {profile.Name}: {cfg.Playlist.Count} MIDI(s).";
        }
        catch (Exception ex) { playlistMessage = "Playlist switch: " + ex.Message; }
    }

    private void DrawSavedPlaylistControls()
    {
        int purpose = SavedPlaylistLibrary.PurposeForRole(cfg.EnsembleMode);
        string kind = purpose == 0 ? "SOLO" : "ENSEMBLE";
        var current = SavedPlaylistLibrary.GetActive(cfg);
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), $"SAVED {kind} PLAYLISTS");
        if (ImGui.SmallButton("Solo library") && cfg.EnsembleMode != (int)EnsembleRole.Solo)
            SetEnsembleRole(EnsembleRole.Solo);
        ImGui.SameLine();
        if (ImGui.SmallButton("Ensemble library") && cfg.EnsembleMode == (int)EnsembleRole.Solo)
            SetEnsembleRole(EnsembleRole.Conductor);
        ImGui.SameLine();
        ImGui.TextDisabled("Changing libraries saves the previous one automatically.");
        ImGui.SetNextItemWidth(Math.Min(420, Math.Max(220, ImGui.GetContentRegionAvail().X - 160)));
        if (ImGui.BeginCombo("Playlist library##StoredList", current.Name))
        {
            foreach (var profile in cfg.SavedPlaylists.Where(x => x.Purpose == purpose))
            {
                if (ImGui.Selectable($"{profile.Name} ({profile.Files.Count} songs)##Saved{profile.Id}", profile.Id == current.Id))
                    SwitchSavedPlaylist(profile.Id);
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Save playlist"))
        {
            PersistActivePlaylist();
            playlistMessage = $"Saved {current.Name} ({cfg.Playlist.Count} MIDIs).";
        }
        ImGui.SetNextItemWidth(210);
        ImGui.InputText("New playlist name", ref playlistNameInput, 80);
        ImGui.SameLine();
        if (ImGui.Button("Create playlist"))
        {
            try
            {
                var profile = SavedPlaylistLibrary.NewProfile(cfg, playlistNameInput, purpose, copyPlaylistOnCreate);
                PersistActivePlaylist();
                SwitchSavedPlaylist(profile.Id);
                playlistNameInput = "";
            }
            catch (Exception ex) { playlistMessage = "Create failed: " + ex.Message; }
        }
        bool copy = copyPlaylistOnCreate;
        if (ImGui.Checkbox("Copy songs from current playlist into new playlist", ref copy)) copyPlaylistOnCreate = copy;
        ImGui.SetNextItemWidth(210);
        ImGui.InputText("Rename current list", ref playlistRenameInput, 80);
        ImGui.SameLine();
        if (ImGui.Button("Rename"))
        {
            try { SavedPlaylistLibrary.Rename(cfg, current.Id, playlistRenameInput);
                cfg.Save(); playlistRenameInput = current.Name;
                playlistMessage = $"Renamed playlist to {current.Name}."; }
            catch (Exception ex) { playlistMessage = "Rename failed: " + ex.Message; }
        }
        if (ImGui.Button("Export playlist JSON...")) ExportSavedPlaylist();
        ImGui.SameLine();
        if (ImGui.Button("Import playlist JSON...")) ImportSavedPlaylist();
        ImGui.TextDisabled("Solo and Ensemble playlists are separate. Switching modes restores that mode's saved library.");
        if (!string.IsNullOrWhiteSpace(playlistMessage)) ImGui.TextWrapped(playlistMessage);
    }

    private void ExportSavedPlaylist()
    {
        var profile = SavedPlaylistLibrary.GetActive(cfg);
        string stem = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        fileDialogs.SaveFileDialog("Export Windbound playlist", ".json", stem + ".json", ".json",
            (bool ok, string path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                    SavedPlaylistLibrary.Export(path, profile, cfg.Playlist);
                    playlistMessage = "Exported playlist: " + path;
                }
                catch (Exception ex) { playlistMessage = "Export failed: " + ex.Message; }
            }, GetFileDialogStartFolder());
    }

    private void ImportSavedPlaylist()
    {
        fileDialogs.OpenFileDialog("Import Windbound playlist", ".json",
            (bool ok, List<string> paths) =>
            {
                if (!ok || paths.Count == 0) return;
                try
                {
                    var imported = SavedPlaylistLibrary.Import(paths[0]);
                    int purpose = SavedPlaylistLibrary.PurposeForRole(cfg.EnsembleMode);
                    // Imported lists belong to the mode that is currently selected;
                    // users can import a shared file into either Solo or Ensemble.
                    var profile = SavedPlaylistLibrary.NewProfile(cfg, imported.Name, purpose, false);
                    profile.Files = imported.Files;
                    PersistActivePlaylist();
                    SwitchSavedPlaylist(profile.Id);
                    playlistMessage = $"Imported {profile.Name}: {profile.Files.Count} entries. Missing paths remain visible.";
                }
                catch (Exception ex) { playlistMessage = "Import failed: " + ex.Message; }
            }, 1, GetFileDialogStartFolder());
    }

    private void DrawOptimizer()
    {
        ImGui.TextWrapped("Non-destructive octave adaptation for Bard Performance (C3–C6). Full-chord mode preserves MIDI note start times and durations; FFXIV may still limit the audible overlap.");
        if (song is null)
        {
            ImGui.TextDisabled("Load a MIDI in Songs & Playback first."); return;
        }
        foreach (MidiLanePlan lane in lanes.ToArray())
        {
            int index = lanes.FindIndex(x => x.Key == lane.Key);
            string name = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
            bool include = lane.Include;
            if (ImGui.Checkbox($"##lane{index}", ref include)) ChangeLane(index, lane with { Include = include });
            ImGui.SameLine();
            ImGui.TextUnformatted($"{name} | Ch {lane.Key.Channel + 1:00}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"-12##lane{index}")) ChangeLane(index, lane with { OctaveShift = Math.Max(-48, lane.OctaveShift - 12) });
            ImGui.SameLine();
            ImGui.TextUnformatted($"{lane.OctaveShift:+0;-0;+0}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"+12##lane{index}")) ChangeLane(index, lane with { OctaveShift = Math.Min(48, lane.OctaveShift + 12) });
        }
        bool repair = cfg.RepairOctaves;
        if (ImGui.Checkbox("Repair remaining high/low notes by octaves", ref repair))
        {
            cfg.RepairOctaves = repair; cfg.Save(); Rebuild();
        }
        int transpose = cfg.GlobalTranspose;
        if (ImGui.SliderInt("Playback-only transpose (semitones)", ref transpose, -24, 24))
        {
            cfg.GlobalTranspose = transpose; cfg.Save(); Rebuild();
        }
        int strategy = cfg.ChordStrategy;
        ImGui.TextUnformatted("Chord handling:");
        if (ImGui.RadioButton("Full chords / original timing (experimental)", strategy == 4)) SetStrategy(4);
        if (ImGui.RadioButton("Highest (melody)", strategy == 0)) SetStrategy(0);
        ImGui.SameLine();
        if (ImGui.RadioButton("Lowest", strategy == 1)) SetStrategy(1);
        ImGui.SameLine();
        if (ImGui.RadioButton("Loudest", strategy == 2)) SetStrategy(2);
        ImGui.SameLine();
        if (ImGui.RadioButton("Gentle arpeggio", strategy == 3)) SetStrategy(3);
        int gap = cfg.MinimumGapMs;
        if (ImGui.SliderInt("Minimum note gap (ms)", ref gap, 40, 160))
        {
            cfg.MinimumGapMs = gap; cfg.Save(); Rebuild();
        }
        int arp = cfg.ArpeggioSpacingMs;
        if (ImGui.SliderInt("Arpeggio spacing (ms)", ref arp, 60, 180))
        {
            cfg.ArpeggioSpacingMs = arp; cfg.Save(); Rebuild();
        }
        if (arranged != null)
        {
            var report = MidiOptimization.Analyze(song, GetOptimiseOptions());
            ImGui.Separator();
            ImGui.TextUnformatted($"Source: {report.SourceNotes:N0} notes | {report.SourceInRange:N0} initially in range");
            ImGui.TextUnformatted($"Octave-optimised: {report.PlayableNotes:N0} | Shifted: {report.ShiftedNotes:N0} | Excluded/skipped: {report.ExcludedNotes + report.OmittedNotes:N0}");
            ImGui.TextUnformatted($"Prepared playback: {arranged.Report.FinalNotes:N0} notes ({arranged.Report.ChordGroups:N0} chord groups)");
            if (ImGui.Button("Export new FFXIV C3-C6 MIDI")) ExportOptimized();
            ImGui.TextDisabled("Exporter keeps original MIDI unchanged; arrangement modes affect playback only.");
        }
    }

    private void DrawKeyMapping()
    {
        ImGui.TextWrapped("Direct Performance output bypasses Windows keyboard events entirely. This 37-key map remains a MANUAL reference for playing notes yourself; the player sends pitch indices through the expanded Performance interface, even if you chat or alt-tab.");
        string keys = cfg.PerformanceKeys;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##Keys", ref keys, 64))
        {
            cfg.PerformanceKeys = keys.ToUpperInvariant(); cfg.Save();
        }
        bool valid = PerformanceKeyMap.TryValidate(cfg.PerformanceKeys, out string message);
        ImGui.TextColored(valid ? new Vector4(.48f, 1f, .60f, 1f) : new Vector4(1f, .4f, .4f, 1f), message);
        if (ImGui.Button("Restore 37-key preset"))
        {
            cfg.PerformanceKeys = PerformanceKeyMap.DefaultKeys; cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button("Copy bindings to clipboard"))
            ImGui.SetClipboardText(GetKeyTable());

        if (valid)
        {
            for (int i = NoteMapper.LowestNote; i <= NoteMapper.HighestNote; i += 6)
                ImGui.TextUnformatted(string.Join("    ", Enumerable.Range(i, Math.Min(6, NoteMapper.HighestNote + 1 - i))
                    .Select(pitch => $"{NoteMapper.GetNoteName(pitch)}={cfg.PerformanceKeys[pitch - 48]}")));
        }
        ImGui.Separator();
        ImGui.TextWrapped("Backend: native FFXIV Performance addon callbacks, not SendInput, not Windows Messages or global virtual keys. Chat input remains separate; no focus requirement.");
        ImGui.TextColored(plugin.IsExpandedKeyboard ? new Vector4(.48f, 1f, .6f, 1f) : new Vector4(1f, .42f, .42f, 1f),
            plugin.IsExpandedKeyboard ? "Expanded Performance addon detected" : "Expanded Performance addon not open");
        int hold = cfg.KeyHoldMs;
        if (ImGui.SliderInt("Minimum note duration (ms)", ref hold, 15, 180)) { cfg.KeyHoldMs = hold; cfg.Save(); }
        if (ImGui.Button("Test C4 (only when armed)")) TestNote();
        ImGui.SameLine();
        ImGui.TextDisabled(outputArmed ? "Output armed" : "Arm output in Songs & Playback first");
        ImGui.TextDisabled("A callback being sent does not prove the game sounded every overlapping note.");
    }

    private void DrawHelp()
    {
        ImGui.TextWrapped("1. In Instruments, choose the instrument for the MIDI. With auto-switch enabled, Play first closes any different Performance instrument through the game UI, equips the new one, and starts notes after the keyboard is confirmed ready. 'Keep current' retains your existing selection.");
        ImGui.TextWrapped("2. Songs & Playback shows every MIDI track/channel, with -12 and +12 octave buttons. Changes update the FFXIV Optimiser and are saved per song.");
        ImGui.TextWrapped("3. Open Bard Performance in EXPANDED (37-note) mode. Mini mode is not supported by this output adapter.");
        ImGui.TextWrapped("2. Load your MIDI. Optimise into C3–C6, leave Full chords selected, or choose a legacy reduction for extremely dense passages.");
        ImGui.TextWrapped("3. Tick ARM, test C4, then Play. The plugin calls the game Performance addon directly on each framework update. Normal chat keystrokes remain free and alt-tab is allowed.");
        ImGui.TextWrapped("4. Click Browse MIDI to load a file, Add MIDI files to select several files, or Add MIDI folder to import a directory (optional subfolders). All pickers are drawn inside Dalamud.");
        ImGui.TextWrapped("5. For electric guitar, open the Guitar Tones tab, enable Override by Track, and choose Overdriven/Clean/Muted/Power Chords/Special for each MIDI track/channel. The guitar Tone Change UI must be available in Performance.");
        ImGui.TextWrapped("6. /windbound stop stops the scheduler. Exported MIDI keeps the original source unchanged.");
        ImGui.Separator();
        ImGui.TextWrapped("IMPORTANT: Experimental native game interface; callback behaviour can change with FFXIV patches. Some instruments may not sustain every simultaneous chord tone. Verify by ear. Third-party plugins may violate the game's terms and Performance music rules.");
    }

    private void SetStrategy(int value)
    {
        cfg.ChordStrategy = value; cfg.Save(); Rebuild();
    }
    private MidiOptimizeOptions GetOptimiseOptions() => new(lanes.ToArray(),
        cfg.MultiDeviceEnabled && cfg.EnsembleMode == (int)EnsembleRole.Follower && networkArrangement is not null
            ? networkArrangement.RepairOctaves : cfg.RepairOctaves);
    private void ChangeLane(int index, MidiLanePlan lane)
    {
        if (index < 0 || index >= lanes.Count) return;
        lanes[index] = lane;
        RememberParts();
        RememberOctaves();
        Rebuild();
        if (plugin.Network.IsHosting && cfg.MultiDeviceEnabled) TryShareNetworkSession();
    }
    private void RememberParts()
    {
        if (string.IsNullOrEmpty(loaded)) return;
        cfg.EnsemblePartsByFile[loaded] = lanes.Where(l => l.Include)
            .Select(l => $"{l.Key.Track}:{l.Key.Channel}").ToList();
        cfg.Save();
    }

    private void RememberOctaves()
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        cfg.OctaveShiftsByFile[loaded] = lanes.ToDictionary(
            lane => GuitarToneOverrides.Key(lane.Key), lane => lane.OctaveShift);
        cfg.Save();
    }

    private void Rebuild()
    {
        InvalidateSoulweaver();
        if (song is null) return;
        try
        {
            var remote = cfg.MultiDeviceEnabled && cfg.EnsembleMode == (int)EnsembleRole.Follower
                ? networkArrangement : null;
            arranged = BardArranger.Build(song, new BardArrangeOptions(GetOptimiseOptions(),
                (ChordStrategy)Math.Clamp(remote?.ChordStrategy ?? cfg.ChordStrategy, 0, 4),
                remote?.GlobalTranspose ?? cfg.GlobalTranspose,
                remote?.MinimumGapMs ?? cfg.MinimumGapMs,
                remote?.ArpeggioSpacingMs ?? cfg.ArpeggioSpacingMs));
            status = $"Prepared {arranged.Report.FinalNotes:N0} Bard notes from {song.Notes.Count:N0} MIDI notes.";
        }
        catch (Exception error) { arranged = null; status = "Arrangement error: " + error.Message; }
    }

    private bool Load(string path)
    {
        try
        {
            CancelPendingInstrumentStart();
            if (plugin.Player.IsPlaying) plugin.Player.Stop();
            networkArrangement = null;
            string full = Path.GetFullPath(path.Trim().Trim('"'));
            if (!File.Exists(full)) throw new FileNotFoundException("No MIDI file at the chosen location.", full);
            if (!new[] { ".mid", ".midi" }.Contains(Path.GetExtension(full).ToLowerInvariant()))
                throw new InvalidDataException("Choose a .mid or .midi file.");
            MidiSong candidate = MidiFileReader.Load(full);
            song = candidate;
            loaded = full;
            fileInput = full;
            cfg.LastFile = full;
            cfg.Save();
            currentSongHash = EnsembleCoordinator.FingerprintFile(full);
            lanes = MidiOptimization.Suggest(candidate).ToList();
            laneNoteCounts = candidate.Notes.GroupBy(n => new MidiLaneKey(n.Track, n.Channel))
                .ToDictionary(group => group.Key, group => group.Count());
            if (cfg.EnsemblePartsByFile.TryGetValue(full, out var savedParts))
                lanes = lanes.Select(l => l with {
                    Include = savedParts.Contains($"{l.Key.Track}:{l.Key.Channel}")
                }).ToList();
            if (cfg.OctaveShiftsByFile.TryGetValue(full, out var shifts))
                lanes = lanes.Select(l => l with {
                    OctaveShift = shifts.TryGetValue(GuitarToneOverrides.Key(l.Key), out int shift) &&
                        shift is >= -48 and <= 48 && shift % 12 == 0 ? shift : l.OctaveShift
                }).ToList();
            activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            LoadPartyAssignmentsForFile();
            partyAssignmentsReceivedForCurrentMidi = false;
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor && partyAssignments.Count > 0)
            {
                ApplyPartyAssignmentsToLocal();
                TryPublishPartyAssignments();
            }
            Rebuild();
            if (arranged is null)
                throw new InvalidDataException("The MIDI arrangement could not be prepared.");
            if (cfg.MultiDeviceEnabled && plugin.Network.IsHosting)
                TryShareNetworkSession();
            return true;
        }
        catch (Exception error)
        {
            // Prevent Auto Next from ever playing the previous arrangement when
            // the next file is absent, unreadable or has no playable notes.
            song = null;
            arranged = null;
            loaded = "";
            lanes.Clear();
            laneNoteCounts.Clear();
            activePlaylist = -1;
            status = "Could not load MIDI: " + error.Message;
            return false;
        }
    }

    private string GetFileDialogStartFolder()
    {
        // Resume browsing where the last loaded/typed MIDI lives whenever possible.
        string candidate = !string.IsNullOrWhiteSpace(loaded) ? loaded : fileInput;
        try
        {
            candidate = Path.GetFullPath(candidate.Trim().Trim('"'));
            if (Directory.Exists(candidate)) return candidate;
            string? parent = Path.GetDirectoryName(candidate);
            if (parent is not null && Directory.Exists(parent)) return parent;
        }
        catch { /* malformed/empty typed path: fall back to Documents */ }
        return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
    }

    private void BrowseMidiToLoad()
    {
        fileDialogs.OpenFileDialog("Open MIDI for Windbound Bard", MidiFilter,
            (bool ok, List<string> files) =>
            {
                if (ok && files.Count > 0) LoadFromUser(files[0]);
            }, 1, GetFileDialogStartFolder());
    }

    private void BrowseMidiToAdd()
    {
        // selectionCountMax=0 allows multi-select of any number of files.
        fileDialogs.OpenFileDialog("Add MIDI files to the playlist", MidiFilter,
            (bool ok, List<string> files) =>
            {
                if (ok && files.Count > 0) AddFilesToPlaylist(files);
            }, 0, GetFileDialogStartFolder());
    }

    private void BrowseMidiFolder()
    {
        fileDialogs.OpenFolderDialog("Choose MIDI folder to import",
            (bool ok, string folder) =>
            {
                if (ok && !string.IsNullOrWhiteSpace(folder)) AddFolder(folder);
            }, GetFileDialogStartFolder());
    }

    private static bool IsMidiPath(string path) =>
        Path.GetExtension(path).Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
        Path.GetExtension(path).Equals(".midi", StringComparison.OrdinalIgnoreCase);

    private void AddToPlaylist(string input) => AddFilesToPlaylist(new[] { input });

    private void AddFilesToPlaylist(IEnumerable<string> inputs)
    {
        try
        {
            int added = 0, skipped = 0;
            var known = new HashSet<string>(cfg.Playlist, StringComparer.OrdinalIgnoreCase);
            foreach (string input in inputs)
            {
                if (string.IsNullOrWhiteSpace(input)) { skipped++; continue; }
                string full = Path.GetFullPath(input.Trim().Trim('"'));
                if (!File.Exists(full) || !IsMidiPath(full) || !known.Add(full)) { skipped++; continue; }
                cfg.Playlist.Add(full);
                added++;
                fileInput = full;
            }
            if (added > 0) PersistActivePlaylist();
            status = $"Added {added} MIDI file(s) to playlist; skipped {skipped} duplicate/invalid file(s).";
        }
        catch (Exception error) { status = "Could not add MIDI files: " + error.Message; }
    }

    private void AddFolder(string folder)
    {
        try
        {
            string root = Path.GetFullPath(folder.Trim().Trim('"'));
            if (!Directory.Exists(root)) throw new DirectoryNotFoundException("The selected MIDI folder was not found.");
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = cfg.IncludeMidiSubfolders,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint
            };
            // Bounded to protect game performance from huge folders or accidental drive-root imports.
            string[] files = Directory.EnumerateFiles(root, "*", options).Where(IsMidiPath).Take(10001).ToArray();
            bool limited = files.Length > 10000;
            var known = new HashSet<string>(cfg.Playlist, StringComparer.OrdinalIgnoreCase);
            int added = 0, duplicates = 0;
            foreach (string file in files.Take(10000))
            {
                if (!known.Add(file)) { duplicates++; continue; }
                cfg.Playlist.Add(file); added++;
            }
            if (added > 0) PersistActivePlaylist();
            status = $"Folder imported: {added} new MIDI(s), {duplicates} duplicates. " +
                (cfg.IncludeMidiSubfolders ? "Subfolders included." : "Top-level files only.") +
                (limited ? " Stopped at 10,000 files for safety." : "");
        }
        catch (Exception error) { status = "Folder scan failed: " + error.Message; }
    }

    private int GetToneForLane(MidiLaneKey key)
    {
        if (!string.IsNullOrWhiteSpace(loaded) &&
            cfg.GuitarTonesByFile.TryGetValue(loaded, out var choices) &&
            choices.TryGetValue(GuitarToneOverrides.Key(key), out int tone) &&
            GuitarToneOverrides.IsValid(tone))
            return tone;
        return GuitarToneOverrides.UseCurrent;
    }

    private void SetToneForLane(MidiLaneKey key, int tone)
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        if (!cfg.GuitarTonesByFile.TryGetValue(loaded, out var choices))
        {
            choices = new Dictionary<string, int>();
            cfg.GuitarTonesByFile[loaded] = choices;
        }
        string id = GuitarToneOverrides.Key(key);
        if (GuitarToneOverrides.IsValid(tone)) choices[id] = tone;
        else choices.Remove(id);
        if (choices.Count == 0) cfg.GuitarTonesByFile.Remove(loaded);
        cfg.Save();
    }

    private IReadOnlyDictionary<MidiLaneKey, int> GetActiveToneOverrides()
    {
        var result = new Dictionary<MidiLaneKey, int>();
        if (string.IsNullOrWhiteSpace(loaded)) return result;
        foreach (var lane in lanes.Where(x => x.Include))
        {
            int tone = GetToneForLane(lane.Key);
            if (GuitarToneOverrides.IsValid(tone)) result[lane.Key] = tone;
        }
        return result;
    }

    private void DrawGuitarTones()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), "GUITAR TONE OVERRIDE BY TRACK");
        ImGui.TextWrapped("While performing with an electric guitar, select a different tone for each MIDI track/channel. The player changes the guitar tone just before each matching note-on, without sending keyboard input into chat.");
        bool enabled = cfg.GuitarToneOverridesEnabled;
        if (ImGui.Checkbox("Enable per-track guitar-tone overrides", ref enabled))
        {
            cfg.GuitarToneOverridesEnabled = enabled; cfg.Save();
        }
        bool detected = PerformanceOutput.HasGuitarToneAddon();
        ImGui.TextColored(detected ? new Vector4(.5f, 1f, .6f, 1f) : new Vector4(1f, .75f, .4f, 1f),
            detected ? "Guitar tone selector detected in FFXIV." :
            "No guitar tone selector detected; equip an electric guitar in Performance for tone switching.");
        if (song is null)
        {
            ImGui.TextDisabled("Load a MIDI to configure guitar tones by track/channel.");
            return;
        }
        ImGui.Separator();
        ImGui.TextUnformatted($"MIDI parts ({lanes.Count}) — scroll to assign each guitar tone:");
        ImGui.BeginChild("##GuitarPartScroll", new Vector2(0, 260), true);
        for (int i = 0; i < lanes.Count; ++i)
        {
            var lane = lanes[i];
            string title = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
            ImGui.TextUnformatted($"{title}  /  Ch {lane.Key.Channel + 1:00}" + (lane.Include ? "" : "  (muted)"));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(205);
            int current = GetToneForLane(lane.Key);
            if (ImGui.BeginCombo($"##GuitarTone{i}", GuitarToneOverrides.Display(current)))
            {
                for (int tone = -1; tone <= 4; tone++)
                {
                    bool selected = tone == current;
                    if (ImGui.Selectable(GuitarToneOverrides.Display(tone) + $"##Tone{i}_{tone}", selected))
                        SetToneForLane(lane.Key, tone);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        ImGui.EndChild();
        if (ImGui.Button("Clear tone choices for this MIDI"))
        {
            cfg.GuitarTonesByFile.Remove(loaded); cfg.Save();
        }
        ImGui.TextDisabled("Tone choices are remembered per MIDI path and separately for each track/channel.");
        ImGui.TextDisabled("Changes take effect at the next Play or Ensemble cue, not mid-song.");
        ImGui.TextWrapped("A guitar has one active tone at a time: simultaneous notes from tracks assigned different tones may be limited by FFXIV. Select 'Use current in-game tone' for tracks that should not trigger a switch.");
        ImGui.TextUnformatted($"Tone switches this playback: {plugin.Player.ToneSwitches}");
        if (!string.IsNullOrWhiteSpace(plugin.Player.GuitarWarning))
            ImGui.TextColored(new Vector4(1f, .72f, .35f, 1f), plugin.Player.GuitarWarning);
    }

    private int GetInstrumentChoice()
    {
        if (string.IsNullOrWhiteSpace(loaded)) return 0;
        return cfg.InstrumentsByFile.TryGetValue(loaded, out int id) && InstrumentCatalog.Valid(id) ? id : 0;
    }

    private void SetInstrumentChoice(int instrumentId)
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        if (!InstrumentCatalog.Valid(instrumentId)) return;
        if (instrumentId == 0) cfg.InstrumentsByFile.Remove(loaded);
        else cfg.InstrumentsByFile[loaded] = instrumentId;
        cfg.Save();
    }

    private void DrawInstruments()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), "INSTRUMENT SELECTION — PER MIDI");
        ImGui.TextWrapped("Choose which FFXIV Performance instrument should be equipped before this MIDI plays. Windbound switches in-game, waits for the 37-note Performance keyboard, then begins playback. No virtual-key typing or focus requirement.");
        ImGui.TextUnformatted($"Currently equipped: {plugin.Instruments.CurrentName}");
        if (song is null)
        {
            ImGui.TextDisabled("Load a MIDI in Songs & Playback first to store an instrument for it.");
            return;
        }
        ImGui.TextUnformatted($"MIDI: {Path.GetFileName(loaded)}");
        int choice = GetInstrumentChoice();
        ImGui.SetNextItemWidth(Math.Max(230, ImGui.GetContentRegionAvail().X * .75f));
        if (ImGui.BeginCombo("Instrument for this song", InstrumentCatalog.Display(choice)))
        {
            if (ImGui.Selectable("Keep current instrument##inst0", choice == 0)) SetInstrumentChoice(0);
            ImGui.Separator();
            foreach (var instrument in InstrumentCatalog.All)
            {
                bool selected = choice == instrument.Id;
                if (ImGui.Selectable($"{instrument.DisplayName}##ins{instrument.Id}", selected))
                    SetInstrumentChoice(instrument.Id);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        bool auto = cfg.AutoSwitchInstrumentBeforePlay;
        if (ImGui.Checkbox("Automatically switch instrument before Play", ref auto))
        {
            cfg.AutoSwitchInstrumentBeforePlay = auto;
            cfg.Save();
        }
        ImGui.TextDisabled("Choices are saved separately for every MIDI path; 'Keep current' never changes the instrument.");
        if (ImGui.Button("Switch to selected instrument NOW"))
        {
            try
            {
                if (plugin.Player.IsPlaying) throw new InvalidOperationException("Stop playback before changing instruments.");
                int target = GetInstrumentChoice();
                if (target == 0) throw new InvalidOperationException("Choose an instrument rather than Keep current.");
                plugin.Instruments.Request(target);
                status = $"Instrument switch requested: {InstrumentCatalog.Display(target)}.";
            }
            catch (Exception error) { status = "Can't switch: " + error.Message; }
        }
        ImGui.SameLine();
        ImGui.TextDisabled("Switch before ensemble cues so every performer is ready.");
        ImGui.TextWrapped(plugin.Instruments.Status);
        if (ImGui.TreeNode("Instrument switching diagnostics"))
        {
            ImGui.TextWrapped(plugin.Instruments.Diagnostics);
            if (ImGui.Button("Copy instrument diagnostics"))
                ImGui.SetClipboardText(plugin.Instruments.Status + "\n" + plugin.Instruments.Diagnostics);
            ImGui.TreePop();
        }
        ImGui.Separator();
        ImGui.TextWrapped("Electric guitars: choose Overdriven, Clean, Muted, Power Chords or Special above. Guitar Tones can still override tone per MIDI part while the song plays.");
        ImGui.TextWrapped("Automatic switching: when a different instrument is already active, Windbound requests a native Performance-window close, waits for FFXIV to exit Performance, then equips your chosen instrument through the game-owned hotbar dispatcher. No keyboard injection. If FFXIV rejects the native close on your game build, Windbound reports the reason and can continue after manual exit. Test after FFXIV updates.");
    }

    private void DrawTracksOnMainPage()
    {
        if (song is null) return;
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), $"MIDI TRACKS & OCTAVES ({lanes.Count} parts)");
        ImGui.TextDisabled("Mute/unmute each track/channel; change octave in 12-semitone steps. Same settings as the Optimiser.");
        ImGui.BeginChild("##MainMidiTracks", new Vector2(0, Math.Min(205f, 45f + lanes.Count * 32f)), true);
        for (int i = 0; i < lanes.Count; i++)
        {
            var lane = lanes[i];
            string name = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
            int count = laneNoteCounts.GetValueOrDefault(lane.Key, 0);
            bool include = lane.Include;
            if (ImGui.Checkbox($"##MainLaneEnabled{i}", ref include))
                ChangeLane(i, lane with { Include = include });
            ImGui.SameLine();
            ImGui.TextUnformatted($"T{lane.Key.Track + 1} Ch{lane.Key.Channel + 1}: {name} ({count:N0} notes)");
            ImGui.Indent(24);
            if (ImGui.SmallButton($"-12##MainLaneMinus{i}"))
                ChangeLane(i, lane with { OctaveShift = Math.Max(-48, lane.OctaveShift - 12) });
            ImGui.SameLine();
            ImGui.TextUnformatted($"Octave shift: {lane.OctaveShift:+0;-0;+0}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"+12##MainLanePlus{i}"))
                ChangeLane(i, lane with { OctaveShift = Math.Min(48, lane.OctaveShift + 12) });
            ImGui.Unindent(24);
            ImGui.Separator();
        }
        ImGui.EndChild();
        if (ImGui.SmallButton("Recalculate suggested octaves"))
        {
            var suggestion = MidiOptimization.Suggest(song).ToDictionary(x => x.Key, x => x.OctaveShift);
            lanes = lanes.Select(l => l with { OctaveShift = suggestion.GetValueOrDefault(l.Key, 0) }).ToList();
            RememberOctaves(); Rebuild();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton("Reset all octaves to 0"))
        {
            lanes = lanes.Select(l => l with { OctaveShift = 0 }).ToList();
            RememberOctaves(); Rebuild();
        }
        ImGui.TextDisabled("Playable octave shifts: -48 to +48 semitones, by 12. Edits affect the next playback.");
    }

    public void CancelPendingInstrumentStart()
    {
        pendingInstrumentSong = null;
        plugin.Instruments.Cancel();
    }

    public void PollInstrumentStart()
    {
        if (pendingInstrumentSong is not { } pending) return;
        if (!string.Equals(pending.Path, loaded, StringComparison.OrdinalIgnoreCase) ||
            !outputArmed || plugin.Player.IsPlaying)
        {
            pendingInstrumentSong = null;
            return;
        }
        if (plugin.Instruments.HasFailed)
        {
            pendingInstrumentSong = null;
            status = "Instrument switch failed; playback not started: " + plugin.Instruments.Status;
            return;
        }
        // An interrupted request or missing Performance window should not
        // leave Windbound permanently waiting. The switcher itself controls its
        // bounded native retries; do not forcibly reset the user's instrument.
        double secondsWaiting = (Stopwatch.GetTimestamp() - pending.StartedQpc) /
            (double)Stopwatch.Frequency;
        if (secondsWaiting > 55)
        {
            pendingInstrumentSong = null;
            status = $"Instrument preparation timed out after {secondsWaiting:0}s: " +
                plugin.Instruments.Diagnostics + " Open Performance and press Play to retry.";
            return;
        }
        if (plugin.Instruments.InProgress) return;
        if (!plugin.Instruments.IsReadyFor(pending.InstrumentId))
        {
            pendingInstrumentSong = null;
            status = "Instrument selection stopped without confirming the requested instrument. " +
                plugin.Instruments.Diagnostics + " Press Play to retry.";
            return;
        }
        if (!plugin.IsGameOutputSafe()) return;
        pendingInstrumentSong = null;
        try { StartSongCore(null); }
        catch (Exception error) { status = "Could not play after instrument switch: " + error.Message; }
    }

    private void StartSong(long? ensembleStartQpc = null)
    {
        try
        {
            if (arranged is null) throw new InvalidOperationException("Load and prepare a MIDI first.");
            if (plugin.Player.IsPlaying)
            {
                if (!ensembleStartQpc.HasValue && plugin.Player.IsStopPending)
                {
                    if (!songFlow.HasPending) songFlow.QueueStartLoaded();
                    status = "Finishing the previous song's note releases; playback will start automatically.";
                    return;
                }
                throw new InvalidOperationException("Stop current playback before playing again.");
            }
            if (pendingInstrumentSong is not null)
            {
                status = "The instrument change is already preparing this MIDI; playback will begin when ready.";
                return;
            }

            int wanted = GetInstrumentChoice();
            if (outputArmed && cfg.AutoSwitchInstrumentBeforePlay && wanted != 0 &&
                !plugin.Instruments.IsReadyFor(wanted))
            {
                if (ensembleStartQpc.HasValue)
                    throw new InvalidOperationException("Switch instrument in the Instruments tab BEFORE ensemble cue; timed starts cannot wait for instrument changes.");
                if (plugin.Instruments.InProgress)
                {
                    if (plugin.Instruments.TargetId != wanted)
                        throw new InvalidOperationException("Another instrument is being equipped. Finish that change before starting this MIDI.");
                }
                else plugin.Instruments.Request(wanted);
                pendingInstrumentSong = new PendingInstrumentSong(loaded, wanted, Stopwatch.GetTimestamp());
                status = $"Preparing {InstrumentCatalog.Display(wanted)}; MIDI will start automatically when Performance is ready.";
                return;
            }
            StartSongCore(ensembleStartQpc);
        }
        catch (Exception error) { status = "Can't start: " + error.Message; }
    }

    private void StartSongCore(long? ensembleStartQpc)
    {
        if (arranged is null) throw new InvalidOperationException("No prepared MIDI available.");
        if (outputArmed && !plugin.IsGameOutputSafe())
            throw new InvalidOperationException("Open expanded 37-note Bard Performance before playing. Focus is not required.");
        plugin.Player.Start(arranged.Notes, new BardPlaybackSettings(outputArmed,
            cfg.PerformanceKeys, cfg.PlaybackSpeed, cfg.KeyHoldMs,
            cfg.GuitarToneOverridesEnabled, GetActiveToneOverrides()), plugin.IsGameOutputSafe,
            ensembleStartQpc);
        songFlow.MarkStarted();
        status = ensembleStartQpc.HasValue ? "Ensemble queued using shared Windows clock; no focus requirement." :
            outputArmed ? "Playing with direct FFXIV Performance callbacks (no keyboard output)." :
            "Preview playback: no notes sent to FFXIV.";
    }

    private void TestNote()
    {
        try
        {
            if (!outputArmed) throw new InvalidOperationException("Arm output in Songs & Playback first.");
            if (!plugin.IsGameOutputSafe()) throw new InvalidOperationException("Open full 37-note Performance mode first.");
            plugin.Player.QueueTestNote(60, cfg.KeyHoldMs, plugin.IsGameOutputSafe);
            status = "C4 test queued. This does not generate a Windows keystroke.";
        }
        catch (Exception error) { status = "C4 test failed: " + error.Message; }
    }

    private void NextSong()
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo)
        {
            ensembleStatus = "Ensemble: select the next song on every client, then conductor sends a new cue.";
            if (plugin.Player.IsPlaying) plugin.Player.Stop();
            return;
        }
        if (cfg.Playlist.Count == 0) { status = "The active Solo playlist is empty."; return; }
        if (plugin.Player.IsPlaying)
        {
            songFlow.QueueNext();
            CancelPendingInstrumentStart();
            plugin.Player.Stop();
            status = "Releasing notes before the next song...";
            return;
        }
        songFlow.Cancel();
        AdvancePlaylist();
    }

    private void AdvancePlaylist()
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo || cfg.Playlist.Count == 0) return;
        int start = activePlaylist;
        int skipped = 0;
        // Try each entry at most once. Missing/broken files are skipped rather
        // than accidentally replaying the old MIDI or looping forever.
        for (int attempt = 1; attempt <= cfg.Playlist.Count; attempt++)
        {
            int index = ((start + attempt) % cfg.Playlist.Count + cfg.Playlist.Count) % cfg.Playlist.Count;
            string path = cfg.Playlist[index];
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                skipped++;
                continue;
            }
            if (!Load(path) || arranged is null || arranged.Notes.Count == 0)
            {
                // An all-muted song can still be loaded manually to edit its
                // tracks; Auto Next simply skips it instead of falling silent.
                skipped++;
                continue;
            }
            StartSong(); // May queue a native instrument change first.
            if (skipped > 0) status += $" (Skipped {skipped} unavailable MIDI(s).)";
            return;
        }
        status = $"No playable MIDI in this playlist. Skipped {skipped} entries; select or add a valid song.";
    }

    private void ExportOptimized()
    {
        if (song is null || string.IsNullOrEmpty(loaded)) return;
        try
        {
            string directory = Path.GetDirectoryName(loaded) ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            string stem = Path.GetFileNameWithoutExtension(loaded) + " - FFXIV C3-C6";
            string target = Path.Combine(directory, stem + ".mid");
            for (int i = 2; File.Exists(target); i++) target = Path.Combine(directory, $"{stem} ({i}).mid");
            MidiOptimizationExporter.Export(loaded, target, song, GetOptimiseOptions());
            status = $"Exported and verified: {target}";
            fileInput = target;
        }
        catch (Exception error) { status = "Export failed: " + error.Message; }
    }

    private string GetKeyTable() => string.Join(Environment.NewLine,
        Enumerable.Range(48, 37).Select(p => $"{NoteMapper.GetNoteName(p),-4} = {cfg.PerformanceKeys.ElementAtOrDefault(p - 48)}"));

    private static string PartKey(MidiLaneKey key) => $"{key.Track}:{key.Channel}";

    private void LoadPartyAssignmentsForFile()
    {
        partyAssignments.Clear();
        if (string.IsNullOrWhiteSpace(loaded) ||
            !cfg.PartyAssignmentsByFile.TryGetValue(loaded, out var saved)) return;
        foreach (var lane in lanes)
            if (saved.TryGetValue(PartKey(lane.Key), out ulong id) && id != 0)
                partyAssignments[lane.Key] = id;
    }

    private void StorePartyAssignments()
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        if (partyAssignments.Count == 0) cfg.PartyAssignmentsByFile.Remove(loaded);
        else cfg.PartyAssignmentsByFile[loaded] = partyAssignments.ToDictionary(
            p => PartKey(p.Key), p => p.Value);
        cfg.Save();
    }

    private void ApplyPartyAssignmentsToLocal()
    {
        if (song is null || plugin.Player.IsPlaying) return;
        ulong localId = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
        if (localId == 0 && partyAssignments.Count > 0)
        {
            assignmentStatus = "Can't determine local character identity; assignment not applied.";
            return;
        }
        cfg.EnsemblePartsByFile.TryGetValue(loaded, out var manual);
        bool changed = false;
        bool assignmentsAuthoritative = partyAssignments.Count > 0 ||
            ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
             cfg.FollowConductorAssignments && partyAssignmentsReceivedForCurrentMidi);
        lanes = lanes.Select(lane =>
        {
            bool include = assignmentsAuthoritative
                ? (partyAssignments.TryGetValue(lane.Key, out ulong cid) && cid == localId)
                : manual is null ? lane.Key.Channel != 9 : manual.Contains(PartKey(lane.Key));
            if (include != lane.Include) changed = true;
            return lane with { Include = include };
        }).ToList();
        if (changed) Rebuild();
    }

    private bool TryPublishPartyAssignments()
    {
        if (string.IsNullOrWhiteSpace(loaded) || currentSongHash == 0 ||
            (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor) return false;
        try
        {
            var revision = plugin.Assignments.Publish(cfg.EnsembleGroup, currentSongHash, partyAssignments);
            assignmentStatus = $"Published {partyAssignments.Count} MIDI part assignment(s) to local Ensemble Stage (rev {revision}).";
            return true;
        }
        catch (Exception ex)
        {
            assignmentStatus = "Couldn't publish local part assignments: " + ex.Message;
            return false;
        }
    }

    private void SetPartyAssignment(MidiLaneKey lane, ulong contentId)
    {
        if (plugin.Player.IsPlaying)
        {
            assignmentStatus = "STOP the song before changing ensemble parts.";
            return;
        }
        if (contentId == 0) partyAssignments.Remove(lane);
        else partyAssignments[lane] = contentId;
        StorePartyAssignments();
        ApplyPartyAssignmentsToLocal();
        TryPublishPartyAssignments();
        if (cfg.MultiDeviceEnabled && plugin.Network.IsHosting) TryShareNetworkSession();
    }

    private void DrawPartyAssignmentControls(EnsembleRole role)
    {
        if (role == EnsembleRole.Solo) return;
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), "PARTY-ROSTER TRACK ASSIGNMENTS");
        var choices = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        if (role == EnsembleRole.Conductor)
        {
            if (song is null)
            {
                ImGui.TextDisabled("Load an Ensemble MIDI to choose party members for each part.");
                return;
            }
            if (choices.Count == 0)
                ImGui.TextDisabled("No party members detected. Enter a party or log in as a character.");
            ImGui.BeginChild("##ConductorPartyAssignments", new Vector2(0, Math.Min(265f, 44f + 46f * lanes.Count)), true);
            foreach (var lane in lanes)
            {
                string name = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
                int count = laneNoteCounts.GetValueOrDefault(lane.Key);
                ImGui.TextUnformatted($"T{lane.Key.Track + 1} / Ch{lane.Key.Channel + 1}: {name} ({count:N0} notes)");
                ImGui.SetNextItemWidth(Math.Min(330, Math.Max(190, ImGui.GetContentRegionAvail().X)));
                ulong selected = partyAssignments.GetValueOrDefault(lane.Key);
                string label = selected == 0 ? "Unassigned / silent" :
                    choices.FirstOrDefault(x => x.ContentId == selected)?.Name ?? "Player no longer in party";
                if (ImGui.BeginCombo($"##PartyForTrack{lane.Key.Track}_{lane.Key.Channel}", label))
                {
                    if (ImGui.Selectable("Unassigned / silent", selected == 0)) SetPartyAssignment(lane.Key, 0);
                    foreach (var member in choices)
                    {
                        if (ImGui.Selectable((member.IsLocal ? "[Me] " : "") + member.Name +
                            $"##Party{lane.Key.Track}_{lane.Key.Channel}_{member.ContentId}", selected == member.ContentId))
                            SetPartyAssignment(lane.Key, member.ContentId);
                    }
                    ImGui.EndCombo();
                }
                ImGui.Separator();
            }
            ImGui.EndChild();
            if (ImGui.SmallButton("Assign all parts to me"))
            {
                ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
                if (me == 0) assignmentStatus = "Could not identify local performer.";
                else if (!plugin.Player.IsPlaying)
                {
                    partyAssignments.Clear();
                    foreach (var lane in lanes.Where(x => x.Key.Channel != 9)) partyAssignments[lane.Key] = me;
                    StorePartyAssignments(); ApplyPartyAssignmentsToLocal(); TryPublishPartyAssignments();
                }
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Clear party assignments") && !plugin.Player.IsPlaying)
            {
                partyAssignments.Clear();
                StorePartyAssignments(); ApplyPartyAssignmentsToLocal(); TryPublishPartyAssignments();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Publish assignments")) TryPublishPartyAssignments();
            ImGui.TextDisabled(cfg.MultiDeviceEnabled
                ? "Assignments are sent to connected multi-device followers. Stop the song before editing."
                : "Assignments are saved for this MIDI and shared with same-PC Windbound clients.");
        }
        else
        {
            bool follow = cfg.FollowConductorAssignments;
            if (ImGui.Checkbox("Follow conductor's party-track assignments", ref follow))
            {
                cfg.FollowConductorAssignments = follow;
                if (!follow)
                {
                    partyAssignmentsReceivedForCurrentMidi = false;
                    partyAssignments.Clear();
                    ApplyPartyAssignmentsToLocal();
                }
                cfg.Save();
                lastPartyPollTicks = 0;
            }
            var me = choices.FirstOrDefault(x => x.IsLocal);
            ImGui.TextUnformatted("My party character: " + (me?.Name ?? "Not resolved"));
            ImGui.TextDisabled("The conductor's assigned parts override your manual track checkboxes before the cue.");
        }
        ImGui.TextWrapped(assignmentStatus);
    }

    // Poll on Dalamud's framework thread, throttled to five times per second.
    public void PollPartyAssignments()
    {
        if (cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower ||
            !cfg.FollowConductorAssignments || song is null || plugin.Player.IsPlaying) return;
        long now = Stopwatch.GetTimestamp();
        if (now - lastPartyPollTicks < Stopwatch.Frequency / 5) return;
        lastPartyPollTicks = now;
        try
        {
            var snapshot = plugin.Assignments.Poll(cfg.EnsembleGroup);
            if (snapshot is null) return;
            if (snapshot.SongHash != currentSongHash)
            {
                assignmentStatus = "Conductor is assigning a different MIDI. Load the same file.";
                return;
            }
            partyAssignments.Clear();
            foreach (var pair in snapshot.Parts) partyAssignments[pair.Key] = pair.Value;
            partyAssignmentsReceivedForCurrentMidi = true;
            ApplyPartyAssignmentsToLocal();
            assignmentStatus = $"Received conductor's track map: {lanes.Count(x => x.Include)} part(s) assigned to me.";
        }
        catch (Exception ex) { assignmentStatus = "Party assignment receive error: " + ex.Message; }
    }

    private void DrawEnsemble()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), "NUNU'S ENSEMBLE STAGE");
        ImGui.TextWrapped("CONDUCTOR: assign MIDI tracks to party members. Local Mode shares a Windows clock on one PC; Multi-Device joins another player's Windbound Bard over a private LAN / Tailscale connection.");
        ImGui.TextDisabled("For Multi-Device, both players need v0.9, the same stage name and one-time private code. This does not use game chat or override FFXIV's own Ready Check.");
        int mode = Math.Clamp(cfg.EnsembleMode, 0, 2);
        if (ImGui.RadioButton("Solo", mode == 0)) SetEnsembleRole(EnsembleRole.Solo);
        ImGui.SameLine();
        if (ImGui.RadioButton("Conductor", mode == 1)) SetEnsembleRole(EnsembleRole.Conductor);
        ImGui.SameLine();
        if (ImGui.RadioButton("Follower", mode == 2)) SetEnsembleRole(EnsembleRole.Follower);
        string group = cfg.EnsembleGroup;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText("Ensemble stage / group", ref group, 50))
        {
            cfg.EnsembleGroup = group;
            waitingForEnsembleCue = false; // re-arm when changing groups
            plugin.Network.Stop(); // must re-pair with the new stage
            cfg.Save();
        }
        if (mode == (int)EnsembleRole.Conductor)
        {
            int lead = cfg.EnsembleLeadMilliseconds;
            if (ImGui.SliderInt("Count-in before start (ms)", ref lead, 2000, 15000))
            {
                cfg.EnsembleLeadMilliseconds = lead; cfg.Save();
            }
        }
        ImGui.Separator();
        DrawMultiDeviceControls((EnsembleRole)mode);
        ImGui.Separator();
        DrawPartyAssignmentControls((EnsembleRole)mode);
        ImGui.Separator();
        ImGui.TextUnformatted("Parts assigned to THIS Bard client:");
        if (partyAssignments.Count > 0 && mode != 0)
            ImGui.TextDisabled("Party assignments are active; conductor controls the note lanes. Manual checkboxes may be replaced.");
        if (song is null) ImGui.TextDisabled("Load a song first; both clients must load the SAME MIDI file.");
        else
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                string title = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track+1}";
                bool selected = lane.Include;
                if (ImGui.Checkbox($"{title} / Channel {lane.Key.Channel+1}##ensLane{i}", ref selected))
                    ChangeLane(i, lane with { Include = selected });
                ImGui.SameLine();
                if (ImGui.SmallButton($"Only this##ensSolo{i}"))
                {
                    lanes = lanes.Select((x, j) => x with { Include = j == i }).ToList();
                    RememberParts(); Rebuild();
                }
            }
            if (ImGui.SmallButton("Enable all pitched parts"))
            {
                lanes = lanes.Select(x => x with { Include = x.Key.Channel != 9 }).ToList();
                RememberParts(); Rebuild();
            }
            ImGui.TextDisabled($"{lanes.Count(x => x.Include)} part(s) enabled; per-file selection saved separately for each client.");
        }
        ImGui.Separator();
        if (mode == (int)EnsembleRole.Conductor)
        {
            if (ImGui.Button("Cue ensemble start")) CueEnsemble();
            ImGui.SameLine();
            if (ImGui.Button(cfg.MultiDeviceEnabled ? "STOP connected bards" : "STOP all local bards")) StopPlayback();
            ImGui.TextDisabled("Have followers click 'Wait for cue' and arm output; Multi-Device waits for remote READY before starting.");
        }
        if (mode == (int)EnsembleRole.Follower)
        {
            bool wait = waitingForEnsembleCue;
            if (ImGui.Checkbox("Wait for conductor cue", ref wait))
            {
                waitingForEnsembleCue = wait;
                ensembleStatus = wait ? (cfg.MultiDeviceEnabled
                    ? "Waiting for a network cue from conductor..." : "Waiting for a NEW cue in this Windows session...")
                    : "Follower not waiting.";
            }
            ImGui.TextDisabled("Output must be ARMed in Songs & Playback; expanded Performance keyboard must be open.");
        }
        if (mode == 0) ImGui.TextDisabled("Switch to Conductor or Follower to use synchronized starts.");
        ImGui.TextWrapped(ensembleStatus);
        if (ImGui.SmallButton("Copy ensemble diagnostics"))
        {
            ImGui.SetClipboardText($"Mode: {(EnsembleRole)cfg.EnsembleMode}\nStage: {cfg.EnsembleGroup}\n" +
                $"MIDI: {Path.GetFileName(loaded)}\nParty members: {Plugin.Party.Length}\n" +
                $"Assigned tracks in conductor map: {partyAssignments.Count}\n" +
                $"Tracks enabled locally: {lanes.Count(x => x.Include)}\n" +
                $"Follow conductor map: {cfg.FollowConductorAssignments}\n" +
                $"Assignment status: {assignmentStatus}\nCue status: {ensembleStatus}\n" +
                $"Network enabled: {cfg.MultiDeviceEnabled}\nNetwork status: {plugin.Network.Status}\n" +
                $"Network revision: {plugin.Network.SessionRevision}\nClock RTT: {plugin.Network.RoundTripMs:0}ms");
        }
        if (plugin.Player.IsEnsemblePlayback && plugin.Player.EnsembleCountInSeconds > 0)
            ImGui.TextColored(new Vector4(.56f, 1f, .87f, 1f),
                $"Shared-clock count-in: {plugin.Player.EnsembleCountInSeconds:0.0}s");
    }

    private void SetEnsembleRole(EnsembleRole role)
    {
        if (cfg.EnsembleMode != (int)role)
        {
            plugin.Network.Stop();
            networkRevision = 0;
            networkSongHash = "";
            networkArrangement = null;
        }
        int oldPurpose = SavedPlaylistLibrary.PurposeForRole(cfg.EnsembleMode);
        int newPurpose = SavedPlaylistLibrary.PurposeForRole((int)role);
        if (oldPurpose != newPurpose)
        {
            try
            {
                // Save the previous working playlist before changing mode.
                StopPlayback();
                SavedPlaylistLibrary.ChangeMode(cfg, newPurpose);
                ClearLoadedMidiForPlaylistChange();
                playlistRenameInput = SavedPlaylistLibrary.GetActive(cfg).Name;
                playlistMessage = $"Switched to {(newPurpose == 0 ? "Solo" : "Ensemble")} playlist: {playlistRenameInput}.";
            }
            catch (Exception ex)
            {
                status = "Could not change playlist mode: " + ex.Message;
                return;
            }
        }
        cfg.EnsembleMode = (int)role;
        waitingForEnsembleCue = false;
        songFlow.Cancel();
        ensembleStatus = role switch
        {
            EnsembleRole.Conductor => "Conductor: load a MIDI and cue other local bards.",
            EnsembleRole.Follower => "Follower: load the same MIDI, arm, then wait for cue.",
            _ => "Solo playback."
        };
        cfg.Save();
    }

    private void CueEnsemble()
    {
        try
        {
            if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor)
                throw new InvalidOperationException("Select Conductor first.");
            if (cfg.MultiDeviceEnabled)
            {
                CueNetworkEnsemble();
                return;
            }
            bool conductingWithoutNotes = partyAssignments.Count > 0 &&
                !partyAssignments.Values.Contains(PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party));
            if (!conductingWithoutNotes && (!outputArmed || !plugin.IsGameOutputSafe()))
                throw new InvalidOperationException("ARM output and open the full Performance keyboard first.");
            if (arranged is null || song is null || string.IsNullOrEmpty(loaded))
                throw new InvalidOperationException("Load and prepare a MIDI first.");
            if (plugin.Player.IsPlaying) throw new InvalidOperationException("STOP current playback before another cue.");
            // Publish the roster's assignment map before the cue so follower clients
            // can apply their designated lanes on the next framework tick.
            if (partyAssignments.Count > 0)
            {
                ApplyPartyAssignmentsToLocal();
                if (!TryPublishPartyAssignments())
                    throw new IOException("Party assignments could not be shared with local followers. " + assignmentStatus);
            }
            long start = EnsembleCoordinator.StartAfterMilliseconds(cfg.EnsembleLeadMilliseconds);
            bool silentConductor = partyAssignments.Count > 0 && !lanes.Any(x => x.Include);
            if (!silentConductor)
            {
                // Prepare local performance first: a failed start must not cue other clients.
                StartSong(start);
                if (!plugin.Player.IsPlaying) return;
            }
            var cue = plugin.Ensemble.Publish(cfg.EnsembleGroup, EnsembleCommand.Start,
                start, currentSongHash, (int)Math.Round(cfg.PlaybackSpeed * 1000));
            ensembleStatus = silentConductor
                ? $"Silent conductor cue sent! No tracks allocated locally. Sequence {cue.Sequence}."
                : $"Cue sent! Group '{cfg.EnsembleGroup}', start in {cfg.EnsembleLeadMilliseconds/1000.0:0.0}s. Sequence {cue.Sequence}.";
        }
        catch (Exception error)
        {
            if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
            ensembleStatus = "Ensemble cue failed: " + error.Message;
        }
    }

    /// <summary>Polled from Dalamud's framework thread; no foreground focus is required.</summary>
    public void PollEnsemble()
    {
        if (cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !waitingForEnsembleCue)
            return;
        try
        {
            var cue = plugin.Ensemble.Poll(cfg.EnsembleGroup);
            if (!cue.HasValue) return;
            if (cue.Value.Command == EnsembleCommand.Stop)
            {
                if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
                ensembleStatus = "Conductor sent STOP; waiting for next cue.";
                return;
            }
            if (arranged is null || song is null)
            {
                ensembleStatus = "Cue ignored: load the conductor's MIDI first.";
                return;
            }
            if (cue.Value.SongHash != currentSongHash)
            {
                ensembleStatus = "Cue ignored: MIDI file differs from conductor's song. Load the same file.";
                return;
            }
            if (cue.Value.SpeedThousand != (int)Math.Round(cfg.PlaybackSpeed * 1000))
            {
                ensembleStatus = "Cue ignored: Playback speed differs from conductor. Set the same speed.";
                return;
            }
            if (cfg.FollowConductorAssignments && !partyAssignmentsReceivedForCurrentMidi)
            {
                ensembleStatus = "Cue ignored: no conductor part map received. Publish assignments, or disable Follow conductor assignments for manual ensemble mode.";
                return;
            }
            double remaining = EnsembleCoordinator.RemainingSeconds(cue.Value.StartQpc);
            if (remaining < 0.2)
            {
                ensembleStatus = "Cue arrived too late. Ask conductor to use a longer count-in.";
                return;
            }
            if (plugin.Player.IsPlaying)
            {
                ensembleStatus = "Cue ignored: already playing. Stop before next cue.";
                return;
            }
            if (!lanes.Any(l => l.Include) || arranged.Notes.Count == 0)
            {
                ensembleStatus = "Cue received, but no MIDI tracks are assigned to this Bard. Remaining silent.";
                return;
            }
            if (!outputArmed || !plugin.IsGameOutputSafe())
            {
                ensembleStatus = "Cue ignored: ARM output and enter expanded Performance first.";
                return;
            }
            StartSong(cue.Value.StartQpc);
            ensembleStatus = plugin.Player.IsPlaying
                ? $"Received matching cue! Shared-clock start in {remaining:0.0}s."
                : "Could not start after cue: " + status;
        }
        catch (Exception error)
        {
            ensembleStatus = "Ensemble receiver: " + error.Message;
        }
    }

    public void StopPlayback()
    {
        songFlow.Cancel();
        CancelPendingInstrumentStart();
        plugin.Player.Stop();
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
        {
            try
            {
                if (cfg.MultiDeviceEnabled)
                {
                    plugin.Network.BroadcastStop();
                    ensembleStatus = "Network STOP sent to connected followers.";
                }
                else
                {
                    plugin.Ensemble.Publish(cfg.EnsembleGroup, EnsembleCommand.Stop,
                        0, currentSongHash, (int)Math.Round(cfg.PlaybackSpeed * 1000));
                    ensembleStatus = "STOP sent to all local Windbound followers.";
                }
            }
            catch (Exception error) { ensembleStatus = "Stopped locally; group STOP failed: " + error.Message; }
        }
    }

    private RemoteArrangementSettings GetNetworkArrangement() => new(
        cfg.RepairOctaves, cfg.ChordStrategy, cfg.GlobalTranspose, cfg.MinimumGapMs,
        cfg.ArpeggioSpacingMs, lanes.ToDictionary(l => PartKey(l.Key), l => l.OctaveShift));

    private bool TryShareNetworkSession()
    {
        if (!cfg.MultiDeviceEnabled || !plugin.Network.IsHosting || song is null || string.IsNullOrEmpty(loaded)) return false;
        try
        {
            bool changed = plugin.Network.PublishSession(loaded, GetNetworkArrangement(),
                partyAssignments.ToDictionary(p => PartKey(p.Key), p => p.Value));
            if (changed) networkStatus = "MIDI/part map sent; followers must acknowledge the updated revision.";
            return true;
        }
        catch (Exception ex) { networkStatus = "Could not share MIDI: " + ex.Message; return false; }
    }

    private void DrawMultiDeviceControls(EnsembleRole role)
    {
        bool multi = cfg.MultiDeviceEnabled;
        if (ImGui.Checkbox("Enable Multi-Device (two or more PCs)", ref multi))
        {
            plugin.Network.Stop();
            cfg.MultiDeviceEnabled = multi;
            waitingForEnsembleCue = false;
            networkRevision = 0;
            networkSongHash = "";
            networkArrangement = null;
            networkStatus = multi ? "Multi-Device enabled. Host or join a private stage below." : "Local same-PC ensemble mode.";
            cfg.Save();
        }
        if (!multi)
        {
            ImGui.TextDisabled("LOCAL mode: all Windbound Bard clients share the same Windows PC. No network connection required.");
            return;
        }
        ImGui.TextColored(new Vector4(.58f, .94f, .98f, 1f), "PRIVATE MULTI-DEVICE ENSEMBLE");
        ImGui.TextWrapped("Best for Edea on another PC: connect both PCs with Tailscale, use the conductor's Tailscale IP, and share the private code directly. Ordinary TCP data is not encrypted by this plugin.");
        int port = cfg.MultiDevicePort;
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt("TCP port", ref port))
        {
            cfg.MultiDevicePort = Math.Clamp(port, 1024, 65535);
            cfg.Save();
        }
        ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
        ImGui.InputText("Private duet code (not saved)", ref networkCode, 128);
        ImGui.SameLine();
        if (ImGui.SmallButton("Generate code")) networkCode = MultiDeviceEnsemble.NewSessionCode();
        if (role == EnsembleRole.Conductor)
        {
            if (!plugin.Network.IsHosting)
            {
                if (ImGui.Button("HOST private ensemble"))
                {
                    try
                    {
                        plugin.Network.Host(cfg.MultiDevicePort, cfg.EnsembleGroup, networkCode);
                        networkStatus = "Hosting. Tell Edea your private IP, port, stage and code.";
                        TryShareNetworkSession();
                    }
                    catch (Exception ex) { networkStatus = "Host failed: " + ex.Message; }
                }
            }
            else
            {
                if (ImGui.Button("Stop hosting / disconnect"))
                {
                    StopPlayback();
                    plugin.Network.Stop();
                    networkStatus = "Hosting stopped.";
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Send updated MIDI + assignments")) TryShareNetworkSession();
                ImGui.TextUnformatted("Connected Windbound performers:");
                var performers = plugin.Network.Performers;
                if (performers.Count == 0) ImGui.TextDisabled("No followers connected yet.");
                foreach (var peer in performers)
                {
                    var color = peer.Ready ? new Vector4(.45f, .96f, .58f, 1f) : new Vector4(1f, .77f, .38f, 1f);
                    ImGui.TextColored(color, $"{peer.Character} — {(peer.Ready ? "READY" : "WAITING")} (rev {peer.Revision})");
                    if (!peer.Ready) ImGui.TextWrapped("   " + peer.Detail);
                }
            }
        }
        else if (role == EnsembleRole.Follower)
        {
            ImGui.SetNextItemWidth(Math.Min(370, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputText("Conductor LAN / Tailscale IP", ref networkHost, 255))
            {
                cfg.MultiDeviceHostAddress = networkHost;
                cfg.Save();
            }
            if (!plugin.Network.IsConnected && !plugin.Network.IsJoining)
            {
                if (ImGui.Button("JOIN conductor"))
                {
                    try
                    {
                        ulong id = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
                        string name = Plugin.Objects.LocalPlayer?.Name.TextValue ?? "";
                        plugin.Network.Join(networkHost, cfg.MultiDevicePort, cfg.EnsembleGroup, networkCode, name, id);
                        networkStatus = "Joining. Waiting for the conductor to share MIDI and tracks.";
                    }
                    catch (Exception ex) { networkStatus = "Join failed: " + ex.Message; }
                }
            }
            else if (ImGui.Button("Disconnect from conductor"))
            {
                if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
                plugin.Network.Stop();
                networkStatus = "Disconnected.";
            }
            ImGui.TextDisabled($"Clock synchronisation: {(plugin.Network.HasClockSync ? "READY" : "waiting for samples")} / {plugin.Network.RoundTripMs:0}ms RTT");
            ImGui.TextDisabled("MIDI is received into your plugin cache, never added to your saved Solo/Ensemble playlists.");
        }
        else ImGui.TextDisabled("Choose Conductor to HOST or Follower to JOIN.");
        ImGui.TextWrapped(plugin.Network.Status);
        if (!string.IsNullOrEmpty(networkStatus)) ImGui.TextWrapped(networkStatus);
        ImGui.TextDisabled("Connection code is only kept in memory. Reconnect after restarting FFXIV. Never expose TCP port publicly.");
    }

    private void CueNetworkEnsemble()
    {
        if (!plugin.Network.IsHosting) throw new InvalidOperationException("HOST a private ensemble first.");
        if (song is null || arranged is null || string.IsNullOrWhiteSpace(loaded))
            throw new InvalidOperationException("Load a MIDI first.");
        if (partyAssignments.Count == 0)
            throw new InvalidOperationException("Assign the MIDI tracks to yourself and Edea in the party dropdowns first.");
        if (plugin.Player.IsPlaying) throw new InvalidOperationException("STOP the current song before starting another duet.");
        if (!TryShareNetworkSession())
            throw new InvalidOperationException("MIDI/assignment sharing failed: " + networkStatus);
        if (!plugin.Network.CanCue(out string reason))
            throw new InvalidOperationException("Followers are not ready: " + reason);
        // A five-second minimum allows for network latency and framework scheduling jitter.
        int countIn = Math.Max(5000, cfg.EnsembleLeadMilliseconds);
        long localStart = EnsembleCoordinator.StartAfterMilliseconds(countIn);
        double hostStartMs = localStart * 1000d / Stopwatch.Frequency;
        bool silent = !lanes.Any(x => x.Include);
        if (!silent)
        {
            if (!outputArmed || !plugin.IsGameOutputSafe())
                throw new InvalidOperationException("ARM output and open the full Bard Performance keyboard.");
            StartSong(localStart);
            if (!plugin.Player.IsPlaying)
                throw new InvalidOperationException("Local playback could not be prepared: " + status);
        }
        try { plugin.Network.BroadcastStart(hostStartMs, (int)Math.Round(cfg.PlaybackSpeed * 1000)); }
        catch { if (plugin.Player.IsPlaying) plugin.Player.Stop(); throw; }
        ensembleStatus = $"Multi-Device cue sent to {plugin.Network.Performers.Count} follower(s)! Starts in {countIn/1000.0:0.0}s.";
    }

    private bool FollowerReady(out string why)
    {
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !cfg.MultiDeviceEnabled)
            { why = "Not in Multi-Device Follower mode."; return false; }
        if (!plugin.Network.IsConnected) { why = "Not connected to conductor."; return false; }
        if (networkRevision <= 0 || string.IsNullOrEmpty(networkSongHash) || song is null || arranged is null)
            { why = "Waiting for conductor's MIDI."; return false; }
        // Use the already-calculated full SHA-256 fingerprint rather than reading a MIDI
        // from disk every 500 ms on the FFXIV framework thread.
        if (Convert.FromHexString(networkSongHash) is var sha &&
            currentSongHash != BitConverter.ToInt64(sha, 0))
            { why = "Loaded song differs from conductor."; return false; }
        if (cfg.FollowConductorAssignments && !partyAssignmentsReceivedForCurrentMidi)
            { why = "Waiting for party-track assignments."; return false; }
        if (!waitingForEnsembleCue) { why = "Enable Wait for conductor cue."; return false; }
        if (!plugin.Network.HasClockSync) { why = "Synchronising clocks..."; return false; }
        if (plugin.Player.IsPlaying) { why = "Already playing; STOP before cue."; return false; }
        if (arranged.Notes.Count == 0) { why = "No notes are assigned to your Bard."; return false; }
        if (!outputArmed || !plugin.IsGameOutputSafe()) { why = "ARM output and open expanded Performance mode."; return false; }
        int wanted = GetInstrumentChoice();
        if (cfg.AutoSwitchInstrumentBeforePlay && wanted != 0 && !plugin.Instruments.IsReadyFor(wanted))
            { why = $"Equip {InstrumentCatalog.Display(wanted)} before cue."; return false; }
        why = "Ready to perform.";
        return true;
    }

    /// <summary>Called on the Dalamud framework thread even when the window is closed or game is alt-tabbed.</summary>
    public void PollNetwork()
    {
        while (plugin.Network.TryDequeue(out var notice))
        {
            try
            {
                switch (notice.Kind)
                {
                    case NetworkNoticeKind.Connected:
                    case NetworkNoticeKind.Error:
                        networkStatus = notice.Detail;
                        break;
                    case NetworkNoticeKind.Disconnected:
                        networkStatus = notice.Detail;
                        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
                        {
                            networkRevision = 0;
                            networkSongHash = "";
                            if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
                        }
                        else if (cfg.MultiDeviceEnabled && (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor &&
                                 plugin.Player.IsEnsemblePlayback)
                        {
                            plugin.Player.Stop();
                            plugin.Network.BroadcastStop();
                            ensembleStatus = "A network performer disconnected; ensemble stopped to avoid an incomplete duet.";
                        }
                        break;
                    case NetworkNoticeKind.Session:
                        if (!cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower ||
                            notice.Midi is null || notice.Arrangement is null || notice.Assignments is null) break;
                        ReceiveNetworkSession(notice);
                        break;
                    case NetworkNoticeKind.Cue:
                        if (cfg.MultiDeviceEnabled && (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower)
                            ReceiveNetworkCue(notice);
                        break;
                    case NetworkNoticeKind.Stop:
                        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower && plugin.Player.IsEnsemblePlayback)
                            plugin.Player.Stop();
                        ensembleStatus = "Network conductor sent STOP; wait for next cue.";
                        break;
                }
            }
            catch (Exception ex) { networkStatus = "Network stage error: " + ex.Message; }
        }
        if (!cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower ||
            !plugin.Network.IsConnected) return;
        long now = Stopwatch.GetTimestamp();
        if (now - lastNetworkReadyQpc < Stopwatch.Frequency / 2) return;
        lastNetworkReadyQpc = now;
        try
        {
            bool ready = FollowerReady(out string reason);
            plugin.Network.SendReady(networkRevision, networkSongHash, ready, reason);
            if (!ready) networkStatus = "Follower not ready: " + reason;
            else networkStatus = "READY — received MIDI, assigned parts, synced clock and armed output.";
        }
        catch (Exception ex) { networkStatus = "Follower status check: " + ex.Message; }
    }

    private void ReceiveNetworkSession(NetworkNotice notice)
    {
        if (notice.Midi is null || notice.Arrangement is null || notice.Assignments is null) return;
        if (plugin.Player.IsPlaying) plugin.Player.Stop();
        string directory = Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "NetworkMIDICache");
        Directory.CreateDirectory(directory);
        // NEVER treat a remote pathname as a filesystem location. The SHA is verified
        // before reception and is used as the entire cached filename.
        string cached = Path.Combine(directory, notice.Hash.ToUpperInvariant() + ".mid");
        if (!File.Exists(cached) || !string.Equals(MultiDeviceEnsemble.Sha256(File.ReadAllBytes(cached)), notice.Hash,
                StringComparison.OrdinalIgnoreCase))
            File.WriteAllBytes(cached, notice.Midi);
        // Loading a remote file never adds it to the currently selected saved playlist.
        Load(cached);
        if (song is null || currentSongHash != EnsembleCoordinator.FingerprintFile(cached))
            throw new InvalidDataException("Network MIDI failed to load.");
        networkRevision = notice.Revision;
        networkSongHash = notice.Hash;
        networkArrangement = notice.Arrangement;
        lanes = lanes.Select(l => l with {
            OctaveShift = notice.Arrangement.Octaves.TryGetValue(PartKey(l.Key), out int shift)
                && shift is >= -48 and <= 48 && shift % 12 == 0 ? shift : l.OctaveShift
        }).ToList();
        partyAssignments.Clear();
        foreach (var (key, contentId) in notice.Assignments)
        {
            string[] parts = key.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int track) &&
                int.TryParse(parts[1], out int channel) && track is >= 0 and <= 10000 && channel is >= 0 and <= 15 &&
                contentId != 0) partyAssignments[new MidiLaneKey(track, channel)] = contentId;
        }
        partyAssignmentsReceivedForCurrentMidi = true;
        ApplyPartyAssignmentsToLocal();
        Rebuild();
        assignmentStatus = $"Network conductor assigned {lanes.Count(x => x.Include)} track(s) to my character.";
        networkStatus = $"Received {notice.Name}, song revision {notice.Revision}; awaiting ready check.";
    }

    private void ReceiveNetworkCue(NetworkNotice notice)
    {
        if (!waitingForEnsembleCue || networkRevision != notice.Revision ||
            !string.Equals(networkSongHash, notice.Hash, StringComparison.OrdinalIgnoreCase))
        {
            ensembleStatus = "Ignored network START: follower not waiting, or MIDI revision does not match.";
            return;
        }
        if (!FollowerReady(out string reason))
        {
            ensembleStatus = "Ignored network START: " + reason;
            return;
        }
        if (!plugin.Network.TryConvertHostTimeToLocalQpc(notice.HostStartMs, out long target))
        {
            ensembleStatus = "Ignored network START: clock not synchronized.";
            return;
        }
        double remaining = EnsembleCoordinator.RemainingSeconds(target);
        if (remaining < 0.5)
        {
            ensembleStatus = "START cue arrived late; ask conductor for a longer count-in.";
            return;
        }
        float speed = notice.SpeedThousand / 1000.0f;
        if (speed is < 0.5f or > 2f)
        {
            ensembleStatus = "Ignored network START: invalid playback speed.";
            return;
        }
        cfg.PlaybackSpeed = speed;
        cfg.Save();
        StartSong(target);
        ensembleStatus = plugin.Player.IsPlaying
            ? $"Network ensemble starting in {remaining:0.0}s (clock RTT {plugin.Network.RoundTripMs:0}ms)!"
            : "Could not start network ensemble: " + status;
    }

    private void DrawRoll()
    {
        if (arranged is null || arranged.Notes.Count == 0) return;
        float width = Math.Max(400f, ImGui.GetContentRegionAvail().X);
        const float height = 170;
        Vector2 top = ImGui.GetCursorScreenPos();
        Vector2 bottom = top + new Vector2(width, height);
        var draw = ImGui.GetWindowDrawList();
        uint background = ImGui.ColorConvertFloat4ToU32(new Vector4(.07f, .06f, .12f, .93f));
        uint guides = ImGui.ColorConvertFloat4ToU32(new Vector4(.29f, .24f, .36f, 1f));
        uint noteColor = ImGui.ColorConvertFloat4ToU32(new Vector4(.98f, .34f, .68f, 1f));
        uint currentColor = ImGui.ColorConvertFloat4ToU32(new Vector4(.35f, .95f, .93f, 1f));
        draw.AddRectFilled(top, bottom, background);
        for (int i = 0; i < 37; i += 12)
        {
            float y = top.Y + height * (36 - i) / 37f;
            draw.AddLine(new Vector2(top.X, y), new Vector2(bottom.X, y), guides);
        }
        double now = plugin.Player.IsPlaying ? plugin.Player.PositionMs : 0;
        double from = Math.Max(0, now - 750);
        const double duration = 7600;
        foreach (BardNote note in arranged.Notes)
        {
            if (note.TimeMs < from - 500 || note.TimeMs > from + duration) continue;
            float x = top.X + (float)((note.TimeMs - from) / duration * width);
            float w = Math.Clamp((float)(note.DurationMs / duration * width), 3, width * .18f);
            float y = top.Y + (84 - note.Pitch) * height / 37f;
            draw.AddRectFilled(new Vector2(Math.Clamp(x, top.X, bottom.X), y),
                new Vector2(Math.Clamp(x + w, top.X, bottom.X), y + Math.Max(3f, height / 45f)), noteColor);
        }
        float cursor = top.X + (float)((now - from) / duration * width);
        draw.AddLine(new Vector2(cursor, top.Y), new Vector2(cursor, bottom.Y), currentColor, 2f);
        ImGui.Dummy(new Vector2(width, height));
        ImGui.TextDisabled("Piano roll: C6 (top) to C3 (bottom) | 7.6s window");
    }
}
