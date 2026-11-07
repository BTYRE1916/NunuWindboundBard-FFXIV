using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NunuWindboundBard.Core;

public enum AudioPreviewMode { OriginalFile = 0, WindboundArrangement = 1, SelectedTrack = 2 }

/// <summary>
/// Constructs a small independent Standard MIDI File for preview of Windbound's
/// ARRANGED notes or a single ORIGINAL lane. Original whole-file preview always
/// passes the .mid unmodified to the SoundFont sequencer instead.
///
/// SMF 0; PPQ=1000; tempo=500000us; one tick=0.5ms. Program selections are
/// from channel/track metadata. This is for audio rehearsal only: no game calls.
/// </summary>
public static class AudioPreviewMidiBuilder
{
    private const int MaxEvents = 1_000_000;
    private readonly record struct MidiEvent(long Tick, int Priority, int Channel, byte[] Data);

    public static byte[] Build(MidiSong song, BardArrangement? arrangement, AudioPreviewMode mode,
        MidiLaneKey? selected, IReadOnlyList<MidiLanePlan>? plans = null, bool applyOctaves = true)
    {
        if (mode == AudioPreviewMode.OriginalFile)
            throw new ArgumentException("Original MIDI preview opens the existing file, it does not rebuild it.", nameof(mode));
        if (mode == AudioPreviewMode.SelectedTrack && !selected.HasValue)
            throw new ArgumentException("Choose a MIDI track before previewing it.", nameof(selected));

        List<(MidiLaneKey Key, double StartMs, double EndMs, int Pitch, int Velocity)> notes = new();
        if (mode == AudioPreviewMode.WindboundArrangement)
        {
            if (arrangement is null) throw new InvalidOperationException("Prepare a Windbound arrangement first.");
            foreach (var note in arrangement.Notes)
                notes.Add((new MidiLaneKey(note.Track, note.Channel), note.TimeMs,
                    note.TimeMs + Math.Max(20, note.DurationMs), note.Pitch, note.Velocity));
        }
        else
        {
            var plan = plans?.FirstOrDefault(l => l.Key == selected!.Value);
            foreach (var note in song.Notes)
            {
                if (note.Track != selected!.Value.Track || note.Channel != selected.Value.Channel) continue;
                int pitch = note.Pitch;
                if (applyOctaves && plan is not null)
                    pitch = Math.Clamp(pitch + plan.OctaveShift, 0, 127);
                notes.Add((selected.Value, note.TimeMs, note.TimeMs + Math.Max(20, note.DurationMs),
                    pitch, note.Velocity));
            }
        }
        if (notes.Count == 0)
            throw new InvalidOperationException("This preview selection has no MIDI notes. Choose another track or enable an arrangement lane.");
        if (notes.Count * 2L > MaxEvents)
            throw new InvalidDataException("Too many MIDI events for a preview.");

        var keys = notes.Select(n => n.Key).Distinct().OrderBy(k => k.Track).ThenBy(k => k.Channel).ToArray();
        var channels = new Dictionary<MidiLaneKey, int>();
        int nextMelodic = 0;
        foreach (var key in keys)
        {
            if (key.Channel == 9) { channels[key] = 9; continue; }
            while (nextMelodic == 9) nextMelodic++;
            // More than 15 melodic lanes can share sound patches. Only audio
            // preview is approximate in that unusually dense case.
            channels[key] = nextMelodic < 16 ? nextMelodic++ : (key.Channel == 9 ? 0 : key.Channel);
        }
        var events = new List<MidiEvent>(notes.Count * 2 + keys.Length);
        foreach (var key in keys)
        {
            int channel = channels[key];
            int program = PreviewProgram(song, key);
            events.Add(new MidiEvent(0, 0, channel,
                new byte[] { (byte)(0xC0 | channel), (byte)Math.Clamp(program, 0, 127) }));
        }
        foreach (var note in notes)
        {
            int channel = channels[note.Key];
            byte pitch = (byte)Math.Clamp(note.Pitch, 0, 127);
            byte velocity = (byte)Math.Clamp(note.Velocity, 1, 127);
            long start = ToTick(note.StartMs);
            long end = Math.Max(start + 1, ToTick(note.EndMs));
            // note-offs are processed before simultaneous rearticulations.
            events.Add(new MidiEvent(start, 2, channel, new byte[] { (byte)(0x90 | channel), pitch, velocity }));
            events.Add(new MidiEvent(end, 1, channel, new byte[] { (byte)(0x80 | channel), pitch, 0 }));
        }
        events.Sort((a, b) => a.Tick != b.Tick ? a.Tick.CompareTo(b.Tick) :
            a.Priority != b.Priority ? a.Priority.CompareTo(b.Priority) : a.Channel.CompareTo(b.Channel));
        using var track = new MemoryStream();
        using (var writer = new BinaryWriter(track, Encoding.ASCII, leaveOpen: true))
        {
            // Fixed tempo, which preserves note absolute timestamps in milliseconds.
            writer.Write(new byte[] { 0x00, 0xFF, 0x51, 0x03, 0x07, 0xA1, 0x20 });
            long prev = 0;
            foreach (var e in events)
            {
                WriteVlq(writer, e.Tick - prev);
                writer.Write(e.Data);
                prev = e.Tick;
            }
            writer.Write(new byte[] { 0x00, 0xFF, 0x2F, 0x00 });
        }
        using var result = new MemoryStream();
        using (var writer = new BinaryWriter(result, Encoding.ASCII, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes("MThd"));
            WriteU32(writer, 6);
            WriteU16(writer, 0);     // format 0
            WriteU16(writer, 1);     // one track
            WriteU16(writer, 1000);  // PPQ
            writer.Write(Encoding.ASCII.GetBytes("MTrk"));
            WriteU32(writer, checked((uint)track.Length));
            writer.Write(track.ToArray());
        }
        return result.ToArray();
    }

