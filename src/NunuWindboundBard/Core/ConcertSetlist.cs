using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>Optional concert annotations. No scripts, chat or automatic START actions.</summary>
public sealed class ConcertSongCue
{
    public string Announcement { get; set; } = "";
    public int BreakAfterSeconds { get; set; } = 0;
    public bool HoldAfter { get; set; }
    public bool Encore { get; set; }
}

public static class ConcertSetlist
{
    // Keep annotations scoped to the active saved playlist and file path.
    public static string EntryKey(string playlistId, string path) => playlistId + "::" + path.ToUpperInvariant();

    /// <summary>Queue requests and the Smart Playlist order both work, but a
    /// Native Concert setlist bypasses Repeat One (the audience needs the next song).
    /// This method does not remove a queue entry until the song loads successfully.</summary>
    public static string? PickNext(SmartPlaylistState state, IReadOnlyList<string> files,
        int currentIndex, Func<string, bool> exists)
    {
        foreach (var candidate in SmartPlaylistManager.Candidates(state, files, currentIndex, manual: true))
        {
            if (currentIndex >= 0 && currentIndex < files.Count &&
                string.Equals(files[currentIndex], candidate.Path, StringComparison.OrdinalIgnoreCase) &&
                candidate.Origin != PlaylistCandidateOrigin.PlayNext) continue;
            if (exists(candidate.Path)) return candidate.Path;
        }
        return null;
    }
}
