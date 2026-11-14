using System.Security.Cryptography;

namespace NunuWindboundBard.Core;

public sealed record WorkbenchTrack(Guid Id, string Name, int InstrumentId, bool Enabled,
    bool IsDrumKit, int? Program, IReadOnlyList<MidiNote> Notes)
{
    public int OctaveShift { get; init; }
}
public sealed record WorkbenchDrumRule(int InstrumentId, int Pitch, bool Enabled = true);
public sealed class MidiWorkbenchSettings
{
    public bool ConvertDrumPitches { get; set; } = true;
    public Dictionary<int, WorkbenchDrumRule> DrumMap { get; set; } = DefaultDrums();

    public static Dictionary<int, WorkbenchDrumRule> DefaultDrums()
    {
        var map = Enumerable.Range(35, 47).ToDictionary(n => n, n => new WorkbenchDrumRule(0, n));
        void Put(int source, int instrument, int pitch) => map[source] = new(instrument, pitch);
        Put(35, 12, 60); Put(36, 12, 60);
        int[] toms = [41, 43, 45, 47, 48, 50];
        int[] tomPitches = [53, 55, 57, 59, 60, 62];
        for (int i = 0; i < toms.Length; i++) Put(toms[i], 12, tomPitches[i]);
        Put(37, 13, 70); Put(38, 13, 72); Put(39, 13, 72); Put(40, 13, 74);
        Put(49, 14, 72); Put(52, 14, 78); Put(55, 14, 76); Put(57, 14, 84);
        int[] bongos = [60, 61, 62, 63, 64, 65, 66, 67, 68];
        int[] bongoPitches = [70, 67, 72, 65, 74, 69, 67, 72, 70];
        for (int i = 0; i < bongos.Length; i++) Put(bongos[i], 11, bongoPitches[i]);
        return map;
    }
}

public sealed record WorkbenchSnapshot(string SourcePath, string SourceHash,
    IReadOnlyList<WorkbenchTrack> Tracks);

/// <summary>Independent MIDI working copy. Notes and snapshots are immutable;
/// editing never changes the source file or the running game arrangement.</summary>
public sealed class MidiWorkbenchDocument
{
    private readonly Stack<WorkbenchTrack[]> undo = new();
    private readonly Stack<WorkbenchTrack[]> redo = new();
    private WorkbenchTrack[] tracks;
    public string SourcePath { get; }
    public string SourceHash { get; }
    public MidiSong Source { get; }
    public MidiWorkbenchClock Clock { get; }
    public IReadOnlyList<WorkbenchTrack> Tracks => tracks;
    public int Revision { get; private set; }
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public double DurationMs => tracks.SelectMany(t => t.Notes).Select(n => n.TimeMs + n.DurationMs).DefaultIfEmpty().Max();

