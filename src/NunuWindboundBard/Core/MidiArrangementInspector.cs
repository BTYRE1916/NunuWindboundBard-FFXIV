using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

public enum MidiInspectionSeverity { Info, Warning, Error }

public sealed record MidiInspectionIssue(MidiInspectionSeverity Severity, string Description,
    MidiLaneKey? Lane = null);

public sealed record MidiInspectedLane(MidiLaneKey Key, string Name, int SourceNotes,
    int SourceLowest, int SourceHighest, int OriginalOutOfRange,
    int PlayableNotes, int OmittedNotes, int OctaveShift,
    bool Included, int SuggestedInstrument, string InstrumentSource,
    bool HasExplicitOctave, int ExplicitOctave, int OverlappingDuplicates,
    int DenseChords, ulong AssignedPerformer);

public sealed record MidiInspectionReport(int SourceNotes, int PlayableNotes,
    int ExcludedNotes, int DroppedNotes, double DurationMs,
    IReadOnlyList<MidiInspectedLane> Lanes,
    IReadOnlyList<MidiInspectionIssue> Issues)
{
    public int Errors => Issues.Count(i => i.Severity == MidiInspectionSeverity.Error);
    public int Warnings => Issues.Count(i => i.Severity == MidiInspectionSeverity.Warning);
}

