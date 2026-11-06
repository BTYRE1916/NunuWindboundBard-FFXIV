using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class Program
{
    private static void Check(bool ok, string message)
    {
        if (!ok) throw new InvalidOperationException("Native leader sync: " + message);
    }

    public static void Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "windbound-native-sync-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            File.WriteAllBytes(path, new byte[] {0x4D,0x54,0x68,0x64,0,0,0,6,0,0,0,1,0,96,0x4D,0x54,0x72,0x6B,0,0,0,4,0,0xFF,0x2F,0});
            string nonce = "0123456789ABCDEF";
            var roster = new ulong[] {202UL, 101UL, 303UL};
            var lanes = new[] {new MidiLaneKey(0,0),new MidiLaneKey(1,1),new MidiLaneKey(2,2)};
            var parts = new Dictionary<MidiLaneKey,ulong> { [lanes[0]]=101, [lanes[1]]=202, [lanes[2]]=303 };
            string packed = OneClickEnsembleCue.EncodeAssignments(lanes,parts,roster,out string tag);
            string command = GamePartyCueProtocol.NativeFile(3,path,nonce,tag,packed);
            Check(command.Length <= GamePartyCueProtocol.MaxChatCharacters,"file command too long");
            Check(GamePartyCueProtocol.TryParse(command,out var parsed) && parsed.Kind==GamePartyCueKind.NativeFile,
                "file command parse");
            Check(parsed.PlaylistIndex==3 && parsed.Nonce==nonce && parsed.PackedAssignments==packed,
                "selected index, nonce, parts preserved");
            Check(OneClickEnsembleCue.TryDecodeAssignments(parsed.PackedAssignments,parsed.RosterTag,
                lanes,roster.Reverse().ToArray(),out var received,out _), "cross-machine part map");
            Check(received.Count==parts.Count && parts.All(x=>received[x.Key]==x.Value), "parts assigned to correct character");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.NativeFileReady(nonce),out var ack) &&
                ack.Kind==GamePartyCueKind.NativeFileReady && ack.Nonce==nonce, "file acknowledgement parse");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.NativeFileFailed(nonce,"NOTFOUND"),out var failed) &&
                failed.Kind==GamePartyCueKind.NativeFileFailed && failed.PackedInstruments=="NOTFOUND", "missing MIDI response");
            Check(!GamePartyCueProtocol.TryParse(command+" extra",out _),"reject injected command text");
            Check(!GamePartyCueProtocol.TryParse(command.Replace(nonce,"G"+nonce[1..]),out _),"reject invalid nonce");
            var instruments = new Dictionary<ulong,int> { [101]=2, [202]=20, [303]=3 };
            string map = PartyInstrumentPlan.Encode(instruments,roster,out string rosterTag);
            string set = GamePartyCueProtocol.NativeSetInstruments(nonce,rosterTag,map);
            Check(GamePartyCueProtocol.TryParse(set,out var selected) &&
                selected.Kind==GamePartyCueKind.NativeSetInstruments, "SET command parse");
            Check(PartyInstrumentPlan.TryDecode(selected.PackedInstruments,selected.RosterTag,
                roster.Reverse().ToArray(),out var instrumentMap,out _) &&
                instrumentMap[101]==2 && instrumentMap[202]==20 && instrumentMap[303]==3,
                "automatic leader instrument assignments roundtrip");
            Check(!PartyInstrumentPlan.TryDecode(selected.PackedInstruments,selected.RosterTag,
                new ulong[]{888,999,1000},out _,out _),"mismatched roster blocked");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Ready(nonce,20),out var ready) &&
                ready.Kind==GamePartyCueKind.Ready && ready.Nonce==nonce,"instrument ready ack");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop,out var stop) &&
                stop.Kind==GamePartyCueKind.Stop,"legacy STOP backwards compatible");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Song(0,path),out var song) &&
                song.Kind==GamePartyCueKind.Song,"legacy song backwards compatible");
            Console.WriteLine("PASS: MIDI file swap, track map, auto instrument SET, acknowledgements, invalid input and old commands.");
        }
        finally { try {File.Delete(path);} catch { } }
    }
}
