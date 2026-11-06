using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NunuWindboundBard.Core;

public enum PlaylistRepeatMode { Off = 0, All = 1, One = 2 }

/// <summary>Smart controls belong to the saved playlist ID, never the current Solo/Ensemble role.</summary>
public sealed class SmartPlaylistState
{
    public bool Shuffle { get; set; }
    public PlaylistRepeatMode Repeat { get; set; } = PlaylistRepeatMode.All; // match legacy wraparound
    public List<string> Favorites { get; set; } = new();
    public Dictionary<string, List<string>> Tags { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<RecentlyPlayedSong> Recent { get; set; } = new();
    public List<string> PlayNext { get; set; } = new();
    public List<string> ShuffleRemaining { get; set; } = new();
    public bool ShuffleCycleStarted { get; set; }
}

public sealed class RecentlyPlayedSong
{
    public string Path { get; set; } = "";
    public DateTime PlayedUtc { get; set; }
}

public enum PlaylistCandidateOrigin { PlayNext, RepeatOne, Playlist }
public readonly record struct PlaylistCandidate(string Path, PlaylistCandidateOrigin Origin);

/// <summary>
/// Pure, GUI-independent playlist sequencing. No file is opened or deleted here.
/// The queue is independent of the saved order and only consumed on successful load.
/// Solo mode is the ONLY caller that advances automatically; conductor cues are unchanged.
/// </summary>
public static class SmartPlaylistManager
{
    private static readonly StringComparer Compare = StringComparer.OrdinalIgnoreCase;

    public static SmartPlaylistState Get(Dictionary<string, SmartPlaylistState> states, string playlistId)
    {
        if (!states.TryGetValue(playlistId, out var state) || state is null)
            states[playlistId] = state = new SmartPlaylistState();
        state.Favorites ??= new();
        state.Tags ??= new(StringComparer.OrdinalIgnoreCase);
        state.Recent ??= new();
        state.PlayNext ??= new();
        state.ShuffleRemaining ??= new();
        if (!Enum.IsDefined(state.Repeat)) state.Repeat = PlaylistRepeatMode.All;
        return state;
    }

    public static void ResetShuffle(SmartPlaylistState state)
    {
        state.ShuffleRemaining.Clear();
        state.ShuffleCycleStarted = false;
    }

    public static bool IsFavorite(SmartPlaylistState state, string path) => state.Favorites.Contains(path, Compare);
    public static void ToggleFavorite(SmartPlaylistState state, string path)
    {
        if (IsFavorite(state, path)) state.Favorites.RemoveAll(p => Compare.Equals(p, path));
        else state.Favorites.Add(path);
    }

    public static IReadOnlyList<string> GetTags(SmartPlaylistState state, string path) =>
        state.Tags.TryGetValue(path, out var tags) ? tags : Array.Empty<string>();

    public static void SetTags(SmartPlaylistState state, string path, string csv)
    {
        var tags = (csv ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length is >= 1 and <= 24)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(8).ToList();
        if (tags.Count == 0) state.Tags.Remove(path);
        else state.Tags[path] = tags;
    }

