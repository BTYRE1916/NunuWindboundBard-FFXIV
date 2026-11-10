using System;
using System.Collections.Generic;
using System.Linq;
using Lumina.Excel.Sheets;

namespace NunuWindboundBard.Core;

/// <summary>
/// A read-only view of the current character's actual FFXIV emotes. The game
/// remains authoritative about which emotes each character has unlocked.
/// Nothing here enables or fabricates an emote unlock.
/// </summary>
public sealed record ShowmasterEmoteEntry(uint Id, string Name, string Command, uint IconId, bool Unlocked);

public sealed class ShowmasterEmoteCatalog
{
    private List<ShowmasterEmoteEntry> all = new();
    private Dictionary<uint, ShowmasterEmoteEntry> byId = new();
    public ulong ContentId { get; private set; }
    public bool Loaded { get; private set; }
    public string Status { get; private set; } = "Emote catalog has not been loaded.";
    public IReadOnlyList<ShowmasterEmoteEntry> All => all;
    public IReadOnlyList<ShowmasterEmoteEntry> Unlocked => all.Where(x => x.Unlocked).ToArray();

    /// <summary>Accept ONLY a single slash-command token from FFXIV's own Emote sheet.</summary>
    public static bool SafeCommand(string? command) => command is { Length: >= 2 and <= 49 } &&
        command[0] == '/' && command.Skip(1).All(c => char.IsLetterOrDigit(c) || c is '_' or '-');

    public void Refresh(ulong contentId)
    {
        all.Clear();
        byId.Clear();
        ContentId = contentId;
        Loaded = false;
        if (contentId == 0) { Status = "Log in to view your unlocked emotes."; return; }
        try
        {
            foreach (var emote in Plugin.Data.GetExcelSheet<Emote>())
            {
                if (emote.RowId is 0 or > ushort.MaxValue or 218 or 219 || emote.Icon == 0)
                    continue; // no command, decorative or obsolete game-data rows
                var text = emote.TextCommand.ValueNullable;
                if (text is null) continue;
                string command = text.Value.Command.ToString().Trim();
                string name = emote.Name.ToString().Trim();
                if (!SafeCommand(command) || string.IsNullOrWhiteSpace(name)) continue;
                bool unlocked = Plugin.UnlockState.IsEmoteUnlocked(emote);
                var entry = new ShowmasterEmoteEntry(emote.RowId, name, command, emote.Icon, unlocked);
                all.Add(entry);
                byId[entry.Id] = entry;
            }
            all.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            Loaded = true;
            Status = $"{all.Count(x => x.Unlocked)} unlocked of {all.Count} player emotes in game data.";
        }
        catch (Exception error)
        {
            all.Clear(); byId.Clear();
            Status = "Could not read the game's emote catalog: " + error.Message;
        }
    }

    public ShowmasterEmoteEntry? Find(uint id) => byId.TryGetValue(id, out var value) ? value : null;

    public ShowmasterEmoteEntry? FindCommand(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;
        string slash = token.StartsWith('/') ? token : "/" + token;
        return all.FirstOrDefault(e => e.Command.Equals(slash, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>One bit per requested step; does not reveal the member's full unlock inventory.</summary>
    public int AvailabilityMask(IReadOnlyList<ShowmasterStep> steps)
    {
        int mask = 0;
        for (int i = 0; i < Math.Min(12, steps.Count); i++)
            if (steps[i].EmoteId > 0 && Find(steps[i].EmoteId)?.Unlocked == true)
                mask |= (1 << i);
        return mask;
    }
}
