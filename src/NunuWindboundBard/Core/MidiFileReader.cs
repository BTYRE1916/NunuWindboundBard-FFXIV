using System.IO;
using System.Text;

namespace NunuWindboundBard.Core;

public sealed record MidiNote(long Tick, double TimeMs, int Pitch, int Velocity, int Track, int Channel, double DurationMs = 160);
public sealed record MidiTrack(int Index, string Name, int NoteCount, bool IsPercussion);
public sealed record MidiDrumPitch(int Pitch, int Count);
/// <summary>MIDI metadata per track/channel, including Program Change (0xCn)
/// weighted by actual note-on events, FF 03 track title and FF 04 instrument name.
/// Standard MIDI format 0 can put multiple instrument channels in one track.</summary>
public sealed record MidiLaneInstrument(int Track, int Channel, string TrackName,
    string InstrumentName, int? Program, int DistinctPrograms, int NoteCount,
    bool IsPercussion, IReadOnlyList<MidiDrumPitch> DrumPitches)
{
    // Some DAWs put #Lute / #+12 in FF01 text, FF06 marker or FF07 cue events,
    // rather than in the FF03 track-name or FF04 instrument-name metadata.
    // Keep these labels separately: they belong to the same track/channel,
    // but should not alter the track's displayed name.
    public string TagMetadata { get; init; } = "";
}
public sealed record MidiSong(string FileName, IReadOnlyList<MidiNote> Notes,
    IReadOnlyList<MidiTrack> Tracks, double DurationMs, double InitialBpm)
{
    // Additive metadata keeps old readers/tests/source consumers compatible.
    public IReadOnlyList<MidiLaneInstrument> LaneInstruments { get; init; } = Array.Empty<MidiLaneInstrument>();
}

/// <summary>
/// Small dependency-free Standard MIDI File (SMF 0/1, PPQ) reader.
/// Converts tempo changes across all tracks into absolute milliseconds.
/// Playback is for note-on events only; the Performance instrument will use note-on events.
/// </summary>
public static class MidiFileReader
{
    private sealed record RawNote(long Tick, int Pitch, int Velocity, int Track, int Channel, bool IsOn);
    private sealed record TempoChange(long Tick, int MicrosecondsPerBeat, int Order);
    private sealed record TimelineEvent(long Tick, int Kind, int Order, RawNote? Note, int Tempo);

