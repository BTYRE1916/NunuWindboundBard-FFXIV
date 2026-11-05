using System.IO;
using System.Text;

namespace NunuWindboundBard.Core;

/// <summary>Track/channel is the arrangement unit: even a format-0 MIDI can contain several parts.</summary>
public readonly record struct MidiLaneKey(int Track, int Channel);
public sealed record MidiLanePlan(MidiLaneKey Key, bool Include, int OctaveShift);
public sealed record MidiOptimizeOptions(IReadOnlyList<MidiLanePlan> Lanes, bool RepairRemainingOctaves);
public sealed record MidiOptimizationPreview(double TimeMs, int Track, int Channel, int FromPitch, int? ToPitch, string Change);
public sealed record MidiLaneReport(MidiLaneKey Key, int Total, int SourceInRange, int Output,
    int Shifted, int Repaired, int Omitted, bool Included, int Shift);

public sealed record MidiOptimizationReport(int SourceNotes, int SourceInRange, int IncludedSourceNotes,
    int PlayableNotes, int OmittedNotes, int ExcludedNotes, int ShiftedNotes, int RepairedNotes,
    int DenseChordGroups, int CollidingKeyGroups, IReadOnlyList<MidiLaneReport> Lanes,
    IReadOnlyList<MidiOptimizationPreview> Changes);

/// <summary>
/// Non-destructive MIDI arrangement: maximise playable notes by selecting one octave shift
/// per track/channel; optionally repair the remaining extremes by full octaves. Never
/// quantise, change pitch classes, alter time signatures/tempo, or silently modify velocity.
/// </summary>
public static class MidiOptimization
{
    public static IReadOnlyList<MidiLanePlan> Suggest(MidiSong song) => song.Notes
        .GroupBy(n => new MidiLaneKey(n.Track, n.Channel))
        .OrderBy(g => g.Key.Track).ThenBy(g => g.Key.Channel)
        .Select(g => new MidiLanePlan(g.Key, g.Key.Channel != 9,
            g.Key.Channel == 9 ? 0 : NoteMapper.FindBestOctaveShift(g.Select(n => n.Pitch), 0)))
        .ToArray();

    public static int? TransformPitch(int original, MidiLanePlan lane, bool repairRemaining)
    {
        if (!lane.Include) return null;
        if (lane.OctaveShift % 12 != 0 || lane.OctaveShift is < -48 or > 48)
            throw new ArgumentOutOfRangeException(nameof(lane), "Octave shifts must be multiples of 12 from -48 to +48.");
        int pitch = original + lane.OctaveShift;
        if (repairRemaining)
        {
            while (pitch < NoteMapper.LowestNote) pitch += 12;
            while (pitch > NoteMapper.HighestNote) pitch -= 12;
        }
        return pitch is >= NoteMapper.LowestNote and <= NoteMapper.HighestNote ? pitch : null;
    }

