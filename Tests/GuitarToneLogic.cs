using NunuWindboundBard.Core;

static void Assert(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}

var guitarParts = new Dictionary<MidiLaneKey, int>
{
    [new MidiLaneKey(1, 2)] = GuitarToneOverrides.Clean,
    [new MidiLaneKey(1, 3)] = GuitarToneOverrides.Overdriven,
    [new MidiLaneKey(2, 2)] = GuitarToneOverrides.PowerChords
};
Assert(GuitarToneOverrides.TryResolve(guitarParts, 1, 2, out var tone) && tone == 1,
    "Clean guitar resolves by track AND channel");
Assert(GuitarToneOverrides.TryResolve(guitarParts, 1, 3, out tone) && tone == 0,
    "Overdriven guitar resolves independently on same track");
Assert(GuitarToneOverrides.TryResolve(guitarParts, 2, 2, out tone) && tone == 3,
    "Power Chords tone resolves independently on another track");
Assert(!GuitarToneOverrides.TryResolve(guitarParts, 1, 4, out tone),
    "Unmapped parts leave current game tone unchanged");
Assert(GuitarToneOverrides.Names.Length == 5 && GuitarToneOverrides.Display(-1).StartsWith("Use current"),
    "Five FFXIV guitar tone choices and an explicit no-override option");

var song = new MidiSong("guitar-duet.mid",
    new MidiNote[]
    {
        new(0, 0, 60, 90, 1, 2, 300),
        new(0, 0, 64, 85, 1, 3, 300),
        new(0, 250, 67, 80, 2, 2, 300)
    },
    new MidiTrack[] { new(1, "Clean", 1, false), new(2, "Power", 1, false) }, 1000, 120);
var plans = new[]
{
    new MidiLanePlan(new MidiLaneKey(1, 2), true, 0),
    new MidiLanePlan(new MidiLaneKey(1, 3), true, 0),
    new MidiLanePlan(new MidiLaneKey(2, 2), true, 0)
};
var arranged = BardArranger.Build(song, new BardArrangeOptions(
    new MidiOptimizeOptions(plans, true), ChordStrategy.PreserveAll, 0, 80, 95));
Assert(arranged.Notes.Count == 3, "Full chords remain unsimplified");
Assert(arranged.Notes.Any(x => x.Track == 1 && x.Channel == 2 && x.Pitch == 60),
    "Clean part channel survives optimisation");
Assert(arranged.Notes.Any(x => x.Track == 1 && x.Channel == 3 && x.Pitch == 64),
    "Second guitar part channel survives optimisation");
Assert(arranged.Notes.Any(x => x.Track == 2 && x.Channel == 2 && x.Pitch == 67),
    "Separate MIDI track survives optimisation");
Console.WriteLine("Guitar tone / arrangement smoke test complete.");
