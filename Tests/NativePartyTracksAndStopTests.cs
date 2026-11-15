using System;
using System.Collections.Generic;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    // Test-only MIDI key: compile the *real* protocol/encoding source, no FFXIV dependency.
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

public static class NativePartyTracksAndStopTests
{
    private static void Must(bool ok, string message)
    {
        if (!ok) throw new Exception("Native party track/STOP regression: " + message);
    }

    public static void Main()
    {
        var roster = new ulong[] { 78, 23, 156, 44, 99, 201, 300, 1001 }; // octet
        var lanes = Enumerable.Range(0, 24).Select(i => new MidiLaneKey(i, i % 9)).ToList();
        var assigned = new Dictionary<MidiLaneKey, ulong>();
        for (int i = 0; i < lanes.Count; i++) assigned[lanes[i]] = roster[i % roster.Length];
        string packed = OneClickEnsembleCue.EncodeAssignments(lanes, assigned, roster, out string rosterTag,
            explicitMap: true);
        Must(OneClickEnsembleCue.TryDecodeAssignments(packed, rosterTag, lanes,
            roster.Reverse().ToArray(), out var decoded, out _), "Octet assignments decode independent of party list order");
        Must(assigned.Count == decoded.Count && assigned.All(p => decoded[p.Key] == p.Value),
            "24 lanes round-trip across all 8 party members");
        string cue = GamePartyCueProtocol.NativeSetTracks("AABBCCDDEEFF1122", "0123456789ABCDEF",
            rosterTag, packed);
        Must(cue.Length <= GamePartyCueProtocol.MaxChatCharacters, "FFXIV party-chat length bound");
        Must(GamePartyCueProtocol.TryParse(cue, out var parsed) && parsed.Kind == GamePartyCueKind.NativeSetTracks &&
            parsed.Hash == "AABBCCDDEEFF1122" && parsed.RosterTag == rosterTag &&
            parsed.PackedAssignments == packed && parsed.Nonce == "0123456789ABCDEF", "track command parses");
        Must(!GamePartyCueProtocol.TryParse(cue + " extraneous", out _), "additional tokens rejected");
        Must(!GamePartyCueProtocol.TryParse(cue.Replace("AABBCCDDEEFF1122", "ZZBBCCDDEEFF1122"), out _),
            "invalid SHA-256 prefix rejected");
        Must(!GamePartyCueProtocol.TryParse(cue.Replace("0123456789ABCDEF", "0123456789ABCDEZ"), out _),
            "invalid nonce rejected");
        Must(!OneClickEnsembleCue.TryDecodeAssignments(packed, rosterTag, lanes, new ulong[]{1,2,3,4,5,6,7,8},
            out _, out _), "mismatched party roster rejected");

        var silent = OneClickEnsembleCue.EncodeAssignments(lanes,
            new Dictionary<MidiLaneKey, ulong>(), roster, out string silentTag, explicitMap: true);
        Must(silent != "-", "explicit all-silent track map must not fall back to manual assignments");
        Must(OneClickEnsembleCue.TryDecodeAssignments(silent, silentTag, lanes, roster,
            out var noParts, out _) && noParts.Count == 0, "all-silent round-trip");
        var legacyEmpty = OneClickEnsembleCue.EncodeAssignments(lanes,
            new Dictionary<MidiLaneKey,ulong>(), roster, out _, explicitMap: false);
        Must(legacyEmpty == "-", "legacy file sync no-map behavior preserved");

        string ack = GamePartyCueProtocol.NativeTracksReady("0123456789ABCDEF");
        string nack = GamePartyCueProtocol.NativeTracksFailed("0123456789ABCDEF", "TRACKMAP");
        Must(GamePartyCueProtocol.TryParse(ack, out var ready) && ready.Kind == GamePartyCueKind.NativeTracksReady,
            "track ready acknowledgement");
        Must(GamePartyCueProtocol.TryParse(nack, out var failed) && failed.Kind == GamePartyCueKind.NativeTracksFailed,
            "track fail acknowledgement");
        string stop = GamePartyCueProtocol.NativeStopAndUnequip("0123456789ABCDEF");
        Must(GamePartyCueProtocol.TryParse(stop, out var stow) && stow.Kind == GamePartyCueKind.NativeStopUnequip,
            "leader STOP+unequip command");
        Must(!GamePartyCueProtocol.TryParse(stop.Replace("0123456789ABCDEF", "00000NOTHEX00000"), out _),
            "invalid stow nonce rejected");
        Must(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop, out var legacyStop) &&
            legacyStop.Kind == GamePartyCueKind.Stop, "legacy stop preserved");
        Console.WriteLine("PASS: octet assignments, explicit all-silent, roster SHA, track ack/fail, STOP+unequip, and legacy compatibility.");
    }
}
