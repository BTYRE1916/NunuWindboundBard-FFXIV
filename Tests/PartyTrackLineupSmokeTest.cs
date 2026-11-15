using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    // The real MIDI lane-key source is excluded so pure mapping logic can be
    // tested without an FFXIV/Dalamud runtime.
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class Program
{
    private static void Require(bool value, string message)
    {
        if (!value) throw new Exception("FAILED: " + message);
    }

    public static void Main()
    {
        var first = Enumerable.Range(0, 8).Select(i => new MidiLaneKey(i+1, 0)).ToList();
        var owners = Enumerable.Range(0, 8).ToDictionary(i => first[i], i => 50000UL + (ulong)i);
        var lineup = PartyTrackLineup.Capture(first, owners);
        Require(lineup.PerformerIdsBySlot.Count == 8, "capture all eight slots");
        var second = Enumerable.Range(0, 8).Select(i => new MidiLaneKey(i*2+3, i%5)).ToList();
        var reused = lineup.Apply(second);
        Require(reused.Assignments.Count == 8 && !reused.HasDifferentLaneCount, "reuse full octet on differently numbered MIDI");
        for (int i=0; i<8; i++)
            Require(reused.Assignments[second[i]] == 50000UL+(ulong)i, "stable performer by lane position " + i);
        Require(owners.Count == 8 && first.All(x => owners.ContainsKey(x)), "source assignment unchanged");

        var shortMidi = lineup.Apply(second.Take(4).ToList());
        Require(shortMidi.HasDifferentLaneCount && shortMidi.Assignments.Count == 4 && shortMidi.UnassignedNewSlots == 0,
            "short song keeps four matching performer slots");
        var extended = second.Concat(new[] { new MidiLaneKey(31, 9), new MidiLaneKey(33, 4) }).ToList();
        var longMidi = lineup.Apply(extended);
        Require(longMidi.HasDifferentLaneCount && longMidi.UnassignedNewSlots == 2 && longMidi.Assignments.Count == 8 &&
            !longMidi.Assignments.ContainsKey(extended[8]), "new tracks not assigned to random Bards");

        var muted = PartyTrackLineup.Capture(first, new Dictionary<MidiLaneKey,ulong> { [first[0]] = 111, [first[3]] = 222 });
        var muteMapped = muted.Apply(second);
        Require(muteMapped.Assignments.Count == 2 && !muteMapped.Assignments.ContainsKey(second[1]),
            "silent lanes stay silent");
        var profiles = new Dictionary<string, PartyTrackLineup> { ["ensemble-octet"] = lineup, ["ensemble-duet"] = muted };
        Require(profiles["ensemble-octet"].Apply(second).Assignments.Count == 8 &&
            profiles["ensemble-duet"].Apply(second).Assignments.Count == 2, "independent named Ensemble playlists");

        var restored = JsonSerializer.Deserialize<PartyTrackLineup>(JsonSerializer.Serialize(lineup));
        Require(restored != null && restored.Apply(second).Assignments[second[7]] == 50007, "saved JSON lineup survives reload");
        Console.WriteLine("PASS: octet reuse, reordered source keys, missing/extra parts, silent slots, profile isolation, JSON persistence.");
    }
}
