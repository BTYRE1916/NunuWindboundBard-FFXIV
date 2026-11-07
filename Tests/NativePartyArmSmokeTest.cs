using NunuWindboundBard.Core;
using System;

static class Program
{
    static void Check(bool ok, string what)
    {
        if (!ok) throw new Exception("Party ARM failure: " + what);
    }

    static void Main()
    {
        const string nonce = "0123456789ABCDEF";
        const string hash = "1122334455667788";
        const string roster = "AABBCCDD";
        string arm = GamePartyCueProtocol.NativeArmParty(nonce, hash, roster, true);
        string disarm = GamePartyCueProtocol.NativeArmParty(nonce, hash, roster, false);
        Check(arm.Length <= GamePartyCueProtocol.MaxChatCharacters, "compact party chat command");
        Check(GamePartyCueProtocol.TryParse(arm, out var a) && a.Kind == GamePartyCueKind.NativeArmAll &&
              a.ArmRequested && a.Hash == hash && a.RosterTag == roster && a.Nonce == nonce, "arm parse");
        Check(GamePartyCueProtocol.TryParse(disarm, out var b) && b.Kind == GamePartyCueKind.NativeArmAll &&
              !b.ArmRequested, "disarm parse");
        Check(!GamePartyCueProtocol.TryParse(arm + " garbage", out _), "reject extra fields");
        Check(!GamePartyCueProtocol.TryParse(arm.Replace(roster, "BADROST!"), out _), "reject invalid roster hash");
        Check(!GamePartyCueProtocol.TryParse(arm.Replace(nonce, "X123456789ABCDEF"), out _), "reject malformed nonce");
        foreach (int status in new[] {0, 1, 2, 3})
        {
            var ack = GamePartyCueProtocol.NativeArmAck(nonce, status);
            Check(GamePartyCueProtocol.TryParse(ack, out var parsed) &&
                  parsed.Kind == GamePartyCueKind.NativeArmReady && parsed.ArmReplyCode == status,
                  "arm ack status " + status);
        }
        Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.NativeArmFail(nonce, "WRONGMIDI"), out var fail)
              && fail.Kind == GamePartyCueKind.NativeArmFailed, "arm failure report");
        Check(!GamePartyCueProtocol.TryParse($"!windbound armok {nonce} 4", out _), "reject unknown ack");
        Check(!GamePartyCueProtocol.TryParse($"!windbound armfail {nonce} BAD!", out _), "reject unknown failure format");
        var good = NativePartyArmPlan.Evaluate(true, true, true, false, false, false, hash, hash, true, 3);
        Check(good.Decision == PartyArmDecision.Armed, "matching file and active parts are armable");
        Check(NativePartyArmPlan.Evaluate(true, true, true, false, false, false, hash, hash, true, 0).Decision
              == PartyArmDecision.Silent, "skip silent Bard");
        Check(NativePartyArmPlan.Evaluate(true, true, true, false, false, false, hash, roster, true, 3).Decision
              == PartyArmDecision.Reject, "reject wrong MIDI");
        Check(NativePartyArmPlan.Evaluate(true, true, true, false, true, true, hash, "", false, 0).Decision
              == PartyArmDecision.Wait, "wait for a requested remote MIDI to finish loading");
        Check(NativePartyArmPlan.Evaluate(true, true, true, false, false, false, hash, "", false, 0).Decision
              == PartyArmDecision.Reject, "reject missing file when no load is pending");
        Check(NativePartyArmPlan.Evaluate(true, true, false, false, false, false, hash, hash, true, 3).Decision
              == PartyArmDecision.Reject, "honour explicit follower opt-out");
        Check(NativePartyArmPlan.Evaluate(true, false, true, false, false, false, hash, hash, true, 3).Decision
              == PartyArmDecision.Reject, "reject solo/non-follower mode");
        Check(NativePartyArmPlan.Evaluate(true, true, true, true, false, false, hash, hash, true, 3).Decision
              == PartyArmDecision.Reject, "do not ARM while playing");
        Check(NativePartyArmPlan.Evaluate(false, true, true, false, false, false, hash, "", false, 0).Decision
              == PartyArmDecision.Disarmed, "DISARM even if no MIDI is loaded");
        Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop, out var stop) &&
              stop.Kind == GamePartyCueKind.Stop, "backward compatible STOP");
        Console.WriteLine("PASS: group ARM command + ACK validation, matching MIDI/role/consent, silent parts, late loading and DISARM.");
    }
}
