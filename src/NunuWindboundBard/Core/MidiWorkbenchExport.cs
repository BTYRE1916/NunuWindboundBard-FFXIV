using System.Security.Cryptography;
using System.Text;
using NAudio.Midi;

namespace NunuWindboundBard.Core;

/// <summary>Source PPQ clock used for beat-aware edits and export across tempo changes.</summary>
public sealed class MidiWorkbenchClock
{
    private sealed record Segment(long Tick, double Ms, int Micros);
    private readonly Segment[] segments;
    internal IReadOnlyList<NAudio.Midi.MidiEvent> Metadata { get; }
    public int Ppq { get; }
    public double MaxTickMs => segments.Max(s => s.Micros / (1000.0 * Ppq));

    private MidiWorkbenchClock(MidiFile file)
    {
        Ppq = file.DeltaTicksPerQuarterNote;
        if (Ppq <= 0 || (Ppq & 0x8000) != 0) throw new InvalidDataException("Choose a MIDI with PPQ timing.");
        var events = file.Events.SelectMany(t => t).ToArray();
        var clock = new List<Segment> {new(0, 0, 500_000)};
        foreach (var tempo in events.OfType<TempoEvent>().OrderBy(t => t.AbsoluteTime))
        {
            if (tempo.MicrosecondsPerQuarterNote <= 0) throw new InvalidDataException("Invalid MIDI tempo.");
            var last = clock[^1];
            double ms = last.Ms + (tempo.AbsoluteTime - last.Tick) * last.Micros / (1000.0 * Ppq);
            clock.Add(new(tempo.AbsoluteTime, ms, tempo.MicrosecondsPerQuarterNote));
        }
        segments = clock.ToArray();
        Metadata = events.Where(e => e is MetaEvent m && m.MetaEventType is
            MetaEventType.TimeSignature or MetaEventType.KeySignature or
            MetaEventType.Lyric or MetaEventType.Marker or MetaEventType.CuePoint or MetaEventType.Copyright)
            .Concat(events.OfType<TempoEvent>().GroupBy(t => t.AbsoluteTime).Select(g => g.Last()))
            .OrderBy(e => e.AbsoluteTime).ToArray();
    }

    public static MidiWorkbenchClock Load(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024) throw new InvalidDataException("MIDI exceeds 50 MB.");
        return new(new MidiFile(path, false));
    }
    public long ToTicks(double ms)
    {
        int lo = 0, hi = segments.Length - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (segments[mid].Ms <= ms) lo = mid; else hi = mid - 1; }
        var s = segments[lo];
        return checked((long)Math.Round(s.Tick + (ms - s.Ms) * 1000 * Ppq / s.Micros));
    }
    public double ToMs(long tick)
    {
        int lo = 0, hi = segments.Length - 1;
        while (lo < hi) { int mid = (lo + hi + 1) / 2; if (segments[mid].Tick <= tick) lo = mid; else hi = mid - 1; }
        var s = segments[lo]; return s.Ms + (tick - s.Tick) * s.Micros / (1000.0 * Ppq);
    }
}

public static class MidiWorkbenchExport
{
    private static TextEvent Utf8Text(string text, MetaEventType kind, long tick) =>
        new("", kind, tick) {Data = Encoding.UTF8.GetBytes(text)};
    public static int InstrumentProgram(int id) => id switch
    {
        1 => 46, 2 => 0, 3 => 25, 4 => 45, 5 => 73, 6 => 68, 7 => 71, 8 => 72, 9 => 75,
        10 => 47, 11 => 114, 12 => 116, 13 => 118, 14 => 119,
        15 => 56, 16 => 57, 17 => 58, 18 => 60, 19 => 65,
        20 => 40, 21 => 41, 22 => 42, 23 => 43,
        24 => 29, 25 => 27, 26 => 28, 27 or 28 => 30, _ => 0
    };

