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
    private static string MidiFilter => Loc.IsGerman ? "MIDI-Dateien{.mid,.midi}"
        : Loc.IsFrench ? "Fichiers MIDI{.mid,.midi}" : "MIDI files{.mid,.midi}";
    private MidiSong? song;
    private BardArrangement? arranged;
    private List<MidiLanePlan> lanes = new();
    private Dictionary<MidiLaneKey, int> laneNoteCounts = new();
    private string loaded = "";
    private string fileInput;
    private string status = "Welcome to Windbound Bard. Load a MIDI or the bundled calibration file.";
    // Diagnostic information for support. No files are sent automatically.
    private string lastMidiLoadDiagnostics = "No MIDI load errors recorded this session.";
    private bool outputArmed; // intentionally never persisted
    private readonly SongFlowCoordinator songFlow = new();
    private int activePlaylist = -1;
    private bool showPianoRoll = true;
    private string playlistFilter = "";
    private string playlistTagFilter = "";
    private bool playlistFavoritesOnly;
    private string playlistTagsInput = "";
    private string playlistTagsTarget = "";
    private List<string> missingFileCandidates = new();
    private string pendingQueuedSongPath = "";
    private string playlistNameInput = "";
    private string playlistRenameInput = "";
    private bool copyPlaylistOnCreate;
    private string playlistMessage = "";
    private readonly Dictionary<MidiLaneKey, ulong> partyAssignments = new();
    private string partyLineupStatus = "Assign performers once; subsequent Ensemble MIDI files will reuse the lineup.";
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

    public MainWindow(Plugin plugin) : base(Loc.T("Windbound Bard | Nunu's MIDI Studio") + "###NunuWindboundBard")
    {
        this.plugin = plugin;
        cfg = plugin.Config;
        fileInput = cfg.LastFile;
        soundFontInput = cfg.PreviewSoundFontPath ?? "";
        networkHost = cfg.MultiDeviceHostAddress;
        playlistRenameInput = SavedPlaylistLibrary.GetActive(cfg).Name;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(540, 410), MaximumSize = new Vector2(1400, 1300)
        };
    }

    public void Dispose()
    {
        plugin.AudioPreview.Stop();
        DisposeMemberPortraitCapture();
        fileDialogs.Reset();
    }
    public void DrawFileDialogs() => fileDialogs.Draw();

    public override void Draw()
    {
        // Process asynchronous portrait capture/save completion regardless of
        // which Windbound tab is currently visible.
        PollMemberPortraitTasks();
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), Loc.T("NUNU'S WINDBOUND BARD   /windbound"));
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("FFXIV • C3–C6 • Dalamud 15 • Native Ensemble + Soulweaver"));
        if (ImGui.SmallButton(Loc.T(plugin.IsHudVisible ? "Hide Performance HUD##windbound" : "Show Performance HUD##windbound")))
            plugin.SetHudVisible(!plugin.IsHudVisible);
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("/windbound hud  |  independent per-client HUD"));
        // Language changes immediately; place the selector on its own row so it
        // remains accessible even with Dalamud UI scaling and narrow windows.
        ImGui.TextUnformatted(Loc.T("Interface language:"));
        ImGui.SameLine();
        ImGui.SetNextItemWidth(145);
        string selectedLanguageName = cfg.Language switch
        {
            "fr" => "Français",
            "de" => "Deutsch",
            _ => "English"
        };
        if (ImGui.BeginCombo("##windboundLocale", selectedLanguageName))
        {
            if (ImGui.Selectable("English##lang-en", cfg.Language == "en")) SetLanguage("en");
            if (ImGui.Selectable("Français##lang-fr", cfg.Language == "fr")) SetLanguage("fr");
            if (ImGui.Selectable("Deutsch##lang-de", cfg.Language == "de")) SetLanguage("de");
            ImGui.EndCombo();
        }
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T(status));
        ImGui.TextWrapped(Loc.T(plugin.Player.Status));
        if (!string.IsNullOrWhiteSpace(plugin.Player.GuitarWarning))
            ImGui.TextColored(new Vector4(1f, .75f, .38f, 1f), Loc.T(plugin.Player.GuitarWarning));
        ImGui.TextColored(plugin.IsGameOutputSafe() ? new Vector4(.49f, .95f, .55f, 1f) : new Vector4(1f, .75f, .38f, 1f),
            Loc.T(plugin.IsGameOutputSafe() ? "Performance: FULL 37-KEY READY" :
            plugin.IsPerforming ? "Open expanded 37-note Performance keyboard for direct output" : "Performance mode not detected (preview still works)"));

        if (ImGui.BeginTabBar("WindboundTabs"))
        {
            if (ImGui.BeginTabItem(Loc.T("Songs & Playback"))) { DrawPlayer(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Audio Preview"))) { DrawAudioPreview(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Ensemble"))) { DrawEnsemble(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Stage Formations"))) { DrawStageFormations(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("FFXIV Optimiser"))) { DrawOptimizer(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Concert Snapshots"))) { DrawConcertSnapshots(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("MIDI Inspector"))) { DrawArrangementInspector(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Soulweaver Studio"))) { DrawSoulweaver(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Instruments"))) { DrawInstruments(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Live MIDI Piano"))) { DrawLiveMidiInput(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Guitar Tones"))) { DrawGuitarTones(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Direct Output & Test"))) { DrawKeyMapping(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("About / Setup"))) { DrawHelp(); ImGui.EndTabItem(); }
            ImGui.EndTabBar();
        }
    }

    private void SetLanguage(string language)
    {
        cfg.Language = language is "fr" or "de" ? language : "en";
        Loc.SetLanguage(cfg.Language);
        WindowName = Loc.T("Windbound Bard | Nunu's MIDI Studio") + "###NunuWindboundBard";
        plugin.RefreshHudLanguage();
        cfg.Save();
    }

    private void DrawPlayer()
    {
        if (ImGui.SmallButton(Loc.T(plugin.IsHudVisible ? "Hide Performance HUD" : "Show Performance HUD")))
            plugin.SetHudVisible(!plugin.IsHudVisible);
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("/windbound hud | per-character compact control, movable for multiple accounts"));
        ImGui.TextUnformatted(Loc.T("Load a Standard MIDI File (.mid / .midi), format 0 or 1:"));
        ImGui.SetNextItemWidth(-235);
        ImGui.InputText("##MidiPath", ref fileInput, 1024);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Load typed path"))) LoadFromUser(fileInput);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Browse MIDI..."))) BrowseMidiToLoad();
        if (ImGui.Button(Loc.T("Load sample")))
        {
            string sample = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "Bard-37-Note-Calibration.mid");
            if (File.Exists(sample)) LoadFromUser(sample);
            else status = "Sample is included in the source ZIP under Samples. Enter its full path to load it.";
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Load chord test")))
        {
            string sample = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "Bard-Chord-Callback-Test.mid");
            if (File.Exists(sample)) LoadFromUser(sample);
            else status = "Chord test is included in Samples. Enter its full path to load it.";
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Add MIDI files..."))) BrowseMidiToAdd();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Add MIDI folder..."))) BrowseMidiFolder();
        ImGui.TextDisabled(Loc.T("Or use the typed path above:"));
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Add typed file"))) AddToPlaylist(fileInput);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Add typed folder"))) AddFolder(fileInput);
        bool subfolders = cfg.IncludeMidiSubfolders;
        if (ImGui.Checkbox(Loc.T("Include subfolders when importing MIDI folders"), ref subfolders))
        {
            cfg.IncludeMidiSubfolders = subfolders;
            cfg.Save();
        }

        ImGui.Separator();
        DrawSavedPlaylistControls();
        ImGui.Separator();
        DrawSmartPlaylistControls();
        bool autoNext = cfg.AutoNext;
        if (ImGui.Checkbox(Loc.T("Auto next##SmartPlaylist"), ref autoNext)) { cfg.AutoNext = autoNext; cfg.Save(); }
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo)
            ImGui.TextDisabled(Loc.T("Auto next is suspended in Ensemble mode; cue each song together."));

        ImGui.Separator();
        if (song is not null && arranged is not null)
        {
            ImGui.TextUnformatted(Loc.T($"Loaded: {Path.GetFileName(loaded)}  |  {song.Tracks.Count} tracks  |  {song.InitialBpm:0.#} BPM  |  {song.DurationMs / 1000:0.0}s"));
            ImGui.TextUnformatted(Loc.T($"Bard arrangement: {arranged.Report.FinalNotes:N0} notes  |  {arranged.Report.ChordGroups:N0} chord groups  |  {arranged.Report.TimingDrops:N0} dense-event drops"));
        }
        else ImGui.TextDisabled(Loc.T("No song loaded."));

        if (song is not null)
        {
            ImGui.TextUnformatted(Loc.T($"Instrument for this MIDI: {InstrumentCatalog.Display(GetInstrumentChoice())}  |  Equipped: {plugin.Instruments.CurrentName}"));
            ImGui.TextDisabled(Loc.T("Change the instrument in the Instruments tab. Track octave controls are below."));
            DrawTracksOnMainPage();
        }

        bool arm = outputArmed;
        if (ImGui.Checkbox(Loc.T("ARM: Direct FFXIV Performance notes (no typing into chat)"), ref arm))
        {
            outputArmed = arm;
            if (!arm)
            {
                plugin.LiveMidi.Panic();
                CancelPendingInstrumentStart();
                if (plugin.Player.IsPlaying) plugin.Player.Stop();
            }
        }
        ImGui.TextDisabled(Loc.T(outputArmed ? "Armed: direct game callbacks. Alt-tab and chat are NOT keyboard output targets." : "Safe preview mode: no keys sent to the game."));

        if (ImGui.Button(Loc.T(cfg.GameEnsembleEnabled && cfg.EnsembleMode != (int)EnsembleRole.Solo
                ? "START ENSEMBLE (native)" : "Play"))) StartSong();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T(plugin.Player.IsPaused ? "Resume" : "Pause"))) plugin.Player.TogglePause();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("STOP"))) StopPlayback();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Next"))) NextSong();
        // Explicit local recovery: never starts music, issues party PLAY or
        // overwrites saved playlists. Safe if a prior test/cue was left active.
        if (ImGui.SmallButton(Loc.T("EMERGENCY: Reset stuck playback##windboundRecover")))
            RecoverLocalPlayback();
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Also: /windbound recover"));

        if (ImGui.TreeNode(Loc.T("Song switching diagnostics")))
        {
            string details = "Loaded MIDI: " + (string.IsNullOrWhiteSpace(loaded) ? "(none)" : loaded) + "\n" +
                $"Playlist entry: {activePlaylist + 1}/{cfg.Playlist.Count}; Solo: {cfg.EnsembleMode == (int)EnsembleRole.Solo}; Auto Next: {cfg.AutoNext}\n" +
                $"Playing: {plugin.Player.IsPlaying}; Stop pending: {plugin.Player.IsStopPending}; Last completed: {plugin.Player.CompletedSuccessfully}\n" +
                $"Queued action: {songFlow.PendingDescription}\n" +
                $"Instrument: {plugin.Instruments.CurrentName}; Requested: {(pendingInstrumentSong is null ? "None" : InstrumentCatalog.Display(pendingInstrumentSong.InstrumentId))}\n" +
                $"Output armed: {outputArmed}; Performance ready: {plugin.IsGameOutputSafe()}\n" +
                $"Playback: {plugin.Player.Status}\n" +
                $"Instrument switch: {plugin.Instruments.Status}\n" +
                $"Stage navigation active: {stageNavigator.Active}; Stage status: {stageNavigator.Status}\n" +
                $"Native ensemble: {plugin.GameEnsemble.ReadyCheckStatus}\n" +
                $"Windbound: {status}";
            ImGui.TextWrapped(Loc.T(details));
            if (ImGui.Button(Loc.T("Copy song switching diagnostics"))) ImGui.SetClipboardText(details);
            ImGui.TreePop();
        }
        if (ImGui.TreeNode(Loc.T("MIDI loading diagnostics (for sharing errors)")))
        {
            ImGui.TextWrapped(Loc.T(lastMidiLoadDiagnostics));
            if (ImGui.Button(Loc.T("Copy MIDI loading diagnostics"))) ImGui.SetClipboardText(lastMidiLoadDiagnostics);
            ImGui.TextDisabled(Loc.T("The copied text can contain a local file path. Review it before sharing."));
            ImGui.TreePop();
        }

        float speed = cfg.PlaybackSpeed;
        if (ImGui.SliderFloat(Loc.T("Playback speed"), ref speed, .5f, 2f, "%.2fx"))
        {
            cfg.PlaybackSpeed = speed; cfg.Save();
        }
        if (song != null)
        {
            float fraction = (float)Math.Clamp(plugin.Player.PositionMs / Math.Max(1, song.DurationMs), 0, 1);
            ImGui.ProgressBar(fraction, new Vector2(-1, 0), Loc.T($"{fraction * 100:0}%"));
            // Direct right-click, not a seek: STOP + note release + rewind to 00:00.
            // Never seek only the drawing of the progress bar; reset the scheduler.
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(Loc.T("Right-click to STOP and reset this MIDI to 00:00 (local player only)."));
                if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) ResetCurrentMidiToStart();
            }
            if (ImGui.SmallButton(Loc.T("Reset MIDI to start (00:00)"))) ResetCurrentMidiToStart();
            ImGui.SameLine();
            ImGui.TextDisabled(Loc.T("or right-click the progress bar"));
            ImGui.TextUnformatted(Loc.T($"{plugin.Player.NotesPlayed:N0} notes played / {arranged?.Notes.Count ?? 0:N0}"));
            if (ImGui.Checkbox(Loc.T("Show piano roll"), ref showPianoRoll)) { }
            if (showPianoRoll) DrawRoll();
        }
    }

    /// <summary>
    /// Stop and rewind only this client. A conductor deliberately does not send a
    /// remote STOP here: ensemble members should re-cue together after a reset.
    /// </summary>
    private void ResetCurrentMidiToStart()
    {
        if (song is null) return;
        songFlow.Cancel();                 // no Auto Next / stale pending song selection
        pendingQueuedSongPath = "";
        CancelPendingInstrumentStart();    // no delayed automatic start after rewind
        gameStartGate.Cancel("MIDI reset locally to 00:00; request a fresh Ensemble Ready Check.");
        plugin.Player.RewindToStart();
        status = cfg.EnsembleMode == (int)EnsembleRole.Solo
            ? "MIDI reset to 00:00. Press Play when ready."
            : "MIDI reset locally to 00:00. Other Bards were not changed; conductor must cue the ensemble again.";
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
                AdvancePlaylist(action.PlayAfterLoad);
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
        pendingQueuedSongPath = "";
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

    private void SelectPlaylistSong(string path)
    {
        pendingQueuedSongPath = "";
        LoadFromUser(path);
    }

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
        nativeLoadedSha = "";
        CancelNativeLeaderSync("Playlist switched. Select a MIDI to resynchronise the party.");
        currentSongHash = 0;
        fileInput = "";
        activePlaylist = -1;
        pendingQueuedSongPath = "";
        playlistTagsTarget = "";
        waitingForEnsembleCue = false;
        songFlow.Cancel();
        plugin.Player.RewindToStart();
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
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), Loc.T($"SAVED {kind} PLAYLISTS"));
        if (ImGui.SmallButton(Loc.T("Solo library")) && cfg.EnsembleMode != (int)EnsembleRole.Solo)
            SetEnsembleRole(EnsembleRole.Solo);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Ensemble library")) && cfg.EnsembleMode == (int)EnsembleRole.Solo)
            SetEnsembleRole(EnsembleRole.Conductor);
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T("Changing libraries saves the previous one automatically."));
        ImGui.SetNextItemWidth(Math.Min(420, Math.Max(220, ImGui.GetContentRegionAvail().X - 160)));
        if (ImGui.BeginCombo(Loc.T("Playlist library##StoredList"), Loc.T(current.Name)))
        {
            foreach (var profile in cfg.SavedPlaylists.Where(x => x.Purpose == purpose))
            {
                if (ImGui.Selectable(Loc.T($"{profile.Name} ({profile.Files.Count} songs)##Saved{profile.Id}"), profile.Id == current.Id))
                    SwitchSavedPlaylist(profile.Id);
            }
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Save playlist")))
        {
            PersistActivePlaylist();
            playlistMessage = $"Saved {current.Name} ({cfg.Playlist.Count} MIDIs).";
        }
        ImGui.SetNextItemWidth(210);
        ImGui.InputText(Loc.T("New playlist name"), ref playlistNameInput, 80);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Create playlist")))
        {
            try
            {
                var sourceState = GetActiveSmartPlaylist();
                var profile = SavedPlaylistLibrary.NewProfile(cfg, playlistNameInput, purpose, copyPlaylistOnCreate);
                if (copyPlaylistOnCreate)
                {
                    var copyState = SmartPlaylistManager.Get(cfg.SmartPlaylistsById, profile.Id);
                    copyState.Favorites = sourceState.Favorites.ToList();
                    copyState.Tags = sourceState.Tags.ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                }
                PersistActivePlaylist();
                SwitchSavedPlaylist(profile.Id);
                playlistNameInput = "";
            }
            catch (Exception ex) { playlistMessage = "Create failed: " + ex.Message; }
        }
        bool copy = copyPlaylistOnCreate;
        if (ImGui.Checkbox(Loc.T("Copy songs from current playlist into new playlist"), ref copy)) copyPlaylistOnCreate = copy;
        ImGui.SetNextItemWidth(210);
        ImGui.InputText(Loc.T("Rename current list"), ref playlistRenameInput, 80);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Rename")))
        {
            try { SavedPlaylistLibrary.Rename(cfg, current.Id, playlistRenameInput);
                cfg.Save(); playlistRenameInput = current.Name;
                playlistMessage = $"Renamed playlist to {current.Name}."; }
            catch (Exception ex) { playlistMessage = "Rename failed: " + ex.Message; }
        }
        if (ImGui.Button(Loc.T("Export playlist JSON..."))) ExportSavedPlaylist();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Import playlist JSON..."))) ImportSavedPlaylist();
        ImGui.TextDisabled(Loc.T("Solo and Ensemble playlists are separate. Switching modes restores that mode's saved library."));
        if (!string.IsNullOrWhiteSpace(playlistMessage)) ImGui.TextWrapped(Loc.T(playlistMessage));
    }

    private void ExportSavedPlaylist()
    {
        var profile = SavedPlaylistLibrary.GetActive(cfg);
        string stem = string.Concat(profile.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        fileDialogs.SaveFileDialog(Loc.T("Export Windbound playlist"), ".json", stem + ".json", ".json",
            (bool ok, string path) =>
            {
                if (!ok || string.IsNullOrWhiteSpace(path)) return;
                try
                {
                    if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) path += ".json";
                    SavedPlaylistLibrary.Export(path, profile, cfg.Playlist, GetActiveSmartPlaylist());
                    playlistMessage = "Exported playlist: " + path;
                }
                catch (Exception ex) { playlistMessage = "Export failed: " + ex.Message; }
            }, GetFileDialogStartFolder());
    }

    private void ImportSavedPlaylist()
    {
        fileDialogs.OpenFileDialog(Loc.T("Import Windbound playlist"), ".json",
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
                    var importedState = SmartPlaylistManager.Get(cfg.SmartPlaylistsById, profile.Id);
                    importedState.Favorites = imported.Favorites.Where(path => profile.Files.Contains(path, StringComparer.OrdinalIgnoreCase)).ToList();
                    importedState.Tags = imported.Tags.Where(kv => profile.Files.Contains(kv.Key, StringComparer.OrdinalIgnoreCase))
                        .ToDictionary(kv => kv.Key, kv => kv.Value.ToList(), StringComparer.OrdinalIgnoreCase);
                    importedState.Shuffle = imported.Shuffle;
                    importedState.Repeat = imported.Repeat;
                    PersistActivePlaylist();
                    SwitchSavedPlaylist(profile.Id);
                    playlistMessage = $"Imported {profile.Name}: {profile.Files.Count} entries. Missing paths remain visible.";
                }
                catch (Exception ex) { playlistMessage = "Import failed: " + ex.Message; }
            }, 1, GetFileDialogStartFolder());
    }

    private void DrawOptimizer()
    {
        ImGui.TextWrapped(Loc.T("Non-destructive octave adaptation for Bard Performance (C3–C6). Full-chord mode preserves MIDI note start times and durations; FFXIV may still limit the audible overlap."));
        if (song is null)
        {
            ImGui.TextDisabled(Loc.T("Load a MIDI in Songs & Playback first.")); return;
        }
        foreach (MidiLanePlan lane in lanes.ToArray())
        {
            int index = lanes.FindIndex(x => x.Key == lane.Key);
            string name = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
            bool include = lane.Include;
            if (ImGui.Checkbox($"##lane{index}", ref include)) ChangeLane(index, lane with { Include = include });
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T($"{name} | Ch {lane.Key.Channel + 1:00}"));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T($"-12##lane{index}"))) ChangeLane(index, lane with { OctaveShift = Math.Max(-48, lane.OctaveShift - 12) });
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T($"{lane.OctaveShift:+0;-0;+0}"));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T($"+12##lane{index}"))) ChangeLane(index, lane with { OctaveShift = Math.Min(48, lane.OctaveShift + 12) });
        }
        bool repair = cfg.RepairOctaves;
        if (ImGui.Checkbox(Loc.T("Repair remaining high/low notes by octaves"), ref repair))
        {
            cfg.RepairOctaves = repair; cfg.Save(); Rebuild();
        }
        int transpose = cfg.GlobalTranspose;
        if (ImGui.SliderInt(Loc.T("Playback-only transpose (semitones)"), ref transpose, -24, 24))
        {
            cfg.GlobalTranspose = transpose; cfg.Save(); Rebuild();
        }
        int strategy = cfg.ChordStrategy;
        ImGui.TextUnformatted(Loc.T("Chord handling:"));
        if (ImGui.RadioButton(Loc.T("Full chords / original timing (experimental)"), strategy == 4)) SetStrategy(4);
        if (ImGui.RadioButton(Loc.T("Highest (melody)"), strategy == 0)) SetStrategy(0);
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Lowest"), strategy == 1)) SetStrategy(1);
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Loudest"), strategy == 2)) SetStrategy(2);
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Gentle arpeggio"), strategy == 3)) SetStrategy(3);
        int gap = cfg.MinimumGapMs;
        if (ImGui.SliderInt(Loc.T("Minimum note gap (ms)"), ref gap, 40, 160))
        {
            cfg.MinimumGapMs = gap; cfg.Save(); Rebuild();
        }
        int arp = cfg.ArpeggioSpacingMs;
        if (ImGui.SliderInt(Loc.T("Arpeggio spacing (ms)"), ref arp, 60, 180))
        {
            cfg.ArpeggioSpacingMs = arp; cfg.Save(); Rebuild();
        }
        if (arranged != null)
        {
            var report = MidiOptimization.Analyze(song, GetOptimiseOptions());
            ImGui.Separator();
            ImGui.TextUnformatted(Loc.T($"Source: {report.SourceNotes:N0} notes | {report.SourceInRange:N0} initially in range"));
            ImGui.TextUnformatted(Loc.T($"Octave-optimised: {report.PlayableNotes:N0} | Shifted: {report.ShiftedNotes:N0} | Excluded/skipped: {report.ExcludedNotes + report.OmittedNotes:N0}"));
            ImGui.TextUnformatted(Loc.T($"Prepared playback: {arranged.Report.FinalNotes:N0} notes ({arranged.Report.ChordGroups:N0} chord groups)"));
            if (ImGui.Button(Loc.T("Export new FFXIV C3-C6 MIDI"))) ExportOptimized();
            ImGui.TextDisabled(Loc.T("Exporter keeps original MIDI unchanged; arrangement modes affect playback only."));
        }
    }

    private void DrawKeyMapping()
    {
        ImGui.TextWrapped(Loc.T("Direct Performance output bypasses Windows keyboard events entirely. This 37-key map remains a MANUAL reference for playing notes yourself; the player sends pitch indices through the expanded Performance interface, even if you chat or alt-tab."));
        string keys = cfg.PerformanceKeys;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputText("##Keys", ref keys, 64))
        {
            cfg.PerformanceKeys = keys.ToUpperInvariant(); cfg.Save();
        }
        bool valid = PerformanceKeyMap.TryValidate(cfg.PerformanceKeys, out string message);
        ImGui.TextColored(valid ? new Vector4(.48f, 1f, .60f, 1f) : new Vector4(1f, .4f, .4f, 1f), Loc.T(message));
        if (ImGui.Button(Loc.T("Restore 37-key preset")))
        {
            cfg.PerformanceKeys = PerformanceKeyMap.DefaultKeys; cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("Copy bindings to clipboard")))
            ImGui.SetClipboardText(GetKeyTable());

        if (valid)
        {
            for (int i = NoteMapper.LowestNote; i <= NoteMapper.HighestNote; i += 6)
                ImGui.TextUnformatted(Loc.T(string.Join("    ", Enumerable.Range(i, Math.Min(6, NoteMapper.HighestNote + 1 - i))
                    .Select(pitch => $"{NoteMapper.GetNoteName(pitch)}={cfg.PerformanceKeys[pitch - 48]}"))));
        }
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T("Backend: native FFXIV Performance addon callbacks, not SendInput, not Windows Messages or global virtual keys. Chat input remains separate; no focus requirement."));
        ImGui.TextColored(plugin.IsExpandedKeyboard ? new Vector4(.48f, 1f, .6f, 1f) : new Vector4(1f, .42f, .42f, 1f),
            Loc.T(plugin.IsExpandedKeyboard ? "Expanded Performance addon detected" : "Expanded Performance addon not open"));
        int hold = cfg.KeyHoldMs;
        if (ImGui.SliderInt(Loc.T("Minimum note duration (ms)"), ref hold, 15, 180)) { cfg.KeyHoldMs = hold; cfg.Save(); }
        if (ImGui.Button(Loc.T("Test C4 (only when armed)"))) TestNote();
        ImGui.SameLine();
        ImGui.TextDisabled(Loc.T(outputArmed ? "Output armed" : "Arm output in Songs & Playback first"));
        ImGui.TextDisabled(Loc.T("A callback being sent does not prove the game sounded every overlapping note."));
    }

    private void DrawHelp()
    {
        ImGui.TextWrapped(Loc.T("1. In Instruments, choose the instrument for the MIDI. With auto-switch enabled, Play first closes any different Performance instrument through the game UI, equips the new one, and starts notes after the keyboard is confirmed ready. 'Keep current' retains your existing selection."));
        ImGui.TextWrapped(Loc.T("2. Songs & Playback shows every MIDI track/channel, with -12 and +12 octave buttons. Changes update the FFXIV Optimiser and are saved per song."));
        ImGui.TextWrapped(Loc.T("3. Open Bard Performance in EXPANDED (37-note) mode. Mini mode is not supported by this output adapter."));
        ImGui.TextWrapped(Loc.T("2. Load your MIDI. Optimise into C3–C6, leave Full chords selected, or choose a legacy reduction for extremely dense passages."));
        ImGui.TextWrapped(Loc.T("3. Tick ARM, test C4, then Play. The plugin calls the game Performance addon directly on each framework update. Normal chat keystrokes remain free and alt-tab is allowed."));
        ImGui.TextWrapped(Loc.T("4. Click Browse MIDI to load a file, Add MIDI files to select several files, or Add MIDI folder to import a directory (optional subfolders). All pickers are drawn inside Dalamud."));
        ImGui.TextWrapped(Loc.T("5. For electric guitar, open the Guitar Tones tab, enable Override by Track, and choose Overdriven/Clean/Muted/Power Chords/Special for each MIDI track/channel. The guitar Tone Change UI must be available in Performance."));
        ImGui.TextWrapped(Loc.T("6. /windbound stop stops the scheduler. Exported MIDI keeps the original source unchanged."));
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T("IMPORTANT: Experimental native game interface; callback behaviour can change with FFXIV patches. Some instruments may not sustain every simultaneous chord tone. Verify by ear. Third-party plugins may violate the game's terms and Performance music rules."));
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
        if (lane.OctaveShift != lanes[index].OctaveShift) MarkOctaveManual(lane.Key);
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
        if (plugin.AudioPreview.IsPlaying || plugin.AudioPreview.IsLoading)
            plugin.AudioPreview.Stop("Arrangement changed; start a fresh audio preview.");
        concertInspection = null; // report is valid only for a specific arrangement configuration
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
            if (plugin.AudioPreview.IsPlaying || plugin.AudioPreview.IsLoading)
                plugin.AudioPreview.Stop("MIDI selection changed; start a new audio preview.");
            if (!File.Exists(full)) throw new FileNotFoundException("No MIDI file at the chosen location.", full);
            if (!new[] { ".mid", ".midi" }.Contains(Path.GetExtension(full).ToLowerInvariant()))
                throw new InvalidDataException("Choose a .mid or .midi file.");
            MidiSong candidate = MidiFileReader.Load(full);
            song = candidate;
            loaded = full;
            nativeLoadedSha = GamePartyCueProtocol.GetHash(full);
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
            ApplyInitialOctaveShifts(candidate, full);
            activePlaylist = cfg.Playlist.FindIndex(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            LoadPartyAssignmentsForFile();
            partyAssignmentsReceivedForCurrentMidi = false;
            nativeConductorTrackMapExplicit = false;
            if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor && partyAssignments.Count > 0)
            {
                ApplyPartyAssignmentsToLocal();
                if (!suppressSnapshotSync) TryPublishPartyAssignments();
            }
            Rebuild();
            if (arranged is null)
                throw new InvalidDataException("The MIDI arrangement could not be prepared.");
            // Previous versions left the old song's last position visible here.
            // Auto Next calls Load and then StartSong in one framework tick, so an
            // idle rewind must complete immediately (no deferred reset).
            plugin.Player.RewindToStart();
            if (!suppressSnapshotSync && cfg.MultiDeviceEnabled && plugin.Network.IsHosting)
                TryShareNetworkSession();
            lastMidiLoadDiagnostics = $"Successfully loaded {Path.GetFileName(full)} ({candidate.Tracks.Count} MIDI tracks).";
            if (!suppressSnapshotSync) OnNativeConductorSongLoaded(); // Snapshot restore is local-only.
            return true;
        }
        catch (Exception error)
        {
            // Prevent Auto Next from ever playing the previous arrangement when
            // the next file is absent, unreadable or has no playable notes.
            song = null;
            arranged = null;
            loaded = "";
            nativeLoadedSha = "";
            lanes.Clear();
            laneNoteCounts.Clear();
            activePlaylist = -1;
            status = "Could not load MIDI: " + error.Message;
            lastMidiLoadDiagnostics = $"Load failed for: {path}\n" +
                $"Error type: {error.GetType().FullName}\n" + error;
            Plugin.Log.Warning(error, "Windbound MIDI loading failed.");
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
        fileDialogs.OpenFileDialog(Loc.T("Open MIDI for Windbound Bard"), MidiFilter,
            (bool ok, List<string> files) =>
            {
                if (ok && files.Count > 0) LoadFromUser(files[0]);
            }, 1, GetFileDialogStartFolder());
    }

    private void BrowseMidiToAdd()
    {
        // selectionCountMax=0 allows multi-select of any number of files.
        fileDialogs.OpenFileDialog(Loc.T("Add MIDI files to the playlist"), MidiFilter,
            (bool ok, List<string> files) =>
            {
                if (ok && files.Count > 0) AddFilesToPlaylist(files);
            }, 0, GetFileDialogStartFolder());
    }

    private void BrowseMidiFolder()
    {
        fileDialogs.OpenFolderDialog(Loc.T("Choose MIDI folder to import"),
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
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), Loc.T("GUITAR TONE OVERRIDE BY TRACK"));
        ImGui.TextWrapped(Loc.T("While performing with an electric guitar, select a different tone for each MIDI track/channel. The player changes the guitar tone just before each matching note-on, without sending keyboard input into chat."));
        bool enabled = cfg.GuitarToneOverridesEnabled;
        if (ImGui.Checkbox(Loc.T("Enable per-track guitar-tone overrides"), ref enabled))
        {
            cfg.GuitarToneOverridesEnabled = enabled; cfg.Save();
        }
        bool detected = PerformanceOutput.HasGuitarToneAddon();
        ImGui.TextColored(detected ? new Vector4(.5f, 1f, .6f, 1f) : new Vector4(1f, .75f, .4f, 1f),
            Loc.T(detected ? "Guitar tone selector detected in FFXIV." :
            "No guitar tone selector detected; equip an electric guitar in Performance for tone switching."));
        if (song is null)
        {
            ImGui.TextDisabled(Loc.T("Load a MIDI to configure guitar tones by track/channel."));
            return;
        }
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T($"MIDI parts ({lanes.Count}) — scroll to assign each guitar tone:"));
        ImGui.BeginChild("##GuitarPartScroll", new Vector2(0, 260), true);
        for (int i = 0; i < lanes.Count; ++i)
        {
            var lane = lanes[i];
            string title = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
            ImGui.TextUnformatted(Loc.T($"{title}  /  Ch {lane.Key.Channel + 1:00}" + (lane.Include ? "" : "  (muted)")));
            ImGui.SameLine();
            ImGui.SetNextItemWidth(205);
            int current = GetToneForLane(lane.Key);
            if (ImGui.BeginCombo($"##GuitarTone{i}", Loc.T(GuitarToneOverrides.Display(current))))
            {
                for (int tone = -1; tone <= 4; tone++)
                {
                    bool selected = tone == current;
                    if (ImGui.Selectable(Loc.T(GuitarToneOverrides.Display(tone) + $"##Tone{i}_{tone}"), selected))
                        SetToneForLane(lane.Key, tone);
                    if (selected) ImGui.SetItemDefaultFocus();
                }
                ImGui.EndCombo();
            }
        }
        ImGui.EndChild();
        if (ImGui.Button(Loc.T("Clear tone choices for this MIDI")))
        {
            cfg.GuitarTonesByFile.Remove(loaded); cfg.Save();
        }
        ImGui.TextDisabled(Loc.T("Tone choices are remembered per MIDI path and separately for each track/channel."));
        ImGui.TextDisabled(Loc.T("Changes take effect at the next Play or Ensemble cue, not mid-song."));
        ImGui.TextWrapped(Loc.T("A guitar has one active tone at a time: simultaneous notes from tracks assigned different tones may be limited by FFXIV. Select 'Use current in-game tone' for tracks that should not trigger a switch."));
        ImGui.TextUnformatted(Loc.T($"Tone switches this playback: {plugin.Player.ToneSwitches}"));
        if (!string.IsNullOrWhiteSpace(plugin.Player.GuitarWarning))
            ImGui.TextColored(new Vector4(1f, .72f, .35f, 1f), Loc.T(plugin.Player.GuitarWarning));
    }

    private int GetInstrumentChoice()
    {
        if (string.IsNullOrWhiteSpace(loaded)) return 0;
        if (cfg.InstrumentsByFile.TryGetValue(loaded, out int id) && InstrumentCatalog.Valid(id))
            return id; // Explicitly chosen by user: retain the manual preference.
        return GetMidiSoloInstrumentSuggestion();
    }

    private void SetInstrumentChoice(int instrumentId)
    {
        if (string.IsNullOrWhiteSpace(loaded)) return;
        if (!InstrumentCatalog.Valid(instrumentId)) return;
        // 0 is an explicit manual "Keep current" choice. Removing the saved
        // entry instead means "follow MIDI auto-detection"; those are different.
        cfg.InstrumentsByFile[loaded] = instrumentId;
        cfg.Save();
    }

    private void DrawInstruments()
    {
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), Loc.T("INSTRUMENT SELECTION — PER MIDI"));
        ImGui.TextWrapped(Loc.T("Choose which FFXIV Performance instrument should be equipped before this MIDI plays. Windbound switches in-game, waits for the 37-note Performance keyboard, then begins playback. No virtual-key typing or focus requirement."));
        ImGui.TextUnformatted(Loc.T($"Currently equipped: {plugin.Instruments.CurrentName}"));
        if (song is null)
        {
            ImGui.TextDisabled(Loc.T("Load a MIDI in Songs & Playback first to store an instrument for it."));
            return;
        }
        ImGui.TextUnformatted(Loc.T($"MIDI: {Path.GetFileName(loaded)}"));
        int choice = GetInstrumentChoice();
        bool manualInstrument = !string.IsNullOrWhiteSpace(loaded) &&
            cfg.InstrumentsByFile.ContainsKey(loaded);
        ImGui.TextDisabled(Loc.T(manualInstrument
            ? "Instrument preference: manual (saved for this MIDI)"
            : "Instrument preference: automatic MIDI detection"));
        if (manualInstrument && ImGui.SmallButton(Loc.T("Use detected instrument for this MIDI")))
        {
            cfg.InstrumentsByFile.Remove(loaded);
            cfg.Save();
            choice = GetInstrumentChoice();
        }
        if (DrawInstrumentPreviewIcon(choice)) ImGui.SameLine(0f, 6f);
        ImGui.SetNextItemWidth(Math.Max(210, ImGui.GetContentRegionAvail().X * .72f));
        if (ImGui.BeginCombo(Loc.T("Instrument for this song"), Loc.T(InstrumentCatalog.Display(choice))))
        {
            if (InstrumentIconSelectable(0, "Keep current instrument", "inst0", choice == 0))
                SetInstrumentChoice(0);
            ImGui.Separator();
            foreach (var instrument in InstrumentCatalog.All)
            {
                bool selected = choice == instrument.Id;
                if (InstrumentIconSelectable(instrument.Id, instrument.DisplayName,
                    "ins" + instrument.Id, selected))
                    SetInstrumentChoice(instrument.Id);
                if (selected) ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        bool auto = cfg.AutoSwitchInstrumentBeforePlay;
        if (ImGui.Checkbox(Loc.T("Automatically switch instrument before Play"), ref auto))
        {
            cfg.AutoSwitchInstrumentBeforePlay = auto;
            cfg.Save();
        }
        DrawMidiMetadataControls();
        ImGui.TextDisabled(Loc.T("Choices are saved separately for every MIDI path; 'Keep current' never changes the instrument."));
        if (ImGui.Button(Loc.T("Switch to selected instrument NOW")))
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
        ImGui.TextDisabled(Loc.T("Switch before ensemble cues so every performer is ready."));
        ImGui.TextWrapped(Loc.T(plugin.Instruments.Status));
        if (ImGui.TreeNode(Loc.T("Instrument switching diagnostics")))
        {
            ImGui.TextWrapped(Loc.T(plugin.Instruments.Diagnostics));
            if (ImGui.Button(Loc.T("Copy instrument diagnostics")))
                ImGui.SetClipboardText(plugin.Instruments.Status + "\n" + plugin.Instruments.Diagnostics);
            ImGui.TreePop();
        }
        ImGui.Separator();
        ImGui.TextWrapped(Loc.T("Electric guitars: choose Overdriven, Clean, Muted, Power Chords or Special above. Guitar Tones can still override tone per MIDI part while the song plays."));
        ImGui.TextWrapped(Loc.T("Automatic switching: Windbound stows the previous instrument with FFXIV's native Performance action (0), confirms that Performance mode has fully ended, then equips the next instrument. Electric guitar tones can switch directly. If native stow is unavailable, the UI will explain how to finish manually. No keyboard injection. Test after FFXIV updates."));
    }

    private void DrawTracksOnMainPage()
    {
        if (song is null) return;
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), Loc.T($"MIDI TRACKS & OCTAVES ({lanes.Count} parts)"));
        ImGui.TextDisabled(Loc.T("Mute/unmute each track/channel; change octave in 12-semitone steps. Same settings as the Optimiser."));
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
            ImGui.TextUnformatted(Loc.T($"T{lane.Key.Track + 1} Ch{lane.Key.Channel + 1}: {name} ({count:N0} notes)"));
            ImGui.Indent(24);
            if (ImGui.SmallButton(Loc.T($"-12##MainLaneMinus{i}")))
                ChangeLane(i, lane with { OctaveShift = Math.Max(-48, lane.OctaveShift - 12) });
            ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T($"Octave shift: {lane.OctaveShift:+0;-0;+0}"));
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T($"+12##MainLanePlus{i}")))
                ChangeLane(i, lane with { OctaveShift = Math.Min(48, lane.OctaveShift + 12) });
            ImGui.Unindent(24);
            ImGui.Separator();
        }
        ImGui.EndChild();
        if (ImGui.SmallButton(Loc.T("Recalculate suggested octaves")))
        {
            var suggestion = MidiOptimization.Suggest(song).ToDictionary(x => x.Key, x => x.OctaveShift);
            lanes = lanes.Select(l => l with { OctaveShift = suggestion.GetValueOrDefault(l.Key, 0) }).ToList();
            MarkAllOctavesManual(); RememberOctaves(); Rebuild();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Reset all octaves to 0")))
        {
            lanes = lanes.Select(l => l with { OctaveShift = 0 }).ToList();
            MarkAllOctavesManual(); RememberOctaves(); Rebuild();
        }
        ImGui.TextDisabled(Loc.T("Playable octave shifts: -48 to +48 semitones, by 12. Edits affect the next playback."));
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
            StopStageMovementBeforeMusic();
            if (cfg.GameEnsembleEnabled && cfg.EnsembleMode != (int)EnsembleRole.Solo)
            {
                if (cfg.EnsembleMode == (int)EnsembleRole.Conductor)
                    CueEnsemble();
                else status = "Native Ensemble follower: ARM, equip the instrument, and wait for the conductor's game Ready Check; it is accepted automatically.";
                return;
            }
            if (arranged is null) throw new InvalidOperationException("Load and prepare a MIDI first.");
            if (plugin.Player.IsRewindPending)
            {
                if (!ensembleStartQpc.HasValue && (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo)
                {
                    songFlow.QueueStartLoaded();
                    status = "Rewind finishing; the MIDI will start at 00:00 on the next game update.";
                    return;
                }
                throw new InvalidOperationException("Reset is still releasing notes; use a fresh ensemble cue afterward.");
            }
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
        StopStageMovementBeforeMusic();
        if (arranged is null) throw new InvalidOperationException("No prepared MIDI available.");
        if (outputArmed && !plugin.IsGameOutputSafe())
            throw new InvalidOperationException("Open expanded 37-note Bard Performance before playing. Focus is not required.");
        plugin.Player.Start(arranged.Notes, new BardPlaybackSettings(outputArmed,
            cfg.PerformanceKeys, cfg.PlaybackSpeed, cfg.KeyHoldMs,
            cfg.GuitarToneOverridesEnabled, GetActiveToneOverrides()), plugin.IsGameOutputSafe,
            ensembleStartQpc);
        songFlow.MarkStarted();
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Solo && activePlaylist >= 0 &&
            activePlaylist < cfg.Playlist.Count)
        {
            var state = GetActiveSmartPlaylist();
            SmartPlaylistManager.RecordStarted(state, loaded);
            if (string.Equals(pendingQueuedSongPath, loaded, StringComparison.OrdinalIgnoreCase))
                SmartPlaylistManager.ConsumeQueueItem(state, loaded);
            pendingQueuedSongPath = "";
            cfg.Save();
        }
        status = ensembleStartQpc.HasValue ? "Ensemble queued for a scheduled local clock start; no focus requirement." :
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
            songFlow.QueueNext(manual: true);
            CancelPendingInstrumentStart();
            plugin.Player.Stop();
            status = "Releasing notes before the next song...";
            return;
        }
        songFlow.Cancel();
        AdvancePlaylist(manual: true);
    }

    private void AdvancePlaylist(bool manual = false)
    {
        // Advance only a Solo playlist; ensemble cues and remote performers are untouched.
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Solo || cfg.Playlist.Count == 0) return;
        var state = GetActiveSmartPlaylist();
        var candidates = SmartPlaylistManager.Candidates(state, cfg.Playlist, activePlaylist, manual);
        int skipped = 0;
        foreach (var candidate in candidates)
        {
            string path = candidate.Path;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                skipped++;
                if (candidate.Origin == PlaylistCandidateOrigin.PlayNext)
                    SmartPlaylistManager.ConsumeQueueItem(state, path);
                continue;
            }
            if (!Load(path) || arranged is null || arranged.Notes.Count == 0)
            {
                skipped++;
                if (candidate.Origin == PlaylistCandidateOrigin.PlayNext)
                    SmartPlaylistManager.ConsumeQueueItem(state, path);
                continue;
            }
            pendingQueuedSongPath = candidate.Origin == PlaylistCandidateOrigin.PlayNext ? path : "";
            cfg.Save();
            StartSong(); // Instrument preflight may delay the actual start; queue consumed on successful StartSongCore.
            if (skipped > 0) status += $" (Skipped {skipped} unavailable MIDI(s).)";
            return;
        }
        cfg.Save();
        status = candidates.Count == 0
            ? "End of playlist. Choose Repeat All, add songs to Play Next, or press Play again."
            : $"No playable MIDI remained in this playlist. Skipped {skipped} entries.";
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
        partyLineupStatus = "No track lineup assigned for this MIDI yet.";
        if (string.IsNullOrWhiteSpace(loaded)) return;
        // Always honor the specific song's saved overrides before the shared lineup.
        if (cfg.PartyAssignmentsByFile.TryGetValue(loaded, out var saved) && saved is not null)
        {
            foreach (var lane in lanes)
                if (saved.TryGetValue(PartKey(lane.Key), out ulong id) && id != 0)
                    partyAssignments[lane.Key] = id;
            partyLineupStatus = Loc.IsGerman
                ? $"{partyAssignments.Count} songspezifische Spurzuweisungen wiederhergestellt."
                : Loc.IsFrench ? $"{partyAssignments.Count} affectations de pistes propres à ce morceau restaurées."
                : $"Restored {partyAssignments.Count} song-specific track assignments.";
            // On upgrade, promote the first existing conductor song map to the
            // new playlist lineup automatically. Users who already configured
            // an octet don't have to reassign or press a setup button.
            if (cfg.KeepPartyAssignmentsAcrossSongs &&
                (EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor &&
                partyAssignments.Count > 0 && lanes.Count > 0)
            {
                string id = SavedPlaylistLibrary.GetActive(cfg).Id;
                if (!cfg.EnsembleLineupsByPlaylistId.ContainsKey(id))
                {
                    cfg.EnsembleLineupsByPlaylistId[id] = PartyTrackLineup.Capture(
                        lanes.Select(l => l.Key).ToList(), partyAssignments);
                    cfg.Save();
                    partyLineupStatus += Loc.IsGerman
                        ? " Besetzung für zukünftige Songs gespeichert."
                        : Loc.IsFrench ? " Formation mémorisée pour les prochains morceaux."
                        : " Saved as the reusable lineup for future songs.";
                }
            }
            return;
        }
        // Followers only accept the conductor's authenticated native track map.
        // The shared lineup is a local CONDUCTOR convenience; it is not a new
        // independent cross-PC synchronisation or an auto-start instruction.
        if ((EnsembleRole)cfg.EnsembleMode != EnsembleRole.Conductor ||
            !cfg.KeepPartyAssignmentsAcrossSongs || lanes.Count == 0) return;
        string profileId = SavedPlaylistLibrary.GetActive(cfg).Id;
        if (!cfg.EnsembleLineupsByPlaylistId.TryGetValue(profileId, out var lineup) || lineup is null) return;
        var match = lineup.Apply(lanes.Select(l => l.Key).ToList());
        foreach (var item in match.Assignments) partyAssignments[item.Key] = item.Value;
        partyLineupStatus = match.HasDifferentLaneCount
            ? Loc.IsGerman
                ? $"{match.MatchedSlots} Musikerplätze wiederverwendet. Diese MIDI hat {match.NewLaneCount} Spuren " +
                    $"(gespeicherte Besetzung: {match.SavedLaneCount}); bitte {match.UnassignedNewSlots} neue Plätze prüfen."
                : Loc.IsFrench
                ? $"{match.MatchedSlots} emplacements réutilisés. Ce MIDI comporte {match.NewLaneCount} pistes " +
                    $"(formation enregistrée : {match.SavedLaneCount}). Vérifiez les {match.UnassignedNewSlots} nouvelles pistes."
                : $"Reused {match.MatchedSlots} performer slots from this Ensemble playlist. " +
                    $"MIDI has {match.NewLaneCount} lanes (saved layout: {match.SavedLaneCount}); " +
                    $"review the track list and assign {match.UnassignedNewSlots} new slot(s)."
            : Loc.IsGerman
                ? $"Alle {match.MatchedSlots} Musikerplätze aus der gespeicherten Ensemble-Besetzung wiederverwendet."
                : Loc.IsFrench
                ? $"Les {match.MatchedSlots} emplacements de la formation Ensemble ont été réutilisés."
                : $"Reused all {match.MatchedSlots} performer slots from the saved Ensemble lineup.";
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
        bool oneClickMapping = cfg.GameEnsembleEnabled && cfg.GameEnsembleOneClick &&
            (((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor && partyAssignments.Count > 0) ||
             ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower && cfg.OneClickFollowTrackAssignments &&
              partyAssignmentsReceivedForCurrentMidi));
        bool nativeLeaderMap = cfg.GameEnsembleEnabled && cfg.NativeLeaderSyncEnabled &&
            (partyAssignments.Count > 0 || nativeConductorTrackMapExplicit ||
             partyAssignmentsReceivedForCurrentMidi) && (((EnsembleRole)cfg.EnsembleMode) switch
            {
                EnsembleRole.Conductor => true,
                EnsembleRole.Follower => partyAssignmentsReceivedForCurrentMidi,
                _ => false
            });
        bool assignmentsAuthoritative = nativeLeaderMap || oneClickMapping || (!cfg.GameEnsembleEnabled && (partyAssignments.Count > 0 ||
            ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Follower &&
             cfg.FollowConductorAssignments && partyAssignmentsReceivedForCurrentMidi)));
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
        RememberCurrentPartyLineup();
        ApplyPartyAssignmentsToLocal();
        TryPublishPartyAssignments();
        if (cfg.MultiDeviceEnabled && plugin.Network.IsHosting) TryShareNetworkSession();
    }

    private void DrawPartyAssignmentControls(EnsembleRole role)
    {
        if (role == EnsembleRole.Solo) return;
        ImGui.TextColored(new Vector4(.9f, .65f, 1f, 1f), Loc.T("PARTY-ROSTER TRACK ASSIGNMENTS"));
        var choices = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        DrawMemberPortraitManager(choices);
        if (role == EnsembleRole.Conductor)
        {
            if (song is null)
            {
                ImGui.TextDisabled(Loc.T("Load an Ensemble MIDI to choose party members for each part."));
                return;
            }
            if (choices.Count == 0)
                ImGui.TextDisabled(Loc.T("No party members detected. Enter a party or log in as a character."));
            DrawReusablePartyLineupControls();
            ImGui.BeginChild("##ConductorPartyAssignments", new Vector2(0, Math.Min(265f, 44f + 46f * lanes.Count)), true);
            foreach (var lane in lanes)
            {
                string name = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track + 1}";
                int count = laneNoteCounts.GetValueOrDefault(lane.Key);
                ImGui.TextUnformatted(Loc.T($"T{lane.Key.Track + 1} / Ch{lane.Key.Channel + 1}: {name} ({count:N0} notes)"));
                ulong selected = partyAssignments.GetValueOrDefault(lane.Key);
                if (DrawMemberPortraitThumbnail(selected, 27f)) ImGui.SameLine(0f, 5f);
                ImGui.SetNextItemWidth(Math.Min(330, Math.Max(190, ImGui.GetContentRegionAvail().X)));
                string label = selected == 0 ? "Unassigned / silent" :
                    choices.FirstOrDefault(x => x.ContentId == selected)?.Name ?? "Player no longer in party";
                if (ImGui.BeginCombo($"##PartyForTrack{lane.Key.Track}_{lane.Key.Channel}", Loc.T(label)))
                {
                    if (ImGui.Selectable(Loc.T("Unassigned / silent"), selected == 0)) SetPartyAssignment(lane.Key, 0);
                    foreach (var member in choices)
                    {
                        if (MemberPortraitSelectable(member, $"Party{lane.Key.Track}_{lane.Key.Channel}_{member.ContentId}",
                            selected == member.ContentId))
                            SetPartyAssignment(lane.Key, member.ContentId);
                    }
                    ImGui.EndCombo();
                }
                ImGui.Separator();
            }
            ImGui.EndChild();
            if (ImGui.SmallButton(Loc.T("Assign all parts to me")))
            {
                ulong me = PartyRoster.GetLocalContentId(Plugin.Objects, Plugin.Party);
                if (me == 0) assignmentStatus = "Could not identify local performer.";
                else if (!plugin.Player.IsPlaying)
                {
                    partyAssignments.Clear();
                    foreach (var lane in lanes.Where(x => x.Key.Channel != 9)) partyAssignments[lane.Key] = me;
                    StorePartyAssignments(); RememberCurrentPartyLineup();
                    ApplyPartyAssignmentsToLocal(); TryPublishPartyAssignments();
                }
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Clear party assignments")) && !plugin.Player.IsPlaying)
            {
                partyAssignments.Clear();
                StorePartyAssignments(); ApplyPartyAssignmentsToLocal(); TryPublishPartyAssignments();
            }
            ImGui.SameLine();
            if (ImGui.SmallButton(Loc.T("Publish assignments")))
            {
                TryPublishPartyAssignments();
                if (NativeSyncEnabled) BroadcastNativeFile();
            }
            if (NativeSyncEnabled)
            {
                ImGui.SameLine();
                if (ImGui.Button(Loc.T("SET PARTY TRACKS — send to every Bard"))) SetNativePartyTracks();
                ImGui.TextDisabled(Loc.T("Once set, each client uses only its assigned parts. " +
                    "A silent/unassigned part will not play on any Bard."));
            }
            ImGui.TextWrapped(Loc.T(partyLineupStatus));
            ImGui.TextDisabled(Loc.T(cfg.GameEnsembleEnabled
                ? "Native: track assignments are sent to followers when the leader selects a MIDI; music START still comes from FFXIV."
                : cfg.MultiDeviceEnabled
                ? "Assignments are sent to connected multi-device followers. Stop the song before editing."
                : "Assignments are saved for this MIDI and shared with same-PC Windbound clients."));
        }
        else
        {
            bool follow = cfg.FollowConductorAssignments;
            if (!cfg.GameEnsembleEnabled && ImGui.Checkbox(Loc.T("Follow conductor's party-track assignments"), ref follow))
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
            if (me is not null && DrawMemberPortraitThumbnail(me.ContentId, 32f)) ImGui.SameLine();
            ImGui.TextUnformatted(Loc.T("My party character: " + (me?.Name ?? "Not resolved")));
            ImGui.TextDisabled(Loc.T(cfg.GameEnsembleEnabled
                ? "Native mode: the leader's MIDI selection can load your matching file and assigned tracks automatically."
                : "The conductor's assigned parts override your manual track checkboxes before the cue."));
            if (ImGui.TreeNode(Loc.T("Band members and assigned tracks##wbFollowerBandPortraits")))
            {
                foreach (var player in choices)
                {
                    if (DrawMemberPortraitThumbnail(player.ContentId, 30f)) ImGui.SameLine(0f, 6f);
                    int assigned = partyAssignments.Count(x => x.Value == player.ContentId);
                    ImGui.TextUnformatted(player.Name + (player.IsLocal ? " [Me]" : "") +
                        (song is null ? "" : $"  —  {assigned} lane(s)"));
                }
                ImGui.TreePop();
            }
        }
        ImGui.TextWrapped(Loc.T(assignmentStatus));
    }

    // Poll on Dalamud's framework thread, throttled to five times per second.
    public void PollPartyAssignments()
    {
        if (cfg.GameEnsembleEnabled || cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower ||
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
        ImGui.TextColored(new Vector4(1f, .51f, .80f, 1f), Loc.T("NUNU'S ENSEMBLE STAGE"));
        ImGui.TextWrapped(Loc.T("Recommended: native FFXIV Ensemble synchronisation (the same Ready Check and game START mechanism used by MidiBard 2). " +
            "Works across PCs and on the same PC without VPN. Legacy local/TCP controls remain available when native mode is disabled."));
        int mode = Math.Clamp(cfg.EnsembleMode, 0, 2);
        if (ImGui.RadioButton(Loc.T("Solo"), mode == 0)) SetEnsembleRole(EnsembleRole.Solo);
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Conductor"), mode == 1)) SetEnsembleRole(EnsembleRole.Conductor);
        ImGui.SameLine();
        if (ImGui.RadioButton(Loc.T("Follower"), mode == 2)) SetEnsembleRole(EnsembleRole.Follower);
        string group = cfg.EnsembleGroup;
        ImGui.SetNextItemWidth(260);
        if (ImGui.InputText(Loc.T("Ensemble stage / group"), ref group, 50))
        {
            cfg.EnsembleGroup = group;
            waitingForEnsembleCue = false; // re-arm when changing groups
            plugin.Network.Stop(); // must re-pair with the new stage
            cfg.Save();
        }
        if (mode == (int)EnsembleRole.Conductor)
        {
            int lead = cfg.EnsembleLeadMilliseconds;
            if (ImGui.SliderInt(Loc.T("Count-in before start (ms)"), ref lead, 2000, 15000))
            {
                cfg.EnsembleLeadMilliseconds = lead; cfg.Save();
            }
        }
        ImGui.Separator();
        DrawGameEnsembleControls((EnsembleRole)mode);
        DrawConcertDirector();
        if (!cfg.GameEnsembleEnabled) DrawMultiDeviceControls((EnsembleRole)mode);
        ImGui.Separator();
        DrawPartyAssignmentControls((EnsembleRole)mode);
        ImGui.Separator();
        ImGui.TextUnformatted(Loc.T("Parts assigned to THIS Bard client:"));
        if (partyAssignments.Count > 0 && mode != 0 && !cfg.GameEnsembleEnabled)
            ImGui.TextDisabled(Loc.T("Party assignments are active; conductor controls the note lanes. Manual checkboxes may be replaced."));
        if (song is null) ImGui.TextDisabled(Loc.T("Load a song first; each PC must have the SAME MIDI for FFXIV Game Ensemble."));
        else
        {
            for (int i = 0; i < lanes.Count; i++)
            {
                var lane = lanes[i];
                string title = song.Tracks.FirstOrDefault(t => t.Index == lane.Key.Track)?.Name ?? $"Track {lane.Key.Track+1}";
                bool selected = lane.Include;
                if (ImGui.Checkbox(Loc.T($"{title} / Channel {lane.Key.Channel+1}##ensLane{i}"), ref selected))
                    ChangeLane(i, lane with { Include = selected });
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.T($"Only this##ensSolo{i}")))
                {
                    lanes = lanes.Select((x, j) => x with { Include = j == i }).ToList();
                    RememberParts(); Rebuild();
                }
            }
            if (ImGui.SmallButton(Loc.T("Enable all pitched parts")))
            {
                lanes = lanes.Select(x => x with { Include = x.Key.Channel != 9 }).ToList();
                RememberParts(); Rebuild();
            }
            ImGui.TextDisabled(Loc.T($"{lanes.Count(x => x.Include)} part(s) enabled; per-file selection saved separately for each client."));
        }
        ImGui.Separator();
        if (mode == (int)EnsembleRole.Conductor)
        {
            if (ImGui.Button(Loc.T(cfg.GameEnsembleEnabled
                ? "START ENSEMBLE (FFXIV native)"
                : "Cue ensemble start"))) CueEnsemble();
            ImGui.SameLine();
            if (ImGui.Button(Loc.T(cfg.GameEnsembleEnabled ? "STOP my playback" :
                cfg.MultiDeviceEnabled ? "STOP connected bards" : "STOP all local bards"))) StopPlayback();
            ImGui.TextDisabled(Loc.T(cfg.GameEnsembleEnabled
                ? "FFXIV Ready Check auto-accepts on prepared participants; game sends the synchronised START. No party-chat timers."
                : "All performers must arm output before the conductor cues the local or TCP ensemble."));
        }
        if (mode == (int)EnsembleRole.Follower)
        {
            if (cfg.GameEnsembleEnabled)
            {
                ImGui.TextDisabled(Loc.T("Native Ensemble: keep MIDI loaded, ARM and open the 37-key Performance UI. Incoming Ready Checks are auto-confirmed."));
            }
            else
            {
                bool wait = waitingForEnsembleCue;
                if (ImGui.Checkbox(Loc.T("Wait for conductor cue"), ref wait))
                {
                    waitingForEnsembleCue = wait;
                    ensembleStatus = wait ? (cfg.MultiDeviceEnabled
                        ? "Waiting for a network cue from conductor..." : "Waiting for a NEW cue in this Windows session...")
                        : "Follower not waiting.";
                }
            }
            ImGui.TextDisabled(Loc.T("ARM in Songs & Playback; expanded Performance keyboard must be open."));
        }
        if (mode == 0) ImGui.TextDisabled(Loc.T("Switch to Conductor or Follower to use synchronized starts."));
        ImGui.TextWrapped(Loc.T(ensembleStatus));
        if (ImGui.SmallButton(Loc.T("Copy ensemble diagnostics")))
        {
            ImGui.SetClipboardText($"Mode: {(EnsembleRole)cfg.EnsembleMode}\nStage: {cfg.EnsembleGroup}\n" +
                $"MIDI: {Path.GetFileName(loaded)}\nParty members: {Plugin.Party.Length}\n" +
                $"Assigned tracks in conductor map: {partyAssignments.Count}\n" +
                $"Tracks enabled locally: {lanes.Count(x => x.Include)}\n" +
                $"Follow conductor map: {cfg.FollowConductorAssignments}\n" +
                $"Assignment status: {assignmentStatus}\nCue status: {ensembleStatus}\n" +
                $"Game Ensemble: {cfg.GameEnsembleEnabled}; native auto-confirm: {cfg.NativeEnsembleAutoAccept}; legacy UTC start disabled\n" +
                $"Listener: {plugin.GameEnsemble.Diagnostics}\nGate: {gameStartGate.LastOutcome}\nOutput ARMed: {outputArmed}; Expanded Performance: {plugin.IsGameOutputSafe()}\nLeader sync enabled: {cfg.NativeLeaderSyncEnabled}; Chat status: {plugin.PartyChat.Status}\n" +
                plugin.PartyChat.Diagnostics + $"\nFile sync: {nativeFileSyncStatus}\nInstrument SET: {nativeInstrumentSyncStatus}\nGame status: {gameEnsembleStatus}\nNetwork enabled: {cfg.MultiDeviceEnabled}\nNetwork status: {plugin.Network.Status}\n" +
                $"Network revision: {plugin.Network.SessionRevision}\nClock RTT: {plugin.Network.RoundTripMs:0}ms");
        }
        if (plugin.Player.IsEnsemblePlayback && plugin.Player.EnsembleCountInSeconds > 0)
            ImGui.TextColored(new Vector4(.56f, 1f, .87f, 1f),
                Loc.T($"Shared-clock count-in: {plugin.Player.EnsembleCountInSeconds:0.0}s"));
    }

    private void SetEnsembleRole(EnsembleRole role)
    {
        if (role != EnsembleRole.Solo) plugin.LiveMidi.Panic();
        if (cfg.EnsembleMode != (int)role)
        {
            CancelOneClickInstrumentPreparation();
            CancelNativeLeaderSync("Ensemble role changed. Choose a MIDI and resynchronise.");
        }
        gameStartGate.Cancel("Ensemble role changed; waiting for a fresh FFXIV start.");
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
            EnsembleRole.Conductor => cfg.GameEnsembleEnabled ?
                "Conductor: prepare MIDI/instruments, then START ENSEMBLE (native)." :
                "Conductor: load a MIDI and cue other local bards.",
            EnsembleRole.Follower => cfg.GameEnsembleEnabled ?
                "Follower: ARM and open Performance; incoming FFXIV Ensemble Ready Check auto-accepts." :
                "Follower: load the same MIDI, arm, then wait for cue.",
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
            if (cfg.GameEnsembleEnabled)
            {
                if (!plugin.GameEnsemble.Available)
                    throw new InvalidOperationException(plugin.GameEnsemble.Status);
                if (song is null || arranged is null)
                    throw new InvalidOperationException("Load the matching ensemble MIDI before opening FFXIV Ready Check.");
                if (!plugin.IsGameOutputSafe())
                    throw new InvalidOperationException("Enter the expanded 37-note Performance keyboard before starting Ensemble.");
                if (arranged.Notes.Count > 0 && !outputArmed)
                    throw new InvalidOperationException("ARM output before starting Ensemble with assigned MIDI notes.");
                var readiness = GetGameEnsembleReadiness(out string why);
                if (readiness is not (GameEnsembleReadiness.Ready or GameEnsembleReadiness.Silent))
                    throw new InvalidOperationException("This Bard isn't ready for Ensemble: " + why);
                if (plugin.GameEnsemble.TryBeginReadyCheck(out string detail))
                {
                    ensembleStatus = detail;
                    gameEnsembleStatus = detail;
                }
                else throw new InvalidOperationException(detail);
                return;
            }
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
        if (cfg.GameEnsembleEnabled || cfg.MultiDeviceEnabled || (EnsembleRole)cfg.EnsembleMode != EnsembleRole.Follower || !waitingForEnsembleCue)
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

    /// <summary>
    /// Emergency recovery for a local performer stuck in Playing/Stopping or in
    /// a stage navigation state. Does not broadcast a musical START, edit the
    /// playlist or create FFXIV note callbacks on the ImGui thread.
    /// The engine releases held notes on its next Framework.Update.
    /// </summary>
    public void RecoverLocalPlayback()
    {
        try
        {
            RecoverStageAfterFailure("Manual playback recovery requested.");
            plugin.LiveMidi.Panic();
            plugin.AudioPreview.Stop("Preview stopped for playback recovery.");
            pendingQueuedSongPath = "";
            songFlow.Cancel();
            CancelPendingInstrumentStart();
            CancelOneClickInstrumentPreparation();
            CancelNativeLeaderSync("Local playback reset; prepare native ensemble again.");
            ConcertCancelPreparation("Local playback reset.");
            ConcertCancelSetlist("Local playback reset.");
            gameStartGate.Cancel("Local playback reset: old ensemble cue cleared.");
            plugin.GameEnsemble.CancelLocalPreparation();
            waitingForEnsembleCue = false;
            outputArmed = false;
            plugin.Player.RewindToStart();
            status = "Local playback/stage state reset. Allow one game frame for notes to release, then ARM and Play. " +
                "If FFXIV still reports a native ensemble is running, end that ensemble in-game first.";
            ensembleStatus = status;
        }
        catch (Exception error)
        {
            status = "Recovery encountered an error: " + error.Message +
                ". You can unload Windbound, reload and try again.";
        }
    }

    public void StopPlayback()
    {
        ConcertCancelPreparation("Conductor STOP. Concert preparation cancelled.");
        ConcertCancelSetlist("Concert STOP. Automatic next-song preparation cancelled.");
        plugin.LiveMidi.Panic();
        pendingQueuedSongPath = "";
        songFlow.Cancel();
        CancelOneClickInstrumentPreparation();
        CancelPendingInstrumentStart();
        plugin.Player.Stop();
        // Native ensemble STOP sends a leader-only party command even when the
        // optional legacy chat controls are off. Both leader and followers exit
        // Performance AFTER their note-off scheduler has finished.
        if (NativeSyncEnabled && cfg.EnsembleMode == (int)EnsembleRole.Conductor)
            NativeConductorStopAndUnequip();
        if ((EnsembleRole)cfg.EnsembleMode == EnsembleRole.Conductor)
        {
            try
            {
                if (cfg.GameEnsembleEnabled)
                {
                    if (NativeSyncEnabled)
                        ensembleStatus = nativeStopStatus;
                    else if (cfg.GameEnsembleOneClick && cfg.GamePartyChatEnabled)
                        plugin.PartyChat.TryBroadcast(GamePartyCueProtocol.Stop, out ensembleStatus);
                    else ensembleStatus = "Stopped locally; remote STOP isn't configured.";
                }
                else if (cfg.MultiDeviceEnabled)
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
        if (ImGui.Checkbox(Loc.T("Enable Multi-Device (two or more PCs)"), ref multi))
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
            ImGui.TextDisabled(Loc.T("LOCAL mode: all Windbound Bard clients share the same Windows PC. No network connection required."));
            return;
        }
        ImGui.TextColored(new Vector4(.58f, .94f, .98f, 1f), Loc.T("PRIVATE MULTI-DEVICE ENSEMBLE"));
        ImGui.TextWrapped(Loc.T("Best for Edea on another PC: connect both PCs with Tailscale, use the conductor's Tailscale IP, and share the private code directly. Ordinary TCP data is not encrypted by this plugin."));
        int port = cfg.MultiDevicePort;
        ImGui.SetNextItemWidth(130);
        if (ImGui.InputInt(Loc.T("TCP port"), ref port))
        {
            cfg.MultiDevicePort = Math.Clamp(port, 1024, 65535);
            cfg.Save();
        }
        ImGui.SetNextItemWidth(Math.Min(360, ImGui.GetContentRegionAvail().X));
        ImGui.InputText(Loc.T("Private duet code (not saved)"), ref networkCode, 128);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Generate code"))) networkCode = MultiDeviceEnsemble.NewSessionCode();
        if (role == EnsembleRole.Conductor)
        {
            if (!plugin.Network.IsHosting)
            {
                if (ImGui.Button(Loc.T("HOST private ensemble")))
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
                if (ImGui.Button(Loc.T("Stop hosting / disconnect")))
                {
                    StopPlayback();
                    plugin.Network.Stop();
                    networkStatus = "Hosting stopped.";
                }
                ImGui.SameLine();
                if (ImGui.SmallButton(Loc.T("Send updated MIDI + assignments"))) TryShareNetworkSession();
                ImGui.TextUnformatted(Loc.T("Connected Windbound performers:"));
                var performers = plugin.Network.Performers;
                if (performers.Count == 0) ImGui.TextDisabled(Loc.T("No followers connected yet."));
                foreach (var peer in performers)
                {
                    var color = peer.Ready ? new Vector4(.45f, .96f, .58f, 1f) : new Vector4(1f, .77f, .38f, 1f);
                    ImGui.TextColored(color, Loc.T($"{peer.Character} — {(peer.Ready ? "READY" : "WAITING")} (rev {peer.Revision})"));
                    if (!peer.Ready) ImGui.TextWrapped(Loc.T("   " + peer.Detail));
                }
            }
        }
        else if (role == EnsembleRole.Follower)
        {
            ImGui.SetNextItemWidth(Math.Min(370, ImGui.GetContentRegionAvail().X));
            if (ImGui.InputText(Loc.T("Conductor LAN / Tailscale IP"), ref networkHost, 255))
            {
                cfg.MultiDeviceHostAddress = networkHost;
                cfg.Save();
            }
            if (!plugin.Network.IsConnected && !plugin.Network.IsJoining)
            {
                if (ImGui.Button(Loc.T("JOIN conductor")))
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
            else if (ImGui.Button(Loc.T("Disconnect from conductor")))
            {
                if (plugin.Player.IsEnsemblePlayback) plugin.Player.Stop();
                plugin.Network.Stop();
                networkStatus = "Disconnected.";
            }
            ImGui.TextDisabled(Loc.T($"Clock synchronisation: {(plugin.Network.HasClockSync ? "READY" : "waiting for samples")} / {plugin.Network.RoundTripMs:0}ms RTT"));
            ImGui.TextDisabled(Loc.T("MIDI is received into your plugin cache, never added to your saved Solo/Ensemble playlists."));
        }
        else ImGui.TextDisabled(Loc.T("Choose Conductor to HOST or Follower to JOIN."));
        ImGui.TextWrapped(Loc.T(plugin.Network.Status));
        if (!string.IsNullOrEmpty(networkStatus)) ImGui.TextWrapped(Loc.T(networkStatus));
        ImGui.TextDisabled(Loc.T("Connection code is only kept in memory. Reconnect after restarting FFXIV. Never expose TCP port publicly."));
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
        ImGui.TextDisabled(Loc.T("Piano roll: C6 (top) to C3 (bottom) | 7.6s window"));
    }
}
