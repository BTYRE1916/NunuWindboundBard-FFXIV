using NAudio.Midi;
using NunuWindboundBard.Core;

int checks = 0;
void Check(bool value, string context)
{
    if (!value) throw new InvalidOperationException(context);
    checks++;
}

string folder = Path.Combine(Path.GetTempPath(), "WindboundEditorTests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string source = Path.Combine(folder, "editor-source.mid");
    string edited = Path.Combine(folder, "edited.mid");
    var doc = MidiEditorDocument.New();
    int conductor = doc.State.Tracks[0].Id;
    int lead = doc.AddTrack("Mélodie 🎹");
    int chord = doc.AddTrack("Harmony");
    EditorEvent New(int track, long tick, int channel, EditorEventKind kind, int value = 0, int secondary = 0, int length = 1, string text = "") =>
        new(doc.NextId(), track, tick, channel, kind, value, secondary, length, text);
    var melody = New(lead, 420, 2, EditorEventKind.Note, 68, 112, 480);
    doc.Add([
        New(conductor, 0, 0, EditorEventKind.Tempo, 500_000),
        New(conductor, 960, 0, EditorEventKind.Tempo, 750_000),
        New(conductor, 0, 0, EditorEventKind.Meter, 3, 3),
        New(conductor, 720, 0, EditorEventKind.Marker, text: "Entrée 🎵"),
        New(lead, 0, 2, EditorEventKind.Program, 40),
        New(lead, 12, 2, EditorEventKind.Control, 64, 127),
        New(lead, 28, 2, EditorEventKind.PitchBend, 9200),
        New(lead, 300, 2, EditorEventKind.KeyPressure, 68, 55),
        New(lead, 310, 2, EditorEventKind.ChannelPressure, 92),
        melody,
        New(chord, 480, 3, EditorEventKind.Note, 55, 85, 180),
        New(chord, 1200, 3, EditorEventKind.Note, 57, 85, 180),
    ]);
    Check(doc.Clock.Bar(0) == (0, 720, 240), "3/8 measure and beat lengths");
    Check(doc.Clock.Bar(721).Start == 720, "measure navigation across a 3/8 boundary");
    Check(doc.Clock.Snap(418, 2) == 480, "quarter subdivisions snap by musical ticks");
    Check(Math.Abs(doc.Clock.Milliseconds(1440) - 1750) < .01, "tempo changes drive tick-to-time mapping");
    Check(doc.Clock.Ticks(1750) == 1440, "time-to-tick round trip across a tempo change");

    doc.Save(doc.State, source, false);
    byte[] original = File.ReadAllBytes(source);
    var sourceFile = new MidiFile(source, true);
    Check(sourceFile.Tracks == 3 && sourceFile.DeltaTicksPerQuarterNote == 480, "export contains the correct SMF track count and PPQ");
    var reopened = MidiEditorDocument.Open(source);
    Check(reopened.State.Events.Length == doc.State.Events.Length, "round trip preserves notes and event counts");
    Check(reopened.State.Tracks[1].Name == "Mélodie 🎹", "round trip preserves Unicode track names");
    Check(reopened.State.Events.Any(e => e.Kind == EditorEventKind.Marker && e.Text == "Entrée 🎵"), "round trip preserves Unicode markers");
    Check(reopened.State.Events.Any(e => e.Kind == EditorEventKind.Control && e.Value == 64 && e.Secondary == 127), "sustain and controller value survive export");
    Check(reopened.State.Events.Any(e => e.Kind == EditorEventKind.PitchBend && e.Value == 9200), "14-bit pitch bend survives export");
    Check(reopened.State.Events.Any(e => e.Kind == EditorEventKind.KeyPressure && e.Value == 68 && e.Secondary == 55), "polyphonic pressure survives export");
    Check(reopened.State.Events.Any(e => e.Kind == EditorEventKind.ChannelPressure && e.Value == 92), "channel pressure survives export");
    var openedNote = reopened.State.Events.Single(e => e.Kind == EditorEventKind.Note && e.Value == 68);
    Check(openedNote.Secondary == 112 && openedNote.Length == 480 && openedNote.Tick == 420 && openedNote.Channel == 2,
        "note length, pitch, velocity, time, and channel survive export");

    var picked = new HashSet<int> { openedNote.Id };
    reopened.Transform(picked, e => e with { Tick = 450, Length = 180, Value = 69, Secondary = 101 });
    var transformed = reopened.State.Events.Single(e => e.Id == openedNote.Id);
    Check(transformed.Tick == 450 && transformed.Value == 69 && transformed.Length == 180 && transformed.Secondary == 101, "one note edit updates its position, length and velocity");
    reopened.Undo();
    Check(reopened.State.Events.Single(e => e.Id == openedNote.Id) == openedNote, "undo restores the entire note edit");
    reopened.Redo();
    Check(reopened.State.Events.Single(e => e.Id == openedNote.Id) == transformed, "redo restores the edited note");

    reopened.InsertTime(720, 720);
    Check(reopened.State.Events.Single(e => e.Kind == EditorEventKind.Marker).Tick == 1440, "insert bars shifts markers");
    reopened.RemoveTime(720, 1440);
    Check(reopened.State.Events.Single(e => e.Kind == EditorEventKind.Marker).Tick == 720, "remove inserted time restores marker location");
    reopened.SetLength(540);
    Check(reopened.State.Events.Single(e => e.Id == openedNote.Id).Length == 90, "song trim clips crossing notes");
    Check(reopened.State.EndTick == 540, "song trim sets the end of the file");
    reopened.Save(reopened.State, edited, false);
    Check(original.SequenceEqual(File.ReadAllBytes(source)), "save as preserves the imported file bytes");
    try { reopened.Save(reopened.State, edited, false); throw new Exception("Save As overwrote an existing output."); }
    catch (IOException) { Check(true, "save as refuses an existing name"); }

    var preview = MidiEditorPlayback.FromCursor(doc.State, doc.Clock, 600, new HashSet<int> { chord }, new HashSet<int>(), 0, 0, true);
    Check(preview.Events.Any(e => e.Kind == EditorEventKind.Program && e.Tick == 0 && e.Value == 40), "seek restores program state at cursor");
    Check(preview.Events.Any(e => e.Kind == EditorEventKind.Control && e.Tick == 0 && e.Value == 64), "seek restores controller state at cursor");
    Check(preview.Events.Any(e => e.Id == melody.Id && e.Tick == 0 && e.Length == 300), "seek reattacks a note with only its remaining duration");
    Check(!preview.Events.Any(e => e.IsChannelEvent && e.Track == chord), "muted track events are excluded from preview");
    Check(preview.Events.Any(e => e.Channel == 10 && e.Kind == EditorEventKind.Note && e.Tick == 120), "metronome starts on the next actual meter beat");
    Check(MidiEditorPlayback.PreviewBytes(doc, preview).Take(4).SequenceEqual("MThd"u8.ToArray()), "preview emits a standard MIDI header");
    Console.WriteLine($"PASS: {checks} MIDI editor timeline, event preservation, save safety, and preview checks.");
}
finally { Directory.Delete(folder, true); }
