using System;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;
using NunuWindboundBard.Core;

static void Check(bool ok, string name) { if (!ok) throw new Exception("FAILED: " + name); }
static bool Near(Vector3 a, Vector3 b, float tolerance = .03f) => Vector3.Distance(a,b) < tolerance;
const string nonce = "0123456789ABCDEF", roster = "1234ABCD";
var anchor = new Vector3(150.25f, 3.8f, -36.7f);
for (int count = 2; count <= 8; ++count)
{
    var saved = new StageFormationPreset { Name = "Test octet", TerritoryId = 339,
        Anchor = anchor, AnchorHeading = .68f };
    for (int i = 1; i <= count; ++i)
    {
        var point = anchor + new Vector3(i * 1.2f - 5, 0f, i % 3);
        var slot = StageFormationMath.Capture((ulong)(100+i), "Bard " + i,point, -.15f*i, anchor, .68f);
        saved.Members.Add(slot);
        Check(Near(StageFormationMath.Destination(slot,anchor,.68f),point),"roundtrip member position " + i);
        Check(MathF.Abs(StageFormationMath.WrapHeading(StageFormationMath.DesiredHeading(slot,.68f)+.15f*i))<.001f,
            "roundtrip member facing " + i);
    }
    StageFormationMath.Validate(saved);
    Check(saved.Members.Count==count,"number of saved bards");
}
var a = GamePartyCueProtocol.FormationCapture(nonce,roster,339);
Check(GamePartyCueProtocol.TryParse(a,out var cap) && cap.Kind == GamePartyCueKind.FormationCapture &&
    cap.FormationTerritory == 339, "capture command parsed");
var report=GamePartyCueProtocol.FormationPosition(nonce,339,anchor,1.3f);
Check(GamePartyCueProtocol.TryParse(report,out var pos) && pos.Kind==GamePartyCueKind.FormationPosition &&
    Near(pos.FormationPosition,anchor) && MathF.Abs(pos.FormationHeading-1.3f)<.001,"follower coordinate report");
var move = GamePartyCueProtocol.FormationMove(nonce,roster,8,339,anchor,-.5f);
Check(move.Length < 130 && GamePartyCueProtocol.TryParse(move,out var go) && go.TargetSlot==8 &&
    go.Kind==GamePartyCueKind.FormationMove && Near(go.FormationPosition,anchor),"targeted octet movement");
Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.FormationArrived(nonce),out var ok) &&
    ok.Kind==GamePartyCueKind.FormationArrived, "arrived reply");
Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.FormationFailed(nonce,"NAVFAIL"),out var fail) &&
    fail.Kind==GamePartyCueKind.FormationFailed, "failure reply");
Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.FormationStop(nonce),out var stop) &&
    stop.Kind==GamePartyCueKind.FormationStop, "formation stop");
Check(!GamePartyCueProtocol.TryParse(move+" EXTRA", out _),"reject unexpected args");
Check(!GamePartyCueProtocol.TryParse(move.Replace("1234ABCD","NOPE1234"),out _), "reject fake roster tag");
Check(!GamePartyCueProtocol.TryParse(move.Replace("339","0"),out _), "reject invalid territory");
Check(!GamePartyCueProtocol.TryParse(move.Replace("150.25","NaN"),out _), "reject NaN coordinate");
try { StageFormationMath.Capture(18,"Too distant",anchor+new Vector3(45,0,0),0,anchor,0);
      throw new Exception("FAILED far stage position wasn't rejected"); }
catch (InvalidOperationException) { }
Console.WriteLine("PASS: 2-8-member stage presets, positions/facing, stage capture, targeted move, stop, ACK and malformed input.");
