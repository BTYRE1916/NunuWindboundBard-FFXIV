using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NunuWindboundBard.Core;

string input = args.Length > 0 ? Path.GetFullPath(args[0]) :
    Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Samples", "Soulweaver-Quartet-Demo.mid"));
if (!File.Exists(input)) throw new FileNotFoundException("Missing test MIDI.", input);
string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input)));
var song = MidiFileReader.Load(input);
var originalTempo = song.InitialBpm;
var lanes = MidiOptimization.Suggest(song).ToArray();
var prepared = BardArranger.Build(song, new BardArrangeOptions(
    new MidiOptimizeOptions(lanes, true), ChordStrategy.PreserveAll, 0, 80, 95));
if (prepared.Notes.Count < 20) throw new Exception("Test file has insufficient musical material.");
string folder = Path.Combine(Path.GetTempPath(), "windbound-soulweaver-test-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(folder);
try
{
    foreach (int performers in new[] { 1, 2, 4, 8 })
    {
        var arrangement = SoulweaverArranger.Generate(prepared, new SoulweaverOptions(performers));
        if (arrangement.Parts.Count != performers || arrangement.Analysis.SourceNotes != prepared.Notes.Count)
            throw new Exception("Part generation lost its source or performer count.");
        if (arrangement.Parts.Sum(p => p.Notes.Count) != prepared.Notes.Count)
            throw new Exception("Unlimited chord mode dropped source notes.");
        if (arrangement.Parts.SelectMany(p => p.Notes).Any(n => n.Pitch is < 48 or > 84))
            throw new Exception("Generated notes outside C3-C6.");
        string output = Path.Combine(folder, $"soulweaver-{performers}.mid");
        SoulweaverMidiExport.Save(input, output, arrangement);
        var reopened = MidiFileReader.Load(output);
        if (reopened.Notes.Count != prepared.Notes.Count || reopened.Tracks.Count != performers + 1)
            throw new Exception("Reopened export has wrong note or track count.");
        if (Math.Abs(reopened.InitialBpm - originalTempo) > 0.05)
            throw new Exception("Export did not retain initial tempo.");
        if (reopened.Notes.Any(n => n.Pitch is < 48 or > 84))
            throw new Exception("Reopened export contains an out-of-range note.");
        bool refusedDuplicate = false;
        try { SoulweaverMidiExport.Save(input, output, arrangement); }
        catch (IOException) { refusedDuplicate = true; }
        if (!refusedDuplicate) throw new Exception("Export overwrote an existing file.");
        Console.WriteLine($"{performers} performer(s): {arrangement.Analysis.ExportNotes} notes, " +
            $"{arrangement.Analysis.ChordOnsets} chord onsets, {reopened.Tracks.Count} tracks — OK");
    }
    var limited = SoulweaverArranger.Generate(prepared, new SoulweaverOptions(4, 1));
    if (limited.Analysis.ExportNotes > prepared.Notes.Count)
        throw new Exception("Density-limited arrangement generated extra notes.");
    if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))) != hash)
        throw new Exception("The original MIDI was modified.");
    bool refusedSource = false;
    try { SoulweaverMidiExport.Save(input, input, SoulweaverArranger.Generate(prepared, new SoulweaverOptions(1))); }
    catch (IOException) { refusedSource = true; }
    if (!refusedSource) throw new Exception("Exporter allowed source overwrite.");
    Console.WriteLine("Original source intact; no overwriting; optional chord cap — OK");
    Console.WriteLine("SOULWEAVER SMOKE TEST PASSED");
}
finally
{
    Directory.Delete(folder, recursive: true);
}
