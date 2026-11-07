using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NunuWindboundBard.Core;

/// <summary>
/// Portable, self-contained snapshot of the current Solo/Ensemble setlist, local
/// arrangement preferences, native conductor settings and per-MIDI metadata.
/// No MIDI contents, private network codes, ARM state or remote START cues.
/// </summary>
public sealed class ConcertSnapshot
{
    public int FormatVersion { get; set; } = 1;
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Concert backup";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public int Role { get; set; } // 0 Solo, 1 Conductor, 2 Follower
    public string PlaylistName { get; set; } = "Concert";
    public List<string> PlaylistFiles { get; set; } = new();
    public string SelectedMidiPath { get; set; } = "";
    public ConcertSnapshotOptions Options { get; set; } = new();
    public SmartPlaylistState SmartPlaylist { get; set; } = new();
    public PartyTrackLineup? Lineup { get; set; }
    public Dictionary<string, ConcertSnapshotSong> Songs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ConcertSnapshotOptions
{
    public float PlaybackSpeed { get; set; } = 1f;
    public int GlobalTranspose { get; set; }
    public bool RepairOctaves { get; set; } = true;
    public int ChordStrategy { get; set; }
    public int MinimumGapMs { get; set; } = 80;
    public int ArpeggioSpacingMs { get; set; } = 95;
    public int KeyHoldMs { get; set; } = 48;
    public bool AutoNext { get; set; }
    public bool AutoDetectMidiTrackInstruments { get; set; } = true;
    public bool AutoReadMidiTrackOctaves { get; set; } = true;
    public bool AutoSwitchInstrumentBeforePlay { get; set; } = true;
    public bool GuitarToneOverridesEnabled { get; set; }
    public bool KeepPartyAssignmentsAcrossSongs { get; set; } = true;
    public bool GameEnsembleEnabled { get; set; } = true;
    public bool NativeLeaderSyncEnabled { get; set; } = true;
    public bool NativeEnsembleAutoAccept { get; set; } = true;
    public bool FollowConductorAssignments { get; set; } = true;
    public bool ConcertAutoPrepareNext { get; set; }
}

public sealed class ConcertSnapshotSong
{
    public int? Instrument { get; set; }
    public Dictionary<string, int> PartyInstruments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ulong> PartyTracks { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, int> Octaves { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<string> ManualOctaveOverrides { get; set; } = new();
    public List<string> EnabledLanes { get; set; } = new();
    public Dictionary<string, int> GuitarTones { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public SoulweaverPreset? Soulweaver { get; set; }
    public ConcertSongCue? Cue { get; set; }
}

public static class ConcertSnapshotFormat
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public const int MaxSnapshots = 30;
    public const int MaxSongs = 2500;
    public const long MaxJsonBytes = 8 * 1024 * 1024;

    public static T Copy<T>(T value) where T : class =>
        JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))
        ?? throw new InvalidDataException("Could not clone snapshot data.");

    public static ConcertSnapshot CopyWithNewId(ConcertSnapshot snapshot)
    {
        var result = Copy(snapshot);
        result.Id = Guid.NewGuid().ToString("N");
        result.CreatedUtc = DateTime.UtcNow;
        return result;
    }