    public static MidiOptimizationReport Analyze(MidiSong song, MidiOptimizeOptions options)
    {
        Dictionary<MidiLaneKey, MidiLanePlan> lookup = BuildLookup(options);
        int originalInRange = 0, included = 0, playable = 0, skipped = 0, excluded = 0, shifted = 0, repaired = 0;
        List<MidiOptimizationPreview> changes = new();
        Dictionary<MidiLaneKey, MutableStats> stats = new();
        List<(double TimeMs, int Pitch)> output = new();
        foreach (MidiNote note in song.Notes)
        {
            MidiLaneKey key = new(note.Track, note.Channel);
            MidiLanePlan lane = lookup.GetValueOrDefault(key, new MidiLanePlan(key, key.Channel != 9, 0));
            if (!stats.TryGetValue(key, out MutableStats? group))
            {
                group = new MutableStats(lane);
                stats.Add(key, group);
            }
            group.Total++;
            if (note.Pitch is >= NoteMapper.LowestNote and <= NoteMapper.HighestNote)
            {
                originalInRange++;
                group.SourceInRange++;
            }
            if (!lane.Include)
            {
                excluded++;
                group.Omitted++;
                continue;
            }
            included++;
            int? destination = TransformPitch(note.Pitch, lane, options.RepairRemainingOctaves);
            if (destination is null)
            {
                skipped++;
                group.Omitted++;
                if (changes.Count < 600)
                    changes.Add(new MidiOptimizationPreview(note.TimeMs, note.Track, note.Channel, note.Pitch, null, "Skipped: outside C3–C6"));
                continue;
            }
            playable++;
            group.Output++;
            output.Add((note.TimeMs, destination.Value));
            bool groupShifted = lane.OctaveShift != 0;
            bool individuallyRepaired = destination != note.Pitch + lane.OctaveShift;
            if (groupShifted || individuallyRepaired)
            {
                shifted++;
                group.Shifted++;
                if (individuallyRepaired) { repaired++; group.Repaired++; }
                if (changes.Count < 600)
                    changes.Add(new MidiOptimizationPreview(note.TimeMs, note.Track, note.Channel, note.Pitch, destination,
                        individuallyRepaired ? $"Track {lane.OctaveShift:+0;-0;+0}; individual octave repair" : $"Track octave shift {lane.OctaveShift:+0;-0;+0}"));
            }
        }
        int dense = 0, collision = 0;
        foreach (var chord in output.GroupBy(n => (long)Math.Round(n.TimeMs)))
        {
            if (chord.Count() > 1) dense++;
            // Count simultaneous onsets for suitability diagnostics.
            // Full-chord mode retains these; optional reduction is user-selected.
            if (chord.Count() > 1) collision++;
        }
        IReadOnlyList<MidiLaneReport> lanes = stats.OrderBy(s => s.Key.Track).ThenBy(s => s.Key.Channel)
            .Select(s => s.Value.ToReport(s.Key)).ToArray();
        return new MidiOptimizationReport(song.Notes.Count, originalInRange, included, playable,
            skipped, excluded, shifted, repaired, dense, collision, lanes, changes);
    }

    internal static Dictionary<MidiLaneKey, MidiLanePlan> BuildLookup(MidiOptimizeOptions options)
    {
        Dictionary<MidiLaneKey, MidiLanePlan> result = new();
        foreach (MidiLanePlan lane in options.Lanes)
        {
            if (lane.OctaveShift % 12 != 0 || lane.OctaveShift is < -48 or > 48)
                throw new ArgumentException("Octave shifts must be between -48 and +48, in steps of 12.");
            if (!result.TryAdd(lane.Key, lane)) throw new ArgumentException("Duplicate track/channel optimisation plan.");
        }
        return result;
    }

    private sealed class MutableStats(MidiLanePlan lane)
    {
        public readonly MidiLanePlan Plan = lane;
        public int Total, SourceInRange, Output, Shifted, Repaired, Omitted;
        public MidiLaneReport ToReport(MidiLaneKey key) => new(key, Total, SourceInRange, Output,
            Shifted, Repaired, Omitted, Plan.Include, Plan.OctaveShift);
    }
}

/// <summary>
/// Preserves Standard MIDI File tick timing, track order and non-note events. Rewrites note-on
/// and its paired note-off together; octave-repaired notes therefore cannot get stuck from a
/// mismatched note-off pitch. Explicit status bytes eliminate running-status ambiguities.
/// </summary>
public static class MidiOptimizationExporter
{
    private const int MaxFileBytes = 50 * 1024 * 1024;
    private const int MaxTrackBytes = 32 * 1024 * 1024;

    private sealed class Event
    {
        public long Delta;
        public byte[] Message = [];
        public bool Skip;
    }
    private sealed record NoteStart(Event Message, int? NewPitch);