    /// <summary>Explicit #Lute/#Violin or named tracks take precedence over a
    /// DAW-exported default Piano patch for the REBUILT arrangement only.
    /// Original-file preview always uses the MIDI's own Program Change events.</summary>
    public static int PreviewProgram(MidiSong song, MidiLaneKey key)
    {
        var lane = song.LaneInstruments.FirstOrDefault(l => l.Track == key.Track && l.Channel == key.Channel);
        if (lane is null) return 0;
        int raw = lane.Program ?? 0;
        var detected = MidiInstrumentInference.ForLane(lane, song.FileName,
            song.LaneInstruments.Count(x => !x.IsPercussion));
        if (detected.InstrumentId == 0 || detected.Source.StartsWith("GM ", StringComparison.Ordinal))
            return Math.Clamp(raw, 0, 127);
        return FfxivInstrumentToGm(detected.InstrumentId);
    }

    private static int FfxivInstrumentToGm(int id) => id switch
    {
        1 => 46, 2 => 0, 3 => 25, 4 => 45,
        5 => 73, 6 => 68, 7 => 71, 8 => 72, 9 => 75,
        10 => 47, 11 => 114, 12 => 116, 13 => 118, 14 => 119,
        15 => 56, 16 => 57, 17 => 58, 18 => 60, 19 => 65,
        20 => 40, 21 => 41, 22 => 42, 23 => 43,
        24 => 29, 25 => 27, 26 => 28, 27 => 30, 28 => 30,
        _ => 0
    };

    private static long ToTick(double ms)
    {
        if (!double.IsFinite(ms) || ms < 0 || ms > 3_600_000)
            throw new InvalidDataException("Preview note timestamp is outside the supported 0–60 minute range.");
        return checked((long)Math.Round(ms * 2, MidpointRounding.AwayFromZero));
    }
    private static void WriteU16(BinaryWriter writer, ushort v)
    {
        writer.Write((byte)(v >> 8)); writer.Write((byte)v);
    }
    private static void WriteU32(BinaryWriter writer, uint v)
    {
        writer.Write((byte)(v >> 24)); writer.Write((byte)(v >> 16));
        writer.Write((byte)(v >> 8)); writer.Write((byte)v);
    }
    private static void WriteVlq(BinaryWriter writer, long value)
    {
        if (value < 0 || value > 0x0FFFFFFF) throw new InvalidDataException("Invalid MIDI delta time.");
        uint buffer = (uint)(value & 0x7F);
        while ((value >>= 7) > 0) { buffer <<= 8; buffer |= (uint)((value & 0x7F) | 0x80); }
        while (true)
        {
            writer.Write((byte)buffer);
            if ((buffer & 0x80) == 0) break;
            buffer >>= 8;
        }
    }
}