/// <summary>
/// Read-only pre-concert analysis. Never mutates source MIDI, track plans or party assignments.
/// The user explicitly approves any proposed optimizer changes in the UI.
/// </summary>
public static class MidiArrangementInspector
{
    public static MidiInspectionReport Inspect(MidiSong midi, IReadOnlyList<MidiLanePlan> plans,
        bool repairOctaves, IReadOnlyDictionary<MidiLaneKey, ulong>? assignments = null,
        IReadOnlyCollection<ulong>? party = null,
        IReadOnlyDictionary<ulong,int>? selectedInstruments = null)
    {
        var opts = new MidiOptimizeOptions(plans, repairOctaves);
        var analysis = MidiOptimization.Analyze(midi, opts);
        var stats = analysis.Lanes.ToDictionary(x => x.Key);
        var grouped = midi.Notes.GroupBy(n => new MidiLaneKey(n.Track, n.Channel))
            .ToDictionary(g => g.Key, g => g.ToArray());
        var metadata = midi.LaneInstruments.ToDictionary(i => new MidiLaneKey(i.Track, i.Channel));
        var issues = new List<MidiInspectionIssue>();
        var items = new List<MidiInspectedLane>();
        int melodicLanes = midi.LaneInstruments.Count(x => !x.IsPercussion);
        foreach (var plan in plans.OrderBy(l => l.Key.Track).ThenBy(l => l.Key.Channel))
        {
            grouped.TryGetValue(plan.Key, out var notes);
            notes ??= Array.Empty<MidiNote>();
            stats.TryGetValue(plan.Key, out var stat);
            metadata.TryGetValue(plan.Key, out var meta);
            int outOfRange = notes.Count(n => n.Pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote);
            int lowest = notes.Length == 0 ? 0 : notes.Min(n => n.Pitch);
            int highest = notes.Length == 0 ? 0 : notes.Max(n => n.Pitch);
            int playable = stat?.Output ?? 0;
            int omitted = stat?.Omitted ?? 0;
            int overlapping = 0;
            // Same key overlapping with itself is a common stuck-note / missed-note risk.
            foreach (var pitchGroup in notes.GroupBy(n => n.Pitch))
            {
                double until = double.NegativeInfinity;
                foreach (var n in pitchGroup.OrderBy(n => n.TimeMs))
                {
                    if (n.TimeMs < until - .001) overlapping++;
                    until = Math.Max(until, n.TimeMs + Math.Max(1, n.DurationMs));
                }
            }
            int dense = notes.GroupBy(n => (long)Math.Round(n.TimeMs / 2d))
                .Count(g => g.Select(n => n.Pitch).Distinct().Count() >= 6);
            int instrument = meta is null ? 0 : MidiInstrumentInference.ForLane(meta, midi.FileName, melodicLanes).InstrumentId;
            string instrumentSource = meta is null ? "No instrument metadata" :
                MidiInstrumentInference.ForLane(meta, midi.FileName, melodicLanes).Source;
            bool explicitOctave = MidiTrackTags.TryGetLaneOctave(midi, plan.Key, out int octaveTag);
            ulong assigned = assignments?.GetValueOrDefault(plan.Key) ?? 0;
            string label = midi.Tracks.FirstOrDefault(t => t.Index == plan.Key.Track)?.Name ??
                "Track " + (plan.Key.Track + 1);
            items.Add(new MidiInspectedLane(plan.Key, label, notes.Length, lowest, highest, outOfRange,
                playable, omitted, plan.OctaveShift, plan.Include, instrument, instrumentSource,
                explicitOctave, octaveTag, overlapping, dense, assigned));
            if (!plan.Include) continue;
            if (notes.Length == 0)
                issues.Add(new(MidiInspectionSeverity.Warning, "Included track contains no MIDI notes", plan.Key));
            else if (playable == 0)
                issues.Add(new(MidiInspectionSeverity.Error,
                    "All notes in this included track fall outside C3–C6; check its octave", plan.Key));
            else if (omitted > 0)
                issues.Add(new(MidiInspectionSeverity.Warning,
                    $"{omitted} note(s) omitted by octave/range filtering", plan.Key));
            if (outOfRange > 0)
                issues.Add(new(MidiInspectionSeverity.Info,
                    $"{outOfRange} source note(s) outside C3–C6 before octave optimisation", plan.Key));
            if (overlapping > 0)
                issues.Add(new(MidiInspectionSeverity.Warning,
                    $"{overlapping} overlapping same-pitch note(s); check sustained notes", plan.Key));
            if (dense > 0)
                issues.Add(new(MidiInspectionSeverity.Info,
                    $"{dense} dense chord onset(s) of six or more pitches", plan.Key));
            if (party is not null)
            {
                if (assigned == 0)
                    issues.Add(new(MidiInspectionSeverity.Warning,
                        "No Bard assigned to this active track", plan.Key));
                else if (!party.Contains(assigned))
                    issues.Add(new(MidiInspectionSeverity.Error,
                        "Assigned Bard is not in the current FFXIV party", plan.Key));
            }
        }
        if (party is not null && assignments is not null)
        {
            foreach (var member in party)
            {
                if (member == 0) continue;
                if (!items.Any(item => item.Included && item.AssignedPerformer == member && item.PlayableNotes > 0))
                    issues.Add(new(MidiInspectionSeverity.Info,
                        "A party member has no playable assigned MIDI part (may be the silent conductor)"));
            }
            foreach (var suggestion in MidiInstrumentInference.ForParty(midi, assignments))
            {
                if (suggestion.ConflictingTracks)
                    issues.Add(new(MidiInspectionSeverity.Warning,
                        $"Performer #{suggestion.ContentId:X}: assigned tracks request different instruments; review the dominant suggestion"));
                if (selectedInstruments is not null && selectedInstruments.TryGetValue(suggestion.ContentId, out int current) &&
                    current != 0 && suggestion.InstrumentId != 0 && current != suggestion.InstrumentId)
                    issues.Add(new(MidiInspectionSeverity.Info,
                        $"Performer #{suggestion.ContentId:X}: manual instrument #{current} differs from MIDI suggestion #{suggestion.InstrumentId}"));
            }
        }
        if (analysis.PlayableNotes == 0)
            issues.Add(new(MidiInspectionSeverity.Error, "The arrangement has no playable notes."));
        if (analysis.RepairedNotes > 0)
            issues.Add(new(MidiInspectionSeverity.Info,
                $"Octave repair folded {analysis.RepairedNotes} note(s) into the FFXIV range."));
        return new MidiInspectionReport(midi.Notes.Count, analysis.PlayableNotes, analysis.ExcludedNotes,
            analysis.OmittedNotes, midi.DurationMs, items, issues);
    }
}
