using System.Text;
using System.Text.Json;

namespace NunuWindboundBard.Core;

public enum ShortcutKind { Command, Category, Spacer }
public enum ShortcutMode { All, Cycle, Random }
public enum ShortcutDock { Floating, Top, Bottom, Left, Right }
public enum ShortcutHotkeyAction { Radial, Toggle }

[Flags]
public enum ShortcutContext
{
    None = 0,
    Performing = 1,
    InCombat = 2,
    InDuty = 4,
    InParty = 8,
    Solo = 16,
    Conductor = 32,
    Follower = 64,
}

[Flags]
public enum ShortcutModifiers { None = 0, Ctrl = 1, Shift = 2, Alt = 4 }

public sealed class ShortcutItem
{
    public string Id { get; set; } = ShortcutBarSettings.NewId();
    public string Name { get; set; } = "Shortcut";
    public string Tooltip { get; set; } = "";
    public string Commands { get; set; } = "";
    public int Icon { get; set; }
    public string CustomIcon { get; set; } = "";
    public ShortcutKind Kind { get; set; }
    public ShortcutMode Mode { get; set; }
    public List<ShortcutItem> Items { get; set; } = [];
}

public sealed class ShortcutBar
{
    public string Id { get; set; } = ShortcutBarSettings.NewId();
    public string Name { get; set; } = "New bar";
    public bool Enabled { get; set; } = true;
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool IconOnly { get; set; }
    public bool Background { get; set; } = true;
    public ShortcutDock Dock { get; set; } = ShortcutDock.Bottom;
    public float Alignment { get; set; } = 0.5f;
    public float X { get; set; } = 40;
    public float Y { get; set; } = 40;
    public float Opacity { get; set; } = 0.9f;
    public int Columns { get; set; } = 6;
    public int ButtonSize { get; set; } = 34;
    public int FunctionKey { get; set; }
    public ShortcutModifiers Modifiers { get; set; } = ShortcutModifiers.Ctrl;
    public ShortcutHotkeyAction HotkeyAction { get; set; }
    public string ConditionId { get; set; } = "";
    public List<ShortcutItem> Items { get; set; } = [];
}

public sealed class ShortcutConditionSet
{
    public string Id { get; set; } = ShortcutBarSettings.NewId();
    public string Name { get; set; } = "New condition set";
    public ShortcutContext Required { get; set; }
    public ShortcutContext Excluded { get; set; }
    public bool MatchAny { get; set; }

    internal bool Matches(ShortcutContext state) =>
        (state & Excluded) == 0 &&
        (Required == ShortcutContext.None || (MatchAny ? (state & Required) != 0 : (state & Required) == Required));
}

public sealed class ShortcutBarSettings
{
    internal const int MaxBars = 24;
    internal const int MaxConditions = 24;
    internal const int MaxItems = 64;
    internal const int MaxDepth = 6;

    public List<ShortcutBar> Bars { get; set; } = [];
    public List<ShortcutConditionSet> Conditions { get; set; } = [];

    internal static string NewId() => Guid.NewGuid().ToString("N");
    internal bool IsAllowed(ShortcutBar bar, ShortcutContext current) =>
        bar.Enabled && (string.IsNullOrEmpty(bar.ConditionId) ||
            Conditions.Find(set => set.Id == bar.ConditionId)?.Matches(current) == true);

    internal static ShortcutBar CreateExample() => new()
    {
        Name = "Windbound controls",
        Items =
        [
            new() { Name = "Windbound", Commands = "/windbound", Tooltip = "Open the Windbound window." },
            new() { Name = "HUD", Commands = "/windbound hud", Tooltip = "Toggle Windbound's Performance HUD." },
            new()
            {
                Name = "Playback",
                Kind = ShortcutKind.Category,
                Items =
                [
                    new() { Name = "Stop", Commands = "/windbound stop" },
                    new() { Name = "Release held notes", Commands = "/windbound midipanic" },
                ],
            },
        ],
    };

    internal void Normalize()
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        Conditions = (Conditions ?? []).OfType<ShortcutConditionSet>().Take(MaxConditions).ToList();
        foreach (var set in Conditions)
        {
            set.Id = UniqueId(set.Id, ids);
            set.Name = CleanName(set.Name, "Condition set");
        }

