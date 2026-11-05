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

    internal readonly Configuration Config;
    internal readonly PlaybackEngine Player = new();
    internal readonly EnsembleCoordinator Ensemble = new();
    internal readonly EnsembleAssignmentBus Assignments = new();
    internal readonly MultiDeviceEnsemble Network = new();
    internal readonly InstrumentSwitcher Instruments;
    private readonly WindowSystem windows = new("NunuWindboundBard");
    private readonly MainWindow main;
    private volatile bool performing;
    private volatile bool expandedKeyboard;
    internal bool IsPerforming => performing;
    internal bool IsExpandedKeyboard => expandedKeyboard;

    public Plugin()
    {
        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
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
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
=======
<<<<<<< HEAD
>>>>>>> 1ec159be2b9e89e0c09e2c5b16ba45869ea174c8
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
        if (Config.Version < 10)
        {
            // Auto-next now runs on Framework.Update; existing playlists stay intact.
            Config.Version = 10;
            Config.Save();
        }
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
        if (Config.Version < 11)
        {
            // No existing playlist, instrument, ensemble, keybind or music-engine setting is modified.
            Config.Version = 11;
            Config.Save();
        }
        Config.SoulweaverPresetsByFile ??= new(StringComparer.OrdinalIgnoreCase);
=======
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> 1ec159be2b9e89e0c09e2c5b16ba45869ea174c8
=======
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
=======
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
        Config.PartyAssignmentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.GuitarTonesByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.InstrumentsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Config.OctaveShiftsByFile ??= new(StringComparer.OrdinalIgnoreCase);
        Instruments = new InstrumentSwitcher(Objects);
        main = new MainWindow(this);
        windows.AddWindow(main);
        Commands.AddHandler("/windbound", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open Windbound Bard, or /windbound stop to stop playback."
        });
        Framework.Update += OnFrameworkUpdate;
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMain;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMain;
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
        Log.Information("Windbound Bard v0.11 loaded: opt-in cross-PC duet sessions, party part assignments, synced cues, saved playlists.");
=======
<<<<<<< HEAD
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
        Log.Information("Windbound Bard v0.10 loaded: opt-in cross-PC duet sessions, party part assignments, synced cues, saved playlists.");
=======
        Log.Information("Windbound Bard v0.9 loaded: opt-in cross-PC duet sessions, party part assignments, synced cues, saved playlists.");
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
<<<<<<< HEAD
<<<<<<< HEAD
>>>>>>> 1ec159be2b9e89e0c09e2c5b16ba45869ea174c8
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
=======
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
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
        Player.Update(performing && expandedKeyboard);
<<<<<<< HEAD
        main.PollSongTransitions();
=======
<<<<<<< HEAD
<<<<<<< HEAD
<<<<<<< HEAD
        main.PollSongTransitions();
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> 1ec159be2b9e89e0c09e2c5b16ba45869ea174c8
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
=======
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
>>>>>>> fd9dbb68533b1ac48aa8f22fb5cc7c4a3ad49910
    }

    private void Draw()
    {
        windows.Draw();
        // Draw outside the Windbound main ImGui window so pickers remain independent.
        main.DrawFileDialogs();
    }

    private void OnCommand(string command, string args)
    {
        if (args.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase)) main.StopPlayback();
        else main.Toggle();
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
        Player.Dispose();
        Ensemble.Dispose();
        Assignments.Dispose();
        Network.Dispose();
        windows.RemoveAllWindows();
        main.Dispose();
    }
}