    public static string Save(WorkbenchSnapshot snapshot, string destination)
    {
        VerifySource(snapshot);
        destination = Path.GetFullPath(destination);
        if (string.Equals(snapshot.SourcePath, destination, StringComparison.OrdinalIgnoreCase) || File.Exists(destination))
            throw new IOException("Choose a new filename; the workbench preserves existing MIDI files.");
        if (Path.GetExtension(destination).ToLowerInvariant() is not (".mid" or ".midi"))
            throw new IOException("Choose a .mid or .midi filename.");
        var tracks = snapshot.Tracks.Where(t => t.Enabled && t.Notes.Count > 0).ToArray();
        if (tracks.Length is < 1 or > 128 || tracks.Sum(t => (long)t.Notes.Count) > 500_000)
            throw new InvalidOperationException("Enable at least one part; the export limit is 128 parts / 500,000 notes.");
        var clock = MidiWorkbenchClock.Load(snapshot.SourcePath);
        var data = new MidiEventCollection(1, clock.Ppq);
        data.AddTrack();
        data.AddEvent(Utf8Text("Windbound Workbench — Tempo, meter and lyrics", MetaEventType.SequenceTrackName, 0), 0);
        foreach (var e in clock.Metadata) data.AddEvent(e.Clone(), 0);
        if (!clock.Metadata.OfType<TempoEvent>().Any(e => e.AbsoluteTime == 0))
            data.AddEvent(new TempoEvent(500_000, 0), 0);
        int[] channels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16];
        int Program(WorkbenchTrack t) => t.InstrumentId > 0 ? InstrumentProgram(t.InstrumentId) : Math.Clamp(t.Program ?? 0, 0, 127);
        var programs = tracks.Where(t => !t.IsDrumKit).Select(Program).Distinct().ToArray();
        if (programs.Length > channels.Length)
            throw new InvalidOperationException("Standard MIDI supports 15 melodic program sounds plus drums. Export fewer instrument sounds together.");
        var owner = channels.ToDictionary(c => c, _ => -1);
        for (int i = 0; i < programs.Length; i++) owner[channels[i]] = programs[i];
        var active = new Dictionary<(int Channel, int Pitch), long>();
        var used = new HashSet<(int Track, int Channel)>();
        for (int i = 0; i < tracks.Length; i++)
        {
            data.AddTrack(); var t = tracks[i];
            // FF04 is the authoritative instrument directive. A user track name
            // may contain older #instrument text; it must not override the edit.
            string displayName = System.Text.RegularExpressions.Regex.Replace(t.Name, @"#[^#\r\n]+", "").Trim();
            string tag = t.InstrumentId > 0 ? " #" + InstrumentCatalog.Display(t.InstrumentId) : "";
            if (t.OctaveShift != 0) tag += " #" + t.OctaveShift.ToString("+0;-0;0", System.Globalization.CultureInfo.InvariantCulture);
            data.AddEvent(Utf8Text(displayName + tag, MetaEventType.SequenceTrackName, 0), i + 1);
            if (t.InstrumentId > 0)
                data.AddEvent(Utf8Text("#" + InstrumentCatalog.Display(t.InstrumentId), MetaEventType.TrackInstrumentName, 0), i + 1);
        }
        var notes = tracks.SelectMany((t, i) => t.Notes.Select(n => (Track: i, Part: t, Note: n)))
            .OrderBy(x => x.Note.TimeMs).ThenBy(x => x.Track).ThenBy(x => x.Note.Pitch);
        foreach (var item in notes)
        {
            var n = item.Note;
            if (n.Pitch is < 0 or > 127 || n.Velocity is < 1 or > 127 || !double.IsFinite(n.TimeMs) ||
                !double.IsFinite(n.DurationMs) || n.TimeMs < 0 || n.DurationMs is < 35 or > 60_000)
                throw new InvalidDataException("Invalid workbench note.");
            long start = clock.ToTicks(n.TimeMs), end = Math.Max(start + 1, clock.ToTicks(n.TimeMs + n.DurationMs));
            int channel;
            int program = Program(item.Part);
            bool Free(int c) => !active.TryGetValue((c, n.Pitch), out long until) || until <= start;
            if (item.Part.IsDrumKit) channel = Free(10) ? 10 : -1;
            else
            {
                channel = channels.FirstOrDefault(c => owner[c] == program && Free(c));
                if (channel == 0)
                {
                    channel = channels.FirstOrDefault(c => owner[c] == -1);
                    if (channel != 0) owner[channel] = program;
                }
            }
            if (channel <= 0)
                throw new InvalidOperationException("Overlapping copies of the same pitch exceed MIDI channel capacity. Remove duplicates / trim overlaps, or export fewer parts together.");
            active[(channel, n.Pitch)] = end;
            int trackIndex = item.Track + 1;
            if (used.Add((trackIndex, channel))) data.AddEvent(new PatchChangeEvent(0, channel, program), trackIndex);
            var note = new NoteOnEvent(start, channel, n.Pitch, n.Velocity, checked((int)(end - start)));
            data.AddEvent(note, trackIndex); data.AddEvent(note.OffEvent, trackIndex);
        }
        string partial = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            data.PrepareForExport(); MidiFile.Export(partial, data);
            var reopened = MidiFileReader.Load(partial);
            if (reopened.Notes.Count != tracks.Sum(t => t.Notes.Count) || reopened.Tracks.Count != tracks.Length + 1)
                throw new InvalidDataException("Export did not pass the track / note count check.");
            double tolerance = Math.Max(5, 2 * clock.MaxTickMs + .01);
            for (int i = 0; i < tracks.Length; i++)
            {
                var expected = tracks[i].Notes.OrderBy(n => n.Pitch).ThenBy(n => n.TimeMs).ThenBy(n => n.DurationMs).ToArray();
                var actual = reopened.Notes.Where(n => n.Track == i + 1).OrderBy(n => n.Pitch)
                    .ThenBy(n => n.TimeMs).ThenBy(n => n.DurationMs).ToArray();
                if (actual.Length != expected.Length) throw new InvalidDataException("Export lost notes from a part.");
                for (int j = 0; j < actual.Length; j++)
                    if (expected[j].Pitch != actual[j].Pitch || expected[j].Velocity != actual[j].Velocity ||
                        Math.Abs(expected[j].TimeMs - actual[j].TimeMs) > tolerance ||
                        Math.Abs(expected[j].DurationMs - actual[j].DurationMs) > tolerance)
                        throw new InvalidDataException("Export round-trip note validation failed.");
            }
            File.Move(partial, destination); return destination;
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }

    public static byte[] Preview(WorkbenchSnapshot snapshot)
    {
        snapshot = snapshot with {Tracks = snapshot.Tracks.Select(t => t with {
            Notes = MidiWorkbenchDocument.Transpose(t.Notes, t.IsDrumKit ? 0 : t.OctaveShift, false), OctaveShift = 0}).ToArray()};
        string path = Path.Combine(Path.GetTempPath(), "Windbound-Workbench-" + Guid.NewGuid().ToString("N") + ".mid");
        try { Save(snapshot, path); return File.ReadAllBytes(path); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    public static string Lyrics(WorkbenchSnapshot snapshot)
    {
        VerifySource(snapshot);
        var clock = MidiWorkbenchClock.Load(snapshot.SourcePath);
        var lines = clock.Metadata.OfType<TextEvent>().Where(t => t.MetaEventType == MetaEventType.Lyric)
            .Select(t =>
            {
                long centiseconds = (long)Math.Round(clock.ToMs(t.AbsoluteTime) / 10);
                return $"[{centiseconds / 6000:00}:{centiseconds / 100 % 60:00}.{centiseconds % 100:00}]" +
                    MidiFileReader.DecodeText(t.Data).Replace("\r", " ").Replace("\n", " ");
            }).ToArray();
        if (lines.Length == 0) throw new InvalidOperationException("The source MIDI contains no lyric events.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }
    private static void VerifySource(WorkbenchSnapshot snapshot)
    {
        if (new FileInfo(snapshot.SourcePath).Length > 50 * 1024 * 1024 ||
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(snapshot.SourcePath))) != snapshot.SourceHash)
            throw new IOException("The source MIDI changed since import. Export from the original file or import it again.");
    }
}
