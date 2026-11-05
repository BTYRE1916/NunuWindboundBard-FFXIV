using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace NunuWindboundBard.Core;

/// <summary>Each named playlist is separate. The legacy Configuration.Playlist stays as the active working copy.</summary>
public sealed class SavedPlaylist
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "New playlist";
    public int Purpose { get; set; } // 0 solo, 1 ensemble
    public List<string> Files { get; set; } = new();
}

public sealed class PlaylistFile
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = "Imported playlist";
    public int Purpose { get; set; }
    public List<string> Files { get; set; } = new();
}

public static class SavedPlaylistLibrary
{
    public static int PurposeForRole(int role) => role == 0 ? 0 : 1;

    // Don't drop the old v0.7 playlist on migration. If the app was left in ensemble
    // mode, the previous list is copied to both defaults so it remains available.
    public static void EnsureMigrated(Configuration cfg)
    {
        cfg.SavedPlaylists ??= new List<SavedPlaylist>();
        cfg.Playlist ??= new List<string>();
        cfg.SavedPlaylists.RemoveAll(x => x is null);
        foreach (var profile in cfg.SavedPlaylists)
        {
            if (string.IsNullOrWhiteSpace(profile.Id)) profile.Id = Guid.NewGuid().ToString("N");
            if (string.IsNullOrWhiteSpace(profile.Name)) profile.Name = "Untitled";
            profile.Purpose = profile.Purpose == 1 ? 1 : 0;
            profile.Files ??= new List<string>();
        }
        if (cfg.SavedPlaylists.Count == 0)
        {
            cfg.SavedPlaylists.Add(new SavedPlaylist { Name = "My Solo Songs", Purpose = 0, Files = cfg.Playlist.ToList() });
            cfg.SavedPlaylists.Add(new SavedPlaylist { Name = "My Ensemble Songs", Purpose = 1,
                Files = cfg.EnsembleMode == 0 ? new List<string>() : cfg.Playlist.ToList() });
        }
        EnsureDefault(cfg, 0);
        EnsureDefault(cfg, 1);
        var purpose = PurposeForRole(cfg.EnsembleMode);
        string expectedId = purpose == 0 ? cfg.ActiveSoloPlaylistId : cfg.ActiveEnsemblePlaylistId;
        // Config.Playlist was the sole list in v0.7. On a first migration keep it active
        // on whatever mode was last selected, not just the default solo list.
        if (cfg.Version < 8)
        {
            cfg.ActivePlaylistId = expectedId;
            cfg.Playlist = GetActive(cfg).Files.ToList();
        }
        else if (cfg.SavedPlaylists.All(x => x.Id != cfg.ActivePlaylistId))
        {
            cfg.ActivePlaylistId = expectedId;
            cfg.Playlist = GetActive(cfg).Files.ToList();
        }
    }

    private static void EnsureDefault(Configuration cfg, int purpose)
    {
        var storedId = purpose == 0 ? cfg.ActiveSoloPlaylistId : cfg.ActiveEnsemblePlaylistId;
        var candidate = cfg.SavedPlaylists.FirstOrDefault(x => x.Id == storedId && x.Purpose == purpose)
                     ?? cfg.SavedPlaylists.FirstOrDefault(x => x.Purpose == purpose);
        if (candidate is null)
        {
            candidate = new SavedPlaylist { Name = purpose == 0 ? "My Solo Songs" : "My Ensemble Songs", Purpose = purpose };
            cfg.SavedPlaylists.Add(candidate);
        }
        if (purpose == 0) cfg.ActiveSoloPlaylistId = candidate.Id;
        else cfg.ActiveEnsemblePlaylistId = candidate.Id;
    }

    public static SavedPlaylist GetActive(Configuration cfg) =>
        cfg.SavedPlaylists.First(x => x.Id == cfg.ActivePlaylistId);

    public static void SnapshotActive(Configuration cfg)
    {
        var profile = cfg.SavedPlaylists.FirstOrDefault(x => x.Id == cfg.ActivePlaylistId);
        if (profile != null) profile.Files = cfg.Playlist.ToList();
    }