        Bars = (Bars ?? []).OfType<ShortcutBar>().Take(MaxBars).ToList();
        foreach (var bar in Bars)
        {
            bar.Id = UniqueId(bar.Id, ids);
            bar.Name = CleanName(bar.Name, "Shortcut bar");
            bar.Dock = Enum.IsDefined(bar.Dock) ? bar.Dock : ShortcutDock.Bottom;
            bar.Alignment = FiniteClamp(bar.Alignment, 0, 1, 0.5f);
            bar.Opacity = FiniteClamp(bar.Opacity, 0.2f, 1, 0.9f);
            bar.X = FiniteClamp(bar.X, 0, 15000, 40);
            bar.Y = FiniteClamp(bar.Y, 0, 15000, 40);
            bar.ButtonSize = Math.Clamp(bar.ButtonSize, 22, 70);
            bar.Columns = Math.Clamp(bar.Columns, 1, 12);
            bar.FunctionKey = Math.Clamp(bar.FunctionKey, 0, 12);
            if (!Enum.IsDefined(bar.HotkeyAction)) bar.HotkeyAction = ShortcutHotkeyAction.Radial;
            bar.Modifiers &= ShortcutModifiers.Ctrl | ShortcutModifiers.Shift | ShortcutModifiers.Alt;
            bar.ConditionId ??= "";
            bar.Items = NormalizeItems(bar.Items, 0, ids);
        }
    }

    internal static string CleanName(string? name, string fallback)
    {
        string clean = Limit(name, 80).Replace('#', ' ').Replace('\n', ' ').Replace('\r', ' ').Trim();
        return clean.Length == 0 ? fallback : clean;
    }

    internal static string CleanIconName(string? name)
    {
        string candidate = Limit(name, 128).Trim();
        return candidate.EndsWith(".png", StringComparison.OrdinalIgnoreCase) &&
            candidate.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or ' ') &&
            candidate[0] != '.' ? candidate : "";
    }

    private static List<ShortcutItem> NormalizeItems(List<ShortcutItem>? items, int depth, HashSet<string> ids)
    {
        if (depth >= MaxDepth) return [];
        var result = (items ?? []).OfType<ShortcutItem>().Take(MaxItems).ToList();
        foreach (var item in result)
        {
            item.Id = UniqueId(item.Id, ids);
            item.Name = CleanName(item.Name, "Shortcut");
            item.Tooltip = Limit(item.Tooltip, 512);
            item.Commands = Limit(item.Commands, 4096);
            item.Icon = Math.Clamp(item.Icon, 0, 999999);
            item.CustomIcon = CleanIconName(item.CustomIcon);
            if (!Enum.IsDefined(item.Kind)) item.Kind = ShortcutKind.Command;
            if (!Enum.IsDefined(item.Mode)) item.Mode = ShortcutMode.All;
            item.Items = NormalizeItems(item.Items, depth + 1, ids);
        }
        return result;
    }

    private static string UniqueId(string? id, HashSet<string> ids) =>
        id is { Length: 32 } && Guid.TryParseExact(id, "N", out _) && ids.Add(id) ? id : NewId();

    private static string Limit(string? text, int max) =>
        string.IsNullOrEmpty(text) ? "" : text[..Math.Min(text.Length, max)];

    private static float FiniteClamp(float value, float min, float max, float fallback) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    internal const int MaxShareLength = 131072;

    internal sealed class Share
    {
        public string Format { get; set; } = "";
        public ShortcutBar? Bar { get; set; }
        public ShortcutItem? Item { get; set; }
        public ShortcutConditionSet? Conditions { get; set; }
    }

    internal string Export(ShortcutBar bar) => JsonSerializer.Serialize(new Share
    {
        Format = "Windbound.ShortcutBar.1",
        Bar = bar,
        Conditions = Conditions.Find(set => set.Id == bar.ConditionId),
    }, ShareOptions);

    internal static string Export(ShortcutItem item) => JsonSerializer.Serialize(
        new Share { Format = "Windbound.ShortcutBar.1", Item = item }, ShareOptions);

    internal static bool TryImport(string text, out Share? result)
    {
        result = null;
        if (text.Length is 0 or > MaxShareLength) return false;
        try
        {
            var share = JsonSerializer.Deserialize<Share>(text, ShareOptions);
            if (share?.Format != "Windbound.ShortcutBar.1" || (share.Bar is null) == (share.Item is null))
                return false;

            var model = new ShortcutBarSettings();
            if (share.Bar is { } bar)
            {
                model.Bars.Add(bar);
                bar.Id = NewId();
                bar.Enabled = false;
                bar.FunctionKey = 0;
                bar.ConditionId = "";
                if (share.Conditions is { } set)
                {
                    set.Id = NewId();
                    bar.ConditionId = set.Id;
                    model.Conditions.Add(set);
                }
            }
            if (share.Item is { } item)
                model.Bars.Add(new ShortcutBar { Items = [item] });

            model.Normalize();
            foreach (var newBar in model.Bars)
                ResetIds(newBar.Items);
            result = share;
            return true;
        }
        catch (JsonException) { return false; }
        catch (NotSupportedException) { return false; }
    }

    private static void ResetIds(List<ShortcutItem> items)
    {
        foreach (var item in items)
        {
            item.Id = NewId();
            ResetIds(item.Items);
        }
    }

    private static readonly JsonSerializerOptions ShareOptions = new()
    {
        WriteIndented = true,
        MaxDepth = 32,
    };
}

