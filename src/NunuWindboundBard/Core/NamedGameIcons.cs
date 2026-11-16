using Dalamud.Plugin.Services;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using GameAction = Lumina.Excel.Sheets.Action;

namespace NunuWindboundBard.Core;

internal enum GameIconCategory { Emote, Action, Status, Item }

internal readonly record struct NamedGameIcon(int Id, string Name, GameIconCategory Category, string Command = "");

internal static class NamedGameIcons
{
    internal static NamedGameIcon[] Read(IDataManager data)
    {
        var icons = new List<NamedGameIcon>();
        ReadSheet<Emote>(data, icons, row => new((int)row.Icon, row.Name.ToString().Trim(), GameIconCategory.Emote,
            row.TextCommand.ValueNullable?.Command.ToString().Trim() ?? ""));
        ReadSheet<GameAction>(data, icons, row => new(row.Icon, row.Name.ToString().Trim(), GameIconCategory.Action));
        ReadSheet<Status>(data, icons, row => new((int)row.Icon, row.Name.ToString().Trim(), GameIconCategory.Status));
        ReadSheet<Item>(data, icons, row => new((int)row.Icon, row.Name.ToString().Trim(), GameIconCategory.Item));
        return icons.Distinct().ToArray();
    }

    private static void ReadSheet<T>(IDataManager data, List<NamedGameIcon> icons, Func<T, NamedGameIcon> toIcon)
        where T : struct, IExcelRow<T>
    {
        try
        {
            foreach (var row in data.GetExcelSheet<T>())
            {
                var entry = toIcon(row);
                if (entry.Id is > 0 and <= 999999 && entry.Name.Length > 0)
                    icons.Add(entry);
            }
        }
        catch (Exception error)
        {
            Plugin.Log.Warning($"Could not index {typeof(T).Name} icons: {error.Message}");
        }
    }

    internal static NamedGameIcon[] Search(IReadOnlyList<NamedGameIcon> icons, string query)
    {
        string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return [];

        GameIconCategory? category = null;
        foreach (var choice in Enum.GetValues<GameIconCategory>())
        {
            if (!words[0].Equals(choice.ToString(), StringComparison.OrdinalIgnoreCase)) continue;
            category = choice;
            words = words[1..];
            break;
        }
        bool commandOnly = words.Length > 0 && words[0].StartsWith('/');
        var terms = words.Select(word => word.TrimStart('/')).Where(word => word.Length > 0).ToArray();
        if (words.Length > 0 && terms.Length == 0) return [];

        string term = string.Join(' ', terms);
        return icons.Where(icon => category is null || icon.Category == category)
            .Select(icon => (Icon: icon, Rank: MatchRank(icon, term, terms, commandOnly)))
            .Where(result => result.Rank >= 0)
            .OrderBy(result => result.Rank)
            .ThenBy(result => result.Icon.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(result => result.Icon.Category)
            .ThenBy(result => result.Icon.Id)
            .Select(result => result.Icon).ToArray();
    }

    private static int MatchRank(NamedGameIcon icon, string term, string[] words, bool commandOnly)
    {
        const StringComparison comparison = StringComparison.OrdinalIgnoreCase;
        if (words.Length == 0) return 0;
        var name = commandOnly ? ReadOnlySpan<char>.Empty : icon.Name.AsSpan();
        var command = icon.Command.AsSpan().TrimStart('/');
        if (name.Equals(term, comparison) || command.Equals(term, comparison)) return 0;
        if (name.StartsWith(term, comparison) || command.StartsWith(term, comparison)) return 1;
        foreach (string word in words)
            if (!name.Contains(word, comparison) && !command.Contains(word, comparison))
                return -1;
        return 2;
    }
}
