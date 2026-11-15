using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    public readonly record struct MidiLaneKey(int Track, int Channel);
    public sealed record MidiLanePlan(MidiLaneKey Key, bool Include, int OctaveShift);
    public sealed record BardNote(double TimeMs, int Pitch, int Track, int Velocity, double DurationMs, int Channel = 0);
    public sealed record BardArrangement(IReadOnlyList<BardNote> Notes);
}

static class Program
{
    static void Check(bool condition, string label)
    {
        if (!condition) throw new Exception("AudioPreview smoke test FAILED: " + label);
    }
    static MidiSong Parse(byte[] bytes)
    {
        string file = Path.Combine(Path.GetTempPath(), "windbound-audio-" + Guid.NewGuid().ToString("N") + ".mid");
        try { File.WriteAllBytes(file, bytes); return MidiFileReader.Load(file); }
        finally { try { File.Delete(file); } catch { } }
    }
    static void Main()
    {
        var song = new MidiSong("duet.mid", new[] {
            new MidiNote(0, 100, 60, 100, 0, 0, 150),
            new MidiNote(0, 350, 64, 87, 0, 0, 100),
            new MidiNote(0, 200, 70, 96, 1, 1, 300),
        }, new[]{new MidiTrack(0,"Piano",2,false),new MidiTrack(1,"Violin",1,false)}, 550,120)
        {
            LaneInstruments = new[] {
                new MidiLaneInstrument(0,0,"Piano","",0,1,2,false,Array.Empty<MidiDrumPitch>()),
                new MidiLaneInstrument(1,1,"Violin","",40,1,1,false,Array.Empty<MidiDrumPitch>())
            }
        };
        var arranged = new BardArrangement(new[] {
            new BardNote(100,60,0,100,150,0),
            new BardNote(350,64,0,87,100,0),
            new BardNote(200,70,1,96,300,1)
        });
        var full = Parse(AudioPreviewMidiBuilder.Build(song, arranged, AudioPreviewMode.WindboundArrangement, null));
        Check(full.Notes.Count == 3,"arranged note count");
        Check(full.Notes.Any(n => Math.Abs(n.TimeMs - 100) <= 1 && n.Pitch == 60), "arranged note start time");
        Check(full.Notes.Any(n => Math.Abs(n.TimeMs - 350) <= 1 && n.Pitch == 64), "second note timing");
        Check(full.LaneInstruments.Any(l => l.Program == 40), "violin GM Program Change retained in rebuilt MIDI");
        // Explicit #Lute tag must override a DAW's default GM Piano program.
        var tagged = song with { LaneInstruments = new[] {
            new MidiLaneInstrument(0,0,"#+12 #Lute","",0,1,2,false,Array.Empty<MidiDrumPitch>()),
            song.LaneInstruments[1]
        }};
        var tagParsed = Parse(AudioPreviewMidiBuilder.Build(tagged,arranged,AudioPreviewMode.WindboundArrangement,null));
        Check(tagParsed.LaneInstruments.Any(l => l.Program == 25),"#Lute maps to GM steel acoustic guitar for SF2 audio preview");

        var chosen = new MidiLaneKey(1,1);
        var lane = Parse(AudioPreviewMidiBuilder.Build(song, arranged, AudioPreviewMode.SelectedTrack, chosen,
            new[] {new MidiLanePlan(chosen,true,-12)}, true));
        Check(lane.Notes.Count == 1,"selected lane note count");
        Check(lane.Notes[0].Pitch == 58,"selected lane saved octave shift");
        Check(Math.Abs(lane.Notes[0].TimeMs - 200) <= 1,"selected lane timing");
        var withoutShift = Parse(AudioPreviewMidiBuilder.Build(song,arranged,AudioPreviewMode.SelectedTrack,
            chosen,new[]{new MidiLanePlan(chosen,true,-12)},false));
        Check(withoutShift.Notes[0].Pitch == 70,"optional octave bypass");
        Check(song.Notes[2].Pitch == 70,"source not mutated");

        bool emptyRejected = false;
        try { AudioPreviewMidiBuilder.Build(song,arranged,AudioPreviewMode.SelectedTrack,new MidiLaneKey(9,9)); }
        catch (InvalidOperationException) { emptyRejected = true; }
        Check(emptyRejected,"empty track handled");
        Console.WriteLine("PASS: preview MIDI builder, original source untouched, GM instruments, timing, lane filtering, octave shifting.");
    }
}
