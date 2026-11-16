using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace NunuWindboundBard.Core;

internal static class ConcertPack
{
    private const long MaxSongBytes = 50L * 1024 * 1024;
    private const long MaxTotalBytes = 512L * 1024 * 1024;
    private const string Index = "concert.json";
    private static readonly JsonSerializerOptions Options = new() { IncludeFields = true, MaxDepth = 32 };

    internal sealed record Piece(string Name, string Hash, long Bytes, bool Included);
    internal sealed record Manifest(int Version, ConcertSnapshot Snapshot, Piece[] Pieces, StageFormationPreset? Stage);
    internal sealed record Import(ConcertSnapshot Snapshot, StageFormationPreset? Stage, string Folder);
    private static string Key(int i) => $"song-{i + 1:00000}.mid";
    private static string Entry(int i) => "midi/" + Key(i);

    internal static void Export(string filename, ConcertSnapshot snapshot, HashSet<string> include, StageFormationPreset? stage)
    {
        ConcertSnapshotFormat.Validate(snapshot);
        if (stage is not null) StageFormationMath.Validate(stage);
        string temporary = filename + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                string[] paths = snapshot.PlaylistFiles.ToArray();
                long total = 0;
                var pieces = new List<Piece>(paths.Length);
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < paths.Length; i++)
                {
                    using var input = File.OpenRead(paths[i]);
                    long length = input.Length;
                    total = checked(total + length);
                    if (length is < 14 or > MaxSongBytes || total > MaxTotalBytes)
                        throw new InvalidDataException("Concert packs support songs up to 50 MiB and a set up to 512 MiB.");
                    string hash = Convert.ToHexString(SHA256.HashData(input));
                    bool embedded = include.Contains(paths[i]);
                    pieces.Add(new(SafeName(Path.GetFileName(paths[i])), hash, length, embedded));
                    map.Add(paths[i], Key(i));
                    if (embedded)
                    {
                        input.Position = 0;
                        using var output = archive.CreateEntry(Entry(i), CompressionLevel.Optimal).Open();
                        CopyBounded(input, output, length, hash);
                    }
                }
                Remap(snapshot, map);
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(new Manifest(1, snapshot, pieces.ToArray(), stage), Options);
                if (data.Length > ConcertSnapshotFormat.MaxJsonBytes)
                    throw new InvalidDataException("Concert metadata is too large.");
                using var index = archive.CreateEntry(Index).Open();
                index.Write(data);
            }
            File.Move(temporary, filename, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal static Import Open(string filename, string cache, string[] library)
    {
        if (new FileInfo(filename).Length > MaxTotalBytes + ConcertSnapshotFormat.MaxJsonBytes + 2 * 1024 * 1024)
            throw new InvalidDataException("Concert pack is too large.");
        using var archive = ZipFile.OpenRead(filename);
        if (archive.Entries.Count > ConcertSnapshotFormat.MaxSongs + 1)
            throw new InvalidDataException("Concert pack has too many entries.");
        var entries = archive.Entries.ToDictionary(e => e.FullName, StringComparer.Ordinal);
        if (!entries.TryGetValue(Index, out var index) || index.Length is <= 0 or > ConcertSnapshotFormat.MaxJsonBytes)
            throw new InvalidDataException("Concert metadata is missing or too large.");
        Manifest pack;
        using (var stream = index.Open())
        {
            byte[] bytes = new byte[(int)index.Length];
            stream.ReadExactly(bytes);
            if (stream.ReadByte() != -1) throw new InvalidDataException("Invalid concert metadata size.");
            pack = JsonSerializer.Deserialize<Manifest>(bytes, Options) ?? throw new InvalidDataException("Invalid concert metadata.");
        }
        if (pack.Version != 1 || pack.Snapshot is null || pack.Pieces is null || pack.Pieces.Length > ConcertSnapshotFormat.MaxSongs)
            throw new InvalidDataException("Unsupported concert pack.");
        ConcertSnapshotFormat.Validate(pack.Snapshot);
        if (pack.Stage is not null) StageFormationMath.Validate(pack.Stage);
        if (pack.Pieces.Length != pack.Snapshot.PlaylistFiles.Count ||
            pack.Pieces.Any(p => p is null || p.Hash is not { Length: 64 } || !p.Hash.All(Uri.IsHexDigit) ||
                p.Bytes is < 14 or > MaxSongBytes) || pack.Pieces.Sum(p => p.Bytes) > MaxTotalBytes ||
            !pack.Snapshot.PlaylistFiles.SequenceEqual(Enumerable.Range(0, pack.Pieces.Length).Select(Key), StringComparer.Ordinal))
            throw new InvalidDataException("Concert pack contains an invalid song list.");
        if (entries.Count != 1 + pack.Pieces.Count(p => p.Included))
            throw new InvalidDataException("Unexpected files in the concert pack.");

        var local = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var missingSizes = pack.Pieces.Where(p => !p.Included).Select(p => p.Bytes).ToHashSet();
        if (missingSizes.Count > 0)
        {
            foreach (string path in library.Take(5000))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || !missingSizes.Contains(info.Length)) continue;
                    using var stream = info.OpenRead();
                    local.TryAdd(Convert.ToHexString(SHA256.HashData(stream)), path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }

        string destination = Path.Combine(cache, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(destination);
        try
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < pack.Pieces.Length; i++)
            {
                var p = pack.Pieces[i];
                string target = Path.Combine(destination, $"{i + 1:00000} - {SafeName(p.Name)}");
                using var file = File.Open(target, FileMode.CreateNew);
                if (p.Included)
                {
                    if (!entries.TryGetValue(Entry(i), out var entry) || entry.Length != p.Bytes)
                        throw new InvalidDataException($"The bundled MIDI '{p.Name}' is missing or incomplete.");
                    using var input = entry.Open();
                    CopyBounded(input, file, p.Bytes, p.Hash);
                }
                else
                {
                    if (!local.TryGetValue(p.Hash, out string? known))
                        throw new InvalidDataException($"Add the exact MIDI '{p.Name}' to a Windbound playlist, then import again.");
                    using var input = File.OpenRead(known);
                    CopyBounded(input, file, p.Bytes, p.Hash);
                }
                file.Close();
                _ = MidiFileReader.Load(target);
                map.Add(Key(i), target);
            }
            Remap(pack.Snapshot, map);
            var result = ConcertSnapshotFormat.CopyWithNewId(pack.Snapshot);
            return new(result, pack.Stage, destination);
        }
        catch
        {
            Directory.Delete(destination, recursive: true);
            throw;
        }
    }

    private static void CopyBounded(Stream source, Stream target, long size, string hash)
    {
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[64 * 1024];
        long copied = 0;
        for (int count; (count = source.Read(buffer)) > 0;)
        {
            copied += count;
            if (copied > size) throw new InvalidDataException("MIDI exceeded its declared size.");
            digest.AppendData(buffer.AsSpan(0, count));
            target.Write(buffer, 0, count);
        }
        if (copied != size || !Convert.ToHexString(digest.GetHashAndReset()).Equals(hash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The MIDI does not match the concert pack fingerprint.");
    }

    private static string SafeName(string? input)
    {
        var text = string.Concat((input ?? "").Take(100)
            .Select(c => char.IsControl(c) || "<>:\"/\\|?*".Contains(c) ? '_' : c)).Trim().Trim('.');
        if (text.Length == 0) text = "Song";
        return text.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) ||
            text.EndsWith(".midi", StringComparison.OrdinalIgnoreCase) ? text : text + ".mid";
    }

    private static void Remap(ConcertSnapshot snapshot, Dictionary<string, string> map)
    {
        snapshot.PlaylistFiles = snapshot.PlaylistFiles.Select(path => map[path]).ToList();
        snapshot.SelectedMidiPath = map.GetValueOrDefault(snapshot.SelectedMidiPath) ?? "";
        snapshot.Songs = snapshot.Songs.ToDictionary(p => map[p.Key], p => p.Value, StringComparer.OrdinalIgnoreCase);
        var smart = snapshot.SmartPlaylist;
        List<string> Keys(IEnumerable<string> paths) => paths.Where(map.ContainsKey).Select(path => map[path]).ToList();
        smart.Favorites = Keys(smart.Favorites);
        smart.PlayNext = Keys(smart.PlayNext);
        smart.ShuffleRemaining = Keys(smart.ShuffleRemaining ?? []);
        smart.Tags = smart.Tags.Where(p => map.ContainsKey(p.Key))
            .ToDictionary(p => map[p.Key], p => p.Value, StringComparer.OrdinalIgnoreCase);
        smart.Recent = smart.Recent.Where(r => r is not null && map.ContainsKey(r.Path))
            .Select(r => new RecentlyPlayedSong { Path = map[r.Path], PlayedUtc = r.PlayedUtc }).ToList();
    }
}
