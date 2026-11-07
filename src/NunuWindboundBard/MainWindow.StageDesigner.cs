using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Bard Toolbox-style top-down visual formation planner. All movement is still
/// performed by the existing verified party command + vnavmesh recall path.
/// Dragging a dot changes a local draft ONLY: no client moves until SET is clicked.
/// </summary>
public sealed partial class MainWindow
{
    private VisualStageDesigner? stageVisualDraft;
    private int stageVisualSelectedSpot = -1;
    private int stageVisualDragSpot = -1;
    private bool stageVisualSnapToGrid = true;
    private string stageVisualStatus = "Drag the performer dots to plan your stage. No Bard moves until you press SET POSITIONS.";
    private static readonly Vector4[] StageColours = {
        new(.97f,.47f,.70f,1f), new(.46f,.80f,.96f,1f), new(.44f,.90f,.65f,1f),
        new(1f,.75f,.36f,1f), new(.79f,.59f,1f,1f), new(.95f,.62f,.41f,1f),
        new(.52f,.86f,.82f,1f), new(.96f,.59f,.84f,1f)
    };

    private void NewVisualStage()
    {
        try
        {
            var roster = StageRoster();
            var (anchor, heading) = StageLocalPose();
            stageVisualDraft = VisualStageDesigner.New(stageName.Trim(), StageTerritory, anchor, heading,
                roster.Select(p => (p.ContentId, p.Name)).ToList());
            stageVisualSelectedSpot = 0;
            stageVisualDragSpot = -1;
            stageVisualStatus = Loc.T("New diagram created with one dot per Bard. Drag the dots and assign performers to spots.");
        }
        catch (Exception ex) { stageVisualStatus = Loc.T("Cannot create a visual stage: ") + ex.Message; }
    }

    private void LoadVisualStage()
    {
        try
        {
            var preset = SelectedStage() ?? throw new InvalidOperationException("Select a saved formation first.");
            stageVisualDraft = VisualStageDesigner.FromPreset(preset);
            stageVisualSelectedSpot = 0;
            stageVisualDragSpot = -1;
            stageVisualStatus = Loc.T("Saved formation loaded into the stage diagram. Changes are local until saved or SET.");
        }
        catch (Exception ex) { stageVisualStatus = Loc.T("Could not load stage diagram: ") + ex.Message; }
    }

    private void SaveVisualStage(bool moveParty, bool forceNew)
    {
        try
        {
            if (!StageLeader(out string why) || !StageSafe(out why)) throw new InvalidOperationException(why);
            if (capturingStage is not null || recallingStage is not null)
                throw new InvalidOperationException("Finish or stop the previous formation movement first.");
            var draft = stageVisualDraft ?? throw new InvalidOperationException("Create a stage layout first.");
            var roster = StageRoster();
            if (!draft.MatchesRoster(roster.Select(m => m.ContentId).ToList()))
                throw new InvalidOperationException("The party lineup changed. Create a new diagram or load a matching preset.");
            if (StageTerritory != draft.TerritoryId)
                throw new InvalidOperationException("The stage diagram belongs to another territory. Use Re-anchor at my position.");
            var (here, _) = StageLocalPose();
            if (moveParty && Vector3.Distance(here, draft.Anchor) > 20f)
                throw new InvalidOperationException("The saved anchor is over 20m away. Use Re-anchor at my position or approach the original stage.");
            cfg.StageFormations ??= new();
            StageFormationPreset? original = forceNew || string.IsNullOrWhiteSpace(draft.SourcePresetId)
                ? null : cfg.StageFormations.FirstOrDefault(x => x.Id == draft.SourcePresetId);
            if (original is null && cfg.StageFormations.Count >= 32)
                throw new InvalidOperationException("The formation library already contains 32 presets.");
            // Build deep copies to avoid mutating an existing preset until all validation passes.
            var saved = draft.Build(original?.Id ?? "");
            if (original is null) cfg.StageFormations.Add(saved);
            else
            {
                int index = cfg.StageFormations.IndexOf(original);
                cfg.StageFormations[index] = saved;
            }
            draft.SourcePresetId = saved.Id;
            cfg.SelectedStageFormationId = selectedStageId = saved.Id;
            stageName = saved.Name;
            cfg.Save();
            stageVisualStatus = forceNew ? Loc.T("Saved a new stage diagram preset.") :
                Loc.T("Stage diagram saved with the current performer-to-spot assignments.");
            if (moveParty)
            {
                // Uses the same territory, roster, leader, proximity and navigator checks
                // as the legacy SAVE CURRENT POSITIONS workflow.
                RecallStageFormation();
                stageVisualStatus = Loc.T("SET POSITIONS requested. ") + stageStatus;
            }
        }
        catch (Exception ex) { stageVisualStatus = Loc.T("Could not set stage positions: ") + ex.Message; }
    }

