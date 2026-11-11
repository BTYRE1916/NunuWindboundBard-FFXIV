using System;
using System.IO;

namespace NunuWindboundBard.Core;

/// <summary>
/// Pure song-to-OBS-text mapping. The MIDI path is never sent to OBS, only its
/// basename without extension. This is independent of the MIDI output engine.
/// </summary>
public static class ObsSongTitleOverlay
{
    public const string DefaultSourceName = "Windbound Song Title";

    public static string FromMidiPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        string title = Path.GetFileNameWithoutExtension(path);
        return CleanTitle(title);
    }

    public static string CleanTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        // Remove any user-file control characters; keep Unicode artist/song names.
        string clean = string.Join(" ", title.Split((char[]?)null,
            StringSplitOptions.RemoveEmptyEntries)).Trim();
        // Avoid enormous text sources from arbitrarily long MIDI filenames.
        return clean.Length <= 140 ? clean : clean[..137] + "...";
    }

    public static string TextForPlayback(string title, bool playing, bool onlyWhilePlaying)
    {
        title = CleanTitle(title);
        return onlyWhilePlaying && !playing ? "" : title;
    }

    public static bool IsTextInputKind(string kind) => kind is
        "text_gdiplus_v3" or "text_gdiplus_v2" or "text_gdiplus" or
        "text_ft2_source" or "text_ft2_source_v2";
}
