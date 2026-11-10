using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>Self-contained, game-output-free piano practice engine. All timestamps are from the
/// original SMF, not FFXIV's 37-key arrangement. Call only from the framework thread.</summary>
public enum PianoPracticeMode { WaitForNotes, PlayAlong }
public enum PianoPracticeHand { Both, LowerNotes, UpperNotes }

public sealed class PianoAcademyNote
{
    public double StartMs { get; init; }
    public double DurationMs { get; init; }
    public int Pitch { get; init; }
    public int Velocity { get; init; }
    public int Track { get; init; }
    public bool Hit { get; set; }
    public bool Missed { get; set; }
}

public sealed class PianoAcademyEngine
{
    private readonly List<PianoAcademyNote> notes = new();
    private readonly List<(double TimeMs, int Begin, int End)> chords = new();
    private readonly HashSet<int> held = new();
    private readonly HashSet<int> chordHits = new();
    private int currentChord;
    private int nextMiss;
    private bool hasMidi;
    public IReadOnlyList<PianoAcademyNote> Notes => notes;
    public IReadOnlyCollection<int> HeldPitches => held;
    public PianoPracticeMode Mode { get; set; } = PianoPracticeMode.WaitForNotes;
    public PianoPracticeHand Hand { get; private set; } = PianoPracticeHand.Both;
    public int SplitPitch { get; private set; } = 60;
    public int TrackFilter { get; private set; } = -1;
    public bool Running { get; private set; }
    public bool Finished { get; private set; }
    public bool HasMidi => hasMidi;
    public double PositionMs { get; private set; }
    public double DurationMs { get; private set; }
    public int Hits { get; private set; }
    public int Misses { get; private set; }
    public int WrongKeys { get; private set; }
    public int SkippedOutOfRange { get; private set; }
    public double AccuracyPercent => Hits + Misses == 0 ? 100 : Hits * 100.0 / (Hits + Misses);
    public float Speed { get; set; } = 1f;
    public bool LoopEnabled { get; set; }
    public double LoopStartMs { get; set; }
    public double LoopEndMs { get; set; }
    public const double HitWindowMs = 200;
    private const double ChordWindowMs = 45;

    public void Load(MidiSong song, int trackFilter = -1,
        PianoPracticeHand hand = PianoPracticeHand.Both, int splitPitch = 60)
    {
        ArgumentNullException.ThrowIfNull(song);
        if (splitPitch is < 21 or > 108) throw new ArgumentOutOfRangeException(nameof(splitPitch));
        notes.Clear(); chords.Clear(); held.Clear(); chordHits.Clear();
        hasMidi = true;
        TrackFilter = trackFilter;
        Hand = hand;
        SplitPitch = splitPitch;
        DurationMs = Math.Max(100, song.DurationMs);
        SkippedOutOfRange = 0;
        foreach (var source in song.Notes)
        {
            if (source.Channel == 9 || (trackFilter >= 0 && source.Track != trackFilter)) continue;
            if (source.Pitch is < 21 or > 108) { SkippedOutOfRange++; continue; }
            if (hand == PianoPracticeHand.LowerNotes && source.Pitch >= splitPitch) continue;
            if (hand == PianoPracticeHand.UpperNotes && source.Pitch < splitPitch) continue;
            if (!double.IsFinite(source.TimeMs) || !double.IsFinite(source.DurationMs)) continue;
            notes.Add(new PianoAcademyNote {
                StartMs = Math.Max(0, source.TimeMs), DurationMs = Math.Max(60, source.DurationMs),
                Pitch = source.Pitch, Velocity = source.Velocity, Track = source.Track
            });
        }
        notes.Sort((a, b) => a.StartMs.CompareTo(b.StartMs));
        int start = 0;
        while (start < notes.Count)
        {
            int end = start + 1;
            double time = notes[start].StartMs;
            while (end < notes.Count && notes[end].StartMs - time <= ChordWindowMs) end++;
            chords.Add((time, start, end));
            start = end;
        }
        Rewind();
    }

    public void Clear()
    {
        Rewind();
        notes.Clear(); chords.Clear();
        hasMidi = false;
        DurationMs = 0;
        SkippedOutOfRange = 0;
    }

    public void Rewind()
    {
        Running = Finished = false;
        PositionMs = 0;
        currentChord = nextMiss = 0;
        Hits = Misses = WrongKeys = 0;
        held.Clear(); chordHits.Clear();
        foreach (var n in notes) { n.Hit = false; n.Missed = false; }
    }

    public void Play()
    {
        if (notes.Count == 0) return;
        if (Finished) Rewind();
        Running = true;
    }

