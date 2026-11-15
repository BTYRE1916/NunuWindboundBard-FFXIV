using System.Diagnostics;
using NAudio.Midi;

namespace NunuWindboundBard.Core;

internal static class MidiEditorPlayback
{
    // Recreate channel state at the new cursor before emitting its first notes.
    internal static EditorSnapshot FromCursor(EditorSnapshot original, EditorClock clock, long start,
        IReadOnlySet<int> mutedTracks, IReadOnlySet<int> mutedChannels, int soloTrack, int soloChannel, bool click)
    {
        bool audible(EditorEvent e) => !e.IsChannelEvent ||
            (!mutedTracks.Contains(e.Track) && !mutedChannels.Contains(e.Channel) &&
             (soloTrack == 0 || e.Track == soloTrack) && (soloChannel == 0 || e.Channel == soloChannel));

        start = Math.Clamp(start, 0, original.EndTick);
        long end = original.EndTick - start;
        var tracks = original.Tracks;
        int conductor = tracks[0].Id;
        var before = original.Events.Where(e => e.Tick < start && audible(e)).ToArray();
        var events = before.Where(e => e.Kind is EditorEventKind.Control or EditorEventKind.Program or
                EditorEventKind.KeyPressure or EditorEventKind.ChannelPressure or EditorEventKind.PitchBend or EditorEventKind.Meter)
            .GroupBy(e => (e.Kind, e.Channel, Parameter: e.Kind is EditorEventKind.Control or EditorEventKind.KeyPressure ? e.Value : 0))
            .Select(g => g.Last() with { Tick = 0 }).ToList();
        // Sustain and bank/program state are delivered before any note crossing the cursor.
        events.Sort(static (a, b) => a.Id.CompareTo(b.Id));
        events.Add(new(-1, conductor, 0, 0, EditorEventKind.Tempo, clock.TempoAt(start)));
        foreach (var e in original.Events.Where(audible))
        {
            if (e.Tick < start && (e.Kind != EditorEventKind.Note || e.End <= start)) continue;
            events.Add(e with { Tick = Math.Max(0, e.Tick - start), Length = (int)Math.Min(e.Length, e.End - start) });
        }
        if (click)
        {
            int clickId = -2;
            for (long tick = start; tick < original.EndTick && events.Count < MidiEditorDocument.MaxEvents;)
            {
                var bar = clock.Bar(tick);
                long next = bar.Start + (long)Math.Ceiling((tick - bar.Start) / (double)bar.Beat) * bar.Beat;
                if (next >= bar.End) { tick = bar.End; continue; }
                if (next >= original.EndTick) break;
                int length = (int)Math.Min(original.EndTick - next, Math.Max(1, bar.Beat / 8));
                events.Add(new(clickId--, conductor, next - start, 10, EditorEventKind.Note, 37, next == bar.Start ? 94 : 62, length));
                tick = Math.Min(next + bar.Beat, bar.End);
            }
        }
        return new(tracks, events.OrderBy(e => e.Tick).ToArray(), end);
    }

