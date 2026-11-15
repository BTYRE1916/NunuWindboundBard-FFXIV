using NAudio.Midi;
using System.Text;

namespace NunuWindboundBard.Core;

internal enum EditorEventKind { Note, Control, PitchBend, KeyPressure, ChannelPressure, Program, Tempo, Meter, Marker, Other }

internal sealed record EditorEvent(int Id, int Track, long Tick, int Channel, EditorEventKind Kind,
    int Value = 0, int Secondary = 0, int Length = 1, string Text = "", MidiEvent? Original = null)
{
    internal long End => Kind == EditorEventKind.Note ? Tick + Length : Tick;
    internal bool IsChannelEvent => Channel is >= 1 and <= 16;
}

internal sealed record EditorTrack(int Id, string Name);
internal sealed record EditorSnapshot(EditorTrack[] Tracks, EditorEvent[] Events, long EndTick);

/// <summary>General MIDI editing in absolute ticks. Windbound's Bard arrangements use a separate working copy.</summary>
internal sealed class MidiEditorDocument
{
    internal const long MaxTick = 0x0fffffff;
    internal const int MaxEvents = 500_000;
    private const int HistoryDepth = 20;
    private readonly List<EditorSnapshot> undo = new(), redo = new();
    private int nextId = 1;
    private int nextTrack = 1;

    private MidiEditorDocument(int ppq, string source)
    {
        if (ppq is < 1 or > 32767) throw new InvalidDataException("Choose a MIDI file with a PPQ time division.");
        Ppq = ppq;
        Source = source;
        State = new([new EditorTrack(nextTrack++, "Track 1")], [], ppq * 4L);
        Clock = new EditorClock(ppq, []);
    }

    internal int Ppq { get; }
    internal string Source { get; }
    internal EditorSnapshot State { get; private set; }
    internal EditorClock Clock { get; private set; }
    internal bool CanUndo => undo.Count > 0;
    internal bool CanRedo => redo.Count > 0;
    internal IReadOnlyList<EditorSnapshot> History => undo;
    internal int Revision { get; private set; }
    internal int NextId() => nextId++;
    internal static MidiEditorDocument New() => new(480, "");

    internal static MidiEditorDocument Open(string path)
    {
        path = Path.GetFullPath(path);
        if (new FileInfo(path).Length > 32L * 1024 * 1024)
            throw new InvalidDataException("Choose a MIDI file up to 32 MiB.");
        var midi = new MidiFile(path, false);
        if (midi.FileFormat is not (0 or 1) || midi.Tracks is < 1 or > 128)
            throw new InvalidDataException("Choose a format 0 or 1 MIDI with 1–128 tracks.");
        var document = new MidiEditorDocument(midi.DeltaTicksPerQuarterNote, path);
        var tracks = new List<EditorTrack>();
        var events = new List<EditorEvent>();
        var pairedOffs = new HashSet<MidiEvent>(ReferenceEqualityComparer.Instance);
        long end = 0;
        for (int i = 0; i < midi.Tracks; i++)
        {
            int track = document.nextTrack++;
            var trackName = midi.Events[i].OfType<TextEvent>().FirstOrDefault(e => e.MetaEventType == MetaEventType.SequenceTrackName);
            tracks.Add(new(track, trackName is null ? $"Track {i + 1}" : ReadText(trackName)));
            pairedOffs.Clear();
            foreach (var note in midi.Events[i].OfType<NoteOnEvent>())
                if (note.Velocity > 0 && note.OffEvent is not null) pairedOffs.Add(note.OffEvent);
            foreach (var item in midi.Events[i])
            {
                end = Math.Max(end, item.AbsoluteTime);
                if (ReferenceEquals(item, trackName) || pairedOffs.Contains(item) ||
                    item is MetaEvent { MetaEventType: MetaEventType.EndTrack }) continue;
                events.Add(Decode(document.NextId(), track, item));
                if (events.Count > MaxEvents) throw new InvalidDataException("The editor supports up to 500,000 events.");
            }
        }
        document.SetState(Validate(new(tracks.ToArray(), events.ToArray(), end)));
        return document;
    }

