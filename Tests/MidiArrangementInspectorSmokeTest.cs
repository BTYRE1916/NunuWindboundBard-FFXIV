using System;
using System.Collections.Generic;
using System.Linq;
using NunuWindboundBard.Core;

static class Program
{
    static void Check(bool condition, string name)
    { if (!condition) throw new Exception("FAIL: " + name); }
    public static void Main()
    {
        // A two-lane MIDI with out-of-range notes, overlapping pitch and
        // different instruments. All cases are computed without FFXIV or Dalamud.
        var notes = new List<MidiNote> {
            new(0,0,40,90,1,0,500),
            new(25,120,40,90,1,0,500),  // identical pitch overlaps previous
            new(50,250,60,85,1,0,200),
            new(0,0,72,80,2,1,200),
        };
        var tracks = new List<MidiTrack>{new(1,"#+12 #Lute",3,false),new(2,"#Violin",1,false)};
        var meta = new List<MidiLaneInstrument>{
            new(1,0,"#+12 #Lute","Lute",24,1,3,false,Array.Empty<MidiDrumPitch>()),
            new(2,1,"#Violin","Violin",40,1,1,false,Array.Empty<MidiDrumPitch>())
        };
        var midi = new MidiSong("arrangement.mid",notes,tracks,2000,120){LaneInstruments=meta};
        var lanes = new [] {new MidiLanePlan(new MidiLaneKey(1,0),true,12),
                           new MidiLanePlan(new MidiLaneKey(2,1),true,0)};
        var assigned = new Dictionary<MidiLaneKey,ulong>{{lanes[0].Key,123},{lanes[1].Key,123}};
        var report = MidiArrangementInspector.Inspect(midi,lanes,false,assigned,new ulong[]{123,456},
            new Dictionary<ulong,int>{{123,3}});
        Check(report.SourceNotes==4, "counts original notes");
        Check(report.Lanes.Count==2, "two MIDI lanes");
        Check(report.Lanes[0].OverlappingDuplicates==1, "overlapping same pitch detection");
        Check(report.Lanes[0].HasExplicitOctave && report.Lanes[0].ExplicitOctave==12,"octave metadata");
        Check(report.Issues.Any(i=>i.Description.Contains("different instruments")),"instrument conflict");
        Check(report.Issues.Any(i=>i.Description.Contains("out-of-range")) ||
              report.Issues.Any(i=>i.Description.Contains("outside C3")),"pitch range issue");
        Check(report.Issues.Any(i=>i.Description.Contains("no playable assigned")),"silent party member");
        var withoutRepair = MidiArrangementInspector.Inspect(midi,
            new [] {new MidiLanePlan(new MidiLaneKey(1,0),true,0)},false);
        Check(withoutRepair.Lanes[0].OmittedNotes>0,"out of range notes skipped without repair");
        var withRepair = MidiArrangementInspector.Inspect(midi,lanes,true);
        Check(withRepair.PlayableNotes>=report.PlayableNotes,"octave repair never reduces playable total");
        Console.WriteLine("PASS: MIDI Inspector range, overlaps, tags, conflict, performer silence and optimiser statistics.");
    }
}