    public static void Validate(ConcertSnapshot data)
    {
        if (data.FormatVersion != 1) throw new InvalidDataException("Unsupported concert snapshot version.");
        if (string.IsNullOrWhiteSpace(data.Name) || data.Name.Length > 80)
            throw new InvalidDataException("Concert snapshot name must contain 1–80 characters.");
        if (data.Role is < 0 or > 2) throw new InvalidDataException("Invalid Solo/Conductor/Follower role.");
        if (string.IsNullOrWhiteSpace(data.PlaylistName) || data.PlaylistName.Length > 80)
            throw new InvalidDataException("Invalid snapshot playlist name.");
        if (data.PlaylistFiles is null || data.PlaylistFiles.Count > MaxSongs ||
            data.PlaylistFiles.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 2048))
            throw new InvalidDataException("Invalid snapshot MIDI list.");
        if (data.Songs is null || data.Songs.Count > MaxSongs || data.Options is null || data.SmartPlaylist is null)
            throw new InvalidDataException("Snapshot metadata missing or too large.");
        if (data.Songs.Keys.Any(p => string.IsNullOrWhiteSpace(p) || p.Length > 2048) ||
            data.Songs.Values.Any(v => v is null))
            throw new InvalidDataException("Invalid per-song snapshot data.");
        if (data.PlaylistFiles.Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.PlaylistFiles.Count ||
            data.Songs.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Songs.Count)
            throw new InvalidDataException("Duplicate MIDI paths in snapshot.");
        if (data.SmartPlaylist.Favorites is null || data.SmartPlaylist.PlayNext is null ||
            data.SmartPlaylist.Tags is null || data.SmartPlaylist.Recent is null ||
            data.SmartPlaylist.Favorites.Count > MaxSongs || data.SmartPlaylist.PlayNext.Count > MaxSongs ||
            data.SmartPlaylist.Tags.Count > MaxSongs || data.SmartPlaylist.Recent.Count > 50)
            throw new InvalidDataException("Invalid Smart Playlist settings in snapshot.");
        if (data.Songs.Keys.Any(key => !data.PlaylistFiles.Contains(key, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Song metadata references MIDI outside snapshot playlist.");
        if (!string.IsNullOrEmpty(data.SelectedMidiPath) &&
            !data.PlaylistFiles.Contains(data.SelectedMidiPath, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Selected MIDI is not in the snapshot playlist.");
        if (data.Options.PlaybackSpeed is < .25f or > 4f || !float.IsFinite(data.Options.PlaybackSpeed) ||
            data.Options.GlobalTranspose is < -48 or > 48 || data.Options.ChordStrategy is < 0 or > 4 ||
            data.Options.MinimumGapMs is < 0 or > 1000 || data.Options.ArpeggioSpacingMs is < 0 or > 1000 ||
            data.Options.KeyHoldMs is < 1 or > 1000)
            throw new InvalidDataException("Snapshot playback options are outside supported bounds.");
        if (data.Lineup is not null && data.Lineup.PerformerIdsBySlot is { Count: > 256 })
            throw new InvalidDataException("Too many performer slots in snapshot.");
        foreach (var settings in data.Songs.Values)
        {
            if (settings.PartyInstruments is null || settings.PartyTracks is null ||
                settings.Octaves is null || settings.ManualOctaveOverrides is null ||
                settings.EnabledLanes is null || settings.GuitarTones is null)
                throw new InvalidDataException("Incomplete MIDI snapshot settings.");
            if ((settings.Instrument.HasValue && settings.Instrument.Value is < 0 or > 28) ||
                settings.PartyInstruments.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.PartyInstruments.Count ||
                settings.PartyTracks.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.PartyTracks.Count ||
                settings.Octaves.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.Octaves.Count ||
                settings.GuitarTones.Keys.Distinct(StringComparer.OrdinalIgnoreCase).Count() != settings.GuitarTones.Count ||
                settings.PartyInstruments.Count > 256 || settings.PartyTracks.Count > 256 ||
                settings.Octaves.Count > 256 || settings.EnabledLanes.Count > 256 ||
                settings.ManualOctaveOverrides.Count > 256 || settings.GuitarTones.Count > 256 ||
                settings.PartyInstruments.Values.Any(id => id is < 0 or > 28) ||
                settings.Octaves.Values.Any(offset => offset is < -60 or > 60))
                throw new InvalidDataException("Invalid per-MIDI configuration in snapshot.");
        }
    }

    public static int MissingFileCount(ConcertSnapshot snapshot) =>
        snapshot.PlaylistFiles.Count(p => !File.Exists(p));

    public static string ExportToJson(ConcertSnapshot snapshot)
    {
        Validate(snapshot);
        string json = JsonSerializer.Serialize(snapshot, JsonOptions);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new InvalidDataException("Snapshot exceeds the 8 MB export limit.");
        return json;
    }

    public static ConcertSnapshot ImportFromJson(string json)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxJsonBytes)
            throw new InvalidDataException("Snapshot JSON exceeds 8 MB.");
        var snapshot = JsonSerializer.Deserialize<ConcertSnapshot>(json)
            ?? throw new InvalidDataException("Concert snapshot JSON is empty.");
        Validate(snapshot);
        // Imported IDs must never collide with saved snapshots.
        return CopyWithNewId(snapshot);
    }
}
