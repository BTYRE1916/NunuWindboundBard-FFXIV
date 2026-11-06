using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>
/// Reusable conductor lineup: the first MIDI lane belongs to the first saved Bard,
/// second lane to the second, etc. Tracks/channels may be numbered differently
/// in each MIDI; the musical lane ORDER is the stable performer slot.
/// Separate Solo/Ensemble saved playlists never share a lineup by accident.
/// Zero means intentionally silent; unrecognised extra lanes are also silent.
/// </summary>
public sealed class PartyTrackLineup
{
    public List<ulong> PerformerIdsBySlot { get; set; } = new();

    public static PartyTrackLineup Capture(IReadOnlyList<MidiLaneKey> lanes,
        IReadOnlyDictionary<MidiLaneKey, ulong> assignments)
    {
        ArgumentNullException.ThrowIfNull(lanes);
        ArgumentNullException.ThrowIfNull(assignments);
        if (lanes.Count > 256) throw new ArgumentException("Cannot save more than 256 MIDI performer slots.", nameof(lanes));
        var lineup = new PartyTrackLineup();
        for (int i = 0; i < lanes.Count; ++i)
            lineup.PerformerIdsBySlot.Add(assignments.GetValueOrDefault(lanes[i]));
        return lineup;
    }

    public PartyTrackLineupResult Apply(IReadOnlyList<MidiLaneKey> currentLanes)
    {
        ArgumentNullException.ThrowIfNull(currentLanes);
        if (currentLanes.Count > 256) throw new ArgumentException("Cannot apply more than 256 MIDI performer slots.", nameof(currentLanes));
        var result = new Dictionary<MidiLaneKey, ulong>();
        var slots = PerformerIdsBySlot ?? new List<ulong>();
        int count = Math.Min(currentLanes.Count, slots.Count);
        for (int i = 0; i < count; i++)
            if (slots[i] != 0) result[currentLanes[i]] = slots[i];
        return new PartyTrackLineupResult(result, count, currentLanes.Count, slots.Count);
    }
}

public readonly record struct PartyTrackLineupResult(
    Dictionary<MidiLaneKey, ulong> Assignments, int MatchedSlots, int NewLaneCount, int SavedLaneCount)
{
    public bool HasDifferentLaneCount => NewLaneCount != SavedLaneCount;
    public int UnassignedNewSlots => Math.Max(0, NewLaneCount - SavedLaneCount);
}