    internal static EditorEvent Decode(int id, int track, MidiEvent e)
    {
        var item = new EditorEvent(id, track, e.AbsoluteTime, e is MetaEvent or SysexEvent ? 0 : e.Channel, EditorEventKind.Other);
        return e switch
        {
            NoteOnEvent { Velocity: > 0, OffEvent: not null } n => item with
                { Kind = EditorEventKind.Note, Value = n.NoteNumber, Secondary = n.Velocity, Length = Math.Max(1, n.NoteLength), Original = n.OffEvent },
            ControlChangeEvent c => item with { Kind = EditorEventKind.Control, Value = (int)c.Controller, Secondary = c.ControllerValue },
            PitchWheelChangeEvent b => item with { Kind = EditorEventKind.PitchBend, Value = b.Pitch },
            PatchChangeEvent p => item with { Kind = EditorEventKind.Program, Value = p.Patch },
            TempoEvent t => item with { Kind = EditorEventKind.Tempo, Value = t.MicrosecondsPerQuarterNote },
            TimeSignatureEvent s => item with { Kind = EditorEventKind.Meter, Value = s.Numerator, Secondary = s.Denominator, Original = s },
            TextEvent { MetaEventType: MetaEventType.Marker } m => item with { Kind = EditorEventKind.Marker, Text = ReadText(m) },
            _ when e.CommandCode == MidiCommandCode.KeyAfterTouch => item with
                { Kind = EditorEventKind.KeyPressure, Value = e.GetAsShortMessage() >> 8 & 127, Secondary = e.GetAsShortMessage() >> 16 & 127 },
            _ when e.CommandCode == MidiCommandCode.ChannelAfterTouch => item with
                { Kind = EditorEventKind.ChannelPressure, Value = e.GetAsShortMessage() >> 8 & 127 },
            _ => item with { Text = e is TextEvent text ? ReadText(text) : e.CommandCode.ToString(), Original = e },
        };
    }

    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static string ReadText(TextEvent text)
    {
        try { return StrictUtf8.GetString(text.Data); }
        catch (DecoderFallbackException) { return Encoding.Latin1.GetString(text.Data); }
    }

    private static EditorSnapshot Validate(EditorSnapshot snapshot)
    {
        if (snapshot.Tracks.Length is < 1 or > 128 || snapshot.Events.Length > MaxEvents)
            throw new InvalidOperationException("Keep the document within 128 tracks and 500,000 events.");
        var tracks = snapshot.Tracks.Select(t => t.Id).ToHashSet();
        var ids = new HashSet<int>();
        long end = Math.Max(0, snapshot.EndTick);
        foreach (var e in snapshot.Events)
        {
            bool valid = e.Kind switch
            {
                EditorEventKind.Note => e.IsChannelEvent && e.Value is >= 0 and <= 127 && e.Secondary is >= 1 and <= 127 && e.Length > 0,
                EditorEventKind.Control or EditorEventKind.KeyPressure => e.IsChannelEvent && e.Value is >= 0 and <= 127 && e.Secondary is >= 0 and <= 127,
                EditorEventKind.Program or EditorEventKind.ChannelPressure => e.IsChannelEvent && e.Value is >= 0 and <= 127,
                EditorEventKind.PitchBend => e.IsChannelEvent && e.Value is >= 0 and <= 16383,
                EditorEventKind.Tempo => e.Value is >= 1 and <= 0xffffff,
                EditorEventKind.Meter => e.Value is >= 1 and <= 255 && e.Secondary is >= 0 and <= 8,
                EditorEventKind.Marker => e.Text.Length <= 4096,
                EditorEventKind.Other => e.Original is not null,
                _ => false,
            };
            if (!valid || !tracks.Contains(e.Track) || !ids.Add(e.Id) || e.Tick < 0 || e.End > MaxTick)
                throw new InvalidDataException("The proposed edit contains an invalid MIDI event, track, or time.");
            end = Math.Max(end, e.End);
        }
        if (end > MaxTick) throw new InvalidDataException("The proposed song length exceeds the supported MIDI tick range.");
        return snapshot with { Events = snapshot.Events.OrderBy(e => e.Tick).ThenBy(e => e.Id).ToArray(), EndTick = end };
    }

    private void SetState(EditorSnapshot state)
    {
        State = state;
        Clock = new EditorClock(Ppq, state.Events);
        Revision++;
    }

    internal void Commit(IEnumerable<EditorEvent> events, EditorTrack[]? tracks = null, long? end = null)
    {
        var next = Validate(new(tracks ?? State.Tracks, events.ToArray(), end ?? State.EndTick));
        undo.Add(State);
        if (undo.Count > HistoryDepth) undo.RemoveAt(0);
        redo.Clear();
        SetState(next);
    }

    internal void Undo() => Restore(undo, redo);
    internal void Redo() => Restore(redo, undo);
    private void Restore(List<EditorSnapshot> from, List<EditorSnapshot> to)
    {
        if (from.Count == 0) return;
        to.Add(State);
        SetState(from[^1]);
        from.RemoveAt(from.Count - 1);
    }

