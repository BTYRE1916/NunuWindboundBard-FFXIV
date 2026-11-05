using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

public enum ChordStrategy { Highest = 0, Lowest = 1, Loudest = 2, GentleArpeggio = 3, PreserveAll = 4 }
public sealed record BardNote(double TimeMs, int Pitch, int Track, int Velocity, double DurationMs, int Channel = 0);
public sealed record BardArrangeOptions(MidiOptimizeOptions Optimizer, ChordStrategy Strategy,
    int Transpose, int MinimumGapMs, int ArpeggioSpacingMs);
public sealed record BardArrangeReport(int SourceNotes, int OptimizedNotes, int ChordGroups,
    int SimplifiedNotes, int TimingDrops, int FinalNotes, double DurationMs);
public sealed record BardArrangement(IReadOnlyList<BardNote> Notes, BardArrangeReport Report);

/// <summary>
/// Adapt every MIDI note to FFXIV's C3-C6 range and keep simultaneous notes when
/// PreserveAll is selected. Optional reduction modes are available for busy songs.
/// </summary>
public static class BardArranger
{
    private sealed record Candidate(double TimeMs, int Pitch, int Track, int Channel, int Velocity, double DurationMs);

    public static BardArrangement Build(MidiSong song, BardArrangeOptions options)
    {
        if (options.MinimumGapMs is < 40 or > 300) throw new ArgumentOutOfRangeException(nameof(options.MinimumGapMs));
        if (options.ArpeggioSpacingMs is < 40 or > 300) throw new ArgumentOutOfRangeException(nameof(options.ArpeggioSpacingMs));
        if (options.Transpose is < -24 or > 24) throw new ArgumentOutOfRangeException(nameof(options.Transpose));
        Dictionary<MidiLaneKey, MidiLanePlan> lanes = MidiOptimization.BuildLookup(options.Optimizer);
        List<Candidate> candidates = new();
        foreach (MidiNote note in song.Notes)
        {
            MidiLaneKey key = new(note.Track, note.Channel);
            MidiLanePlan lane = lanes.GetValueOrDefault(key, new MidiLanePlan(key, note.Channel != 9, 0));
            if (!lane.Include) continue;
            int? pitch = MidiOptimization.TransformPitch(note.Pitch, lane, options.Optimizer.RepairRemainingOctaves);
            if (pitch is null) continue;
            int target = pitch.Value + options.Transpose;
            if (options.Optimizer.RepairRemainingOctaves)
            {
                while (target < NoteMapper.LowestNote) target += 12;
                while (target > NoteMapper.HighestNote) target -= 12;
            }
            if (target is < NoteMapper.LowestNote or > NoteMapper.HighestNote) continue;
            candidates.Add(new Candidate(note.TimeMs, target, note.Track, note.Channel, note.Velocity, note.DurationMs));
        }
        // Group events with a 2ms tolerance, to avoid floating-point tick rounding drift.
        var groups = candidates.GroupBy(n => (long)Math.Round(n.TimeMs / 2.0))
            .OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
        List<Candidate> selected = new();
        int chordGroups = 0;
        foreach (var (group, index) in groups.Select((g, i) => (g, i)))
        {
            var distinct = group.GroupBy(x => x.Pitch).Select(g => g.OrderByDescending(x => x.Velocity).First()).ToList();
            if (distinct.Count > 1) chordGroups++;
            if (options.Strategy == ChordStrategy.PreserveAll)
            {
                // Multiple distinct pitches share the same timestamp; output them
                // together instead of flattening the harmony into a melody line.
                selected.AddRange(distinct.OrderBy(n => n.Pitch));
                continue;
            }
            List<Candidate> ordered = options.Strategy switch
            {
                ChordStrategy.Lowest => distinct.OrderBy(n => n.Pitch).ToList(),
                ChordStrategy.Loudest => distinct.OrderByDescending(n => n.Velocity).ThenByDescending(n => n.Pitch).ToList(),
                _ => distinct.OrderByDescending(n => n.Pitch).ToList()
            };
            if (ordered.Count == 0) continue;
            Candidate first = ordered[0];
            selected.Add(first);
            if (options.Strategy == ChordStrategy.GentleArpeggio && index + 1 < groups.Count)
            {
                double nextGroupTime = groups[index + 1].Min(n => n.TimeMs);
                // Max two extra chord notes, only when there is real room before the next phrase.
                for (int i = 1; i < Math.Min(3, ordered.Count); i++)
                {
                    double noteTime = first.TimeMs + i * options.ArpeggioSpacingMs;
                    if (noteTime + options.MinimumGapMs >= nextGroupTime) break;
                    selected.Add(ordered[i] with { TimeMs = noteTime });
                }
            }
        }
        // Minimum-gap thinning applies ONLY to the legacy simplified modes.
        // Full chords retain every original onset and its original MIDI duration.
        selected.Sort((a, b) => a.TimeMs != b.TimeMs
            ? a.TimeMs.CompareTo(b.TimeMs) : a.Pitch.CompareTo(b.Pitch));
        if (options.Strategy == ChordStrategy.PreserveAll)
        {
            return new BardArrangement(selected.Select(n => new BardNote(
                n.TimeMs, n.Pitch, n.Track, n.Velocity, n.DurationMs, n.Channel)).ToList(),
                new BardArrangeReport(song.Notes.Count, candidates.Count, chordGroups,
                    candidates.Count - selected.Count, 0, selected.Count, song.DurationMs));
        }
        List<BardNote> result = new();
        int timeDrops = 0;
        double last = double.NegativeInfinity;
        foreach (Candidate note in selected)
        {
            if (note.TimeMs - last < options.MinimumGapMs)
            {
                timeDrops++;
                continue;
            }
            result.Add(new BardNote(note.TimeMs, note.Pitch, note.Track, note.Velocity, note.DurationMs, note.Channel));
            last = note.TimeMs;
        }
        return new BardArrangement(result,
            new BardArrangeReport(song.Notes.Count, candidates.Count, chordGroups,
                candidates.Count - selected.Count, timeDrops, result.Count,
                song.DurationMs));
    }
}
