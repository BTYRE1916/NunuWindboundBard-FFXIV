using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>
/// Compact Stage Manager state. The conductor never trusts party-chat submission
/// as proof of delivery; each participant replies with their actual local state.
/// The report contains only a MIDI hash and flags, not paths, notes, or ContentIds.
/// </summary>
[Flags]
public enum ConcertStageFlags
{
    None = 0,
    MidiMatches = 1,
    TracksAssigned = 2,
    OutputArmed = 4,
    Keyboard37 = 8,
    InstrumentSwitching = 16,
    Busy = 32,
    NativeEnabled = 64,
    Playing = 128
}

public readonly record struct ConcertStageSnapshot(
    string MidiHash, ConcertStageFlags Flags, int CurrentInstrument, int EnabledLanes);

public readonly record struct ConcertStageAssessment(bool Ready, string Message);

public static class ConcertStageHealth
{
    /// <summary>Detect specifically what prevents a native ensemble start.
    /// A deliberately silent conductor can be ready without a keyboard/ARM.
    /// </summary>
    public static ConcertStageAssessment Assess(ConcertStageSnapshot state, string desiredHash,
        int expectedInstrument, int? expectedLanes, bool stale)
    {
        if (stale) return new(false, "No recent status report");
        if (!state.Flags.HasFlag(ConcertStageFlags.NativeEnabled)) return new(false, "Native Ensemble disabled");
        if (!state.Flags.HasFlag(ConcertStageFlags.MidiMatches) ||
            !string.Equals(state.MidiHash, desiredHash, StringComparison.OrdinalIgnoreCase))
            return new(false, "Different MIDI / file not loaded");
        if (state.Flags.HasFlag(ConcertStageFlags.Busy)) return new(false, "Previous song still active / loading");
        if (state.Flags.HasFlag(ConcertStageFlags.InstrumentSwitching)) return new(false, "Switching instrument");
        if (expectedLanes.HasValue && state.EnabledLanes != expectedLanes.Value)
            return new(false, $"Track mismatch: expected {expectedLanes.Value}, got {state.EnabledLanes}");
        if (state.EnabledLanes == 0)
        {
            if (expectedLanes == 0) return new(true, "Ready (silent conductor / unassigned part)");
            return new(false, "No MIDI tracks assigned");
        }
        if (!state.Flags.HasFlag(ConcertStageFlags.TracksAssigned)) return new(false, "Track assignment not ready");
        if (expectedInstrument > 0 && state.CurrentInstrument != expectedInstrument)
            return new(false, $"Wrong instrument: expected #{expectedInstrument}, got #{state.CurrentInstrument}");
        if (!state.Flags.HasFlag(ConcertStageFlags.OutputArmed)) return new(false, "Output not ARMed");
        if (!state.Flags.HasFlag(ConcertStageFlags.Keyboard37)) return new(false, "37-key Performance keyboard not ready");
        return new(true, "Ready");
    }

    public static int? ExpectedLaneCount(IReadOnlyDictionary<MidiLaneKey, ulong> assignments,
        IReadOnlyCollection<MidiLaneKey> lanes, ulong memberId)
    {
        if (assignments.Count == 0) return null;
        return lanes.Count(lane => assignments.TryGetValue(lane, out ulong owner) && owner == memberId);
    }
}
