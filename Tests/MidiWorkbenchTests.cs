using NAudio.Midi;
using NunuWindboundBard.Core;
using System.Security.Cryptography;

int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception(message); checks++; }
void Reject(Action action, string message)
{
    bool rejected = false; try { action(); } catch { rejected = true; }
    Check(rejected, message);
}
string folder = Path.Combine(Path.GetTempPath(), "Workbench-Tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    string path = Path.Combine(folder, "source.mid");
    var midi = new MidiEventCollection(1, 480);
    midi.AddTrack(); midi.AddEvent(new TempoEvent(800_000, 0), 0); midi.AddEvent(new TempoEvent(500_000, 0), 0); midi.AddEvent(new TempoEvent(1_000_000, 480), 0);
    midi.AddEvent(new TimeSignatureEvent(4, 2, 24, 8, 0), 0);
    midi.AddEvent(new TextEvent("Héllo", MetaEventType.Lyric, 720), 0);
    void Track(string name) { midi.AddTrack(); midi.AddEvent(new TextEvent(name, MetaEventType.SequenceTrackName, 0), midi.Tracks - 1); }
    void Note(int track, long at, int channel, int pitch, int velocity, int duration)
    {
        var note = new NoteOnEvent(at, channel, pitch, velocity, duration);
        midi.AddEvent(note, track); midi.AddEvent(note.OffEvent, track);
    }
    Track("Piano #Piano #+12");
    Note(1, 0, 1, 60, 70, 240); Note(1, 0, 2, 60, 90, 480);
    Note(1, 0, 1, 64, 80, 240); Note(1, 120, 3, 60, 90, 720);
    Track("Drums"); Note(2, 0, 10, 35, 90, 120); Note(2, 0, 10, 36, 100, 120);
    Note(2, 240, 10, 38, 90, 120); Note(2, 360, 10, 42, 70, 120); Note(2, 480, 10, 60, 90, 120);
    Track("Bassdrum"); Note(3, 960, 10, 60, 90, 120);
    midi.PrepareForExport(); MidiFile.Export(path, midi);
    string sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    var doc = MidiWorkbenchDocument.Load(path);
    Check(doc.Tracks.Count == 5, "one workbench part per source lane, including format-1 channels");
    // The Piano source uses three channels: these are three independently editable lanes.
    Check(doc.Clock.ToMs(480) == 500 && doc.Clock.ToMs(960) == 1500, "tempo changes retained");
    Check(doc.Clock.ToTicks(1000) == 720, "milliseconds to ticks across tempo changes");
    Check(doc.Tracks.Where(t => t.Name.StartsWith("Piano")).All(t => t.OctaveShift == 12), "source octave directives retained");
    Check(MidiWorkbenchExport.Lyrics(doc.Snapshot()).Contains("[00:01.00]Héllo"), "LRC lyrics use source tempo map and decode legacy accented text");
    Check(MidiFileReader.DecodeText(System.Text.Encoding.UTF8.GetBytes("日本語 Étoile")) == "日本語 Étoile", "UTF-8 metadata is preserved");
    var kit = doc.Tracks.Single(t => t.IsDrumKit);
    var prepared = doc.Tracks.Single(t => t.Name == "Bassdrum");
    Check(prepared.InstrumentId == 12 && !prepared.IsDrumKit, "prepared Bassdrum is not converted again");
    int count = doc.Tracks.Sum(t => t.Notes.Count);
    doc.SplitDrums(new HashSet<Guid> {kit.Id}, new MidiWorkbenchSettings());
    var bass = doc.Tracks.Single(t => t.Name.Contains("— Bass Drum"));
    Check(bass.InstrumentId == 12 && bass.Notes.Count == 1 && bass.Notes[0].Pitch == 60,
        "kick mapping merges duplicate converted hits and selects Bass Drum 12");
    Check(doc.Tracks.Any(t => t.IsDrumKit && t.Notes.Any(n => n.Pitch == 42)), "unmapped hi-hat retained as Other Percussion");
    Check(doc.Tracks.Any(t => t.InstrumentId == 13) && doc.Tracks.Any(t => t.InstrumentId == 11), "snare and bongo split");
    doc.Undo(); Check(doc.Tracks.Sum(t => t.Notes.Count) == count && doc.Tracks.Any(t => t.Id == kit.Id), "drum split undo restores source notes");
    doc.Redo(); Check(doc.Tracks.Any(t => t.Id == bass.Id), "drum split redo");
    doc.Undo();
    var badMap = new MidiWorkbenchSettings(); badMap.DrumMap[35] = new(12, 20);
    int revision = doc.Revision;
    Reject(() => doc.SplitDrums(new HashSet<Guid> {kit.Id}, badMap), "invalid playable drum targets rejected");
    Check(doc.Revision == revision && doc.Tracks.Any(t => t.Id == kit.Id), "failed edit is atomic");
    var notes = new[] {
        new MidiNote(0, 0, 40, 70, 0, 0, 1000),
        new MidiNote(0, 0, 40, 90, 0, 1, 300),
        new MidiNote(240, 250, 40, 80, 0, 0, 500),
        new MidiNote(480, 500, 90, 80, 0, 0, 100)
    };
    var clean = MidiWorkbenchDocument.RemoveDuplicates(notes);
    Check(clean.Count == 3 && clean[0].Velocity == 90 && clean[0].DurationMs == 1000, "duplicate cleanup retains loudest onset and longest sustain");
    var trimmed = MidiWorkbenchDocument.TrimOverlaps(clean);
    Check(trimmed[0].DurationMs == 250, "same-pitch overlap trim");
    var folded = MidiWorkbenchDocument.Transpose(clean, 0, true);
    Check(folded.All(n => n.Pitch is >= 48 and <= 84) && folded.Select(n => n.Pitch % 12).SequenceEqual(clean.Select(n => n.Pitch % 12)), "octave repair preserves pitch classes");
    Check(MidiWorkbenchDocument.LimitVoices(notes, 1).Count == 2, "voice cap handles sustained polyphony");
    Check(MidiWorkbenchDocument.ExtendNotes(clean, 200)[0].DurationMs == 200, "sustain extension is bounded");
    var q = doc.Quantize(new[] {new MidiNote(0, 535, 60, 90, 0, 0, 500)}, 4)[0];
    Check(doc.Clock.ToTicks(q.TimeMs) % 120 == 0, "quantization uses beats after a tempo change");
    var active = doc.Tracks.First();
    Reject(() => doc.SetTrack(active with {Notes = new[] {active.Notes[0] with {DurationMs = 1}}}),
        "note duration matches the playback reader minimum");
    Reject(() => doc.SetTrack(active with {Notes = new[] {active.Notes[0] with {Pitch = 128}}}),
        "out-of-range MIDI pitches rejected before committing an edit");
    doc.BakeOctaves(new HashSet<Guid> {active.Id});
    Check(doc.Tracks[0].OctaveShift == 0 && doc.Tracks[0].Notes[0].Pitch == active.Notes[0].Pitch + 12, "octave bake changes pitches exactly once");
    doc.Undo();
    doc.SetTrack(active with {Notes = notes});
    doc.SplitVoices(new HashSet<Guid> {active.Id});
    var voices = doc.Tracks.Where(t => t.Name.StartsWith(active.Name + " voice")).ToArray();
    Check(voices.Sum(t => t.Notes.Count) == notes.Length, "voice split preserves all notes");
    foreach (var voice in voices)
    {
        var ordered = voice.Notes.OrderBy(n => n.TimeMs).ToArray();
        Check(ordered.Zip(ordered.Skip(1)).All(p => p.First.TimeMs + p.First.DurationMs <= p.Second.TimeMs + .001), "split voices have no overlapping notes");
    }
    doc.Undo(); doc.Undo();
    int prior = doc.Tracks.Count;
    doc.Duplicate(new HashSet<Guid> {prepared.Id}); Check(doc.Tracks.Count == prior + 1, "duplicate part");
    doc.Undo();
    var firstTwo = doc.Tracks.Take(2).Select(t => t.Id).ToHashSet();
    doc.Merge(firstTwo, doc.Tracks[0].Id); Check(doc.Tracks.Count == prior - 1, "merge parts"); doc.Undo();
    doc.Move(prepared.Id, -1); Check(doc.Tracks[^2].Id == prepared.Id, "move part"); doc.Undo();
    string output = Path.Combine(folder, "edited.mid");
    doc.SetTrack(prepared with {Name = "Étoile — Bassdrum"});
    MidiWorkbenchExport.Save(doc.Snapshot(), output);
    var reopened = MidiFileReader.Load(output);
    Check(reopened.Notes.Count == count, "real MIDI export retains every enabled note");
    Check(new MidiFile(output, false).Events.SelectMany(t => t).OfType<TempoEvent>().Count() == 2, "export retains tempo changes");
    Check(MidiWorkbenchClock.Load(output).ToMs(960) == 1500, "exported tempo clock unchanged");
    Check(MidiTrackTags.TryGetLaneOctave(reopened, new MidiLaneKey(1, reopened.Notes.First(n => n.Track == 1).Channel), out int octave) && octave == 12,
        "export retains octave tags as metadata without double-transposing notes");
    var exportedBass = reopened.LaneInstruments.Single(t => t.TrackName.Contains("Bassdrum"));
    Check(MidiInstrumentInference.ForLane(exportedBass).InstrumentId == 12, "Bassdrum instrument tag survives export and re-import");
    Check(exportedBass.TrackName.Contains("Étoile"), "Unicode track titles survive export and re-import");
    Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) == sourceHash, "source remains byte-identical after edits and export");
    var preview = MidiWorkbenchExport.Preview(doc.Snapshot());
    string previewPath = Path.Combine(folder, "preview.mid"); File.WriteAllBytes(previewPath, preview);
    var previewSong = MidiFileReader.Load(previewPath);
    Check(previewSong.Notes.First(n => n.Track == 1).Pitch == doc.Tracks[0].Notes[0].Pitch + 12,
        "local preview bakes source octave exactly once");
    Check(!MidiTrackTags.TryGetLaneOctave(previewSong, new MidiLaneKey(1, previewSong.Notes.First(n => n.Track == 1).Channel), out _),
        "preview MIDI has no remaining octave directive");
    Reject(() => MidiWorkbenchExport.Save(doc.Snapshot(), path), "source overwrite rejected");
    Reject(() => MidiWorkbenchExport.Save(doc.Snapshot(), output), "existing export overwrite rejected");
    var empty = doc.Snapshot() with {Tracks = doc.Tracks.Select(t => t with {Enabled = false}).ToArray()};
    Reject(() => MidiWorkbenchExport.Save(empty, Path.Combine(folder, "empty.mid")), "empty export rejected");
    File.AppendAllText(path, "changed");
    Reject(() => MidiWorkbenchExport.Save(doc.Snapshot(), Path.Combine(folder, "changed.mid")), "changed source rejected");
    Console.WriteLine($"PASS: {checks} workbench checks: edit / undo / redo, drum split and unmapped notes, named Bassdrum, voice splitting, tempo-aware quantization, polyphony, real export / re-import, lyrics and source preservation.");
}
finally { Directory.Delete(folder, true); }