    public static MidiSong Load(string path)
    {
        FileInfo info = new(path);
        if (!info.Exists) throw new FileNotFoundException("The MIDI file was not found.", path);
        if (info.Length > 50 * 1024 * 1024) throw new InvalidDataException("The MIDI file is larger than the 50 MB safety limit.");

        using FileStream stream = File.OpenRead(path);
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        if (ReadTag(reader) != "MThd") throw new InvalidDataException("Not a Standard MIDI File (missing MThd header).");
        uint headerSize = ReadU32(reader);
        if (headerSize < 6 || headerSize > 1024) throw new InvalidDataException("Invalid MIDI header length.");
        ushort format = ReadU16(reader);
        ushort declaredTracks = ReadU16(reader);
        ushort timeDivision = ReadU16(reader);
        stream.Position += headerSize - 6;
        if (format > 1) throw new NotSupportedException("MIDI format 2 is not supported; export as format 0 or 1.");
        if (declaredTracks == 0 || declaredTracks > 512) throw new InvalidDataException("Invalid track count.");
        if ((timeDivision & 0x8000) != 0) throw new NotSupportedException("SMPTE timing is not supported; export the MIDI with PPQ timing.");
        if (timeDivision == 0) throw new InvalidDataException("Invalid PPQ division.");

        List<RawNote> rawNotes = new();
        List<TempoChange> tempoChanges = new();
        List<MidiTrack> tracks = new();
        List<MidiLaneInstrument> laneInstruments = new();
        // Cross-track program events are a common SMF format-1 export pattern:
        // an orchestration/conductor track can declare a channel's GM program,
        // while the actual notes live in a different track.
        Dictionary<int, HashSet<int>> globalPrograms = new();
        int tempoOrder = 0;

        while (tracks.Count < declaredTracks && stream.Position + 8 <= stream.Length)
        {
            string tag = ReadTag(reader);
            uint chunkSize = ReadU32(reader);
            long chunkEnd = stream.Position + chunkSize;
            if (chunkEnd > stream.Length) throw new InvalidDataException("Truncated MIDI chunk.");
            if (tag != "MTrk")
            {
                stream.Position = chunkEnd;
                continue;
            }

            int index = tracks.Count;
            string name = $"Track {index + 1:00}";
            string instrumentName = "";
            var tagLabels = new List<string>();
            var channelPrograms = new Dictionary<int, int>();
            var programVotes = new Dictionary<(int Channel, int Program), int>();
            var channelNotes = new Dictionary<int, int>();
            var drumPitches = new Dictionary<(int Channel, int Pitch), int>();
            long ticks = 0;
            int runningStatus = 0;
            int noteCount = 0;
            int drumCount = 0;

            while (stream.Position < chunkEnd)
            {
                ticks = checked(ticks + ReadVlq(reader, chunkEnd));
                byte first = reader.ReadByte();
                int firstData = -1;
                int status;
                if (first < 0x80)
                {
                    if (runningStatus == 0) throw new InvalidDataException($"Invalid running status in track {index + 1}.");
                    status = runningStatus;
                    firstData = first;
                }
                else
                {
                    status = first;
                    if (status < 0xF0) runningStatus = status;
                    else runningStatus = 0;
                }

                if (status == 0xFF)
                {
                    int meta = reader.ReadByte();
                    long length = ReadVlq(reader, chunkEnd);
                    RequireRemaining(stream, chunkEnd, length);
                    if (meta is 0x01 or 0x03 or 0x04 or 0x06 or 0x07 && length > 0)
                    {
                        // Text, Track Name, Instrument Name, Marker, Cue Point.
                        // Track tags in the first 2 KiB are sufficient and bounded.
                        byte[] bytes = reader.ReadBytes((int)Math.Min(length, 2048));
                        string value = Encoding.UTF8.GetString(bytes).Trim('\0', ' ', '\t');
                        stream.Position += length - bytes.Length;
                        if (meta == 0x03 && !string.IsNullOrWhiteSpace(value)) name = value;
                        if (meta == 0x04 && !string.IsNullOrWhiteSpace(value)) instrumentName = value;
                        if (value.Contains('#') && tagLabels.Count < 24 && !tagLabels.Contains(value))
                            tagLabels.Add(value);
                    }
                    else if (meta == 0x51 && length == 3)
                    {
                        int mpqn = (reader.ReadByte() << 16) | (reader.ReadByte() << 8) | reader.ReadByte();
                        if (mpqn > 0) tempoChanges.Add(new TempoChange(ticks, mpqn, tempoOrder++));
                    }
                    else
                    {
                        stream.Position += length;
                    }
                    if (meta == 0x2F) break;
                }
                else if (status == 0xF0 || status == 0xF7)
                {
                    long length = ReadVlq(reader, chunkEnd);
                    RequireRemaining(stream, chunkEnd, length);
                    stream.Position += length;
                }
                else if (status >= 0xF0)
                {
                    throw new NotSupportedException($"Unexpected MIDI system message 0x{status:X2} in track {index + 1}.");
                }
                else
                {
                    int type = status & 0xF0;
                    int channel = status & 0x0F;
                    int d1 = firstData >= 0 ? firstData : reader.ReadByte();
                    int d2 = type == 0xC0 || type == 0xD0 ? 0 : reader.ReadByte();
                    if (type == 0xC0) // MIDI Program Change selects a GM program per channel.
                    {
                        channelPrograms[channel] = d1;
                        if (!globalPrograms.TryGetValue(channel, out var programsOnChannel))
                            globalPrograms[channel] = programsOnChannel = new HashSet<int>();
                        programsOnChannel.Add(d1);
                    }
                    else if (type == 0x90 && d2 > 0)
                    {
                        rawNotes.Add(new RawNote(ticks, d1, d2, index, channel, true));
                        noteCount++;
                        channelNotes[channel] = channelNotes.GetValueOrDefault(channel) + 1;
                        if (channelPrograms.TryGetValue(channel, out int gmProgram))
                        {
                            var programKey = (channel, gmProgram);
                            programVotes[programKey] = programVotes.GetValueOrDefault(programKey) + 1;
                        }
                        if (channel == 9)
                        {
                            drumCount++;
                            var drumKey = (channel, d1);
                            drumPitches[drumKey] = drumPitches.GetValueOrDefault(drumKey) + 1;
                        }
                    }
                    else if (type == 0x80 || (type == 0x90 && d2 == 0))
                    {
                        rawNotes.Add(new RawNote(ticks, d1, 0, index, channel, false));
                    }
                }
                if (stream.Position > chunkEnd) throw new InvalidDataException("An event overruns its MIDI track.");
            }
            stream.Position = chunkEnd;
            tracks.Add(new MidiTrack(index, name, noteCount, noteCount > 0 && drumCount == noteCount));
            foreach (var (channel, count) in channelNotes.OrderBy(pair => pair.Key))
            {
                var programs = programVotes.Where(v => v.Key.Channel == channel)
                    .OrderByDescending(v => v.Value).ThenBy(v => v.Key.Program).ToArray();
                var drums = drumPitches.Where(v => v.Key.Channel == channel)
                    .OrderByDescending(v => v.Value).ThenBy(v => v.Key.Pitch)
                    .Select(v => new MidiDrumPitch(v.Key.Pitch, v.Value)).ToArray();
                int? chosenProgram = null;
                if (programs.Length > 0) chosenProgram = programs[0].Key.Program;
                else if (channelPrograms.TryGetValue(channel, out int finalProgram)) chosenProgram = finalProgram;
                laneInstruments.Add(new MidiLaneInstrument(index, channel, name, instrumentName,
                    chosenProgram, programs.Length, count, channel == 9, drums)
                {
                    TagMetadata = string.Join(" | ", tagLabels)
                });
            }
        }
        // Only inherit a program from another track when it is UNAMBIGUOUS for
        // that MIDI channel. Multiple declared programs cannot be safely
        // attributed to a lane without a time-resolved arrangement.
        laneInstruments = laneInstruments.Select(lane =>
            lane.Program is null && !lane.IsPercussion &&
            globalPrograms.TryGetValue(lane.Channel, out var programsForChannel) &&
            programsForChannel.Count == 1
                ? lane with { Program = programsForChannel.First() }
                : lane).ToList();
        if (tracks.Count != declaredTracks) throw new InvalidDataException("MIDI track data is missing.");
        if (rawNotes.Count == 0) throw new InvalidDataException("This MIDI contains no note-on events.");
        if (rawNotes.Count > 500_000) throw new InvalidDataException("This MIDI has too many notes for this player.");

        List<TimelineEvent> timeline = new(tempoChanges.Count + rawNotes.Count);
        timeline.AddRange(tempoChanges.Select(t => new TimelineEvent(t.Tick, 0, t.Order, null, t.MicrosecondsPerBeat)));
        timeline.AddRange(rawNotes.Select((n, i) => new TimelineEvent(n.Tick, 1, i, n, 0)));
        timeline.Sort(static (a, b) =>
        {
            int d = a.Tick.CompareTo(b.Tick);
            if (d != 0) return d;
            d = a.Kind.CompareTo(b.Kind); // tempo before notes at same tick
            return d != 0 ? d : a.Order.CompareTo(b.Order);
        });

        List<MidiNote> notes = new(rawNotes.Count);
        // A queue handles overlapping instances of the same pitch/channel on a track.
        Dictionary<(int Track, int Channel, int Pitch), Queue<int>> held = new();
        long previousTick = 0;
        double elapsedMicroseconds = 0;
        int currentMpqn = 500_000;
        int initialMpqn = 500_000;
        foreach (TimelineEvent point in timeline)
        {
            elapsedMicroseconds += (point.Tick - previousTick) * (double)currentMpqn / timeDivision;
            previousTick = point.Tick;
            if (point.Kind == 0)
            {
                currentMpqn = point.Tempo;
                if (point.Tick == 0) initialMpqn = point.Tempo;
            }
            else if (point.Note is { } note)
            {
                var key = (note.Track, note.Channel, note.Pitch);
                double timestampMs = elapsedMicroseconds / 1000.0;
                if (note.IsOn)
                {
                    int index = notes.Count;
                    notes.Add(new MidiNote(point.Tick, timestampMs,
                        note.Pitch, note.Velocity, note.Track, note.Channel));
                    if (!held.TryGetValue(key, out Queue<int>? indices))
                    {
                        indices = new Queue<int>();
                        held[key] = indices;
                    }
                    indices.Enqueue(index);
                }
                else if (held.TryGetValue(key, out Queue<int>? indices) && indices.Count > 0)
                {
                    int index = indices.Dequeue();
                    notes[index] = notes[index] with
                    {
                        DurationMs = Math.Clamp(timestampMs - notes[index].TimeMs, 35, 60_000)
                    };
                }
            }
        }
        double duration = notes.Max(n => n.TimeMs + n.DurationMs) + 180;
        return new MidiSong(Path.GetFileName(path), notes, tracks, duration, 60_000_000d / initialMpqn)
        {
            LaneInstruments = laneInstruments
        };
    }