    internal int AddTrack(string name)
    {
        var track = new EditorTrack(nextTrack++, CleanName(name));
        Commit(State.Events, [.. State.Tracks, track]);
        return track.Id;
    }
    private static string CleanName(string name) => string.IsNullOrWhiteSpace(name) ? "Untitled track" : name.Trim();
    internal void RenameTrack(int id, string name) => Commit(State.Events,
        State.Tracks.Select(t => t.Id == id ? t with { Name = CleanName(name) } : t).ToArray());
    internal void RemoveTrack(int id) => Commit(State.Events.Where(e => e.Track != id), State.Tracks.Where(t => t.Id != id).ToArray());
    internal void Transform(IReadOnlySet<int> ids, Func<EditorEvent, EditorEvent> change) =>
        Commit(State.Events.Select(e => ids.Contains(e.Id) ? change(e) : e));
    internal void Delete(IReadOnlySet<int> ids) => Commit(State.Events.Where(e => !ids.Contains(e.Id)));
    internal void Add(IEnumerable<EditorEvent> items) => Commit(State.Events.Concat(items));

    internal void SetLength(long end) => Commit(State.Events.Where(e => e.Tick <= end && (e.Kind != EditorEventKind.Note || e.Tick < end))
        .Select(e => e.Kind == EditorEventKind.Note ? e with { Length = (int)Math.Min(e.Length, end - e.Tick) } : e), end: end);

    internal void InsertTime(long at, long length) => Commit(State.Events.Select(e =>
        e.Tick >= at ? e with { Tick = e.Tick + length } :
        e.Kind == EditorEventKind.Note && e.End > at ? e with { Length = checked(e.Length + (int)length) } : e),
        end: Math.Max(at, State.EndTick) + length);

    internal void RemoveTime(long start, long end)
    {
        if (end <= start) return;
        long Shift(long tick) => tick < start ? tick : tick < end ? start : tick - (end - start);
        Commit(State.Events.Where(e => e.Tick < start || e.Tick >= end || (e.Kind == EditorEventKind.Note && e.End > end))
            .Select(e => e with { Tick = Shift(e.Tick), Length = e.Kind == EditorEventKind.Note ? (int)(Shift(e.End) - Shift(e.Tick)) : 1 })
            .Where(e => e.Kind != EditorEventKind.Note || e.Length > 0), end: Shift(State.EndTick));
    }

    internal MidiEventCollection ToMidi(EditorSnapshot snapshot)
    {
        var collection = new MidiEventCollection(1, Ppq);
        var indices = new Dictionary<int, int>();
        for (int i = 0; i < snapshot.Tracks.Length; i++)
        {
            var track = snapshot.Tracks[i];
            collection.AddTrack();
            indices.Add(track.Id, i);
            collection.AddEvent(MakeText(track.Name, MetaEventType.SequenceTrackName, 0), i);
        }
        foreach (var e in snapshot.Events)
        {
            var midi = Encode(e);
            int track = indices[e.Track];
            collection.AddEvent(midi, track);
            if (e.Kind == EditorEventKind.Note && midi is NoteOnEvent note) collection.AddEvent(note.OffEvent, track);
        }
        for (int i = 0; i < snapshot.Tracks.Length; i++)
            collection.AddEvent(new MetaEvent(MetaEventType.EndTrack, 0, snapshot.EndTick), i);
        collection.PrepareForExport();
        return collection;
    }

    private static TextEvent MakeText(string text, MetaEventType kind, long tick)
    {
        var e = new TextEvent(text, kind, tick);
        e.Data = Encoding.UTF8.GetBytes(text);
        return e;
    }