    public static SavedPlaylist Select(Configuration cfg, string profileId)
    {
        var next = cfg.SavedPlaylists.FirstOrDefault(x => x.Id == profileId)
            ?? throw new InvalidOperationException("The selected playlist was not found.");
        SnapshotActive(cfg);
        cfg.ActivePlaylistId = next.Id;
        if (next.Purpose == 0) cfg.ActiveSoloPlaylistId = next.Id;
        else cfg.ActiveEnsemblePlaylistId = next.Id;
        cfg.Playlist = next.Files.ToList();
        return next;
    }

    public static SavedPlaylist ChangeMode(Configuration cfg, int purpose)
    {
        EnsureDefault(cfg, purpose);
        string profileId = purpose == 0 ? cfg.ActiveSoloPlaylistId : cfg.ActiveEnsemblePlaylistId;
        return Select(cfg, profileId);
    }

    public static SavedPlaylist NewProfile(Configuration cfg, string name, int purpose, bool copyCurrent)
    {
        name = ValidateName(name);
        if (cfg.SavedPlaylists.Count >= 100) throw new InvalidOperationException("At most 100 saved playlists are supported.");
        var result = new SavedPlaylist { Name = UniqueName(cfg, name, purpose), Purpose = purpose,
            Files = copyCurrent ? cfg.Playlist.ToList() : new List<string>() };
        cfg.SavedPlaylists.Add(result);
        return result;
    }

    public static void Rename(Configuration cfg, string id, string name)
    {
        var profile = cfg.SavedPlaylists.First(x => x.Id == id);
        profile.Name = UniqueName(cfg, ValidateName(name), profile.Purpose, id);
    }

    public static void Delete(Configuration cfg, string id)
    {
        var profile = cfg.SavedPlaylists.FirstOrDefault(x => x.Id == id)
            ?? throw new InvalidOperationException("Playlist not found.");
        if (cfg.SavedPlaylists.Count(x => x.Purpose == profile.Purpose) <= 1)
            throw new InvalidOperationException("Keep at least one Solo and one Ensemble playlist.");
        if (profile.Id == cfg.ActivePlaylistId)
            throw new InvalidOperationException("Switch to another playlist before deleting this one.");
        cfg.SavedPlaylists.Remove(profile);
        EnsureDefault(cfg, profile.Purpose);
    }

    public static string UniqueName(Configuration cfg, string name, int purpose, string exceptId = "")
    {
        string result = name;
        for (int i = 2; cfg.SavedPlaylists.Any(x => x.Id != exceptId && x.Purpose == purpose &&
                                                    x.Name.Equals(result, StringComparison.OrdinalIgnoreCase)); ++i)
            result = name + " (" + i + ")";
        return result;
    }

    private static string ValidateName(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length is < 1 or > 80) throw new InvalidOperationException("Playlist name must be 1–80 characters.");
        return name;
    }

    public static void Export(string path, SavedPlaylist profile, IReadOnlyList<string> activeFiles)
    {
        var dto = new PlaylistFile { Name = profile.Name, Purpose = profile.Purpose, Files = activeFiles.ToList() };
        string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    public static PlaylistFile Import(string path)
    {
        if (new FileInfo(path).Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Playlist is larger than 4 MB.");
        var dto = JsonSerializer.Deserialize<PlaylistFile>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Empty playlist JSON.");
        if (dto.FormatVersion != 1 || dto.Purpose is not (0 or 1))
            throw new InvalidDataException("Unsupported playlist file format.");
        dto.Name = ValidateName(dto.Name);
        if (dto.Files is null || dto.Files.Count > 10000)
            throw new InvalidDataException("Playlist has too many entries.");
        // Paths may be missing on this PC: preserve their text so the user can repair
        // their library. Relative paths in imported JSON resolve beside the JSON file.
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        dto.Files = dto.Files.Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(Path.IsPathRooted(x) ? x : Path.Combine(directory, x)))
            .Where(x => Path.GetExtension(x).Equals(".mid", StringComparison.OrdinalIgnoreCase) ||
                        Path.GetExtension(x).Equals(".midi", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return dto;
    }
}
