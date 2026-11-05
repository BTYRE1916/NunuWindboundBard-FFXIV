using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NunuWindboundBard.Core;

/// <summary>
/// Export one named MIDI track per performer, preserving the source MIDI tempo map,
/// time signature, key signature and note timings. The original file is never overwritten.
/// </summary>
public static class SoulweaverMidiExport
{
    private sealed record Meta(long Tick, int Order, byte Type, byte[] Bytes);
    private sealed record Tempo(long Tick, int Order, int MicrosPerBeat);
    private sealed record Segment(long Tick, double Ms, int MicrosPerBeat);
    private sealed record Event(long Tick, int Sort, byte[] Message);

    public static string Save(string sourcePath, string destinationPath, SoulweaverResult result)
    {
        sourcePath = Path.GetFullPath(sourcePath);
        destinationPath = Path.GetFullPath(destinationPath);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Soulweaver will not overwrite the original MIDI.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Source MIDI is missing.", sourcePath);
        if (File.Exists(destinationPath)) throw new IOException("The destination MIDI already exists. Pick a new filename to preserve existing work.");
        if (!destinationPath.EndsWith(".mid", StringComparison.OrdinalIgnoreCase) &&
            !destinationPath.EndsWith(".midi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Select a .mid or .midi output filename.");
        if (result.Parts.Count != result.Performers || result.Parts.Count is not (1 or 2 or 4 or 8))
            throw new InvalidOperationException("Arrangement performer count is invalid.");
        var expectedNotes = result.Parts.Sum(x => x.Notes.Count);
        if (expectedNotes < 1 || expectedNotes > 500_000) throw new InvalidOperationException("Arrangement has no notes or too many notes.");
        (int ppq, List<Meta> metas, List<Segment> clock) = ReadTiming(sourcePath);
        string temp = destinationPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            using (var writer = new BinaryWriter(File.Create(temp), Encoding.UTF8))
            {
                writer.Write(Encoding.ASCII.GetBytes("MThd"));
                WriteU32(writer, 6);
                WriteU16(writer, 1);  // format 1: dedicated tempo track + a track per musician
                WriteU16(writer, result.Parts.Count + 1);
                WriteU16(writer, ppq);
                WriteChunk(writer, BuildTempoTrack(metas));
                for (int i = 0; i < result.Parts.Count; i++)
                    WriteChunk(writer, BuildPartTrack(result.Parts[i], clock, ppq));
            }
            // Re-read with the same parser used for actual in-game playback before publishing.
            var reopened = MidiFileReader.Load(temp);
            if (reopened.Notes.Count != expectedNotes || reopened.Tracks.Count != result.Parts.Count + 1)
                throw new InvalidDataException("The exported MIDI did not pass the note-count / track-count check.");
            foreach (var part in result.Parts)
            {
                var actual = reopened.Notes.Where(x => x.Track == part.Index + 1)
                    .OrderBy(x => x.Pitch).ThenBy(x => x.TimeMs).ThenBy(x => x.DurationMs).ToArray();
                var expected = part.Notes.OrderBy(x => x.Pitch).ThenBy(x => x.TimeMs).ThenBy(x => x.DurationMs).ToArray();
                if (actual.Length != expected.Length)
                    throw new InvalidDataException("An exported performer track lost notes.");
                for (int i = 0; i < actual.Length; i++)
                    if (actual[i].Pitch != expected[i].Pitch ||
                        Math.Abs(actual[i].TimeMs - expected[i].TimeMs) > 5.0 ||
                        Math.Abs(actual[i].DurationMs - Math.Max(35, expected[i].DurationMs)) > 8.0)
                        throw new InvalidDataException($"Export validation mismatch in performer {part.Index + 1}, note {i + 1}: expected pitch {expected[i].Pitch} at {expected[i].TimeMs:0.0}ms for {expected[i].DurationMs:0.0}ms; reopened pitch {actual[i].Pitch} at {actual[i].TimeMs:0.0}ms for {actual[i].DurationMs:0.0}ms.");
            }
            File.Move(temp, destinationPath);
            return destinationPath;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static byte[] BuildTempoTrack(List<Meta> metas)
    {
        List<Event> events = [new(0, 0, MetaMessage(0x03, Encoding.UTF8.GetBytes("Soulweaver Tempo & Meter")))];
        events.AddRange(metas.Select(m => new Event(m.Tick, 1 + m.Order, MetaMessage(m.Type, m.Bytes))));
        return EncodeTrack(events);
    }

    private static byte[] BuildPartTrack(SoulweaverPart part, List<Segment> segments, int ppq)
    {
        List<Event> events = [new(0, 0, MetaMessage(0x03, Encoding.UTF8.GetBytes("Soulweaver " + (part.Index + 1) + ": " + part.Name)))];
        // MIDI note-offs identify only pitch and channel, not a particular note-on.
        // Soulweaver merges source tracks into one musician track, so repeated notes
        // of the SAME pitch can overlap. Writing every one on channel 0 causes the
        // playback parser (and many synthesizers) to release the wrong note.
        // Allocate overlapping instances to different channels instead. This
        // retains exact start/duration and keeps all other simultaneous chords.
        // Channel 9 (the GM percussion channel) is excluded deliberately.
        int[] melodicChannels = [0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14, 15];
        Dictionary<int, Dictionary<int, long>> activeUntilByPitch = new();
        foreach (BardNote note in part.Notes.OrderBy(n => n.TimeMs).ThenBy(n => n.Pitch).ThenBy(n => n.Track))
        {
            if (note.Pitch < 48 || note.Pitch > 84 || note.Velocity is < 0 or > 127 ||
                double.IsNaN(note.TimeMs) || double.IsInfinity(note.TimeMs) || note.TimeMs < 0 ||
                double.IsNaN(note.DurationMs) || double.IsInfinity(note.DurationMs) || note.DurationMs < 0)
                throw new InvalidDataException("A Soulweaver note is outside the Bard range or has invalid timing/velocity.");
            long on = ToTicks(note.TimeMs, segments, ppq);
            long off = Math.Max(on + 1, ToTicks(note.TimeMs + Math.Max(35, note.DurationMs), segments, ppq));
            if (!activeUntilByPitch.TryGetValue(note.Pitch, out var active))
            {
                active = new Dictionary<int, long>();
                activeUntilByPitch[note.Pitch] = active;
            }
            // Prefer the source channel when available, but don't let a second
            // same-pitch note steal the first note's release event.
            int preferred = note.Channel is >= 0 and <= 15 and not 9 ? note.Channel : 0;
            int channel = -1;
            if (!active.TryGetValue(preferred, out long end) || end <= on)
                channel = preferred;
            else
                foreach (int candidate in melodicChannels)
                    if (!active.TryGetValue(candidate, out end) || end <= on)
                    {
                        channel = candidate;
                        break;
                    }
            if (channel < 0)
                throw new InvalidDataException($"Part {part.Index + 1} has more than 15 overlapping notes of pitch {note.Pitch}. Reduce chord density or simplify duplicate notes.");
            active[channel] = off;
            events.Add(new Event(on, 2, [(byte)(0x90 | channel), (byte)note.Pitch, (byte)Math.Max(1, note.Velocity)]));
            events.Add(new Event(off, 1, [(byte)(0x80 | channel), (byte)note.Pitch, (byte)0]));
        }
        return EncodeTrack(events);
    }

    private static long ToTicks(double ms, List<Segment> segments, int ppq)
    {
        int low = 0, high = segments.Count - 1;
        while (low < high)
        {
            int mid = (low + high + 1) / 2;
            if (segments[mid].Ms <= ms) low = mid; else high = mid - 1;
        }
        var s = segments[low];
        return checked((long)Math.Round(s.Tick + (ms - s.Ms) * ppq * 1000.0 / s.MicrosPerBeat));
    }

    private static byte[] EncodeTrack(IEnumerable<Event> source)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        long last = 0;
        foreach (Event e in source.OrderBy(x => x.Tick).ThenBy(x => x.Sort))
        {
            if (e.Tick < last) throw new InvalidDataException("MIDI event tick order is invalid.");
            WriteVlq(writer, e.Tick - last);
            writer.Write(e.Message);
            last = e.Tick;
        }
        WriteVlq(writer, 0);
        writer.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        writer.Flush();
        if (stream.Length > 32 * 1024 * 1024) throw new InvalidDataException("An exported MIDI track exceeds 32 MB.");
        return stream.ToArray();
    }

    private static byte[] MetaMessage(byte type, byte[] value)
    {
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        w.Write((byte)0xFF); w.Write(type); WriteVlq(w, value.Length); w.Write(value);
        return stream.ToArray();
    }

    private static (int Ppq, List<Meta> Metas, List<Segment> Clock) ReadTiming(string path)
    {
        if (new FileInfo(path).Length > 50 * 1024 * 1024) throw new InvalidDataException("Source MIDI exceeds 50 MB.");
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (ReadTag(reader) != "MThd") throw new InvalidDataException("Invalid SMF header.");
        uint hlen = ReadU32(reader);
        if (hlen < 6 || hlen > 1024) throw new InvalidDataException("Invalid SMF header length.");
        ushort format = ReadU16(reader);
        ushort count = ReadU16(reader);
        ushort division = ReadU16(reader);
        stream.Position += hlen - 6;
        if (format > 1 || count == 0 || division == 0 || (division & 0x8000) != 0)
            throw new NotSupportedException("Soulweaver requires format 0/1 MIDI with PPQ timing.");
        List<Meta> metas = [];
        int actualTrack = 0, order = 0;
        while (actualTrack < count && stream.Position + 8 <= stream.Length)
        {
            string tag = ReadTag(reader);
            uint len = ReadU32(reader);
            long end = stream.Position + len;
            if (end > stream.Length || len > 32 * 1024 * 1024) throw new InvalidDataException("MIDI track is truncated or too large.");
            if (tag != "MTrk") { stream.Position = end; continue; }
            actualTrack++;
            long tick = 0;
            int running = 0;
            while (stream.Position < end)
            {
                tick = checked(tick + ReadVlq(reader));
                int lead = reader.ReadByte();
                int firstData = -1;
                int status;
                if (lead < 0x80)
                {
                    if (running == 0) throw new InvalidDataException("Invalid MIDI running status.");
                    status = running; firstData = lead;
                }
                else { status = lead; running = status < 0xF0 ? status : 0; }
                if (status == 0xFF)
                {
                    byte type = reader.ReadByte();
                    long size = ReadVlq(reader);
                    if (size < 0 || size > end - stream.Position) throw new InvalidDataException("Invalid MIDI meta event size.");
                    if ((type == 0x51 || type == 0x58 || type == 0x59) && size <= 64)
                    {
                        var data = reader.ReadBytes((int)size);
                        if (type != 0x51 || (data.Length == 3 && (((int)data[0] << 16) | ((int)data[1] << 8) | data[2]) > 0))
                            metas.Add(new Meta(tick, order++, type, data));
                    }
                    else stream.Position += size;
                    if (type == 0x2F) break;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    long size = ReadVlq(reader);
                    if (size < 0 || size > end - stream.Position) throw new InvalidDataException("Invalid SysEx size.");
                    stream.Position += size;
                }
                else if (status < 0xF0)
                {
                    int high = status & 0xF0;
                    if (firstData < 0) _ = reader.ReadByte();
                    if (high is not (0xC0 or 0xD0)) _ = reader.ReadByte();
                }
                else throw new NotSupportedException("Unsupported MIDI system event.");
            }
            stream.Position = end;
        }
        if (actualTrack != count) throw new InvalidDataException("Missing MIDI tracks.");
        // Upscale unusually coarse PPQ MIDI files to >= 480 ticks/quarter so exported
        // note timings stay precise, while preserving all source tempo/meter boundaries.
        int originalPpq = division;
        int scale = Math.Max(1, (480 + originalPpq - 1) / originalPpq);
        int ppq = checked(originalPpq * scale);
        List<Segment> timeline = [new(0, 0, 500_000)];
        long lastTick = 0; double elapsed = 0; int micro = 500_000;
        foreach (var change in metas.Where(m => m.Type == 0x51).OrderBy(m => m.Tick).ThenBy(m => m.Order))
        {
            elapsed += (change.Tick - lastTick) * micro / (double)originalPpq / 1000.0;
            lastTick = change.Tick;
            micro = (change.Bytes[0] << 16) | (change.Bytes[1] << 8) | change.Bytes[2];
            timeline.Add(new Segment(checked(lastTick * scale), elapsed, micro));
        }
        // If no tempo event exists, explicitly write 120 BPM so DAWs read the same default.
        if (!metas.Any(m => m.Type == 0x51))
            metas.Add(new Meta(0, 0, 0x51, [0x07, 0xA1, 0x20]));
        if (scale != 1)
            metas = metas.Select(m => m with { Tick = checked(m.Tick * scale) }).ToList();
        return (ppq, metas, timeline);
    }

    private static void WriteChunk(BinaryWriter writer, byte[] chunk)
    {
        writer.Write(Encoding.ASCII.GetBytes("MTrk")); WriteU32(writer, (uint)chunk.Length); writer.Write(chunk);
    }
    private static void WriteVlq(BinaryWriter writer, long value)
    {
        if (value < 0 || value > 0x0FFF_FFFF) throw new InvalidDataException("MIDI delta time out of range.");
        uint n = (uint)value;
        uint buffer = n & 0x7F;
        while ((n >>= 7) != 0) buffer = (buffer << 8) | ((n & 0x7F) | 0x80);
        while (true) { writer.Write((byte)buffer); if ((buffer & 0x80) == 0) break; buffer >>= 8; }
    }
    private static long ReadVlq(BinaryReader reader)
    {
        long v = 0;
        for (int i = 0; i < 4; i++)
        {
            int x = reader.ReadByte(); v = (v << 7) | (x & 0x7F);
            if ((x & 0x80) == 0) return v;
        }
        throw new InvalidDataException("Invalid variable-length MIDI value.");
    }
    private static string ReadTag(BinaryReader r) => Encoding.ASCII.GetString(r.ReadBytes(4));
    private static ushort ReadU16(BinaryReader r) => (ushort)((r.ReadByte() << 8) | r.ReadByte());
    private static uint ReadU32(BinaryReader r) => ((uint)r.ReadByte() << 24) | ((uint)r.ReadByte() << 16) | ((uint)r.ReadByte() << 8) | r.ReadByte();
    private static void WriteU16(BinaryWriter w, int x) { w.Write((byte)(x >> 8)); w.Write((byte)x); }
    private static void WriteU32(BinaryWriter w, uint x) { w.Write((byte)(x >> 24)); w.Write((byte)(x >> 16)); w.Write((byte)(x >> 8)); w.Write((byte)x); }
}
