using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
<<<<<<< HEAD
    public int Version { get; set; } = 10;
=======
    public int Version { get; set; } = 9;
>>>>>>> b4ffdf454a9a1a49ec60fee941f72fc7e23bc8e0
    // Master switch is deliberately opt-in; tone override is meaningful only while playing guitar.
    public bool GuitarToneOverridesEnabled { get; set; } = false;
    // Saved per MIDI path and separate track:channel; the same song may have several guitar parts.
    public Dictionary<string, Dictionary<string, int>> GuitarTonesByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IncludeMidiSubfolders { get; set; } = false;
    // 0 means leave the currently equipped Performance instrument unchanged.
    public Dictionary<string, int> InstrumentsByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoSwitchInstrumentBeforePlay { get; set; } = true;
    // MIDI lane (track:channel) octave shifts survive song reload; increments of 12 semitones.
    public Dictionary<string, Dictionary<string, int>> OctaveShiftsByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string PerformanceKeys { get; set; } = PerformanceKeyMap.DefaultKeys;
    public int ChordStrategy { get; set; } = (int)Core.ChordStrategy.PreserveAll;
    public bool RepairOctaves { get; set; } = true;
    public int GlobalTranspose { get; set; } = 0;
    public int MinimumGapMs { get; set; } = 80;
    public int ArpeggioSpacingMs { get; set; } = 95;
    public int KeyHoldMs { get; set; } = 48;
    public float PlaybackSpeed { get; set; } = 1.0f;
    public bool AutoNext { get; set; } = false;
    public string EnsembleGroup { get; set; } = "NunuStage";
    // Opt-in private TCP duet mode, never automatically connects on plugin startup.
    // Private session code is intentionally held only in memory, not saved in config.
    public bool MultiDeviceEnabled { get; set; } = false;
    public string MultiDeviceHostAddress { get; set; } = "";
    public int MultiDevicePort { get; set; } = MultiDeviceEnsemble.DefaultPort;
    public int EnsembleMode { get; set; } = 0;
    public int EnsembleLeadMilliseconds { get; set; } = 5000;
    // Per-file track/channel choice so clients can keep different instruments across performances.
    public Dictionary<string, List<string>> EnsemblePartsByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    // v0.7's Playlist is retained as the active working copy, and migrated on startup.
    public List<string> Playlist { get; set; } = new();
    public List<SavedPlaylist> SavedPlaylists { get; set; } = new();
    public string ActiveSoloPlaylistId { get; set; } = "";
    public string ActiveEnsemblePlaylistId { get; set; } = "";
    public string ActivePlaylistId { get; set; } = "";
    // Conductor presets: MIDI path -> track:channel -> FFXIV party content id.
    public Dictionary<string, Dictionary<string, ulong>> PartyAssignmentsByFile { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    public bool FollowConductorAssignments { get; set; } = true;
    public string LastFile { get; set; } = "";
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
