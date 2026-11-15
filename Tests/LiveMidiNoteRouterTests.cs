using System;
using System.Collections.Generic;
using NunuWindboundBard.Core;

static class Program
{
    static uint Midi(int status, int data1, int data2 = 0) =>
        (uint)(status | (data1 << 8) | (data2 << 16));
    static void Check(bool ok, string label)
    {
        if (!ok) throw new Exception("FAIL LiveMidi: " + label);
    }
    static void Main()
    {
        var r = new LiveMidiNoteRouter();
        var ons = new List<int>();
        var offs = new List<int>();
        void On(int pitch) => ons.Add(pitch);
        void Off(int pitch) => offs.Add(pitch);

        r.Process(Midi(0x90, 60, 100), On, Off);
        Check(ons.Count == 1 && ons[0] == 60 && r.HeldPitches == 1, "C4 NoteOn");
        r.Process(Midi(0x80, 60), On, Off);
        Check(offs.Count == 1 && offs[0] == 60 && r.HeldPitches == 0, "C4 NoteOff");
        r.Process(Midi(0x90, 61, 90), On, Off);
        r.Process(Midi(0x90, 61, 0), On, Off);
        Check(offs[^1] == 61 && r.HeldPitches == 0, "NoteOn velocity zero means NoteOff");

        // Pitch folding: C2 (36) maps into C3 (48), and C7 (96) maps to C6 (84).
        r.Process(Midi(0x90, 36, 100), On, Off);
        Check(ons[^1] == 48, "lower octave folding");
        r.Process(Midi(0x80, 36), On, Off);
        r.Process(Midi(0x90, 96, 100), On, Off);
        Check(ons[^1] == 84, "upper octave folding");
        r.Process(Midi(0x80, 96), On, Off);

        // Fold collisions reference-count mapped notes: C2 + C3 both map C3;
        // release of either must not prematurely release FFXIV C3.
        ons.Clear(); offs.Clear();
        r.Process(Midi(0x90, 36, 100), On, Off);
        r.Process(Midi(0x90, 48, 100), On, Off);
        Check(ons.Count == 1 && r.HeldPitches == 1, "mapped pitch collision noteOn once");
        r.Process(Midi(0x80, 36), On, Off);
        Check(offs.Count == 0, "mapped pitch collision not released early");
        r.Process(Midi(0x80, 48), On, Off);
        Check(offs.Count == 1 && offs[0] == 48, "mapped pitch collision noteOff after final release");

        // Sustain pedal: deferred NoteOff until CC64 value drops under 64.
        ons.Clear(); offs.Clear();
        r.Process(Midi(0xB0, 64, 127), On, Off);
        r.Process(Midi(0x90, 60, 90), On, Off);
        r.Process(Midi(0x80, 60), On, Off);
        Check(offs.Count == 0 && r.HeldPitches == 1, "sustain holds released physical keys");
        r.Process(Midi(0xB0, 64, 0), On, Off);
        Check(offs.Count == 1 && r.HeldPitches == 0, "sustain pedal release off");

        // Sustain re-press doesn't duplicate notes or leave a key stuck.
        ons.Clear(); offs.Clear();
        r.Process(Midi(0xB0, 64, 127), On, Off);
        r.Process(Midi(0x90, 62, 80), On, Off);
        r.Process(Midi(0x80, 62), On, Off);
        r.Process(Midi(0x90, 62, 80), On, Off);
        r.Process(Midi(0xB0, 64, 0), On, Off);
        Check(ons.Count == 1 && offs.Count == 0, "sustain key re-press remains held");
        r.Process(Midi(0x80, 62), On, Off);
        Check(offs.Count == 1 && r.HeldPitches == 0, "re-press eventually releases");

        // Per-channel filter: channel 2 (MIDI status 0x91) while only channel 1 is accepted.
        r.ChannelFilter = 1;
        ons.Clear(); offs.Clear();
        r.Process(Midi(0x91, 64, 99), On, Off);
        Check(ons.Count == 0, "channel filtering");
        r.Process(Midi(0x90, 64, 99), On, Off);
        Check(ons.Count == 1 && ons[0] == 64, "selected channel accepted");
        r.Process(Midi(0xB0, 123, 0), On, Off);
        Check(offs.Count == 1 && r.HeldPitches == 0, "CC123 all notes off");

        r.ChannelFilter = 0;
        r.Transpose = -12;
        r.FoldInto37Keys = false;
        r.Process(Midi(0x90, 72, 99), On, Off);
        Check(ons[^1] == 60, "octave transpose -12");
        r.Process(Midi(0x80, 72), On, Off);
        int skipped = r.SkippedOutOfRange;
        r.Process(Midi(0x90, 30, 99), On, Off);
        Check(r.SkippedOutOfRange == skipped + 1 && r.HeldPitches == 0, "out-of-range ignored without fold");
        r.Transpose = 0;
        r.Process(Midi(0x90, 60, 99), On, Off);
        r.Process(Midi(0x91, 64, 99), On, Off);
        int nOffs = offs.Count;
        r.AllNotesOff(Off);
        Check(offs.Count == nOffs + 2 && r.HeldPitches == 0, "PANIC releases all mapped pitches");
        Console.WriteLine("PASS: MIDI note on/off, velocity-0, range folding, overlapping mapped notes, sustain, channel filtering, CC123, transpose, panic.");
    }
}
