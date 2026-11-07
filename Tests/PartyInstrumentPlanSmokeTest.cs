using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    // Standalone test build links the shared musical-protocol source without Dalamud.
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class Program
{
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
    }
    public static void Main()
    {
        var roster = new ulong[] {0xA345UL, 0xB111UL, 0xC222UL};
        var choices = new Dictionary<ulong, int> {
            [roster[0]] = 2, // Piano
            [roster[1]] = 20, // Violin
            [roster[2]] = 26  // Muted guitar
        };
        string packed = PartyInstrumentPlan.Encode(choices, roster, out var rosterTag);
        Check(PartyInstrumentPlan.TryDecode(packed, rosterTag, roster.Reverse().ToArray(),
            out var decoded, out _), "instrument roster-order independence");
        Check(decoded.Count == choices.Count && choices.All(x => decoded[x.Key] == x.Value),
            "party instrument choices roundtrip");
        Check(!PartyInstrumentPlan.TryDecode(packed, rosterTag,
            new ulong[] {1,2,3}, out _, out _), "reject roster mismatch");
        Check(!PartyInstrumentPlan.TryDecode(packed+"!", rosterTag, roster,
            out _, out _), "reject invalid encoded characters");
        bool invalidRejected = false;
        try { PartyInstrumentPlan.Encode(new Dictionary<ulong,int> { [roster[0]]=29 },roster,out _); }
        catch(InvalidOperationException) { invalidRejected = true; }
        Check(invalidRejected, "reject invalid instrument IDs");
        string path = Path.Combine(Path.GetTempPath(), "windbound-party-instruments-" + Guid.NewGuid().ToString("N") + ".mid");
        try
        {
            File.WriteAllBytes(path, new byte[] {77,84,104,100,0,0,0,6,0,0,0,1,0,96,77,84,114,107,0,0,0,4,0,255,47,0});
            const string nonce="0123456789ABCDEF";
            var prep = GamePartyCueProtocol.Prepare(0,path,nonce,rosterTag,packed);
            Check(prep.Length <= GamePartyCueProtocol.MaxChatCharacters,"prepare chat length");
            Check(GamePartyCueProtocol.TryParse(prep,out var received) &&
                received.Kind == GamePartyCueKind.Prepare && received.Nonce == nonce &&
                received.RosterTag == rosterTag && received.PackedInstruments == packed,
                "prepare cue parse roundtrip");
            var ack=GamePartyCueProtocol.Ready(nonce,20);
            Check(GamePartyCueProtocol.TryParse(ack,out var ready) &&
                ready.Kind == GamePartyCueKind.Ready && ready.PackedInstruments == "20",
                "ready acknowledgement parse");
            var failure=GamePartyCueProtocol.Failed(nonce,"SWITCH");
            Check(GamePartyCueProtocol.TryParse(failure,out var fail) &&
                fail.Kind == GamePartyCueKind.Failed && fail.Nonce == nonce,"failed response parse");
            Check(!GamePartyCueProtocol.TryParse(ack.Replace("20","90"), out _),"reject bogus ready instrument");
            Check(!GamePartyCueProtocol.TryParse(prep+" trailing", out _),"reject extra prep tokens");
            Check(!GamePartyCueProtocol.TryParse(prep.Replace(nonce,"X"+nonce[1..]),out _),"reject invalid nonce");
            long target=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+8000;
            var start=GamePartyCueProtocol.Start(0,path,target,1000,"00000000","-",nonce);
            Check(GamePartyCueProtocol.TryParse(start,out var play) &&
                play.Kind==GamePartyCueKind.Start && play.Nonce==received.Nonce,
                "preparation and start are linked by nonce");
            Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop,out var stop) &&
                stop.Kind==GamePartyCueKind.Stop,"STOP backward compatible");
            Console.WriteLine("PASS: party instrument map, roster binding, prepare/ready/fail, nonce, START and STOP.");
        }
        finally { try { File.Delete(path); } catch { } }
    }
}
