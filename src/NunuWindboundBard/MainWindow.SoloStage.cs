using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Local single-Bard stage positioning. No party, roster, conductor or network cue
/// is required. Clicks are deliberate: no movement on login, load or MIDI change.
/// </summary>
public sealed partial class MainWindow
{
    private bool soloStageWalking;
    private bool soloStageFacingPending;
    private bool soloStageSnap = true;
    private bool soloStageDragging;
    private bool soloStageDirty;
    private string soloStageStatus = "Mark a stage centre, then choose your spot.";

    private void DrawSoloStage()
    {
        if (!ImGui.CollapsingHeader(Loc.T("SOLO STAGE — WALK & FACE (NO PARTY NEEDED)"))) return;
        ImGui.TextWrapped(Loc.T("Stand near the middle of your stage, face the audience, and mark the centre. " +
            "Drag your Bard's dot to a spot, set her facing and walk there. Zero degrees means forward toward the audience."));

        if (ImGui.Button(Loc.T("MARK / RE-ANCHOR SOLO STAGE AT MY POSITION")))
        {
            try
            {
                if (!StageSafe(out var reason)) throw new InvalidOperationException(reason);
                if (stageNavigator.Active || stageFacing.Active || soloStageWalking ||
                    soloStageFacingPending || capturingStage is not null || recallingStage is not null)
                    throw new InvalidOperationException("Finish or stop stage movement before re-anchoring.");
                var (position, rotation) = StageLocalPose();
                cfg.SoloStage = SoloStageLayout.At(StageTerritory, position, rotation);
                cfg.Save();
                soloStageStatus = "Solo stage centre captured. Facing forward means the way you were looking when you marked it.";
            }
            catch (Exception ex) { soloStageStatus = ex.Message; }
        }
        var layout = cfg.SoloStage;
        if (layout is null)
        {
            ImGui.TextDisabled(Loc.T("No solo stage saved on this client."));
            ImGui.TextWrapped(soloStageStatus);
            return;
        }
        if (!layout.IsValid())
        {
            ImGui.TextWrapped(Loc.T("Saved solo stage is invalid. Mark a new stage centre."));
            return;
        }
        if (layout.TerritoryId != StageTerritory)
            ImGui.TextColored(new Vector4(1f,.68f,.39f,1f),
                Loc.T("This solo layout was saved in another area. Re-anchor here before walking."));

        string name = layout.Name;
        ImGui.SetNextItemWidth(235f);
        if (ImGui.InputText(Loc.T("Solo stage name##soloStageName"), ref name, 64))
        {
            layout.Name = name;
            cfg.Save();
        }
        float width = layout.WidthMetres;
        ImGui.SetNextItemWidth(235f);
        if (ImGui.SliderFloat(Loc.T("Stage width##soloWidth"), ref width, 8f, 40f, "%.0f m"))
        {
            layout.WidthMetres = width;
            layout.SetSpot(layout.Offset.X, layout.Offset.Z, false);
            cfg.Save();
        }
        ImGui.Checkbox(Loc.T("Snap 0.5 metres##soloSnap"), ref soloStageSnap);
        DrawSoloStageCanvas(layout);

        float x = layout.Offset.X, depth = layout.Offset.Z;
        float half = layout.WidthMetres / 2f;
        ImGui.SetNextItemWidth(235f);
        if (ImGui.SliderFloat(Loc.T("Across stage (left / right)##soloX"), ref x, -half, half, "%.1f m"))
        {
            layout.SetSpot(x, depth, soloStageSnap);
            cfg.Save();
        }
        ImGui.SetNextItemWidth(235f);
        if (ImGui.SliderFloat(Loc.T("Stage depth (back / front)##soloZ"), ref depth, -half, half, "%.1f m"))
        {
            layout.SetSpot(layout.Offset.X, depth, soloStageSnap);
            cfg.Save();
        }
        float degrees = layout.FacingOffset * 180f / MathF.PI;
        ImGui.SetNextItemWidth(235f);
        if (ImGui.SliderFloat(Loc.T("Facing from forward##soloFace"), ref degrees, -180f, 180f, "%.0f deg"))
        {
            layout.FacingOffset = StageFormationMath.WrapHeading(degrees * MathF.PI / 180f);
            cfg.Save();
        }
        ImGui.SameLine();
        if (ImGui.SmallButton(Loc.T("Face audience##soloFront")))
        {
            layout.FacingOffset = 0f;
            cfg.Save();
        }
        ImGui.TextDisabled(Loc.T("The facing arrow on the map is the direction your character will turn."));

        if (ImGui.Button(Loc.T("WALK TO SOLO SPOT & FACE##soloGo"))) StartSoloStageWalk();
        ImGui.SameLine();
        if (ImGui.Button(Loc.T("FACE FORWARD NOW##soloFaceNow"))) StartSoloStageFacingOnly();
        if (ImGui.Button(Loc.T("STOP SOLO STAGE MOVEMENT##soloStop"))) StopSoloStage();
        ImGui.TextWrapped(soloStageStatus);
        ImGui.TextDisabled(Loc.T("Walking requires vnavmesh on this client. Facing alone does not. " +
            "Movement is never automatic on song load or login."));
    }

