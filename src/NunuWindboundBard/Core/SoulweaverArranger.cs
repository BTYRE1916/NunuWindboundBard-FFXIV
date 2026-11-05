using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>Offline, deterministic part-generation engine. No external services, game calls or keyboard injection.</summary>
public enum SoulweaverVoice { Melody, Countermelody, Bass, Harmony, Arpeggio, HighHarmony, InnerHarmony, Rhythm, Accent }

public sealed record SoulweaverOptions(int Performers, int MaxSimultaneousNotesPerPart = 0);
public sealed record SoulweaverPart(int Index, string Name, string RoleDescription, int SuggestedInstrumentId,
    IReadOnlyList<BardNote> Notes)
{
    public int LowestPitch => Notes.Count == 0 ? 0 : Notes.Min(n => n.Pitch);
    public int HighestPitch => Notes.Count == 0 ? 0 : Notes.Max(n => n.Pitch);
}
public sealed record SoulweaverAnalysis(string EstimatedKey, double KeyConfidence, int SourceNotes,
    int ExportNotes, int ChordOnsets, int PhraseCount, int DroppedForDensity, int LowestPitch, int HighestPitch,
    IReadOnlyList<string> Warnings);
public sealed record SoulweaverResult(int Performers, IReadOnlyList<SoulweaverPart> Parts, SoulweaverAnalysis Analysis,
    double DurationMs);

public static class SoulweaverArranger
{
    private static readonly double[] MajorProfile = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
    private static readonly double[] MinorProfile = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
    private static readonly string[] PitchNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

    private sealed record Assigned(BardNote Note, SoulweaverVoice Voice);
    private sealed record LaneProfile(MidiLaneKey Key, double Median, int Count);
    private sealed record PartTemplate(string Name, string Description, int InstrumentId);

    public static SoulweaverResult Generate(BardArrangement arrangement, SoulweaverOptions options)
    {
        if (options.Performers is not (1 or 2 or 4 or 8))
            throw new ArgumentOutOfRangeException(nameof(options.Performers), "Select Solo, Duet, Quartet or Octet.");
        if (options.MaxSimultaneousNotesPerPart is < 0 or > 16)
            throw new ArgumentOutOfRangeException(nameof(options.MaxSimultaneousNotesPerPart));
        if (arrangement.Notes.Count == 0) throw new InvalidOperationException("The source arrangement contains no playable notes.");

        // No alteration of onset, pitch, duration, channel or velocity unless an explicit per-part chord cap was selected.
        var notes = arrangement.Notes.OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray();
        var laneProfiles = notes.GroupBy(n => new MidiLaneKey(n.Track, n.Channel))
            .Select(g => new LaneProfile(g.Key, Median(g.Select(n => n.Pitch)), g.Count()))
            .OrderBy(p => p.Median).ThenBy(p => p.Key.Track).ThenBy(p => p.Key.Channel).ToArray();
        var laneRoles = GetLaneRoles(laneProfiles);
        List<Assigned> classified = new(notes.Length);
        int chordOnsets = 0;
        int phraseCount = 0;
        double lastTime = double.NegativeInfinity;
        foreach (var onset in notes.GroupBy(n => (long)Math.Round(n.TimeMs / 2.0)).OrderBy(g => g.Key))
        {
            var group = onset.OrderByDescending(n => n.Pitch).ThenByDescending(n => n.Velocity).ToArray();
            if (group.Length > 1) chordOnsets++;
            if (double.IsNegativeInfinity(lastTime) || onset.Min(n => n.TimeMs) - lastTime >= 750)
                phraseCount++;
            lastTime = onset.Max(n => n.TimeMs);
            var byLane = group.GroupBy(n => new MidiLaneKey(n.Track, n.Channel));
            foreach (var laneGroup in byLane)
            {
                var voiceGroup = laneGroup.OrderByDescending(n => n.Pitch).ToArray();
                SoulweaverVoice baseVoice = laneRoles[laneGroup.Key];
                for (int i = 0; i < voiceGroup.Length; i++)
                {
                    var voice = PickVoice(baseVoice, i, voiceGroup.Length,
                        voiceGroup[0].Pitch - voiceGroup[^1].Pitch);
                    classified.Add(new Assigned(voiceGroup[i], voice));
                }
            }
        }

        var templates = GetTemplates(options.Performers);
        var allocations = Enumerable.Range(0, options.Performers).Select(_ => new List<BardNote>()).ToArray();
        foreach (Assigned n in classified)
        {
            int destination = SelectPart(n.Voice, options.Performers, n.Note);
            allocations[destination].Add(n.Note);
        }

        int dropped = 0;
        if (options.MaxSimultaneousNotesPerPart > 0)
        {
            for (int part = 0; part < allocations.Length; part++)
            {
                var slim = new List<BardNote>(allocations[part].Count);
                foreach (var onset in allocations[part].GroupBy(n => (long)Math.Round(n.TimeMs / 2.0)))
                {
                    var important = onset.OrderByDescending(n => n.Velocity)
                        .ThenByDescending(n => n.DurationMs).ThenByDescending(n => n.Pitch)
                        .Take(options.MaxSimultaneousNotesPerPart).ToArray();
                    dropped += onset.Count() - important.Length;
                    slim.AddRange(important);
                }
                allocations[part] = slim;
            }
        }
        var parts = templates.Select((t, i) =>
        {
            var n = allocations[i].OrderBy(x => x.TimeMs).ThenBy(x => x.Pitch).ToArray();
            int instrument = RecommendInstrument(options.Performers, i, n, t.InstrumentId);
            return new SoulweaverPart(i, t.Name, t.Description, instrument, n);
        }).ToArray();

        List<string> warnings = new();
        if (parts.Any(p => p.Notes.Count == 0))
            warnings.Add("Some ensemble parts are empty: this MIDI lacks enough independent material. Try fewer musicians or a richer MIDI.");
        if (dropped > 0) warnings.Add($"{dropped} notes were removed by the selected per-part chord-density limit.");
        if (notes.Any(n => n.Pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote))
            warnings.Add("The source includes notes outside C3–C6. Use the FFXIV Optimiser before generating parts.");
        var (key, keyConfidence) = DetectKey(notes);
        return new SoulweaverResult(options.Performers, parts,
            new SoulweaverAnalysis(key, keyConfidence, notes.Length, parts.Sum(p => p.Notes.Count),
                chordOnsets, phraseCount, dropped, notes.Min(n => n.Pitch), notes.Max(n => n.Pitch), warnings),
            arrangement.Report.DurationMs);
    }