    private void ReanchorVisualStage()
    {
        try
        {
            if (!StageLeader(out string why) || !StageSafe(out why)) throw new InvalidOperationException(why);
            var draft = stageVisualDraft ?? throw new InvalidOperationException("No visual stage draft.");
            var (position, facing) = StageLocalPose();
            // Deliberate button: never re-anchor just because the character walked.
            draft.Reanchor(StageTerritory, position, facing);
            stageVisualStatus = Loc.T("Stage centre changed to the conductor's current position and facing.");
        }
        catch (Exception ex) { stageVisualStatus = Loc.T("Cannot re-anchor stage: ") + ex.Message; }
    }

    private void DrawVisualStageDesigner()
    {
        ImGui.Separator();
        ImGui.TextColored(new Vector4(.98f, .61f, .84f, 1f), Loc.T("VISUAL STAGE DESIGNER — DRAG YOUR BARDS"));
        ImGui.TextWrapped(Loc.T("Arrange each Bard by dragging a numbered dot on the square stage. Select a dot to choose which party member occupies that spot. SET positions makes everyone walk there."));
        if (stageVisualDraft is null && Plugin.Objects.LocalPlayer is not null)
        {
            var currentRoster = StageRoster();
            var selected = SelectedStage();
            if (selected is not null && selected.TerritoryId == StageTerritory &&
                selected.Members.Count == currentRoster.Count &&
                selected.Members.Select(m => m.ContentId).OrderBy(id => id)
                    .SequenceEqual(currentRoster.Select(m => m.ContentId).OrderBy(id => id)))
                LoadVisualStage();
            else if (currentRoster.Count is >= 2 and <= 15) NewVisualStage();
        }
        if (stageVisualDraft is null)
        {
            if (ImGui.Button(Loc.T("CREATE VISUAL STAGE FROM PARTY"))) NewVisualStage();
            ImGui.TextDisabled(Loc.T("A visual stage needs a party of 2–15 performers and a logged-in conductor."));
            return;
        }
        var draft = stageVisualDraft;
        if (ImGui.Button(Loc.T("NEW STAGE DIAGRAM"))) { NewVisualStage(); draft = stageVisualDraft!; }
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("LOAD SAVED FORMATION INTO DIAGRAM"))) { LoadVisualStage(); draft = stageVisualDraft!; }
        if (draft is null) return;
        var roster = StageRoster();
        if (!draft.MatchesRoster(roster.Select(p => p.ContentId).ToList()))
            ImGui.TextColored(new Vector4(1f,.67f,.40f,1f),
                Loc.T("The current party differs from this diagram; create a new diagram before SET."));

        string name = draft.Name;
        ImGui.SetNextItemWidth(290f);
        if (ImGui.InputText(Loc.T("Diagram name##visualStageName"), ref name, 64)) draft.Name = name;
        float width = draft.StageWidthMetres;
        ImGui.SetNextItemWidth(290f);
        if (ImGui.SliderFloat(Loc.T("Stage width (metres)##visualStageWidth"), ref width, 8f, 64f, "%.0f m"))
            draft.SetWidth(width);
        ImGui.SameLine();
        ImGui.Checkbox(Loc.T("Snap 0.5m##visualStageSnap"), ref stageVisualSnapToGrid);
        ImGui.TextDisabled(Loc.T("Quick formation shapes:"));
        if (ImGui.SmallButton(Loc.T("Two rows##visualShapeRows"))) draft.ApplyTemplate(VisualStageTemplate.TwoRows);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Straight line##visualShapeLine"))) draft.ApplyTemplate(VisualStageTemplate.Line);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Arc##visualShapeArc"))) draft.ApplyTemplate(VisualStageTemplate.Arc);
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Circle##visualShapeCircle"))) draft.ApplyTemplate(VisualStageTemplate.Circle);

        DrawVisualStageCanvas(draft);
        if (stageVisualSelectedSpot >= 0 && stageVisualSelectedSpot < draft.Members.Count)
        {
            var selected = draft.Members[stageVisualSelectedSpot];
            ImGui.TextUnformatted($"{Loc.T("Selected spot")} {stageVisualSelectedSpot + 1}: {selected.Name}");
            if (ImGui.BeginCombo(Loc.T("Assign Bard to selected spot##visualStageBard"), selected.Name))
            {
                foreach (var person in roster)
                    if (ImGui.Selectable(person.Name + "##visualBard" + person.ContentId,
                        selected.ContentId == person.ContentId))
                    {
                        draft.AssignCharacter(stageVisualSelectedSpot, person.ContentId);
                        stageVisualStatus = Loc.T("Performers swapped between spots; no duplicates are assigned.");
                    }
                ImGui.EndCombo();
            }
            float headingDegrees = selected.HeadingOffset * 180f / MathF.PI;
            ImGui.SetNextItemWidth(290f);
            if (ImGui.SliderFloat(Loc.T("Facing guide (degrees)##visualStageFacing"),
                ref headingDegrees, -180f, 180f, "%.0f°"))
                draft.SetHeading(stageVisualSelectedSpot, headingDegrees * MathF.PI / 180f);
            ImGui.TextDisabled(Loc.T("Facing is a visual guide only; vnavmesh walks to the selected dot."));
        }

        if (ImGui.TreeNode(Loc.T("Show all performer spots##visualSpotList")))
        {
            for (int i = 0; i < draft.Members.Count; i++)
            {
                var m = draft.Members[i];
                if (ImGui.Selectable($"{i + 1:00}  {m.Name}  X:{m.Offset.X:+0.0;-0.0;0}  " +
                    $"Z:{m.Offset.Z:+0.0;-0.0;0}##visualList{i}", stageVisualSelectedSpot == i))
                    stageVisualSelectedSpot = i;
            }
            ImGui.TreePop();
        }
        ImGui.TextDisabled(Loc.T("The stage square is centred on the conductor's saved anchor and heading. Top is backstage; bottom is audience-side."));
        if (ImGui.Button(Loc.T("RE-ANCHOR STAGE AT MY POSITION"))) ReanchorVisualStage();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("SAVE DIAGRAM AS NEW PRESET"))) SaveVisualStage(moveParty: false, forceNew: true);
        if (ImGui.Button(Loc.T("SET POSITIONS & MOVE PARTY##visualStageApply")))
            SaveVisualStage(moveParty: true, forceNew: false);
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("SAVE CURRENT DIAGRAM##visualStageSave")))
            SaveVisualStage(moveParty: false, forceNew: false);
        ImGui.TextWrapped(stageVisualStatus);
        ImGui.TextDisabled(Loc.T("Moves only after SET. Requires vnavmesh on each client and follower stage-movement permission. No teleport or music start."));
    }

    private void DrawVisualStageCanvas(VisualStageDesigner draft)
    {
        float side = Math.Clamp(ImGui.GetContentRegionAvail().X - 12f, 205f, 355f);
        Vector2 top = ImGui.GetCursorScreenPos();
        Vector2 bottom = top + new Vector2(side, side);
        ImGui.InvisibleButton("##WindboundVisualStageCanvas", new Vector2(side, side));
        bool hovered = ImGui.IsItemHovered();
        Vector2 mouse = ImGui.GetMousePos() - top;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            stageVisualDragSpot = -1;
            float nearest = 21f * 21f;
            for (int i = 0; i < draft.Members.Count; i++)
            {
                Vector2 spot = draft.ToCanvas(draft.Members[i].Offset, side);
                float distanceSquared = Vector2.DistanceSquared(spot, mouse);
                if (distanceSquared < nearest) { nearest = distanceSquared; stageVisualDragSpot = i; }
            }
            if (stageVisualDragSpot >= 0) stageVisualSelectedSpot = stageVisualDragSpot;
        }
        if (stageVisualDragSpot >= 0 && ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            Vector2 stageMetres = draft.FromCanvas(mouse, side);
            draft.MoveDot(stageVisualDragSpot, stageMetres.X, stageMetres.Y, stageVisualSnapToGrid);
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) stageVisualDragSpot = -1;

        var draw = ImGui.GetWindowDrawList();
        uint background = ImGui.ColorConvertFloat4ToU32(new Vector4(.075f,.08f,.14f,.98f));
        uint border = ImGui.ColorConvertFloat4ToU32(new Vector4(.60f,.62f,.72f,1f));
        uint minor = ImGui.ColorConvertFloat4ToU32(new Vector4(.21f,.25f,.34f,1f));
        uint major = ImGui.ColorConvertFloat4ToU32(new Vector4(.42f,.48f,.60f,1f));
        draw.AddRectFilled(top, bottom, background, 6f);
        // Four lines draw the border; these calls match Windbound's existing
        // Soulweaver and MIDI-roll ImDrawList bindings.
        draw.AddLine(top, new Vector2(bottom.X, top.Y), border, 2f);
        draw.AddLine(new Vector2(bottom.X, top.Y), bottom, border, 2f);
        draw.AddLine(bottom, new Vector2(top.X, bottom.Y), border, 2f);
        draw.AddLine(new Vector2(top.X, bottom.Y), top, border, 2f);
        int gridLines = (int)draft.StageWidthMetres;
        for (int i = 1; i < gridLines; i++)
        {
            float at = i / draft.StageWidthMetres * side;
            bool centre = MathF.Abs(at - side / 2f) < side / draft.StageWidthMetres / 2f;
            draw.AddLine(top + new Vector2(at, 0f), top + new Vector2(at, side), centre ? major : minor);
            draw.AddLine(top + new Vector2(0f, at), top + new Vector2(side, at), centre ? major : minor);
        }
        draw.AddLine(top + new Vector2(side / 2f, 0), top + new Vector2(side / 2f, side), major, 1.3f);
        draw.AddLine(top + new Vector2(0, side / 2f), top + new Vector2(side, side / 2f), major, 1.3f);

        for (int i = 0; i < draft.Members.Count; i++)
        {
            var member = draft.Members[i];
            Vector2 xy = top + draft.ToCanvas(member.Offset, side);
            uint colour = ImGui.ColorConvertFloat4ToU32(StageColours[i % StageColours.Length]);
            bool selected = i == stageVisualSelectedSpot;
            if (selected)
                draw.AddRectFilled(xy - new Vector2(17f, 17f), xy + new Vector2(17f, 17f), border, 17f);
            float radius = selected ? 12.5f : 11f;
            draw.AddRectFilled(xy - new Vector2(radius, radius), xy + new Vector2(radius, radius), colour, radius);
            draw.AddText(xy + new Vector2(i + 1 >= 10 ? -8f : -4f, -7f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(.04f,.05f,.1f,1f)), (i + 1).ToString());
            string label = member.Name.Length <= 11 ? member.Name : member.Name[..10] + "…";
            draw.AddText(xy + new Vector2(-Math.Min(40f, label.Length * 3.4f), 12f),
                ImGui.ColorConvertFloat4ToU32(new Vector4(.94f,.94f,.97f,1f)), label);
        }
        ImGui.TextDisabled(Loc.T("Top: BACKSTAGE                        Bottom: AUDIENCE"));
    }
}
