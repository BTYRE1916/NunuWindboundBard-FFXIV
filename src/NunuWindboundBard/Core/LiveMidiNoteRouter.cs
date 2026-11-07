using System;
using System.Collections.Generic;
using System.Linq;

namespace NunuWindboundBard.Core;

/// <summary>
/// MIDI 1.0 note/CC translation without any device or FFXIV dependency.
/// NoteOn/Off share a mapped-pitch reference count, so two keys folded to the
/// same 37-key pitch never release each other prematurely. Sustain is per channel.
/// IMPORTANT: call from the framework thread, not the winmm callback.
/// </summary>
public sealed class LiveMidiNoteRouter
{
    private sealed class HeldKey(int mapped)
    {
        public int Mapped { get; } = mapped;
        public bool PhysicalDown { get; set; } = true;
    }

    private readonly Dictionary<int, HeldKey> held = new();
    private readonly Dictionary<int, int> pitchCount = new();
    private readonly bool[] sustainDown = new bool[16];
    public int Transpose { get; set; }
    public bool FoldInto37Keys { get; set; } = true;
    public bool UseSustainPedal { get; set; } = true;
    /// <summary>0=omni; 1..16=MIDI channel.</summary>
    public int ChannelFilter { get; set; }
    public int HeldPitches => pitchCount.Count;
    public int ReceivedNotes { get; private set; }
    public int SkippedOutOfRange { get; private set; }
    public int LastOriginalNote { get; private set; } = -1;
    public int LastMappedNote { get; private set; } = -1;

    public void Process(uint rawShortMessage, Action<int> on, Action<int> off)
    {
        int status = (int)(rawShortMessage & 0xff);
        int channel = status & 0x0f;
        int type = status & 0xf0;
        if (status < 0x80 || status >= 0xf0 || (ChannelFilter != 0 && ChannelFilter != channel + 1)) return;
        int noteOrCc = (int)((rawShortMessage >> 8) & 0x7f);
        int value = (int)((rawShortMessage >> 16) & 0x7f);
        int identity = channel * 128 + noteOrCc;
        if (type == 0x90 && value != 0)
        {
            ReceivedNotes++;
            LastOriginalNote = noteOrCc;
            if (held.TryGetValue(identity, out HeldKey? existing))
            {
                // Multiple NoteOn messages for one physical key cannot create
                // an unreleasable key. Re-press during sustain cancels deferred off.
                existing.PhysicalDown = true;
                LastMappedNote = existing.Mapped;
                return;
            }
            int mapped = noteOrCc + Transpose;
            if (FoldInto37Keys)
            {
                while (mapped < NoteMapper.LowestNote) mapped += 12;
                while (mapped > NoteMapper.HighestNote) mapped -= 12;
            }
            if (mapped < NoteMapper.LowestNote || mapped > NoteMapper.HighestNote)
            {
                SkippedOutOfRange++;
                LastMappedNote = -1;
                return;
            }
            LastMappedNote = mapped;
            held.Add(identity, new HeldKey(mapped));
            if (!pitchCount.TryGetValue(mapped, out int count)) count = 0;
            pitchCount[mapped] = count + 1;
            if (count == 0) on(mapped);
        }
        else if (type == 0x80 || (type == 0x90 && value == 0))
        {
            if (!held.TryGetValue(identity, out HeldKey? active)) return;
            if (UseSustainPedal && sustainDown[channel]) active.PhysicalDown = false;
            else Release(identity, off);
        }
        else if (type == 0xB0)
        {
            switch (noteOrCc)
            {
                case 64: // damper pedal
                    bool down = UseSustainPedal && value >= 64;
                    bool wasDown = sustainDown[channel];
                    sustainDown[channel] = down;
                    if (wasDown && !down)
                    {
                        foreach (int key in held.Where(p => p.Key / 128 == channel && !p.Value.PhysicalDown)
                            .Select(p => p.Key).ToArray()) Release(key, off);
                    }
                    break;
                case 120: // all sounds off
                case 123: // all notes off
                    foreach (int key in held.Keys.Where(k => k / 128 == channel).ToArray()) Release(key, off);
                    sustainDown[channel] = false;
                    break;
            }
        }
    }

    private void Release(int key, Action<int> off)
    {
        if (!held.Remove(key, out HeldKey? value)) return;
        int pitch = value.Mapped;
        int count = pitchCount[pitch] - 1;
        if (count <= 0)
        {
            pitchCount.Remove(pitch);
            off(pitch);
        }
        else pitchCount[pitch] = count;
    }

    public void AllNotesOff(Action<int> off)
    {
        foreach (int pitch in pitchCount.Keys.ToArray()) off(pitch);
        pitchCount.Clear();
        held.Clear();
        Array.Clear(sustainDown, 0, sustainDown.Length);
    }
}