    internal static byte[] PreviewBytes(MidiEditorDocument document, EditorSnapshot playback)
    {
        string path = Path.Combine(Path.GetTempPath(), "WindboundPreview-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            MidiFile.Export(path, document.ToMidi(playback));
            return File.ReadAllBytes(path);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}

internal sealed class MidiEditorDevices : IDisposable
{
    private readonly WinMmMidiInput input = new();
    private MidiOut? output;
    private (double Milliseconds, int Raw)[] schedule = [];
    private int scheduleIndex;
    private readonly Stopwatch playClock = new(), recordClock = new();
    private double playbackSpeed = 1;
    private double endMs;
    private readonly List<(double Ms, uint Raw)> recorded = new();
    private readonly HashSet<(int Channel, int Pitch)> heldOutputNotes = new();
    private int overflowAtStart;

    internal bool InputConnected => input.Connected;
    internal string InputName => input.ConnectedName;
    internal string OutputName { get; private set; } = "";
    internal bool OutputConnected => output is not null;
    internal bool Playing => playClock.IsRunning;
    internal double Position => Math.Min(endMs, playClock.Elapsed.TotalMilliseconds * playbackSpeed);
    internal bool Recording => recordClock.IsRunning;
    internal bool Thru { get; private set; }
    internal bool ExplicitNoteOffs { get; set; }
    internal int TakeMessages => recorded.Count;
    internal bool TakeOverflowed => input.OverflowCount != overflowAtStart || recorded.Count >= 200_000;

    internal static IReadOnlyList<WinMmMidiInput.Port> Inputs() => WinMmMidiInput.GetPorts();
    internal static string[] Outputs() => Enumerable.Range(0, MidiOut.NumberOfDevices)
        .Select(i => MidiOut.DeviceInfo(i).ProductName).ToArray();
    internal void OpenInput(WinMmMidiInput.Port port)
    {
        if (Recording) throw new InvalidOperationException("Finish the MIDI take first.");
        SetThru(false);
        input.Open(port);
    }
    internal void CloseInput() { if (Recording) throw new InvalidOperationException("Finish the MIDI take first."); SetThru(false); input.Close(); }
    internal void OpenOutput(int port)
    {
        string name = MidiOut.DeviceInfo(port).ProductName;
        CloseOutput();
        output = new MidiOut(port);
        OutputName = name;
    }
    internal void CloseOutput()
    {
        StopPlayback();
        if (Thru || heldOutputNotes.Count > 0) Panic();
        Thru = false;
        output?.Dispose();
        output = null;
        OutputName = "";
    }
    internal void Panic()
    {
        if (output is null) return;
        if (ExplicitNoteOffs)
        {
            foreach (var (channel, pitch) in heldOutputNotes) output.Send(0x80 | channel | pitch << 8);
            for (int channel = 0; channel < 16; channel++) output.Send(0xb0 | channel | 64 << 8);
        }
        heldOutputNotes.Clear();
        output.Reset();
    }

    internal void ReleaseDevicesAfterError()
    {
        playClock.Reset(); schedule = []; scheduleIndex = 0;
        Thru = false;
        var lost = output;
        output = null; OutputName = ""; heldOutputNotes.Clear();
        try { lost?.Reset(); } catch (Exception) { /* The driver or port may have disappeared. */ }
        try { lost?.Dispose(); } catch (Exception) { /* The driver or port may have disappeared. */ }
    }

    private void Send(int raw)
    {
        output?.Send(raw);
        int code = raw & 0xf0;
        var key = (raw & 15, raw >> 8 & 127);
        if (IsRelease(raw)) heldOutputNotes.Remove(key);
        else if (code == 0x90) heldOutputNotes.Add(key);
    }
    internal void SetThru(bool enabled)
    {
        if (enabled && (!InputConnected || !OutputConnected)) throw new InvalidOperationException("Connect both MIDI ports for MIDI thru.");
        if (!enabled && Thru) Panic();
        Thru = enabled;
        input.Drain();
    }

    internal void Play(MidiEditorDocument document, EditorSnapshot playback, double rate)
    {
        if (output is null) throw new InvalidOperationException("Connect a MIDI output first.");
        StopPlayback();
        var clock = new EditorClock(document.Ppq, playback.Events);
        schedule = playback.Events.Where(e => e.IsChannelEvent).SelectMany(e =>
        {
            var midi = MidiEditorDocument.Encode(e);
            var start = (clock.Milliseconds(e.Tick), midi.GetAsShortMessage());
            return midi is NoteOnEvent { OffEvent: not null } n && e.Kind == EditorEventKind.Note
                ? new[] { start, (clock.Milliseconds(e.End), n.OffEvent.GetAsShortMessage()) }
                : new[] { start };
        }).OrderBy(x => x.Item1).ThenBy(x => IsRelease(x.Item2) ? 0 : 1).ToArray();
        playbackSpeed = Math.Clamp(rate, .5, 2);
        endMs = clock.Milliseconds(playback.EndTick);
        scheduleIndex = 0;
        playClock.Restart();
    }

    private static bool IsRelease(int raw) => (raw & 0xf0) == 0x80 || ((raw & 0xf0) == 0x90 && (raw >> 16 & 127) == 0);
    internal void StopPlayback()
    {
        bool wasPlaying = Playing;
        playClock.Reset();
        schedule = [];
        scheduleIndex = 0;
        if (wasPlaying) Panic();
    }

    internal void StartRecording()
    {
        if (!input.Connected) throw new InvalidOperationException("Connect a MIDI input before recording.");
        recorded.Clear();
        input.Drain();
        overflowAtStart = input.OverflowCount;
        recordClock.Restart();
    }

    internal void CancelRecording() { recordClock.Reset(); recorded.Clear(); }

    internal List<EditorEvent> FinishRecording(MidiEditorDocument doc, long startTick, int track, int channel, double rate)
    {
        double end = recordClock.Elapsed.TotalMilliseconds;
        recordClock.Stop();
        var take = new List<EditorEvent>();
        var held = new Dictionary<(int Channel, int Note), Queue<(long Tick, int Velocity)>>();
        double startMs = doc.Clock.Milliseconds(startTick);
        foreach (var (ms, raw) in recorded)
        {
            int command = (int)(raw & 0xf0), wireChannel = (int)(raw & 15) + 1;
            int destination = channel == 0 ? wireChannel : channel;
            int value = (int)(raw >> 8 & 127), secondary = (int)(raw >> 16 & 127);
            long tick = doc.Clock.Ticks(startMs + ms * rate);
            if (command is 0x80 or 0x90)
            {
                var key = (wireChannel, value);
                if (command == 0x90 && secondary > 0)
                {
                    if (!held.TryGetValue(key, out var queue)) held[key] = queue = new();
                    queue.Enqueue((tick, secondary));
                }
                else if (held.TryGetValue(key, out var queue) && queue.TryDequeue(out var onset))
                    AddNote(value, destination, onset, tick);
                continue;
            }
            if (command is < 0xa0 or > 0xe0) continue;
            var midi = MidiEvent.FromRawMessage((int)raw);
            midi.AbsoluteTime = tick;
            midi.Channel = destination;
            take.Add(MidiEditorDocument.Decode(doc.NextId(), track, midi));
        }
        long stopTick = doc.Clock.Ticks(startMs + end * rate);
        foreach (var (key, queue) in held)
            while (queue.TryDequeue(out var onset)) AddNote(key.Note, channel == 0 ? key.Channel : channel, onset, stopTick);
        recorded.Clear();
        return take;

        void AddNote(int pitch, int destination, (long Tick, int Velocity) onset, long off)
        {
            if (onset.Tick >= MidiEditorDocument.MaxTick) return;
            take.Add(new(doc.NextId(), track, onset.Tick, destination, EditorEventKind.Note, pitch,
                onset.Velocity, (int)Math.Max(1, off - onset.Tick)));
        }
    }

    internal void Update()
    {
        if (Playing && output is not null)
        {
            double time = Position;
            int sent = 0;
            while (scheduleIndex < schedule.Length && schedule[scheduleIndex].Milliseconds <= time && sent++ < 4096)
                Send(schedule[scheduleIndex++].Raw);
            if (scheduleIndex == schedule.Length && time >= endMs) StopPlayback();
        }
        int processed = 0;
        while (processed++ < 512 && input.TryDequeue(out uint raw))
        {
            int command = (int)(raw & 0xf0);
            if (command is < 0x80 or > 0xe0) continue;
            if (Recording && recorded.Count < 200_000) recorded.Add((recordClock.Elapsed.TotalMilliseconds, raw));
            if (Thru) Send((int)raw);
        }
    }

    public void Dispose()
    {
        recordClock.Stop();
        input.Dispose();
        try { CloseOutput(); } catch (Exception) { ReleaseDevicesAfterError(); }
    }
}