    private void DrawSoloStageCanvas(SoloStageLayout layout)
    {
        float side = Math.Clamp(ImGui.GetContentRegionAvail().X - 12f, 195f, 305f);
        Vector2 top = ImGui.GetCursorScreenPos();
        Vector2 bottom = top + new Vector2(side, side);
        ImGui.InvisibleButton("##WindboundSoloStageCanvas", new Vector2(side, side));
        Vector2 mouse = ImGui.GetMousePos() - top;
        Vector2 dot = new Vector2((layout.Offset.X / layout.WidthMetres + .5f) * side,
            (layout.Offset.Z / layout.WidthMetres + .5f) * side);
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left) &&
            Vector2.DistanceSquared(mouse, dot) < 27f * 27f)
            soloStageDragging = true;
        if (soloStageDragging && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            float x = (Math.Clamp(mouse.X / side, 0f, 1f) - .5f) * layout.WidthMetres;
            float z = (Math.Clamp(mouse.Y / side, 0f, 1f) - .5f) * layout.WidthMetres;
            layout.SetSpot(x, z, soloStageSnap);
            soloStageDirty = true;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            soloStageDragging = false;
            if (soloStageDirty) { cfg.Save(); soloStageDirty = false; }
        }
        var draw = ImGui.GetWindowDrawList();
        uint dark = ImGui.ColorConvertFloat4ToU32(new Vector4(.075f,.08f,.14f,.99f));
        uint grid = ImGui.ColorConvertFloat4ToU32(new Vector4(.34f,.38f,.51f,1f));
        uint pink = ImGui.ColorConvertFloat4ToU32(new Vector4(.98f,.49f,.73f,1f));
        uint white = ImGui.ColorConvertFloat4ToU32(new Vector4(.92f,.94f,.98f,1f));
        draw.AddRectFilled(top, bottom, dark, 5f);
        for (int i = 0; i <= 10; i++)
        {
            float offset = side * i / 10f;
            draw.AddLine(top + new Vector2(offset, 0), top + new Vector2(offset, side), grid);
            draw.AddLine(top + new Vector2(0, offset), top + new Vector2(side, offset), grid);
        }
        Vector2 centre = top + new Vector2(side / 2f, side / 2f);
        draw.AddLine(centre + new Vector2(-10, 0), centre + new Vector2(10, 0), white);
        draw.AddLine(centre + new Vector2(0, -10), centre + new Vector2(0, 10), white);
        Vector2 point = top + new Vector2((layout.Offset.X / layout.WidthMetres + .5f) * side,
            (layout.Offset.Z / layout.WidthMetres + .5f) * side);
        draw.AddRectFilled(point - new Vector2(12,12), point + new Vector2(12,12), pink, 12f);
        Vector2 facingVector = new(MathF.Sin(layout.FacingOffset), MathF.Cos(layout.FacingOffset));
        draw.AddLine(point, point + facingVector * 24f, white, 3f);
        draw.AddText(point + new Vector2(-5f, -7f), dark, "1");
        ImGui.TextDisabled(Loc.T("Top: BACKSTAGE                            Bottom: AUDIENCE"));
    }

    private void StartSoloStageWalk()
    {
        try
        {
            var layout = cfg.SoloStage ?? throw new InvalidOperationException("Mark a solo stage first.");
            if (!layout.IsValid() || layout.TerritoryId != StageTerritory)
                throw new InvalidOperationException("Re-anchor this solo layout in the current area.");
            if (!StageSafe(out var reason)) throw new InvalidOperationException(reason);
            if (recallingStage is not null || capturingStage is not null || stageNavigator.Active ||
                stageFacing.Active || soloStageWalking || soloStageFacingPending ||
                (!string.IsNullOrEmpty(stageMovementNonce) && !stageMoveReported))
                throw new InvalidOperationException("Another stage action is in progress. Stop it first.");
            var (here, _) = StageLocalPose();
            if (Vector3.Distance(here, layout.Anchor) > 20f)
                throw new InvalidOperationException("Approach within 20 metres of this stage's centre first.");
            if (!stageNavigator.TryStart(here, layout.Destination, out reason))
                throw new InvalidOperationException(reason);
            soloStageWalking = true;
            soloStageStatus = "Solo Bard moving to spot. Then automatic facing will begin.";
        }
        catch (Exception ex) { soloStageStatus = "Could not position solo Bard: " + ex.Message; }
    }

    private void StartSoloStageFacingOnly()
    {
        try
        {
            var layout = cfg.SoloStage ?? throw new InvalidOperationException("Mark a solo stage first.");
            if (!layout.IsValid() || layout.TerritoryId != StageTerritory)
                throw new InvalidOperationException("Re-anchor this stage in the current area first.");
            if (!StageSafe(out var reason)) throw new InvalidOperationException(reason);
            if (stageNavigator.Active || stageFacing.Active || recallingStage is not null ||
                soloStageWalking || soloStageFacingPending || capturingStage is not null ||
                (!string.IsNullOrEmpty(stageMovementNonce) && !stageMoveReported))
                throw new InvalidOperationException("Another stage action is already active.");
            stageFacing.Start(layout.AnchorHeading);
            soloStageFacingPending = true;
            soloStageStatus = "Turning your solo Bard forward toward the audience...";
        }
        catch (Exception ex) { soloStageStatus = "Could not turn solo Bard: " + ex.Message; }
    }

    private void PollSoloStage(bool safe)
    {
        if (!soloStageWalking && !soloStageFacingPending) return;
        if (!safe)
        {
            StopSoloStage();
            soloStageStatus = "Solo stage movement stopped: not safe to move or turn.";
            return;
        }
        if (soloStageWalking)
        {
            if (stageNavigator.Active) return;
            soloStageWalking = false;
            if (!stageNavigator.Arrived)
            {
                soloStageStatus = "Solo stage movement failed: " + stageNavigator.Status;
                return;
            }
            var layout = cfg.SoloStage;
            if (layout is null || !layout.IsValid() || layout.TerritoryId != StageTerritory)
            {
                soloStageStatus = "Solo stage layout changed while walking. Turn cancelled.";
                return;
            }
            stageFacing.Start(layout.Facing);
            soloStageFacingPending = true;
        }
        if (!soloStageFacingPending || stageFacing.Active) return;
        soloStageFacingPending = false;
        soloStageStatus = stageFacing.Arrived
            ? "Solo Bard is facing the chosen stage direction. Ready for music!"
            : "Solo Bard's automatic facing failed: " + stageFacing.Status;
    }

    private void StopSoloStage()
    {
        if (soloStageWalking || soloStageFacingPending)
        {
            stageNavigator.Stop("Solo stage movement stopped by user.");
            stageFacing.Cancel();
        }
        soloStageWalking = false;
        soloStageFacingPending = false;
        soloStageStatus = "Solo stage movement stopped. Your saved layout is unchanged.";
    }
}