    private static string ReadTag(BinaryReader reader)
    {
        byte[] tag = reader.ReadBytes(4);
        if (tag.Length != 4) throw new EndOfStreamException("Unexpected end of MIDI file.");
        return Encoding.ASCII.GetString(tag);
    }
    private static ushort ReadU16(BinaryReader reader)
    {
        int a = reader.ReadByte(), b = reader.ReadByte();
        return (ushort)((a << 8) | b);
    }
    private static uint ReadU32(BinaryReader reader)
    {
        uint a = reader.ReadByte(), b = reader.ReadByte(), c = reader.ReadByte(), d = reader.ReadByte();
        return (a << 24) | (b << 16) | (c << 8) | d;
    }
    private static long ReadVlq(BinaryReader reader, long end)
    {
        long result = 0;
        for (int i = 0; i < 4; i++)
        {
            if (reader.BaseStream.Position >= end) throw new InvalidDataException("Truncated variable-length MIDI value.");
            int b = reader.ReadByte();
            result = (result << 7) | (uint)(b & 0x7F);
            if ((b & 0x80) == 0) return result;
        }
        throw new InvalidDataException("MIDI variable-length value exceeds four bytes.");
    }
    private static void RequireRemaining(Stream stream, long chunkEnd, long count)
    {
        if (count < 0 || count > chunkEnd - stream.Position)
            throw new InvalidDataException("Truncated MIDI meta/SysEx event.");
    }
}