    private static Dictionary<MidiLaneKey, SoulweaverVoice> GetLaneRoles(LaneProfile[] lanes)
    {
        Dictionary<MidiLaneKey, SoulweaverVoice> result = new();
        if (lanes.Length == 1) { result[lanes[0].Key] = SoulweaverVoice.Melody; return result; }
        for (int i = 0; i < lanes.Length; i++)
        {
            SoulweaverVoice role = i == 0 ? SoulweaverVoice.Bass :
                i == lanes.Length - 1 ? SoulweaverVoice.Melody :
                i == lanes.Length - 2 ? SoulweaverVoice.Countermelody :
                i % 2 == 1 ? SoulweaverVoice.Arpeggio : SoulweaverVoice.Harmony;
            result[lanes[i].Key] = role;
        }
        return result;
    }

    private static SoulweaverVoice PickVoice(SoulweaverVoice lane, int rank, int chordSize, int spread)
    {
        if (chordSize == 1) return lane;
        if (lane == SoulweaverVoice.Bass) return rank == chordSize - 1 ? SoulweaverVoice.Bass : SoulweaverVoice.Harmony;
        if (lane == SoulweaverVoice.Melody)
        {
            if (rank == 0) return SoulweaverVoice.Melody;
            if (rank == chordSize - 1 && spread >= 12) return SoulweaverVoice.Bass;
            return rank == 1 ? SoulweaverVoice.Countermelody : SoulweaverVoice.Harmony;
        }
        if (rank == chordSize - 1 && spread >= 17) return SoulweaverVoice.Bass;
        return rank == 0 ? lane : rank % 2 == 0 ? SoulweaverVoice.Arpeggio : SoulweaverVoice.Harmony;
    }

