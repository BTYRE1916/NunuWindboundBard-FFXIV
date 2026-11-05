using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.Character;

namespace NunuWindboundBard.Core;

public sealed record PartyChoice(ulong ContentId, string Name, bool IsLocal);

public static class PartyRoster
{
    // Read only on Dalamud's framework / UI thread. ContentId is stable across
    // rejoining a party, unlike the per-zone actor EntityId.
    public static unsafe ulong GetLocalContentId(IObjectTable objects, IPartyList party)
    {
        var local = objects.LocalPlayer;
        if (local is null) return 0;
        foreach (var member in party)
            if (member.EntityId == local.EntityId && member.ContentId != 0)
                return member.ContentId;
        // Fallback for cases where the local player is absent from the party array.
        return ((Character*)local.Address)->ContentId;
    }

    public static IReadOnlyList<PartyChoice> GetChoices(IObjectTable objects, IPartyList party)
    {
        ulong localId = GetLocalContentId(objects, party);
        List<PartyChoice> choices = new();
        foreach (var member in party)
        {
            if (member.ContentId == 0 || choices.Any(x => x.ContentId == member.ContentId)) continue;
            string name = member.Name.TextValue;
            if (string.IsNullOrWhiteSpace(name)) name = "Unnamed party member";
            choices.Add(new PartyChoice(member.ContentId, name, member.ContentId == localId));
        }
        if (localId != 0 && choices.All(x => x.ContentId != localId))
        {
            string name = objects.LocalPlayer?.Name.TextValue ?? "Me";
            choices.Insert(0, new PartyChoice(localId, name, true));
        }
        return choices;
    }
}
