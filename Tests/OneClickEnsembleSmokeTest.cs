using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    // Standalone test build uses only pure business logic, not Dalamud.
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception("FAIL: " + message);
    }

    public static void Main()
    {
        string path = Path.Combine(Path.GetTempPath(), "windbound-one-click-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            File.WriteAllBytes(path, new byte[] {0x4D,0x54,0x68,0x64,0,0,0,6,0,0,0,1,0,96,0x4D,0x54,0x72,0x6B,0,0,0,4,0,0xFF,0x2F,0});
            var lanes = new[] {new MidiLaneKey(0,0),new MidiLaneKey(0,1),new MidiLaneKey(1,2),new MidiLaneKey(4,3),new MidiLaneKey(8,4)};
            var ids = new ulong[] {0x100122, 0x100121, 0x100123};
            var parts = new Dictionary<MidiLaneKey, ulong> {{lanes[0],ids[0]}, {lanes[1],ids[1]}, {lanes[4],ids[1]}};
            string roles = OneClickEnsembleCue.EncodeAssignments(lanes,parts,ids,out var tag);
            Check(OneClickEnsembleCue.TryDecodeAssignments(roles,tag,lanes,ids.Reverse().ToArray(),out var decoded,out _), "party roster order independence");
            Check(decoded.Count == parts.Count && parts.All(p=>decoded.TryGetValue(p.Key,out var id) && id==p.Value), "role round trip");
            Check(!OneClickEnsembleCue.TryDecodeAssignments(roles,tag,lanes,new ulong[] {22,23,24},out _,out _), "roster mismatch rejected");
            Check(!OneClickEnsembleCue.TryDecodeAssignments(roles,tag,lanes[..3],ids,out _,out _), "lane count mismatch rejected");
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string cmd = GamePartyCueProtocol.Start(3,path,now+6000,1000,tag,roles,"0123456789ABCDEF");
            Check(cmd.Length <= GamePartyCueProtocol.MaxChatCharacters,"FFXIV party chat message size");
            Check(GamePartyCueProtocol.TryParse(cmd,out var cue) && cue.Kind==GamePartyCueKind.Start && cue.PlaylistIndex==3,"cue parse");
            Check(cue.PackedAssignments==roles && cue.RosterTag==tag && cue.Nonce=="0123456789ABCDEF", "round trip fields");
            Check(!GamePartyCueProtocol.TryParse(cmd+" extra",out _),"injected text rejected");
            Check(!GamePartyCueProtocol.TryParse(cmd.Replace("0123456789ABCDEF","Z123456789ABCDEF"),out _),"invalid nonce rejected");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop,out var stop) && stop.Kind==GamePartyCueKind.Stop,"STOP compatible");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Song(2,path),out var song) && song.Kind==GamePartyCueKind.Song,"SONG compatible");
            long qpc=Stopwatch.GetTimestamp();
            Check(OneClickEnsembleCue.TryGetLocalStartQpc(now+6000,now,qpc,Stopwatch.Frequency,out long local,out _),"local clock anchor");
            Check(Math.Abs((local-qpc)*1000d/Stopwatch.Frequency-6000d)<1, "6s countdown QPC");
            Check(!OneClickEnsembleCue.TryGetLocalStartQpc(now-500,now,qpc,Stopwatch.Frequency,out _,out _),"late cue rejected");
            Check(!OneClickEnsembleCue.TryGetLocalStartQpc(now+60000,now,qpc,Stopwatch.Frequency,out _,out _),"implausible cue rejected");
            var emptyRoles=OneClickEnsembleCue.EncodeAssignments(lanes,new Dictionary<MidiLaneKey,ulong>(),ids,out var emptyTag);
            Check(emptyRoles=="-" && emptyTag=="00000000","manual tracks fallback");
            Console.WriteLine("PASS one-click: leader cue, hash, part mapping, malformed cue guards, UTC->local monotonic clock, song/STOP compatibility.");
        }
        finally { try {File.Delete(path);}catch{} }
    }
}