    private static PartTemplate[] GetTemplates(int n) => n switch
    {
        1 => [new("Solo — Complete Arrangement", "Melody, bass and supporting harmonies", 2)],
        2 => [new("Lead", "Lead melody and main themes", 2),
              new("Supporting Voice", "Countermelody, bass and supporting harmony", 20)],
        4 => [new("Lead", "Main melody", 2),
              new("Countermelody", "Secondary melodic line", 20),
              new("Bass", "Lower harmonic foundation", 22),
              new("Harmony", "Chord tones and arpeggios", 1)],
        8 => [new("Lead", "Primary melody", 2),
              new("Countermelody", "Second melody", 20),
              new("Bass", "Lower harmonic foundation", 22),
              new("Arpeggios", "Moving accompaniment", 1),
              new("High Harmony", "Upper chord tones", 5),
              new("Inner Harmony", "Middle harmonies", 21),
              new("Rhythm", "Rhythmic chord reinforcement", 3),
              new("Accent", "Musical accent tones", 18)],
        _ => throw new ArgumentOutOfRangeException(nameof(n))
    };

    private static int SelectPart(SoulweaverVoice voice, int n, BardNote note)
    {
        if (n == 1) return 0;
        if (n == 2) return voice == SoulweaverVoice.Melody ? 0 : 1;
        if (n == 4) return voice switch
        {
            SoulweaverVoice.Melody => 0,
            SoulweaverVoice.Countermelody => 1,
            SoulweaverVoice.Bass => 2,
            _ => 3
        };
        return voice switch
        {
            SoulweaverVoice.Melody => 0,
            SoulweaverVoice.Countermelody => 1,
            SoulweaverVoice.Bass => 2,
            SoulweaverVoice.Arpeggio => 3,
            SoulweaverVoice.HighHarmony => 4,
            SoulweaverVoice.InnerHarmony => 5,
            SoulweaverVoice.Rhythm => 6,
            SoulweaverVoice.Accent => 7,
            SoulweaverVoice.Harmony => note.Pitch >= 72 ? 4 : note.Pitch <= 59 ? 5 :
                note.DurationMs < 180 ? 6 : note.Velocity >= 100 ? 7 : 5,
            _ => 5
        };
    }

    private static int RecommendInstrument(int total, int part, IReadOnlyList<BardNote> notes, int standard)
    {
        if (notes.Count == 0) return standard;
        double median = Median(notes.Select(n => n.Pitch));
        if (total == 2 && part == 1) return median < 60 ? 22 : 20; // Cello or Violin
        return standard;
    }

    private static double Median(IEnumerable<int> data)
    {
        var a = data.OrderBy(x => x).ToArray();
        if (a.Length == 0) return 0;
        return a.Length % 2 != 0 ? a[a.Length / 2] : (a[a.Length / 2 - 1] + a[a.Length / 2]) / 2.0;
    }

    private static (string Key, double Confidence) DetectKey(IReadOnlyList<BardNote> notes)
    {
        var bins = new double[12];
        foreach (var n in notes) bins[((n.Pitch % 12) + 12) % 12] += Math.Sqrt(Math.Max(1, n.DurationMs)) * Math.Max(1, n.Velocity);
        double best = double.NegativeInfinity, second = double.NegativeInfinity;
        string name = "Unknown";
        for (int tonic = 0; tonic < 12; tonic++)
            foreach (bool minor in new[] { false, true })
            {
                var profile = minor ? MinorProfile : MajorProfile;
                double score = Correlation(bins, Enumerable.Range(0, 12).Select(i => profile[(i - tonic + 12) % 12]).ToArray());
                if (score > best) { second = best; best = score; name = PitchNames[tonic] + (minor ? " minor" : " major"); }
                else if (score > second) second = score;
            }
        return (name, Math.Clamp((best - second) * 100, 0, 100));
    }

    private static double Correlation(double[] values, double[] profile)
    {
        double x = values.Average(), y = profile.Average(), cross = 0, sx = 0, sy = 0;
        for (int i = 0; i < 12; ++i)
        {
            double dx = values[i] - x, dy = profile[i] - y;
            cross += dx * dy; sx += dx * dx; sy += dy * dy;
        }
        return sx * sy == 0 ? 0 : cross / Math.Sqrt(sx * sy);
    }
}
