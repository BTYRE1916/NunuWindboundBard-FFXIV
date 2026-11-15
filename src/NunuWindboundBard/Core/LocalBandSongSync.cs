using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace NunuWindboundBard.Core;

/// <summary>One ordered playlist per stage, with immutable MIDI copies shared by local clients.</summary>
internal static class LocalBandSongSync
{
    private const int MaxSongs = 10_000;
    private const int MaxMidiBytes = 50 * 1024 * 1024;
    private const int MaxIndexBytes = 8 * 1024 * 1024;
    private const string IndexFile = "playlist-v1.json";

    internal sealed record Song(string Hash, string FileName);
    internal sealed record Index(Guid Revision, Song[] Songs);
    internal sealed record PublishedFile(Song Song, long Length, DateTime Modified);
    internal sealed record Publication(Dictionary<string, PublishedFile> Sources, int Count, int Skipped, string? Error);
    internal sealed record IncomingSong(Song Song, string Path, bool AlreadyListed);
    internal sealed record Reception(Guid Revision, List<IncomingSong> Songs, int Unavailable);

    internal static string GetStageDirectory(string root, string stage)
    {
        stage = stage.Trim();
        if (stage.Length is < 1 or > 50)
            throw new ArgumentException("Use a stage name between 1 and 50 characters.");
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(stage.ToLowerInvariant())));
        return Path.Combine(root, key);
    }

    internal static Publication Publish(string directory, string[] paths,
        IReadOnlyDictionary<string, PublishedFile> previous, CancellationToken cancellation)
    {
        Directory.CreateDirectory(directory);
        var sources = new Dictionary<string, PublishedFile>(StringComparer.OrdinalIgnoreCase);
        var songs = new List<Song>();
        var hashes = new HashSet<string>(StringComparer.Ordinal);
        string? error = null;
        int skipped = Math.Max(0, paths.Length - MaxSongs);
        foreach (string path in paths.Take(MaxSongs))
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var file = new FileInfo(path);
                if (previous.TryGetValue(path, out var copied) && file.Length == copied.Length &&
                    file.LastWriteTimeUtc == copied.Modified && File.Exists(GetSongPath(directory, copied.Song)))
                {
                    sources[path] = copied;
                }
                else
                {
                    sources[path] = CopySong(directory, file, cancellation);
                }
                var song = sources[path].Song;
                if (hashes.Add(song.Hash)) songs.Add(song);
            }
            catch (Exception ex) when (IsFileError(ex) || ex is NotSupportedException or ArgumentException)
            {
                skipped++;
                error ??= $"{Path.GetFileName(path)}: {ex.Message}";
            }
        }
        string temporary = Path.Combine(directory, $".playlist-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(new Index(Guid.NewGuid(), songs.ToArray())));
            cancellation.ThrowIfCancellationRequested();
            File.Move(temporary, Path.Combine(directory, IndexFile), overwrite: true);
        }
        finally { File.Delete(temporary); }
        return new Publication(sources, songs.Count, skipped, error);
    }

    private static PublishedFile CopySong(string directory, FileInfo file, CancellationToken cancellation)
    {
        if (!IsMidiName(file.Name)) throw new InvalidDataException("Choose a .mid or .midi file.");
        using var input = File.OpenRead(file.FullName);
        long length = input.Length;
        if (length is < 14 or > MaxMidiBytes) throw new InvalidDataException("Invalid MIDI size or larger than 50 MiB.");
        var song = new Song(Convert.ToHexString(SHA256.HashData(input)), file.Name);
        string destination = GetSongPath(directory, song);
        if (!File.Exists(destination) || !string.Equals(HashFile(destination), song.Hash, StringComparison.Ordinal))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".{Guid.NewGuid():N}.tmp");
            try
            {
                input.Position = 0;
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write))
                    input.CopyTo(output);
                if (!string.Equals(HashFile(temporary), song.Hash, StringComparison.Ordinal))
                    throw new IOException("The MIDI changed during copying. Share the playlist again.");
                _ = MidiFileReader.Load(temporary);
                cancellation.ThrowIfCancellationRequested();
                File.Move(temporary, destination, overwrite: true);
            }
            finally { File.Delete(temporary); }
        }
        return new PublishedFile(song, length, file.LastWriteTimeUtc);
    }

    internal static Reception? Receive(string directory, Guid lastRevision, string[] receivedHashes,
        string[] localPlaylist, CancellationToken cancellation)
    {
        string indexPath = Path.Combine(directory, IndexFile);
        if (!File.Exists(indexPath)) return null;
        // Delete sharing allows the conductor to replace the index while we read the prior revision.
        using var stream = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
        if (stream.Length > MaxIndexBytes) throw new InvalidDataException("The shared playlist is too large.");
        var index = JsonSerializer.Deserialize<Index>(stream);
        if (index is null || index.Revision == Guid.Empty || index.Songs is null || index.Songs.Length > MaxSongs)
            throw new InvalidDataException("The shared playlist index is invalid.");
        if (index.Revision == lastRevision) return null;

        var seen = new HashSet<string>(receivedHashes, StringComparer.OrdinalIgnoreCase);
        var pending = new List<(Song Song, string Path, long Length)>();
        int unavailable = 0;
        foreach (var song in index.Songs)
        {
            cancellation.ThrowIfCancellationRequested();
            if (song is null || !ValidSong(song)) { unavailable++; continue; }
            if (!seen.Add(song.Hash)) continue;
            string path = GetSongPath(directory, song);
            try
            {
                var file = new FileInfo(path);
                if (!file.Exists || (file.Attributes & FileAttributes.ReparsePoint) != 0 ||
                    file.Length is < 14 or > MaxMidiBytes || HashFile(path) != song.Hash)
                {
                    unavailable++;
                    continue;
                }
                pending.Add((song, path, file.Length));
            }
            catch (Exception ex) when (IsFileError(ex)) { unavailable++; }
        }

        // Avoid re-adding songs a follower already owns under a different path or file name.
        var candidateSizes = pending.Select(p => p.Length).ToHashSet();
        var localByHash = new Dictionary<string, string>(StringComparer.Ordinal);
        if (candidateSizes.Count > 0)
        {
            foreach (string path in localPlaylist)
            {
                cancellation.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists && candidateSizes.Contains(file.Length))
                        localByHash.TryAdd(HashFile(path), path);
                }
                catch (Exception ex) when (IsFileError(ex) || ex is ArgumentException) { }
            }
        }
        var incoming = new List<IncomingSong>();
        foreach (var entry in pending)
        {
            bool duplicate = localByHash.TryGetValue(entry.Song.Hash, out string? existing);
            incoming.Add(new IncomingSong(entry.Song, existing ?? entry.Path, duplicate));
        }
        return new Reception(index.Revision, incoming, unavailable);
    }

    private static string HashFile(string path)
    {
        using var input = File.OpenRead(path);
        if (input.Length > MaxMidiBytes) throw new InvalidDataException("The MIDI exceeds 50 MiB.");
        return Convert.ToHexString(SHA256.HashData(input));
    }

    private static bool ValidSong(Song song) =>
        song.Hash is { Length: 64 } && song.Hash.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F') &&
        IsMidiName(song.FileName);

    private static bool IsMidiName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.IndexOfAny(['/', '\\', ':']) < 0 &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        (name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".midi", StringComparison.OrdinalIgnoreCase));

    private static string GetSongPath(string directory, Song song) => Path.Combine(directory, song.Hash, song.FileName);
    private static bool IsFileError(Exception ex) => ex is IOException or UnauthorizedAccessException or InvalidDataException;
}
