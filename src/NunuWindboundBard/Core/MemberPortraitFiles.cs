using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace NunuWindboundBard.Core;

/// <summary>
/// Explicit, local-only Adventurer Plate thumbnail cache. Content ID keys remain
/// stable when party slots and local actor IDs change. Filenames are validated to
/// prevent a tampered JSON configuration from redirecting file access elsewhere.
/// </summary>
public static class MemberPortraitFiles
{
    private static readonly Regex SafeName = new(
        @"\A[0-9A-Fa-f]{16}_[0-9A-Fa-f]{32}\.png\z", RegexOptions.CultureInvariant);

    public static string ContentKey(ulong contentId) => contentId.ToString("X16");
    public static string NewName(ulong contentId)
    {
        if (contentId == 0) throw new ArgumentOutOfRangeException(nameof(contentId));
        return ContentKey(contentId) + "_" + Guid.NewGuid().ToString("N") + ".png";
    }
    public static string Directory(string pluginConfigDirectory) =>
        Path.Combine(pluginConfigDirectory, "BandPortraits");

    public static string? Resolve(string pluginConfigDirectory,
        IReadOnlyDictionary<string, string>? fileNames, ulong contentId)
    {
        if (contentId == 0 || fileNames is null ||
            !fileNames.TryGetValue(ContentKey(contentId), out var file) ||
            string.IsNullOrWhiteSpace(file) || !SafeName.IsMatch(file) ||
            !file.StartsWith(ContentKey(contentId) + "_", StringComparison.OrdinalIgnoreCase))
            return null;
        return Path.Combine(Directory(pluginConfigDirectory), file);
    }
}