    internal static MidiEvent Encode(EditorEvent e)
    {
        int raw(int code, int data, int second = 0) => code | (e.Channel - 1) | (data << 8) | (second << 16);
        MidiEvent output = e.Kind switch
        {
            EditorEventKind.Note => new NoteOnEvent(e.Tick, e.Channel, e.Value, e.Secondary, e.Length),
            EditorEventKind.Control => new ControlChangeEvent(e.Tick, e.Channel, (MidiController)e.Value, e.Secondary),
            EditorEventKind.PitchBend => new PitchWheelChangeEvent(e.Tick, e.Channel, e.Value),
            EditorEventKind.KeyPressure => MidiEvent.FromRawMessage(raw(0xa0, e.Value, e.Secondary)),
            EditorEventKind.ChannelPressure => MidiEvent.FromRawMessage(raw(0xd0, e.Value)),
            EditorEventKind.Program => new PatchChangeEvent(e.Tick, e.Channel, e.Value),
            EditorEventKind.Tempo => new TempoEvent(e.Value, e.Tick),
            EditorEventKind.Meter => new TimeSignatureEvent(e.Tick, e.Value, e.Secondary,
                (e.Original as TimeSignatureEvent)?.TicksInMetronomeClick ?? 24,
                (e.Original as TimeSignatureEvent)?.No32ndNotesInQuarterNote ?? 8),
            EditorEventKind.Marker => MakeText(e.Text, MetaEventType.Marker, e.Tick),
            _ => e.Original!.Clone(),
        };
        output.AbsoluteTime = e.Tick;
        if (e.IsChannelEvent) output.Channel = e.Channel;
        if (output is NoteOnEvent { OffEvent: not null } note && e.Original is NoteEvent noteOff)
            note.OffEvent.Velocity = noteOff.Velocity;
        return output;
    }

    internal void Save(EditorSnapshot snapshot, string path, bool replace)
    {
        path = Path.GetFullPath(path);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".mid" or ".midi")) throw new IOException("Choose a .mid or .midi path.");
        string partial = path + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            MidiFile.Export(partial, ToMidi(snapshot));
            var verified = new MidiFile(partial, false);
            if (verified.Tracks != snapshot.Tracks.Length || verified.DeltaTicksPerQuarterNote != Ppq)
                throw new IOException("The new MIDI failed its format verification.");
            File.Move(partial, path, replace);
        }
        finally { if (File.Exists(partial)) File.Delete(partial); }
    }
}

internal sealed class EditorClock
{
    private readonly record struct TempoPoint(long Tick, double Ms, int Microseconds);
    private readonly record struct MeterPoint(long Tick, int Count, int Power);
    private readonly TempoPoint[] tempo;
    private readonly MeterPoint[] meter;
    private readonly int ppq;

    internal EditorClock(int ppq, IEnumerable<EditorEvent> events)
    {
        this.ppq = ppq;
        var tempos = new List<TempoPoint> { new(0, 0, 500_000) };
        foreach (var e in events.Where(e => e.Kind == EditorEventKind.Tempo))
        {
            var previous = tempos[^1];
            tempos.Add(new(e.Tick, previous.Ms + (e.Tick - previous.Tick) * previous.Microseconds / (ppq * 1000.0), e.Value));
        }
        tempo = tempos.ToArray();
        meter = new[] { new MeterPoint(0, 4, 2) }.Concat(events.Where(e => e.Kind == EditorEventKind.Meter)
            .Select(e => new MeterPoint(e.Tick, e.Value, e.Secondary))).ToArray();
    }

    private static int Find(int count, Func<int, bool> before)
    {
        int first = 0, last = count;
        while (first + 1 < last)
        {
            int mid = (first + last) / 2;
            if (before(mid)) first = mid; else last = mid;
        }
        return first;
    }
    internal int TempoAt(long tick) => tempo[Find(tempo.Length, i => tempo[i].Tick <= tick)].Microseconds;
    internal double Milliseconds(long tick)
    {
        var point = tempo[Find(tempo.Length, i => tempo[i].Tick <= tick)];
        return point.Ms + (tick - point.Tick) * point.Microseconds / (ppq * 1000.0);
    }
    internal long Ticks(double ms)
    {
        var point = tempo[Find(tempo.Length, i => tempo[i].Ms <= ms)];
        return Math.Clamp(point.Tick + (long)Math.Round((ms - point.Ms) * ppq * 1000 / point.Microseconds), 0, MidiEditorDocument.MaxTick);
    }
    internal long Snap(long tick, int divisions)
    {
        if (divisions <= 0) return Math.Clamp(tick, 0, MidiEditorDocument.MaxTick);
        long origin = Bar(tick).Start;
        double step = ppq / (double)divisions;
        return Math.Clamp(origin + (long)Math.Round(Math.Round((tick - origin) / step) * step), 0, MidiEditorDocument.MaxTick);
    }
    internal (long Start, long End, long Beat) Bar(long tick)
    {
        int index = Find(meter.Length, i => meter[i].Tick <= tick);
        var point = meter[index];
        long beat = Math.Max(1, (long)Math.Round(ppq * 4.0 / (1 << point.Power)));
        long bar = beat * point.Count;
        long start = point.Tick + Math.Max(0, tick - point.Tick) / bar * bar;
        return (start, Math.Min(start + bar, index + 1 < meter.Length ? meter[index + 1].Tick : MidiEditorDocument.MaxTick + 1), beat);
    }
}
