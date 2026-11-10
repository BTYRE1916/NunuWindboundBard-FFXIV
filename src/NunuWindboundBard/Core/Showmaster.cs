using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>Only fixed, harmless emote commands may reach FFXIV chat; no arbitrary
/// macro scripts, party messages, URLs or plugin commands are accepted.</summary>
public sealed class ShowmasterStep
{
    public string Emote { get; set; } = "bow";
    public uint EmoteId { get; set; } // v0.38.1: FFXIV Emote-sheet RowId; zero for legacy saved routines
    public int DelayAfterMs { get; set; } = 1000;
}

public sealed class ShowmasterRoutine
{
    public int Id { get; set; }
    public string Name { get; set; } = "New routine";
    public List<ShowmasterStep> Steps { get; set; } = new();
}

public static class ShowmasterCodec
{
    private static readonly (char Code, string Emote)[] Actions = {
        ('B', "bow"), ('C', "clap"), ('W', "wave"), ('H', "cheer"),
        ('D', "dance"), ('P', "pose"), ('S', "salute"), ('J', "joy"),
        ('L', "laugh"), ('F', "flex"), ('V', "victory"), ('G', "goodbye")
    };
    public static IReadOnlyList<string> AllowedEmotes => Actions.Select(x => x.Emote).ToArray();
    public static bool IsAllowed(string? emote) =>
        !string.IsNullOrEmpty(emote) && Actions.Any(x => x.Emote == emote);

    public static void Validate(IReadOnlyList<ShowmasterStep> steps)
    {
        if (steps is null || steps.Count is < 1 or > 12)
            throw new InvalidOperationException("A stage routine requires 1–12 emotes.");
        foreach (var s in steps)
            if (s is null || (s.EmoteId == 0 && !IsAllowed(s.Emote)) ||
                s.EmoteId > ushort.MaxValue || s.DelayAfterMs is < 0 or > 5000)
                throw new InvalidOperationException("Showmaster only supports approved emotes and delays of 0–5000ms.");
    }

    public static string Encode(IReadOnlyList<ShowmasterStep> steps)
    {
        Validate(steps);
        if (steps.Any(s => s.EmoteId != 0))
            throw new InvalidOperationException("Legacy emote commands cannot encode numeric IDs.");
        return string.Concat(steps.Select(s => {
            char code = Actions.First(x => x.Emote == s.Emote).Code;
            int ticks = Math.Clamp((s.DelayAfterMs + 125) / 250, 0, 20);
            return code.ToString() + ticks.ToString("X2", CultureInfo.InvariantCulture);
        }));
    }