    public static void Export(string sourcePath, string destinationPath, MidiSong original, MidiOptimizeOptions options)
    {
        if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a different filename. The original MIDI will never be overwritten.");
        if (!File.Exists(sourcePath)) throw new FileNotFoundException("Original MIDI file not found.", sourcePath);
        if (new FileInfo(sourcePath).Length > MaxFileBytes) throw new InvalidDataException("MIDI exceeds the 50 MB safety limit.");
        MidiOptimizationReport expected = MidiOptimization.Analyze(original, options);
        if (expected.PlayableNotes <= 0) throw new InvalidDataException("No playable notes in the arrangement. Include a musical track first.");
        Dictionary<MidiLaneKey, MidiLanePlan> plans = MidiOptimization.BuildLookup(options);
        string temp = destinationPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (FileStream source = File.OpenRead(sourcePath))
            using (BinaryReader input = new(source, Encoding.UTF8, leaveOpen: true))
            using (FileStream target = File.Create(temp))
            using (BinaryWriter writer = new(target, Encoding.UTF8, leaveOpen: true))
            {
                if (ReadTag(input) != "MThd") throw new InvalidDataException("Invalid MIDI header.");
                uint headerSize = ReadU32(input);
                if (headerSize < 6 || headerSize > 1024) throw new InvalidDataException("Invalid MIDI header size.");
                byte[] header = ReadExact(input, (int)headerSize);
                ushort declaredTracks = (ushort)((header[2] << 8) | header[3]);
                writer.Write(Encoding.ASCII.GetBytes("MThd"));
                WriteU32(writer, headerSize);
                writer.Write(header);
                int track = 0;
                while (source.Position < source.Length)
                {
                    string tag = ReadTag(input);
                    uint chunkLength = ReadU32(input);
                    if (chunkLength > MaxTrackBytes || chunkLength > source.Length - source.Position)
                        throw new InvalidDataException("Invalid or oversized MIDI chunk.");
                    byte[] chunk = ReadExact(input, (int)chunkLength);
                    byte[] converted = tag == "MTrk" ? TransformTrack(chunk, track++, plans, options.RepairRemainingOctaves) : chunk;
                    writer.Write(Encoding.ASCII.GetBytes(tag));
                    WriteU32(writer, (uint)converted.Length);
                    writer.Write(converted);
                }
                if (track != declaredTracks) throw new InvalidDataException("MIDI track count does not match its header.");
                writer.Flush();
            }
            MidiSong validated = MidiFileReader.Load(temp);
            if (validated.Notes.Count != expected.PlayableNotes)
                throw new InvalidDataException($"Export verification failed: expected {expected.PlayableNotes} note-ons, found {validated.Notes.Count}.");
            if (validated.Notes.Any(n => n.Pitch is < NoteMapper.LowestNote or > NoteMapper.HighestNote))
                throw new InvalidDataException("Export verification failed: some pitches are outside the FFXIV Bard range.");
            File.Move(temp, destinationPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static byte[] TransformTrack(byte[] chunk, int track, IReadOnlyDictionary<MidiLaneKey, MidiLanePlan> plans, bool repairRemaining)
    {
        List<Event> events = ReadTrack(chunk);
        Dictionary<(int Channel, int Pitch), Queue<NoteStart>> held = new();
        foreach (Event midi in events)
        {
            byte[] msg = midi.Message;
            if (msg.Length < 3 || msg[0] is < 0x80 or >= 0xF0) continue;
            int category = msg[0] & 0xF0;
            if (category != 0x80 && category != 0x90) continue;
            int channel = msg[0] & 0x0F;
            int originalPitch = msg[1];
            var key = (channel, originalPitch);
            if (category == 0x90 && msg[2] != 0)
            {
                MidiLaneKey laneKey = new(track, channel);
                MidiLanePlan plan = plans.TryGetValue(laneKey, out MidiLanePlan value)
                    ? value : new MidiLanePlan(laneKey, channel != 9, 0);
                int? newPitch = MidiOptimization.TransformPitch(originalPitch, plan, repairRemaining);
                if (!held.TryGetValue(key, out Queue<NoteStart>? starts))
                    held.Add(key, starts = new Queue<NoteStart>());
                starts.Enqueue(new NoteStart(midi, newPitch));
                if (newPitch is int pitch) msg[1] = (byte)pitch;
                else midi.Skip = true;
            }
            else if (held.TryGetValue(key, out Queue<NoteStart>? starts) && starts.Count > 0)
            {
                NoteStart paired = starts.Dequeue();
                if (paired.NewPitch is int pitch) msg[1] = (byte)pitch;
                else midi.Skip = true;
            }
        }
        using MemoryStream buffer = new();
        using BinaryWriter output = new(buffer, Encoding.UTF8, leaveOpen: true);
        long pendingDelta = 0;
        foreach (Event midi in events)
        {
            pendingDelta = checked(pendingDelta + midi.Delta);
            if (midi.Skip) continue;
            WriteVlq(output, pendingDelta);
            output.Write(midi.Message);
            pendingDelta = 0;
        }
        // End-of-track (if present) consumes all discarded note deltas. Preserve
        // trailing original time even if an unusual file has no end marker.
        if (pendingDelta != 0 || !events.Any(e => !e.Skip && e.Message.Length >= 2 && e.Message[0] == 0xFF && e.Message[1] == 0x2F))
        {
            WriteVlq(output, pendingDelta);
            output.Write(new byte[] { 0xFF, 0x2F, 0x00 });
        }
        return buffer.ToArray();
    }

    private static List<Event> ReadTrack(byte[] bytes)
    {
        using MemoryStream stream = new(bytes, writable: false);
        using BinaryReader reader = new(stream);
        List<Event> events = new();
        int runningStatus = 0;
        while (stream.Position < stream.Length)
        {
            long delta = ReadVlq(reader);
            int first = reader.ReadByte();
            int status, firstData = -1;
            if (first < 0x80)
            {
                if (runningStatus == 0) throw new InvalidDataException("Invalid MIDI running status.");
                status = runningStatus;
                firstData = first;
            }
            else
            {
                status = first;
                runningStatus = status < 0xF0 ? status : 0;
            }
            using MemoryStream message = new();
            message.WriteByte((byte)status); // fully explicit status, safe after event filtering
            if (status == 0xFF)
            {
                int meta = reader.ReadByte();
                message.WriteByte((byte)meta);
                long len = ReadVlq(reader);
                if (len > MaxTrackBytes || len > stream.Length - stream.Position) throw new InvalidDataException("Truncated MIDI meta event.");
                WriteVlq(message, len);
                message.Write(ReadExact(reader, (int)len));
            }
            else if (status == 0xF0 || status == 0xF7)
            {
                long len = ReadVlq(reader);
                if (len > MaxTrackBytes || len > stream.Length - stream.Position) throw new InvalidDataException("Truncated MIDI SysEx event.");
                WriteVlq(message, len);
                message.Write(ReadExact(reader, (int)len));
            }
            else if (status is >= 0x80 and < 0xF0)
            {
                int size = (status & 0xF0) is 0xC0 or 0xD0 ? 1 : 2;
                int data1 = firstData >= 0 ? firstData : reader.ReadByte();
                if (data1 > 127) throw new InvalidDataException("Invalid MIDI data byte.");
                message.WriteByte((byte)data1);
                if (size == 2)
                {
                    int data2 = reader.ReadByte();
                    if (data2 > 127) throw new InvalidDataException("Invalid MIDI data byte.");
                    message.WriteByte((byte)data2);
                }
            }
            else throw new NotSupportedException($"Unsupported MIDI system event 0x{status:X2}.");
            events.Add(new Event { Delta = delta, Message = message.ToArray() });
            if (events.Count > 1_000_000) throw new InvalidDataException("MIDI has too many events.");
        }
        return events;
    }

    private static string ReadTag(BinaryReader r)
    {
        byte[] value = ReadExact(r, 4);
        return Encoding.ASCII.GetString(value);
    }
    private static byte[] ReadExact(BinaryReader r, int count)
    {
        byte[] bytes = r.ReadBytes(count);
        if (bytes.Length != count) throw new EndOfStreamException("Truncated MIDI file.");
        return bytes;
    }
    private static uint ReadU32(BinaryReader r) => (uint)((r.ReadByte() << 24) | (r.ReadByte() << 16) | (r.ReadByte() << 8) | r.ReadByte());
    private static void WriteU32(BinaryWriter w, uint n)
    {
        w.Write((byte)(n >> 24)); w.Write((byte)(n >> 16)); w.Write((byte)(n >> 8)); w.Write((byte)n);
    }
    private static long ReadVlq(BinaryReader reader)
    {
        long value = 0;
        for (int i = 0; i < 4; i++)
        {
            byte n = reader.ReadByte();
            value = (value << 7) | (n & 0x7F);
            if ((n & 0x80) == 0) return value;
        }
        throw new InvalidDataException("MIDI variable-length quantity exceeds four bytes.");
    }
    private static void WriteVlq(Stream stream, long value)
    {
        if (value is < 0 or > 0x0FFFFFFF) throw new InvalidDataException("MIDI delta-time exceeds the supported VLQ range.");
        uint buffer = (uint)value & 0x7F;
        while ((value >>= 7) != 0)
        {
            buffer <<= 8;
            buffer |= (uint)((value & 0x7F) | 0x80);
        }
        while (true)
        {
            stream.WriteByte((byte)buffer);
            if ((buffer & 0x80) == 0) break;
            buffer >>= 8;
        }
    }
    private static void WriteVlq(BinaryWriter writer, long value) => WriteVlq(writer.BaseStream, value);
}
