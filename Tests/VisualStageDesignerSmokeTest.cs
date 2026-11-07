using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using NunuWindboundBard.Core;

static void Check(bool ok, string message) { if (!ok) throw new Exception("Visual Stage FAILED: " + message); }
static bool Near(Vector3 a, Vector3 b, float tolerance = .06f) => Vector3.Distance(a, b) < tolerance;

var anchor = new Vector3(102.3f, 4.2f, -77.6f);
for (int count = 2; count <= 15; count++)
{
    var roster = Enumerable.Range(1, count).Select(i => ((ulong)(1000 + i), "Bard " + i)).ToList();
    var draft = VisualStageDesigner.New("Test Stage", 339, anchor, .6f, roster);
    Check(draft.Members.Count == count, "New stage dots for " + count + " performers");
    Check(draft.MatchesRoster(roster.Select(p => p.Item1).ToArray()), "Roster match for " + count);
    draft.SetWidth(20);
    foreach (var shape in Enum.GetValues<VisualStageTemplate>())
    {
        draft.ApplyTemplate(shape);
        Check(draft.Members.All(m => MathF.Abs(m.Offset.X) <= 10f &&
            MathF.Abs(m.Offset.Z) <= 10f), "Template dots fit the stage for " + count);
    }
    for (int i = 0; i < count; i++)
    {
        var pixel = draft.ToCanvas(draft.Members[i].Offset, 300f);
        var metres = draft.FromCanvas(pixel, 300f);
        Check(MathF.Abs(metres.X - draft.Members[i].Offset.X) < .03f &&
              MathF.Abs(metres.Y - draft.Members[i].Offset.Z) < .03f, "Canvas/world roundtrip");
    }
    var first = draft.Members[0].ContentId;
    var second = draft.Members[1].ContentId;
    draft.AssignCharacter(0, second);
    Check(draft.Members[0].ContentId == second && draft.Members[1].ContentId == first,
        "Swapping names by spot is deterministic");
    Check(draft.Members.Select(m => m.ContentId).Distinct().Count() == count, "No duplicate performers");
    draft.MoveDot(0, 3.14f, -2.89f, snapHalfMetre: true);
    Check(MathF.Abs(draft.Members[0].Offset.X - 3f) < .01f &&
          MathF.Abs(draft.Members[0].Offset.Z + 3f) < .01f, "Half metre snapping");
    draft.MoveDot(0, 70f, -90f, snapHalfMetre: false);
    Check(draft.Members[0].Offset.X == 10 && draft.Members[0].Offset.Z == -10, "Stage limits clamp drag");
    draft.SetHeading(0, -.9f);
    var saved = draft.Build();
    StageFormationMath.Validate(saved);
    Check(saved.Members[0].HeadingOffset == -.9f, "Heading guide saved");
    Check(Near(StageFormationMath.Destination(saved.Members[0], saved.Anchor, saved.AnchorHeading),
        anchor + new Vector3(MathF.Cos(.6f)*10f - MathF.Sin(.6f)*-10f, 0,
           MathF.Sin(.6f)*10f + MathF.Cos(.6f)*-10f)), "Old recall destination compatible");
    var old = VisualStageDesigner.FromPreset(saved);
    old.MoveDot(0, 1f, 2f, false);
    Check(saved.Members[0].Offset.X == 10f, "Edit does not modify saved preset until Save");
    old.Reanchor(339, anchor + new Vector3(1, 0, 2), -.4f);
    Check(Near(old.Build().Anchor, anchor + new Vector3(1,0,2)), "Explicit re-anchor");
    // Persisted preset still contains all slot assignments and offsets.
    var json = JsonSerializer.Serialize(saved, new JsonSerializerOptions { IncludeFields = true });
    var restored = JsonSerializer.Deserialize<StageFormationPreset>(json,
        new JsonSerializerOptions { IncludeFields = true })!;
    Check(restored.Members.Count == count, "Persisting " + count + " Bard positions");
    StageFormationMath.Validate(restored);
}

var duo = VisualStageDesigner.New("Duet", 339, anchor, 0f,
    new[] { (1001UL, "Nunu"), (1002UL, "Edea") });
duo.SetWidth(64f);
duo.MoveDot(0, 30f, 30f, false);
Check(duo.Members[0].Offset.Length() <= StageFormationMath.MaxCaptureRadius + .001f,
    "Wide-stage corner remains inside safe saved radius");
var px = duo.FromCanvas(new Vector2(150, 150), 300);
Check(Math.Abs(px.X) < .001 && Math.Abs(px.Y) < .001, "Canvas centre anchored on conductor");
try { duo.AssignCharacter(0, 999); throw new Exception("Unassigned member not rejected"); }
catch (InvalidOperationException) { }
try { duo.MoveDot(99, 0, 0, false); throw new Exception("Invalid dot not rejected"); }
catch (ArgumentOutOfRangeException) { }
try { duo.MoveDot(0, float.NaN, 0, false); throw new Exception("NaN not rejected"); }
catch (ArgumentOutOfRangeException) { }
Console.WriteLine("PASS Visual Stage: 2–15 Bards, drag, stage scale, swap performer, save/reload, re-anchor and existing movement geometry.");