    public static bool TryDecode(string? packed, out List<ShowmasterStep> steps)
    {
        steps = new();
        if (string.IsNullOrEmpty(packed) || packed.Length % 3 != 0 || packed.Length > 36) return false;
        for (int i = 0; i < packed.Length; i += 3)
        {
            var code = packed[i];
            var found = Actions.FirstOrDefault(a => a.Code == code);
            if (found.Emote is null ||
                !int.TryParse(packed.AsSpan(i + 1, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out int delayTicks) || delayTicks > 20)
            { steps.Clear(); return false; }
            steps.Add(new ShowmasterStep { Emote = found.Emote, DelayAfterMs = delayTicks * 250 });
        }
        return true;
    }

    /// <summary>
    /// v0.38.1: 4 hex digits for an Emote-sheet ID, 2 for delay (0–20 x 250ms).
    /// Twelve steps use 72 characters, fitting a 130-character party message.
    /// This transports *only* IDs and timing; never arbitrary text or macros.
    /// </summary>
    public static string EncodeV2(IReadOnlyList<ShowmasterStep> steps)
    {
        Validate(steps);
        if (steps.Any(s => s.EmoteId == 0))
            throw new InvalidOperationException("Resolve legacy emotes from the local game catalog first.");
        return string.Concat(steps.Select(s =>
            s.EmoteId.ToString("X4", CultureInfo.InvariantCulture) +
            Math.Clamp((s.DelayAfterMs + 125) / 250, 0, 20).ToString("X2", CultureInfo.InvariantCulture)));
    }

    public static bool TryDecodeV2(string? packed, out List<ShowmasterStep> steps)
    {
        steps = new();
        if (string.IsNullOrEmpty(packed) || packed.Length % 6 != 0 || packed.Length > 72) return false;
        for (int index = 0; index < packed.Length; index += 6)
        {
            if (!uint.TryParse(packed.AsSpan(index, 4), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out uint id) || id == 0 || id > ushort.MaxValue ||
                !int.TryParse(packed.AsSpan(index + 4, 2), NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture, out int ticks) || ticks > 20)
            { steps.Clear(); return false; }
            steps.Add(new ShowmasterStep { EmoteId = id, Emote = "", DelayAfterMs = ticks * 250 });
        }
        return true;
    }

    public static List<ShowmasterRoutine> Defaults() => new() {
        new() { Id = 1, Name = "Group Bow", Steps = new() {
            new() { Emote = "bow", DelayAfterMs = 1400 },
            new() { Emote = "wave", DelayAfterMs = 1000 } } },
        new() { Id = 2, Name = "Applause for the Audience", Steps = new() {
            new() { Emote = "clap", DelayAfterMs = 1200 },
            new() { Emote = "cheer", DelayAfterMs = 1000 },
            new() { Emote = "wave", DelayAfterMs = 1000 } } },
        new() { Id = 3, Name = "Encore Celebration", Steps = new() {
            new() { Emote = "joy", DelayAfterMs = 1250 },
            new() { Emote = "cheer", DelayAfterMs = 1000 },
            new() { Emote = "bow", DelayAfterMs = 900 } } },
        new() { Id = 4, Name = "Finale Salute", Steps = new() {
            new() { Emote = "salute", DelayAfterMs = 1100 },
            new() { Emote = "bow", DelayAfterMs = 1250 },
            new() { Emote = "wave", DelayAfterMs = 900 } } }
    };
}

/// <summary>A single low-rate locally executed routine. Game-state validation and
/// actual native chat submission are supplied by its caller on the framework thread.</summary>
public sealed class ShowmasterPlayer
{
    private readonly List<ShowmasterStep> steps = new();
    private int index;
    private long nextStepUnixMs;
    public bool Active { get; private set; }
    public int SkippedSteps { get; private set; }
    public string Status { get; private set; } = "Ready for an intermission.";
    public int CompletedSteps => index;
    public int TotalSteps => steps.Count;

    public void Start(IReadOnlyList<ShowmasterStep> source, long startUnixMs)
    {
        ShowmasterCodec.Validate(source);
        steps.Clear();
        steps.AddRange(source.Select(s => new ShowmasterStep {
            Emote = s.Emote, EmoteId = s.EmoteId, DelayAfterMs = s.DelayAfterMs
        }));
        index = 0;
        SkippedSteps = 0;
        nextStepUnixMs = startUnixMs;
        Active = true;
        Status = "Routine queued for the next intermission beat.";
    }

    public void Stop(string reason = "Routine stopped.")
    {
        Active = false;
        Status = reason;
    }

    /// <summary>Compatibility with v0.38 local routines and smoke tests.</summary>
    public void Tick(long unixMs, bool safe, Func<string, bool> execute) =>
        TickWithSteps(unixMs, safe, step => execute(step.Emote) ?
            ShowmasterExecutionResult.Performed : ShowmasterExecutionResult.Failed);

    public void TickWithSteps(long unixMs, bool safe, Func<ShowmasterStep, ShowmasterExecutionResult> execute)
    {
        if (!Active) return;
        if (!safe) { Stop("Routine cancelled: a performance started or the character is not ready."); return; }
        if (unixMs < nextStepUnixMs) return;
        if (index >= steps.Count) { Stop("Routine complete."); return; }
        ShowmasterStep step = steps[index];
        var result = execute(step);
        if (result == ShowmasterExecutionResult.Failed)
        { Stop("An emote could not be sent; routine stopped safely."); return; }
        if (result == ShowmasterExecutionResult.SkippedUnavailable) SkippedSteps++;
        index++;
        if (index >= steps.Count)
        {
            Stop(SkippedSteps == 0 ? "Routine completed successfully." :
                $"Routine completed; {SkippedSteps} unavailable emote(s) skipped or replaced.");
            return;
        }
        nextStepUnixMs = unixMs + Math.Max(250, step.DelayAfterMs);
        Status = result == ShowmasterExecutionResult.Performed ?
            $"Performed emote ({index}/{steps.Count})." :
            $"Skipped/replaced unavailable emote ({index}/{steps.Count}).";
    }
}

public enum ShowmasterExecutionResult { Performed, SkippedUnavailable, Failed }