    public static bool Matches(SmartPlaylistState state, string path, string query, string tagFilter, bool favoritesOnly)
    {
        if (favoritesOnly && !IsFavorite(state, path)) return false;
        var tags = GetTags(state, path);
        if (!string.IsNullOrWhiteSpace(tagFilter) && !tags.Any(t => t.Contains(tagFilter, StringComparison.OrdinalIgnoreCase))) return false;
        return string.IsNullOrWhiteSpace(query) || path.Contains(query, StringComparison.OrdinalIgnoreCase) ||
               Path.GetFileName(path).Contains(query, StringComparison.OrdinalIgnoreCase) ||
               tags.Any(t => t.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    public static void AddPlayNext(SmartPlaylistState state, IReadOnlyCollection<string> files, string path, bool first = false)
    {
        if (!files.Contains(path, Compare)) throw new InvalidOperationException("Choose a song in this playlist first.");
        if (state.PlayNext.Count >= 200) throw new InvalidOperationException("The Play Next queue is full (200 songs).");
        if (first) state.PlayNext.Insert(0, path);
        else state.PlayNext.Add(path);
    }

    public static void Move(IList<string> files, int from, int to)
    {
        if (from < 0 || from >= files.Count || to < 0 || to >= files.Count)
            throw new ArgumentOutOfRangeException(nameof(from));
        if (from == to) return;
        var item = files[from];
        files.RemoveAt(from);
        files.Insert(to, item);
    }

    public static void PruneQueue(SmartPlaylistState state, IReadOnlyCollection<string> files)
    {
        state.PlayNext.RemoveAll(p => !files.Contains(p, Compare));
        state.ShuffleRemaining.RemoveAll(p => !files.Contains(p, Compare));
    }

    public static void RecordStarted(SmartPlaylistState state, string path)
    {
        state.Recent.RemoveAll(p => Compare.Equals(p.Path, path));
        state.Recent.Insert(0, new RecentlyPlayedSong { Path = path, PlayedUtc = DateTime.UtcNow });
        if (state.Recent.Count > 50) state.Recent.RemoveRange(50, state.Recent.Count - 50);
        state.ShuffleRemaining.RemoveAll(p => Compare.Equals(p, path));
    }

    public static void ConsumeQueueItem(SmartPlaylistState state, string path)
    {
        var index = state.PlayNext.FindIndex(p => Compare.Equals(p, path));
        if (index >= 0) state.PlayNext.RemoveAt(index);
    }

    public static string PeekNext(SmartPlaylistState state, IReadOnlyList<string> files, int currentIndex)
    {
        var queued = state.PlayNext.FirstOrDefault(p => files.Contains(p, Compare));
        if (queued is not null) return Path.GetFileNameWithoutExtension(queued) + " [queued]";
        if (state.Repeat == PlaylistRepeatMode.One && currentIndex >= 0 && currentIndex < files.Count)
            return Path.GetFileNameWithoutExtension(files[currentIndex]) + " [repeat]";
        if (state.Shuffle) return "Shuffle: surprise next song";
        var index = currentIndex + 1;
        if (index < files.Count) return Path.GetFileNameWithoutExtension(files[index]);
        if (state.Repeat == PlaylistRepeatMode.All && files.Count > 0) return Path.GetFileNameWithoutExtension(files[0]);
        return "End of playlist";
    }

    /// <summary>Produces at most one pass over the saved songs, after queued items. 'manual' bypasses Repeat One.</summary>
    public static IReadOnlyList<PlaylistCandidate> Candidates(
        SmartPlaylistState state, IReadOnlyList<string> files, int currentIndex, bool manual)
    {
        PruneQueue(state, files);
        var result = new List<PlaylistCandidate>();
        var seen = new HashSet<string>(Compare);
        foreach (var path in state.PlayNext)
            if (seen.Add(path)) result.Add(new(path, PlaylistCandidateOrigin.PlayNext));

        if (!manual && state.Repeat == PlaylistRepeatMode.One && currentIndex >= 0 && currentIndex < files.Count)
        {
            var current = files[currentIndex];
            if (seen.Add(current)) result.Add(new(current, PlaylistCandidateOrigin.RepeatOne));
            return result;
        }
        if (state.Shuffle)
        {
            if (state.ShuffleRemaining.Count == 0)
            {
                if (state.ShuffleCycleStarted && state.Repeat == PlaylistRepeatMode.Off)
                    return result;
                var values = files.ToList();
                // The current song has already played; never repeat it within the new bag.
                if (values.Count > 1 && currentIndex >= 0 && currentIndex < files.Count)
                    values.RemoveAll(p => Compare.Equals(p, files[currentIndex]));
                for (int i = values.Count - 1; i > 0; i--)
                {
                    int other = Random.Shared.Next(i + 1);
                    (values[i], values[other]) = (values[other], values[i]);
                }
                state.ShuffleRemaining = values;
                state.ShuffleCycleStarted = true;
            }
            foreach (var path in state.ShuffleRemaining)
                if (seen.Add(path)) result.Add(new(path, PlaylistCandidateOrigin.Playlist));
            return result;
        }

        if (files.Count == 0) return result;
        for (int step = 1; step <= files.Count; step++)
        {
            int index = currentIndex + step;
            if (index >= files.Count && state.Repeat == PlaylistRepeatMode.Off) break;
            index = (index % files.Count + files.Count) % files.Count;
            if (seen.Add(files[index])) result.Add(new(files[index], PlaylistCandidateOrigin.Playlist));
        }
        return result;
    }
}
