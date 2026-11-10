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
            if (s is null || !IsAllowed(s.Emote) || s.DelayAfterMs is < 0 or > 5000)
                throw new InvalidOperationException("Showmaster only supports approved emotes and delays of 0–5000ms.");
    }

    public static string Encode(IReadOnlyList<ShowmasterStep> steps)
    {
        Validate(steps);
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
    public string Status { get; private set; } = "Ready for an intermission.";
    public int CompletedSteps => index;
    public int TotalSteps => steps.Count;

    public void Start(IReadOnlyList<ShowmasterStep> source, long startUnixMs)
    {
        ShowmasterCodec.Validate(source);
        steps.Clear();
        steps.AddRange(source.Select(s => new ShowmasterStep {
            Emote = s.Emote, DelayAfterMs = s.DelayAfterMs
        }));
        index = 0;
        nextStepUnixMs = startUnixMs;
        Active = true;
        Status = "Routine queued for the next intermission beat.";
    }

    public void Stop(string reason = "Routine stopped.")
    {
        Active = false;
        Status = reason;
    }

    public void Tick(long unixMs, bool safe, Func<string, bool> execute)
    {
        if (!Active) return;
        if (!safe) { Stop("Routine cancelled: a performance started or the character is not ready."); return; }
        if (unixMs < nextStepUnixMs) return;
        if (index >= steps.Count) { Stop("Routine complete."); return; }
        ShowmasterStep step = steps[index];
        if (!execute(step.Emote)) { Stop("FFXIV did not accept the emote command. Check the game client."); return; }
        index++;
        if (index >= steps.Count) { Stop("Routine completed successfully."); return; }
        nextStepUnixMs = unixMs + Math.Max(250, step.DelayAfterMs);
        Status = $"Performed /{step.Emote} motion ({index}/{steps.Count}).";
    }
}
