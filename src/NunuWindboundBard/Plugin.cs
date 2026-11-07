using System;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static ICondition Conditions { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;
    [PluginService] internal static IObjectTable Objects { get; private set; } = null!;
    [PluginService] internal static IPartyList Party { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInterop { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;

    internal readonly Configuration Config;
    internal readonly PlaybackEngine Player = new();
    internal readonly AudioPreviewPlayer AudioPreview = new();
    internal readonly EnsembleCoordinator Ensemble = new();
    internal readonly EnsembleAssignmentBus Assignments = new();
    internal readonly MultiDeviceEnsemble Network = new();
    internal readonly GameEnsembleSync GameEnsemble;
    internal readonly GamePartyChatSync PartyChat;
    internal readonly InstrumentSwitcher Instruments;
    internal readonly LiveMidiInputService LiveMidi = new();
    private readonly WindowSystem windows = new("NunuWindboundBard");
    private readonly MainWindow main;
    private readonly PerformanceHudWindow hud;
    private volatile bool performing;
    private volatile bool expandedKeyboard;
    internal bool IsPerforming => performing;
    internal bool IsExpandedKeyboard => expandedKeyboard;

    public Plugin()
    {
        var previousConfig = PluginInterface.GetPluginConfig();
        bool isFirstInstall = previousConfig is not Configuration;
        Config = previousConfig as Configuration ?? new Configuration();
        if (Config.Version < 2)
        {
            Config.ChordStrategy = (int)ChordStrategy.PreserveAll;
            Config.Version = 2;
            Config.Save();
        }
        if (Config.Version < 3)
        {
            Config.EnsembleMode = 0; // never auto-arm on upgrade
            Config.Version = 3;
            Config.Save();
        }
        if (Config.Version < 4)
        {
            // Existing user playlists / arrangement / ensemble settings are retained.
            Config.GuitarToneOverridesEnabled = false;
            Config.Version = 4;
            Config.Save();
        }
        if (Config.Version < 5)
        {
            // Keep the user's playlists, guitar tones, ensemble configuration and all MIDI preferences.
            Config.AutoSwitchInstrumentBeforePlay = true;
            Config.Version = 5;
            Config.Save();
        }
        if (Config.Version < 6)
        {
            // No existing MIDI, playlist, tone, track or ensemble choices are reset.
            Config.Version = 6;
            Config.Save();
        }
        if (Config.Version < 7)
        {
            // No settings change: instrument switches now try a native UI close first.
            Config.Version = 7;
            Config.Save();
        }
        // Preserve every existing v0.7 song, then create separate Solo and Ensemble profiles.
        SavedPlaylistLibrary.EnsureMigrated(Config);
        if (Config.Version < 8)
        {
            // Existing v0.7 manual ensemble selection remains usable after upgrade.
            // Enable "Follow conductor's party-track assignments" on followers to opt in.
            Config.FollowConductorAssignments = false;
            Config.Version = 8;
            Config.Save();
        }
        if (Config.Version < 9)
        {
            // Network access must be deliberately enabled, never reconnected automatically.
            Config.MultiDeviceEnabled = false;
            Config.Version = 9;
            Config.Save();
        }
        if (Config.Version < 10)
        {
            // Auto-next now runs on Framework.Update; existing playlists stay intact.
            Config.Version = 10;
            Config.Save();
        }
        if (Config.Version < 11)
        {
            // No existing playlist, instrument, ensemble, keybind or music-engine setting is modified.
            Config.Version = 11;
            Config.Save();
        }
        if (Config.Version < 12)
        {
            // New game-native remote ensemble is always opt-in; nothing else resets.
            Config.GameEnsembleEnabled = false;
            Config.GamePartyChatEnabled = false;
            Config.Version = 12;
            Config.Save();
        }
        if (Config.Version < 13)
        {
            // FFXIV native Ensemble is now its own cue; no follower wait checkbox.
            // Preserve the user's existing MIDI, instrument and playlist settings.
            Config.Version = 13;
            Config.Save();
        }
        if (Config.Version < 14)
        {
            // Keep the already-working FFXIV Ready Check path by default.
            // Automatic remote start must be consciously enabled by the follower.
            Config.GameEnsembleOneClick = false;
            Config.OneClickAllowLeaderStart = false;
            Config.Version = 14;
            Config.Save();
        }
        if (Config.Version < 15)
        {
            // Preserve all existing song/instrument/ensemble preferences.
            Config.PartyInstrumentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
            Config.Version = 15;
            Config.Save();
        }
        if (Config.Version < 16)
        {
            // Add HUD preferences without changing playback, instruments, or playlists.
            // The HUD is opt-in and was not shown on earlier releases.
            Config.HudVisible = false;
            Config.HudCompact = true;
            Config.HudLockPosition = false;
            Config.HudShowNextSong = true;
            Config.HudOpacity = 0.92f;
            Config.Version = 16;
            Config.Save();
        }
        if (Config.Version < 17)
        {
            // Non-destructive migration: the existing library is untouched.
            Config.SmartPlaylistsById ??= new();
            Config.Version = 17;
            Config.Save();
        }
        Config.SmartPlaylistsById ??= new();
        if (Config.Version < 18)
        {
            // v0.17 retires the chat/UTC start path and adopts native game
            // Ensemble Ready Check + automatic follower confirmation.
            Config.NativeEnsembleAutoAccept = true;
            Config.GameEnsembleOneClick = false;
            Config.Version = 18;
            Config.Save();
        }
        if (Config.Version < 19)
        {
            // v0.18: enable conductor MIDI/instrument sync by default; no automatic
            // music START is issued by chat. Honour an existing user's auto-accept choice.
            Config.NativeLeaderSyncEnabled = true;
            Config.Version = 19;
            Config.Save();
        }
        if (isFirstInstall)
        {
            // A new installation is ready for native ensemble once the user selects
            // Follower or Conductor. It still never ARM's game output automatically.
            Config.GameEnsembleEnabled = true;
            Config.NativeEnsembleAutoAccept = true;
            Config.NativeLeaderSyncEnabled = true;
            Config.GameEnsembleOneClick = false;
            Config.Save();
        }
        if (Config.GameEnsembleOneClick)
        {
            // Legacy JSON setting must not silently reactivate the old unreliable
            // party chat START in this release. Solo/ensemble libraries stay intact.
            Config.GameEnsembleOneClick = false;
            Config.Save();
        }
        if (Config.Version < 20)
        {
            // No existing options are reset. The new conductor track transfer
            // and group STOP+unequip use the native ensemble/follower settings.
            Config.Version = 20;
            Config.Save();
        }
        if (Config.Version < 21)
        {
            // New live MIDI feature is opt-in, doesn't change existing music
            // libraries, ensemble settings, or the user's instrument choices.
            Config.LiveMidiInputEnabled = false;
            Config.Version = 21;
            Config.Save();
        }
        if (Config.Version < 22)
        {
            // UI-only localisation migration; preserve all MIDI/ensemble settings.
            Config.Language ??= "en";
            Config.Version = 22;
            Config.Save();
        }
        if (Config.Version < 23)
        {
            // MIDI tags and General MIDI mapping are optional defaults. No
            // existing ensemble, manual instrument or octave settings are reset.
            Config.AutoDetectMidiTrackInstruments = true;
            Config.AutoReadMidiTrackOctaves = true;
            Config.ManualOctaveLaneOverridesByFile ??= new(StringComparer.OrdinalIgnoreCase);
            Config.Version = 23;
            Config.Save();
        }
        if (Config.Version < 24)
        {
            // Expanded MIDI metadata detection. Preserve existing instruments,
            // octaves, overrides and all ensemble settings on upgrade.
            Config.Version = 24;
            Config.Save();
        }
        if (Config.Version < 25)
        {
            // Reusable octet lineups are enabled for new/updated installs.
            // Do not overwrite existing per-MIDI assignments, party rosters or profiles.
            Config.KeepPartyAssignmentsAcrossSongs = true;
            Config.EnsembleLineupsByPlaylistId ??= new();
            Config.Version = 25;
            Config.Save();
        }
        if (Config.Version < 26)
        {
            // Stage Manager uses party metadata only; all player settings are preserved.
            Config.Version = 26;
            Config.Save();
        }
        if (Config.Version < 27)
        {
            Config.ConcertAutoPrepareNext = false; // opt-in, no automatic native START
            Config.ConcertSongCuesByKey ??= new();
            Config.Version = 27;
            Config.Save();
        }
        Config.ConcertSongCuesByKey ??= new();
        if (Config.Version < 28)
        {
            // The inspection button is read-only until the user approves a fix.
            Config.Version = 28;
            Config.Save();
        }
        if (Config.Version < 29)
        {
            // Local-only SoundFont audio preview; all concert and user options remain intact.
            Config.PreviewSoundFontPath ??= "";
            Config.PreviewVolume = .65f;
            Config.PreviewSpeed = 1f;
            Config.Version = 29;
            Config.Save();
        }
        if (Config.Version < 30)
        {
            // Add only the snapshot library after ALL previous migrations.
            Config.ConcertSnapshots ??= new();
            Config.Version = 30;
            Config.Save();
        }
        if (Config.Version < 31)
        {
            // Native ensemble followers may be remotely armed by the verified
            // party leader. Their actual outputArmed state is NEVER persisted.
            Config.AllowLeaderArmCommands = true;
            Config.Version = 31;
            Config.Save();
        }
        Config.ConcertSnapshots ??= new();
        Config.EnsembleLineupsByPlaylistId ??= new();
        Config.ManualOctaveLaneOverridesByFile ??= new(StringComparer.OrdinalIgnoreCase);
        AudioPreview.SetVolume(Config.PreviewVolume);
        AudioPreview.SetSpeed(Config.PreviewSpeed);
        Loc.SetLanguage(Config.Language);
        // Remote-playback consent lasts for the current plugin session only.
        // Never auto-enable it just because the user opted in yesterday.
        if (Config.OneClickAllowLeaderStart)
        {
            Config.OneClickAllowLeaderStart = false;
            Config.Save();
        }
        // Never enable both cross-PC transports simultaneously.
        if (Config.GameEnsembleEnabled && Config.MultiDeviceEnabled)
        {
            Config.MultiDeviceEnabled = false;
            Config.Save();
        }
        Config.PartyInstrumentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.SoulweaverPresetsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.PartyAssignmentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.GuitarTonesByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.InstrumentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.OctaveShiftsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Instruments = new InstrumentSwitcher(Objects, Conditions, SigScanner);
        if (Config.LiveMidiInputEnabled && !string.IsNullOrWhiteSpace(Config.LiveMidiInputDeviceName))
        {
            // Re-open only a previously chosen controller. Output ARM stays OFF
            // until the user explicitly enables it in this FFXIV session.
            _ = LiveMidi.Connect(Config.LiveMidiInputDeviceName, out _);
        }
        GameEnsemble = new GameEnsembleSync(SigScanner, GameInterop)
        {
            Enabled = Config.GameEnsembleEnabled && Config.EnsembleMode != (int)EnsembleRole.Solo
        };
        PartyChat = new GamePartyChatSync(Chat, Party, Objects);
        main = new MainWindow(this);
        windows.AddWindow(main);
        hud = new PerformanceHudWindow(this, main);
        hud.IsOpen = Config.HudVisible;
        windows.AddWindow(hud);
        Commands.AddHandler("/windbound", new CommandInfo(OnCommand)
        {
            HelpMessage = Loc.T("Open Windbound Bard; /windbound hud toggles the HUD; /windbound stop stops playback; /windbound midipanic releases live keys.")
        });
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;
        Log.Information("Windbound Bard v0.29 loaded: conductor ARM ALL FOLLOWERS with verified readiness acknowledgements.");
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        // Native Performance addon callbacks MUST run on the game's framework thread.
        // No active-window check: alt-tab and in-game chat do not interrupt playback.
        performing = Conditions[ConditionFlag.Performing];
        expandedKeyboard = performing && PerformanceOutput.HasExpandedPerformanceAddon();
        // Game-native Perform actions and all audio callbacks run on the framework thread.
        Instruments.Update(performing && expandedKeyboard);
        main.PollInstrumentStart();
        main.PollNetwork();
        main.PollPartyAssignments();
        main.PollEnsemble();
        GameEnsemble.Enabled = Config.GameEnsembleEnabled && Config.EnsembleMode != (int)EnsembleRole.Solo;
        // Party preparation commands are not musical START cues. Process them
        // first, then auto-accept only if the MIDI and instrument are prepared.
        PartyChat.Enabled = Config.GameEnsembleEnabled &&
            (Config.GamePartyChatEnabled || Config.NativeLeaderSyncEnabled);
        PartyChat.ReceiveEnabled = Config.GameEnsembleEnabled &&
            (Config.EnsembleMode == (int)EnsembleRole.Follower ||
             Config.EnsembleMode == (int)EnsembleRole.Conductor);
        main.PollGamePartyChat();
        main.PollNativeConductorSync();
        main.PollNativePartyArming(); // ARM/ACK after file/track preparation; no notes generated
        main.PollConcertDirector();
        main.PollNativeReadyChecks();
        main.PollGameEnsemble();
        main.PollOneClickInstrumentPreparation();
        // Flush live controller note-offs before the MIDI-file scheduler so
        // switching into file playback never releases the file's fresh notes.
        LiveMidi.ProcessFrameworkUpdate(Config.LiveMidiInputEnabled,
            Config.EnsembleMode == (int)EnsembleRole.Solo, main.LiveMidiArmed,
            performing && expandedKeyboard,
            Player.IsPlaying || Player.IsStopPending || Player.IsRewindPending,
            Instruments.InProgress, Config.LiveMidiTranspose,
            Config.LiveMidiFoldToRange, Config.LiveMidiSustain, Config.LiveMidiChannel);
        Player.Update(performing && expandedKeyboard);
        // Rehearsal preview never plays concurrently with live game MIDI output.
        if (Player.IsPlaying && (AudioPreview.IsPlaying || AudioPreview.IsLoading))
            AudioPreview.Stop("Preview stopped: FFXIV Performance MIDI started.");
        AudioPreview.Poll();
        main.PollConcertSetlist();
        main.PollSongTransitions();
    }

    private void Draw()
    {
        windows.Draw();
        // Draw outside the Windbound main ImGui window so pickers remain independent.
        main.DrawFileDialogs();
    }

    private void OnCommand(string command, string args)
    {
        string input = (args ?? string.Empty).Trim();
        if (input.Equals("stop", StringComparison.OrdinalIgnoreCase))
        {
            main.StopPlayback();
            return;
        }
        if (input.Equals("midipanic", StringComparison.OrdinalIgnoreCase))
        {
            LiveMidi.Panic();
            return;
        }
        if (input.Equals("hud", StringComparison.OrdinalIgnoreCase))
        {
            SetHudVisible(!hud.IsOpen);
            return;
        }
        if (input.Equals("hud show", StringComparison.OrdinalIgnoreCase))
        {
            SetHudVisible(true);
            return;
        }
        if (input.Equals("hud hide", StringComparison.OrdinalIgnoreCase))
        {
            SetHudVisible(false);
            return;
        }
        main.Toggle();
    }

    internal void RefreshHudLanguage() =>
        hud.WindowName = Loc.T("Windbound Bard | Performance HUD") + "###NunuWindboundHud";

    internal bool IsHudVisible => hud.IsOpen;

    internal void SetHudVisible(bool visible)
    {
        if (hud.IsOpen != visible) hud.IsOpen = visible;
        if (Config.HudVisible != visible)
        {
            Config.HudVisible = visible;
            Config.Save();
        }
    }

    internal bool IsGameOutputSafe() => performing && expandedKeyboard;
    private void ToggleMain() => main.Toggle();

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMain;
        Commands.RemoveHandler("/windbound");
        main.CancelPendingInstrumentStart();
        Instruments.Cancel();
        LiveMidi.Dispose();
        AudioPreview.Dispose();
        Player.Dispose();
        Ensemble.Dispose();
        Assignments.Dispose();
        Network.Dispose();
        PartyChat.Dispose();
        GameEnsemble.Dispose();
        windows.RemoveAllWindows();
        main.Dispose();
    }
}