internal enum ShortcutStepKind { Command, Wait, Macro }
internal readonly record struct ShortcutStep(ShortcutStepKind Kind, string Command = "", int Macro = 0, float Seconds = 0);
internal sealed record ShortcutSequence(ShortcutStep[] Steps, bool Locked);

internal static class ShortcutCommands
{
    internal static ShortcutSequence Parse(string text, ShortcutMode mode, int selected)
    {
        var steps = new List<ShortcutStep>();
        bool macroLock = false;
        foreach (string raw in text.Split('\n', 65))
        {
            var line = raw.Trim();
            if (line.Equals("/macrolock", StringComparison.OrdinalIgnoreCase))
            {
                macroLock = true;
                continue;
            }

            if (line.StartsWith("//m", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line.AsSpan(3), out int slot) && slot is >= 0 and < 200)
                steps.Add(new(ShortcutStepKind.Macro, Macro: slot));
            else if (line.Length < 2 || line[0] != '/' || line[1] == '/' ||
                     line.Any(char.IsControl) || Encoding.UTF8.GetByteCount(line) > 500)
                continue;
            else if (line.StartsWith("/wait ", StringComparison.OrdinalIgnoreCase))
            {
                if (TrySeconds(line.AsSpan(6), out float wait))
                    steps.Add(new(ShortcutStepKind.Wait, Seconds: wait));
            }
            else if (!line.Equals("/wait", StringComparison.OrdinalIgnoreCase))
            {
                int waitTag = line.LastIndexOf("<wait.", StringComparison.OrdinalIgnoreCase);
                if (waitTag > 0 && line.EndsWith('>') && TrySeconds(line.AsSpan(waitTag + 6, line.Length - waitTag - 7), out float delay))
                {
                    steps.Add(new(ShortcutStepKind.Command, line[..waitTag].TrimEnd()));
                    steps.Add(new(ShortcutStepKind.Wait, Seconds: delay));
                }
                else
                    steps.Add(new(ShortcutStepKind.Command, line));
            }
        }

        if (mode == ShortcutMode.All)
            return new(steps.ToArray(), macroLock);
        var actions = steps.Where(step => step.Kind != ShortcutStepKind.Wait).ToArray();
        return actions.Length == 0 ? new([], false) : new([actions[Select(mode, selected, actions.Length)]], false);
    }

    internal static int Select(ShortcutMode mode, int index, int count) =>
        mode == ShortcutMode.Random ? Random.Shared.Next(count) : index % count;

    private static bool TrySeconds(ReadOnlySpan<char> text, out float seconds) =>
        float.TryParse(text, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out seconds) &&
        float.IsFinite(seconds) && seconds is >= 0 and <= 60;
}
