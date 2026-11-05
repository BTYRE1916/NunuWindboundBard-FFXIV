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
        if (Config.Version < 10)
        {
            // Auto-next now runs on Framework.Update; existing playlists stay intact.
            Config.Version = 10;
            Config.Save();
        }
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
        Log.Information("Windbound Bard v0.10 loaded: opt-in cross-PC duet sessions, party part assignments, synced cues, saved playlists.");
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