    private MidiWorkbenchDocument(string path)
    {
        SourcePath = Path.GetFullPath(path);
        if (new FileInfo(SourcePath).Length > 50 * 1024 * 1024) throw new InvalidDataException("MIDI exceeds 50 MB.");
        SourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(SourcePath)));
        Source = MidiFileReader.Load(SourcePath);
        Clock = MidiWorkbenchClock.Load(SourcePath);
        tracks = Source.Notes.GroupBy(n => new MidiLaneKey(n.Track, n.Channel)).Select(g =>
        {
            var lane = Source.LaneInstruments.FirstOrDefault(l => l.Track == g.Key.Track && l.Channel == g.Key.Channel);
            string name = lane?.TrackName ?? Source.Tracks.FirstOrDefault(t => t.Index == g.Key.Track)?.Name ?? "Track";
            int instrument = lane is null ? 0 : MidiInstrumentInference.ForLane(lane).InstrumentId;
            string labels = name + " " + lane?.InstrumentName + " " + lane?.TagMetadata;
            string compact = labels.ToLowerInvariant().Replace(" ", "").Replace("-", "").Replace("_", "");
            bool namedDrum = new[] {"bassdrum", "snare", "bongo", "conga", "cymbal", "timpani", "kick"}.Any(compact.Contains);
            bool kit = g.Key.Channel == 9 && !namedDrum;
            MidiTrackTags.TryGetLaneOctave(Source, g.Key, out int octave);
            return new WorkbenchTrack(Guid.NewGuid(), name, kit ? 0 : instrument, true, kit,
                lane?.Program, g.OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray()) {OctaveShift = kit ? 0 : octave};
        }).ToArray();
        Validate(tracks);
    }

    public static MidiWorkbenchDocument Load(string path) => new(path);
    public WorkbenchSnapshot Snapshot() => new(SourcePath, SourceHash, tracks.ToArray());
    public void Undo() { if (!CanUndo) return; redo.Push(tracks); tracks = undo.Pop(); Revision++; }
    public void Redo() { if (!CanRedo) return; undo.Push(tracks); tracks = redo.Pop(); Revision++; }

    private void Commit(IEnumerable<WorkbenchTrack> next)
    {
        var result = next.ToArray();
        Validate(result);
        undo.Push(tracks);
        if (undo.Count > 20)
        {
            var keep = undo.Take(20).Reverse().ToArray(); undo.Clear();
            foreach (var item in keep) undo.Push(item);
        }
        redo.Clear(); tracks = result; Revision++;
    }

    private static void Validate(WorkbenchTrack[] items)
    {
        if (items.Length > 128 || items.Sum(t => (long)t.Notes.Count) > 500_000)
            throw new InvalidOperationException("The workbench supports up to 128 parts and 500,000 notes.");
        foreach (var t in items)
        {
            if (!InstrumentCatalog.Valid(t.InstrumentId) || t.Name.Length > 240 || t.OctaveShift is < -48 or > 48 || t.OctaveShift % 12 != 0)
                throw new InvalidOperationException("Choose a valid instrument and a track name of at most 240 characters.");
            foreach (var n in t.Notes)
                if (n.Pitch is < 0 or > 127 || n.Velocity is < 1 or > 127 ||
                    !double.IsFinite(n.TimeMs) || n.TimeMs is < 0 or > 86_400_000 ||
                    !double.IsFinite(n.DurationMs) || n.DurationMs is < 35 or > 60_000)
                    throw new InvalidOperationException("A note has invalid MIDI pitch, velocity or timing.");
        }
    }

    public void SetTrack(WorkbenchTrack track) => Commit(tracks.Select(t => t.Id == track.Id ? track : t));
    public void Remove(IReadOnlySet<Guid> ids) => Commit(tracks.Where(t => !ids.Contains(t.Id)));
    public void Move(Guid id, int direction)
    {
        var list = tracks.ToList(); int from = list.FindIndex(t => t.Id == id), to = from + Math.Sign(direction);
        if (from < 0 || to < 0 || to >= list.Count) return;
        (list[from], list[to]) = (list[to], list[from]); Commit(list);
    }
    public void Duplicate(IReadOnlySet<Guid> ids) => Commit(tracks.SelectMany(t => ids.Contains(t.Id)
        ? new[] {t, t with {Id = Guid.NewGuid(), Name = t.Name + " copy"}} : new[] {t}));
    public void Merge(IReadOnlySet<Guid> ids, Guid active)
    {
        var chosen = tracks.Where(t => ids.Contains(t.Id)).ToArray();
        if (chosen.Length < 2) throw new InvalidOperationException("Select at least two tracks to merge.");
        var lead = chosen.FirstOrDefault(t => t.Id == active) ?? chosen[0];
        var merged = lead with {Name = lead.Name + " merged", IsDrumKit = chosen.All(t => t.IsDrumKit), OctaveShift = 0,
            Notes = chosen.SelectMany(t => Transpose(t.Notes, t.OctaveShift, false)).OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray()};
        bool added = false;
        Commit(tracks.SelectMany(t =>
        {
            if (!ids.Contains(t.Id)) return new[] {t};
            if (added) return Array.Empty<WorkbenchTrack>();
            added = true; return new[] {merged};
        }));
    }

    public void Transform(IReadOnlySet<Guid> ids, Func<IReadOnlyList<MidiNote>, IReadOnlyList<MidiNote>> edit) =>
        Commit(tracks.Select(t => ids.Contains(t.Id) ? t with {Notes = edit(t.Notes)} : t));

    public void AdaptRange(IReadOnlySet<Guid> ids) => Commit(tracks.Select(t => ids.Contains(t.Id) && !t.IsDrumKit
        ? t with {Notes = Transpose(t.Notes, t.OctaveShift, true), OctaveShift = 0} : t));
    public void BakeOctaves(IReadOnlySet<Guid> ids) => Commit(tracks.Select(t => ids.Contains(t.Id) && !t.IsDrumKit
        ? t with {Notes = Transpose(t.Notes, t.OctaveShift, false), OctaveShift = 0} : t));

    public void SplitVoices(IReadOnlySet<Guid> ids)
    {
        var result = new List<WorkbenchTrack>();
        foreach (var track in tracks)
        {
            if (!ids.Contains(track.Id)) { result.Add(track); continue; }
            var voices = new List<List<MidiNote>>(); var ends = new List<double>();
            foreach (var note in track.Notes.OrderBy(n => n.TimeMs).ThenByDescending(n => n.Pitch))
            {
                int voice = ends.FindIndex(end => end <= note.TimeMs + .001);
                if (voice < 0) { voice = voices.Count; voices.Add(new()); ends.Add(0); }
                voices[voice].Add(note); ends[voice] = note.TimeMs + note.DurationMs;
                if (voices.Count + result.Count > 128) throw new InvalidOperationException("Voice split would exceed 128 tracks.");
            }
            result.AddRange(voices.Select((notes, i) => track with {Id = Guid.NewGuid(),
                Name = track.Name + " voice " + (i + 1), Notes = notes.ToArray()}));
        }
        Commit(result);
    }

    public void SplitDrums(IReadOnlySet<Guid> ids, MidiWorkbenchSettings settings)
    {
        if (!tracks.Any(t => ids.Contains(t.Id) && t.IsDrumKit))
            throw new InvalidOperationException("Select a GM drum-kit track. Already prepared FFXIV parts should keep their pitches.");
        var result = new List<WorkbenchTrack>();
        foreach (var track in tracks)
        {
            if (!ids.Contains(track.Id) || !track.IsDrumKit) { result.Add(track); continue; }
            var hits = track.Notes.Select(n => (Note: n, Rule: settings.DrumMap.GetValueOrDefault(n.Pitch,
                new WorkbenchDrumRule(0, n.Pitch)))).Where(h => h.Rule.Enabled);
            foreach (var group in hits.GroupBy(h => h.Rule.InstrumentId).OrderBy(g => g.Key == 0 ? 99 : g.Key))
            {
                if (group.Key != 0 && group.Key is not (10 or 11 or 12 or 13 or 14))
                    throw new InvalidOperationException("Drum rules require a percussion instrument or Other percussion.");
                if (settings.ConvertDrumPitches && group.Key != 0 && group.Any(h => h.Rule.Pitch is < 48 or > 84))
                    throw new InvalidOperationException("Playable drum targets must be between MIDI notes 48 and 84.");
                var notes = group.Select(h => h.Note with {Pitch = settings.ConvertDrumPitches && group.Key != 0
                    ? h.Rule.Pitch : h.Note.Pitch, Channel = group.Key == 0 ? 9 : 0}).ToArray();
                if (settings.ConvertDrumPitches && group.Key != 0) notes = RemoveDuplicates(notes).ToArray();
                result.Add(track with {Id = Guid.NewGuid(), Name = track.Name + " — " +
                    (group.Key == 0 ? "Other Percussion" : InstrumentCatalog.Display(group.Key)),
                    InstrumentId = group.Key, IsDrumKit = group.Key == 0, OctaveShift = 0, Notes = notes});
            }
        }
        Commit(result);
    }

    public static IReadOnlyList<MidiNote> RemoveDuplicates(IReadOnlyList<MidiNote> notes) => notes
        .GroupBy(n => (Math.Round(n.TimeMs, 3), n.Pitch))
        .Select(g => g.OrderByDescending(n => n.Velocity).First() with {DurationMs = g.Max(n => n.DurationMs)})
        .OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray();

    public static IReadOnlyList<MidiNote> Transpose(IReadOnlyList<MidiNote> notes, int semitones, bool fold) => notes
        .Select(n => n with {Pitch = fold ? Fold(n.Pitch + semitones) : n.Pitch + semitones}).ToArray();
    public static int Fold(int pitch)
    {
        while (pitch < 48) pitch += 12;
        while (pitch > 84) pitch -= 12;
        return pitch;
    }

    public static IReadOnlyList<MidiNote> TrimOverlaps(IReadOnlyList<MidiNote> notes) => notes.GroupBy(n => n.Pitch)
        .SelectMany(g =>
        {
            var starts = g.Select(n => n.TimeMs).Distinct().Order().ToArray();
            return g.Select(n =>
            {
                int next = Array.BinarySearch(starts, n.TimeMs) + 1;
                return n with {DurationMs = next < starts.Length
                    ? Math.Max(35, Math.Min(n.DurationMs, starts[next] - n.TimeMs)) : n.DurationMs};
            });
        }).OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray();

    public static IReadOnlyList<MidiNote> ExtendNotes(IReadOnlyList<MidiNote> notes, double capMs)
    {
        var starts = notes.Select(n => n.TimeMs).Distinct().Order().ToArray();
        return notes.Select(n =>
        {
            int next = Array.BinarySearch(starts, n.TimeMs) + 1;
            return next < starts.Length ? n with {DurationMs = Math.Clamp(Math.Min(capMs, starts[next] - n.TimeMs), 35, 60_000)} : n;
        }).ToArray();
    }

    public IReadOnlyList<MidiNote> Quantize(IReadOnlyList<MidiNote> notes, int divisions)
    {
        if (divisions is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(divisions));
        double grid = Clock.Ppq / (double)divisions;
        return notes.Select(n =>
        {
            long start = (long)Math.Round(Math.Round(Clock.ToTicks(n.TimeMs) / grid) * grid);
            long end = Math.Max(start + 1, (long)Math.Round(Math.Round(Clock.ToTicks(n.TimeMs + n.DurationMs) / grid) * grid));
            double ms = Clock.ToMs(start);
            return n with {Tick = start, TimeMs = ms, DurationMs = Math.Clamp(Clock.ToMs(end) - ms, 35, 60_000)};
        }).OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ToArray();
    }

    public static IReadOnlyList<MidiNote> LimitVoices(IReadOnlyList<MidiNote> notes, int cap)
    {
        if (cap is < 1 or > 16) throw new ArgumentOutOfRangeException(nameof(cap));
        var accepted = new List<MidiNote>(); var ends = new List<double>();
        foreach (var n in notes.OrderBy(n => n.TimeMs).ThenByDescending(n => n.Velocity).ThenByDescending(n => n.Pitch))
        {
            ends.RemoveAll(e => e <= n.TimeMs + .001);
            if (ends.Count >= cap) continue;
            accepted.Add(n); ends.Add(n.TimeMs + n.DurationMs);
        }
        return accepted;
    }
}