    public void Pause() => Running = false;
    public void Stop() => Rewind();
    public bool IsHeld(int pitch) => held.Contains(pitch);
    public IReadOnlyList<int> ExpectedPitches
    {
        get
        {
            if (currentChord >= chords.Count || Mode != PianoPracticeMode.WaitForNotes) return Array.Empty<int>();
            var chord = chords[currentChord];
            return notes.Skip(chord.Begin).Take(chord.End - chord.Begin)
                .Select(n => n.Pitch).Distinct().OrderBy(p => p).ToArray();
        }
    }

    private int FirstNoteAtOrAfter(double startMs)
    {
        int lo = 0, hi = notes.Count;
        while (lo < hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (notes[mid].StartMs < startMs) lo = mid + 1;
            else hi = mid;
        }
        return lo;
    }

    /// <summary>Render only the current visible window, even for orchestral MIDIs
    /// containing hundreds of thousands of notes.</summary>
    public IEnumerable<PianoAcademyNote> VisibleNotes(double behindMs = 850, double aheadMs = 3100)
    {
        double from = PositionMs - behindMs, to = PositionMs + aheadMs;
        for (int i = FirstNoteAtOrAfter(from); i < notes.Count && notes[i].StartMs <= to; i++)
            yield return notes[i];
    }

    public void NoteOn(int pitch, int velocity)
    {
        if (pitch is < 0 or > 127 || velocity <= 0) return;
        held.Add(pitch);
        if (!Running) return;
        if (Mode == PianoPracticeMode.WaitForNotes)
        {
            if (currentChord >= chords.Count) return;
            var chord = chords[currentChord];
            // A learner may press a chord's notes one-by-one. Advance only once all
            // unique pitches in the current chord have been played.
            if (PositionMs + 0.01 < chord.TimeMs) return;
            bool expected = false;
            for (int i = chord.Begin; i < chord.End; i++)
                if (notes[i].Pitch == pitch) { expected = true; break; }
            if (!expected) { WrongKeys++; return; }
            chordHits.Add(pitch);
            bool done = true;
            for (int i = chord.Begin; i < chord.End; i++)
                if (!chordHits.Contains(notes[i].Pitch)) { done = false; break; }
            if (!done) return;
            for (int i = chord.Begin; i < chord.End; i++)
                if (!notes[i].Hit) { notes[i].Hit = true; Hits++; }
            currentChord++;
            chordHits.Clear();
            if (currentChord >= chords.Count) { Running = false; Finished = true; PositionMs = DurationMs; }
            return;
        }
        // Play-Along matches the nearest unscored note with this pitch within a
        // fixed temporal window. Repeated notes are never double counted.
        PianoAcademyNote? match = null;
        double best = HitWindowMs + .001;
        for (int i = FirstNoteAtOrAfter(PositionMs - HitWindowMs);
             i < notes.Count && notes[i].StartMs <= PositionMs + HitWindowMs; i++)
        {
            var n = notes[i];
            double offset = Math.Abs(n.StartMs - PositionMs);
            if (n.Pitch == pitch && !n.Hit && !n.Missed && offset <= best)
            { best = offset; match = n; }
        }
        if (match is not null) { match.Hit = true; Hits++; }
        else WrongKeys++;
    }

    public void NoteOff(int pitch) => held.Remove(pitch);
    public void AllNotesOff() => held.Clear();

    public void Advance(double frameMs)
    {
        if (!Running || notes.Count == 0 || !double.IsFinite(frameMs) || frameMs <= 0) return;
        // Protect practice against a massive leap after a game hang or loading screen.
        double step = Math.Min(frameMs, 250) * Math.Clamp(Speed, .25f, 2f);
        double end = LoopEnabled && LoopEndMs > LoopStartMs + 100
            ? Math.Min(DurationMs, LoopEndMs) : DurationMs;
        double target = Math.Min(end, PositionMs + step);
        if (Mode == PianoPracticeMode.WaitForNotes && currentChord < chords.Count)
            target = Math.Min(target, chords[currentChord].TimeMs);
        PositionMs = Math.Max(0, target);
        if (Mode == PianoPracticeMode.PlayAlong)
        {
            while (nextMiss < notes.Count && notes[nextMiss].StartMs < PositionMs - HitWindowMs)
            {
                var n = notes[nextMiss++];
                if (!n.Hit && !n.Missed) { n.Missed = true; Misses++; }
            }
        }
        if (PositionMs + .001 >= end && Mode == PianoPracticeMode.PlayAlong)
        {
            if (LoopEnabled && end < DurationMs + 1)
            {
                // Looped practice resets its scoring only for the next pass.
                Rewind();
                PositionMs = Math.Clamp(LoopStartMs, 0, Math.Max(0, end - 100));
                nextMiss = notes.FindIndex(n => n.StartMs >= PositionMs - HitWindowMs);
                if (nextMiss < 0) nextMiss = notes.Count;
                Running = true;
            }
            else
            {
                foreach (var n in notes)
                    if (!n.Hit && !n.Missed) { n.Missed = true; Misses++; }
                PositionMs = DurationMs;
                Running = false;
                Finished = true;
            }
        }
    }
}
