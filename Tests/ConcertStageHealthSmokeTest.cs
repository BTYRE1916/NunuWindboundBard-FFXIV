using System;
using System.Collections.Generic;
using NunuWindboundBard.Core;

namespace NunuWindboundBard.Core
{
    public readonly record struct MidiLaneKey(int Track, int Channel);
}

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
    public static void Main()
    {
        string hash = "1234567890ABCDEF";
        var flags = ConcertStageFlags.NativeEnabled | ConcertStageFlags.MidiMatches |
                    ConcertStageFlags.TracksAssigned | ConcertStageFlags.Keyboard37 | ConcertStageFlags.OutputArmed;
        var state = new ConcertStageSnapshot(hash, flags, 20, 2);
        Check(ConcertStageHealth.Assess(state, hash, 20, 2, false).Ready, "complete readiness");
        Check(!ConcertStageHealth.Assess(state with { Flags = flags & ~ConcertStageFlags.Keyboard37 }, hash, 20, 2, false).Ready, "keyboard");
        Check(!ConcertStageHealth.Assess(state, "AAAAAAAAAAAAAAAA", 20, 2, false).Ready, "MIDI hash");
        Check(!ConcertStageHealth.Assess(state, hash, 3, 2, false).Ready, "instrument");
        Check(!ConcertStageHealth.Assess(state, hash, 20, 1, false).Ready, "lane count");
        Check(!ConcertStageHealth.Assess(state, hash, 20, 2, true).Ready, "stale report");
        Check(ConcertStageHealth.Assess(state with { EnabledLanes = 0, CurrentInstrument = 0 }, hash, 0, 0, false).Ready, "silent conductor");
        const string nonce = "0123456789ABCDEF";
        string request = GamePartyCueProtocol.StageProbe(nonce, hash);
        Check(GamePartyCueProtocol.TryParse(request, out var requestCue) && requestCue.Kind == GamePartyCueKind.StageProbe, "stage probe");
        string response = GamePartyCueProtocol.StageReport(nonce, hash, (int)flags, 20, 2);
        Check(GamePartyCueProtocol.TryParse(response, out var report) && report.Kind == GamePartyCueKind.StageReport &&
              report.StageFlags == (int)flags && report.StageInstrument == 20 && report.StageLanes == 2, "status round-trip");
        Check(!GamePartyCueProtocol.TryParse(response + " EXTRA", out _), "reject extra tokens");
        Check(!GamePartyCueProtocol.TryParse(response.Replace(" 20 2", " 255 2"), out _), "reject bad instrument");
        var assignment = new Dictionary<MidiLaneKey, ulong> { [new MidiLaneKey(1, 0)] = 42, [new MidiLaneKey(2, 1)] = 44 };
        Check(ConcertStageHealth.ExpectedLaneCount(assignment, new[] {new MidiLaneKey(1, 0),new MidiLaneKey(2, 1)}, 44) == 1, "expected parts");
        Console.WriteLine("PASS: Concert Stage preflight, silent conductor, file fingerprint, status message, invalid reports.");
    }
}
