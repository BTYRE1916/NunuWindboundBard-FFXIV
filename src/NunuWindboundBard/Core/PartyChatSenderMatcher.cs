using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>
/// Match the identity provided by FFXIV's party-chat sender field to a unique
/// real member of the active party. A player link exposes a name and home-world
/// ID; legacy sender text may include the world suffix after the name.
/// Reject ambiguous same-name parties rather than letting arbitrary callers
/// issue native Perform actions.
/// </summary>
public readonly record struct PartyChatMemberIdentity(ulong ContentId, string Name, uint WorldId, string WorldName);

public static class PartyChatSenderMatcher
{
    public static bool TryResolve(string senderDisplay, string? playerLinkName, uint playerLinkWorld,
        IReadOnlyCollection<PartyChatMemberIdentity> members, out ulong contentId, out string error)
    {
        contentId = 0;
        error = string.Empty;
        bool fromPlayerLink = !string.IsNullOrWhiteSpace(playerLinkName);
        string sender = (fromPlayerLink ? playerLinkName : senderDisplay).Trim();
        if (string.IsNullOrWhiteSpace(sender))
        { error = "Chat sender name is empty."; return false; }
        var matching = members.Where(m => m.ContentId != 0 &&
            (fromPlayerLink
                ? string.Equals(sender, m.Name, StringComparison.OrdinalIgnoreCase)
                    && (playerLinkWorld == 0 || m.WorldId == 0 || playerLinkWorld == m.WorldId)
                : IsMatchingDisplayName(sender, m.Name, m.WorldName))).ToArray();
        if (matching.Length != 1)
        {
            error = matching.Length == 0
                ? "Sender does not match a member of the active FFXIV party. Check party leader and cross-world name."
                : "More than one party member matches the chat sender. A unique name/world is required.";
            return false;
        }
        contentId = matching[0].ContentId;
        return true;
    }

    private static bool IsMatchingDisplayName(string display, string character, string world)
    {
        if (string.IsNullOrWhiteSpace(character)) return false;
        if (display.Equals(character, StringComparison.OrdinalIgnoreCase)) return true;
        if (string.IsNullOrWhiteSpace(world)) return false;
        return display.Equals(character + "@" + world, StringComparison.OrdinalIgnoreCase) ||
               display.Equals(character + " " + world, StringComparison.OrdinalIgnoreCase) ||
               display.Equals(character + " (" + world + ")", StringComparison.OrdinalIgnoreCase) ||
               display.Equals(character + "·" + world, StringComparison.OrdinalIgnoreCase);
    }
}
