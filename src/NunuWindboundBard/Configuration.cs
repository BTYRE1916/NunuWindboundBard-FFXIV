using System;
using System.Collections.Generic;
using Dalamud.Configuration;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 37;
    // v0.39 local-only optional visual chrome; nothing here changes song timing.
    public bool DarkStageThemeEnabled { get; set; } = true;
    public bool StageUiMotionEnabled { get; set; } = true;
    public int StageUiAccent { get; set; } = 0; // 0 Soul Rose, 1 Void Violet, 2 Aether Cyan
    // v0.27 local-only SoundFont audio preview; no party/FFXIV MIDI output.
    public string PreviewSoundFontPath { get; set; } = "";
    public float PreviewVolume { get; set; } = .65f;
    public float PreviewSpeed { get; set; } = 1f;
    // UI language: en (default) or fr. This does not change file names, notes or protocol.
    public string Language { get; set; } = "en";
    // v0.20 live hardware MIDI keyboard input in Solo mode. Device opt-in is saved,
    // but FFXIV output ARM is never persisted across plugin restarts.
    // v0.38 Piano Academy: independent local full-range piano practice.
    public int PianoAcademySplitPitch { get; set; } = 60;
    public float PianoAcademySpeed { get; set; } = 1f;
    // v0.38 Showmaster: disabled on followers until they explicitly opt in.
    public bool AllowLeaderShowmaster { get; set; } = false;
    public bool ShowmasterUseBowFallback { get; set; } = true;
    public List<ShowmasterRoutine> ShowmasterRoutines { get; set; } = ShowmasterCodec.Defaults();
    public bool LiveMidiInputEnabled { get; set; } = false;
    public string LiveMidiInputDeviceName { get; set; } = "";
    public int LiveMidiTranspose { get; set; } = 0;
    public bool LiveMidiFoldToRange { get; set; } = true;
    public bool LiveMidiSustain { get; set; } = true;
    public int LiveMidiChannel { get; set; } = 0;
    // Performance HUD preferences are deliberately per-client; ARM remains session-only.
    public bool HudVisible { get; set; } = false;
    public bool HudCompact { get; set; } = true;
    public bool HudLockPosition { get; set; } = false;
    public bool HudShowNextSong { get; set; } = true;
    public float HudOpacity { get; set; } = 0.92f;
    // FFXIV native Ensemble Ready Check across separate PCs (no direct network required).
    // New installations use native ensemble by default; Solo remains Solo until configured.
    public bool GameEnsembleEnabled { get; set; } = true;
    // v0.17: automatic confirmation of native FFXIV Ensemble Ready Check.
    // Enabled by default on all new installs, can be disabled without future upgrades resetting it.
    public bool NativeEnsembleAutoAccept { get; set; } = true;
    // Leader-only commands for file, track, and instrument selection. Native game START remains authoritative.
    public bool NativeLeaderSyncEnabled { get; set; } = true;
    // v0.30 optional stage formation movement. Followers must explicitly opt in.
    public bool AllowLeaderStageMovement { get; set; } = false;
    // Each client can opt out. This applies only after explicit stage recall.
    public bool AutoFaceAfterStageMovement { get; set; } = true;
    // A local solo layout has no conductor/party dependency.
    public SoloStageLayout? SoloStage { get; set; }
    // Experimental visual stage UI is opt-in until verified on live clients.
    // Original saved-formation controls remain available when it is disabled.
    public bool VisualStageDesignerEnabled { get; set; } = false;
    // Local snapshots captured from FFXIV's Adventurer Plate viewer. File names only;
    // images are stored beside this client's configuration, never sent via party chat.
    public Dictionary<string, string> MemberPortraitFileByContentId { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<StageFormationPreset> StageFormations { get; set; } = new();
    public string SelectedStageFormationId { get; set; } = "";
    // v0.29: accept ARM/DISARM requests from the verified party leader when
    // configured as a native Ensemble Follower. Can be disabled per character.
    // Output ARM itself is still volatile and never restored at plugin startup.
    public bool AllowLeaderArmCommands { get; set; } = true;
    // v0.25: never automatically launch the next piece: only PREPARE it.
    public bool ConcertAutoPrepareNext { get; set; } = false;
    // v0.28: named local-only concert snapshot backups. No live ARM or networking secrets.
    public List<ConcertSnapshot> ConcertSnapshots { get; set; } = new();
    public Dictionary<string, ConcertSongCue> ConcertSongCuesByKey { get; set; } = new();
    // Legacy UTC party chat start saved only for config compatibility; disabled in v0.17.
    public bool GameEnsembleOneClick { get; set; } = false;
    // Followers must explicitly opt into receiving automatic playback cues.
    public bool OneClickAllowLeaderStart { get; set; } = false;
    public bool OneClickFollowTrackAssignments { get; set; } = true;
    // Conductor's per-MIDI party instrument choices, keyed by ContentId in local config.
    // Party chat transmits only a compact roster-relative map, never ContentIds.
    public Dictionary<string, Dictionary<string, int>> PartyInstrumentsByFile { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    // Visible party chat song/STOP commands are optional and leader-only.
    public bool GamePartyChatEnabled { get; set; } = false;
    // Master switch is deliberately opt-in; tone override is meaningful only while playing guitar.
    public bool GuitarToneOverridesEnabled { get; set; } = false;
    // Saved per MIDI path and separate track:channel; the same song may have several guitar parts.
    public Dictionary<string, Dictionary<string, int>> GuitarTonesByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IncludeMidiSubfolders { get; set; } = false;
    // 0 means leave the currently equipped Performance instrument unchanged.
    public Dictionary<string, int> InstrumentsByFile { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool AutoSwitchInstrumentBeforePlay { get; set; } = true;
    // v0.22: detect per-track #Instrument and #+12/-12 tags or General MIDI programs.
    public bool AutoDetectMidiTrackInstruments { get; set; } = true;
    public bool AutoReadMidiTrackOctaves { get; set; } = true;
    // A manual button edit wins over imported tags on later song loads.
    public Dictionary<string, List<string>> ManualOctaveLaneOverridesByFile { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
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
    // v0.16 playlist metadata, stored independently for every named Solo/Ensemble profile.
    public Dictionary<string, SmartPlaylistState> SmartPlaylistsById { get; set; } = new();
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
    // v0.23: reuse one conductor lineup for every song in the same saved Ensemble playlist.
    // Existing per-MIDI assignments remain overrides when present.
    public bool KeepPartyAssignmentsAcrossSongs { get; set; } = true;
    public Dictionary<string, PartyTrackLineup> EnsembleLineupsByPlaylistId { get; set; } = new();
    public bool FollowConductorAssignments { get; set; } = true;
    // Offline Soulweaver presets persist independently for each original MIDI file.
    public Dictionary<string, SoulweaverPreset> SoulweaverPresetsByFile { get; set; }
        = new(StringComparer.OrdinalIgnoreCase);
    public string LastFile { get; set; } = "";
    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
