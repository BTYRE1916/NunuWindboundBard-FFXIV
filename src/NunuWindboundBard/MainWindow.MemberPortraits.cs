using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

/// <summary>
/// Capture real, game-rendered Adventurer Plate portraits. No arbitrary file picker,
/// online portrait scraping, network data exchange, or native game memory offsets.
/// The user must first OPEN a visible Adventurer Plate in FFXIV and explicitly crop
/// its portrait from a one-time screenshot of the game viewport.
/// Only the confirmed thumbnail is written to the client's local config folder.
/// </summary>
public sealed partial class MainWindow
{
    // Windows Imaging Component PNG container GUID.
    private static readonly Guid PortraitPngCodec = new("1b7cfaf4-713f-473c-bbcd-6137425faeaf");

    private Task<IDalamudTextureWrap>? portraitSnapshotTask;
    private Task? portraitSaveTask;
    private IDalamudTextureWrap? portraitSnapshot;
    private ulong portraitTargetId;
    private string portraitTargetName = "";
    private string portraitNewFileName = "";
    private string portraitStatus = "Open an Adventurer Plate in game, then capture its picture here.";
    private bool portraitEditorOpen;
    private bool portraitClosePending;
    private readonly List<Task<IDalamudTextureWrap>> portraitOrphanedCaptureTasks = new();
    private bool portraitDragging;
    private bool portraitSelectionExists;
    private Vector2 portraitDragStart;
    private Vector2 portraitUvMin;
    private Vector2 portraitUvMax;

    private static string PortraitConfigFolder => Plugin.PluginInterface.GetPluginConfigDirectory();

    private bool TryGetMemberPortrait(ulong contentId, out IDalamudTextureWrap? wrap)
    {
        wrap = null;
        var path = MemberPortraitFiles.Resolve(PortraitConfigFolder,
            cfg.MemberPortraitFileByContentId, contentId);
        if (path is null || !File.Exists(path)) return false;
        try
        {
            // The texture provider owns this shared texture; never dispose the wrap.
            return Plugin.Textures.GetFromFileAbsolute(path).TryGetWrap(out wrap, out _) && wrap is not null;
        }
        catch { return false; }
    }

    private bool DrawMemberPortraitThumbnail(ulong contentId, float size = 35f)
    {
        if (!TryGetMemberPortrait(contentId, out var wrap) || wrap is null) return false;
        ImGui.Image(wrap.Handle, new Vector2(size, size));
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Captured from FFXIV Adventurer Plate (local thumbnail)");
        return true;
    }

    private bool MemberPortraitSelectable(PartyChoice member, string id, bool isSelected)
    {
        bool hasPicture = TryGetMemberPortrait(member.ContentId, out var wrap) && wrap is not null;
        string name = (member.IsLocal ? "[Me] " : "") + member.Name;
        string label = (hasPicture ? "        " : "") + Loc.T(name) + "##" + id;
        bool clicked = ImGui.Selectable(label, isSelected, ImGuiSelectableFlags.None, new Vector2(0f, 38f));
        if (hasPicture && wrap is not null)
        {
            Vector2 at = ImGui.GetItemRectMin() + new Vector2(3f, 2f);
            ImGui.GetWindowDrawList().AddImage(wrap.Handle, at, at + new Vector2(33f, 33f));
        }
        return clicked;
    }

    /// <summary>
    /// Dedicated portrait page: deliberately independent of EnsembleRole and the
    /// current MIDI. PartyRoster.GetChoices includes the local player even when
    /// not in a party, so a Solo Bard can capture their own Adventurer Plate.
    /// </summary>
    private void DrawBandPortraitsTab()
    {
        ImGui.TextColored(new Vector4(.96f, .58f, .83f, 1f),
            Loc.T("BAND PORTRAITS — ADVENTURER PLATES"));
        ImGui.TextWrapped(Loc.T("Capture genuine in-game Adventurer Plate pictures for yourself " +
            "and your band. This page works in Solo, Conductor and Follower modes; " +
            "it does not require a MIDI to be loaded."));
        ImGui.Separator();

        var roster = PartyRoster.GetChoices(Plugin.Objects, Plugin.Party);
        if (roster.Count == 0)
        {
            ImGui.TextDisabled(Loc.T("Log in to show your character. Party members appear here when you join a party."));
            ImGui.TextWrapped(Loc.T(portraitStatus));
            return;
        }
        DrawMemberPortraitManager(roster);
        int saved = cfg.MemberPortraitFileByContentId?.Count ?? 0;
        ImGui.TextDisabled(Loc.T($"Saved on this client: {saved} portrait(s). Pictures never travel through party chat."));
    }

    private void DrawMemberPortraitManager(IReadOnlyList<PartyChoice> roster)
    {
        ImGui.TextWrapped(Loc.T("1. Open the character's Adventurer Plate in FFXIV. " +
            "2. Press CAPTURE beside the matching name below. " +
            "3. Drag a square over the portrait in the preview, then SAVE. " +
            "Only the cropped thumbnail is saved locally; repeat on each client's PC if desired."));
        ImGui.Spacing();
        // A bounded list prevents 8–15 performers pushing the capture guidance
        // and status line off the bottom of the plugin window.
        float listHeight = Math.Clamp(roster.Count * 47f + 10f, 70f, 390f);
        if (ImGui.BeginChild("##WindboundBandPortraitRoster", new Vector2(0f, listHeight), true))
        {
            foreach (var member in roster)
            {
                ImGui.PushID("WBPortrait" + member.ContentId);
                if (DrawMemberPortraitThumbnail(member.ContentId, 32f)) ImGui.SameLine(0f, 6f);
                ImGui.TextUnformatted((member.IsLocal ? "[Me] " : "") + member.Name);
                ImGui.SameLine();
                if (ImGui.SmallButton("CAPTURE##WBPortrait")) BeginMemberPortraitCapture(member);
                var file = MemberPortraitFiles.Resolve(PortraitConfigFolder,
                    cfg.MemberPortraitFileByContentId, member.ContentId);
                if (file is not null)
                {
                    ImGui.SameLine();
                    if (ImGui.SmallButton("Remove##WBPortrait")) RemoveMemberPortrait(member.ContentId);
                }
                ImGui.Separator();
                ImGui.PopID();
            }
        }
        ImGui.EndChild();
        ImGui.TextWrapped(Loc.T(portraitStatus));
        ImGui.TextDisabled(Loc.T("Only party members and your own character appear in this list. " +
            "Existing captured portraits are reused automatically when members rejoin."));
    }

    private void BeginMemberPortraitCapture(PartyChoice member)
    {
        if (member.ContentId == 0 || portraitSaveTask is { IsCompleted: false }) return;
        // The user must deliberately open the member's card; Windbound never
        // automatically requests a private profile or bypasses visibility settings.
        CloseMemberPortraitSnapshot();
        portraitTargetId = member.ContentId;
        portraitTargetName = member.Name;
        portraitEditorOpen = true;
        portraitSelectionExists = false;
        portraitDragging = false;
        try
        {
            portraitSnapshotTask = Plugin.Textures.CreateFromImGuiViewportAsync(
                new ImGuiViewportTextureArgs
                {
                    ViewportId = ImGui.GetMainViewport().ID,
                    AutoUpdate = false,
                    TakeBeforeImGuiRender = true,
                    KeepTransparency = false,
                }, "Windbound Bard game-only Adventurer Plate capture");
            portraitStatus = "Captured the game display for " + member.Name + ". Select the portrait area.";
        }
        catch (Exception ex)
        {
            portraitStatus = "Could not capture the game display: " + ex.Message;
            portraitEditorOpen = false;
        }
    }

    private void RemoveMemberPortrait(ulong id)
    {
        string key = MemberPortraitFiles.ContentKey(id);
        string? path = MemberPortraitFiles.Resolve(PortraitConfigFolder,
            cfg.MemberPortraitFileByContentId, id);
        cfg.MemberPortraitFileByContentId.Remove(key);
        cfg.Save();
        try { if (path is not null && File.Exists(path)) File.Delete(path); }
        catch (Exception ex) { portraitStatus = "Entry removed; could not delete thumbnail: " + ex.Message; return; }
        portraitStatus = "Local Adventurer Plate thumbnail removed.";
    }

    private void PollMemberPortraitTasks()
    {
        if (portraitClosePending)
        {
            portraitClosePending = false;
            CloseMemberPortraitSnapshot();
        }
        // Dispose cancelled screenshot results on the UI thread when possible.
        for (int i = portraitOrphanedCaptureTasks.Count - 1; i >= 0; i--)
        {
            var orphan = portraitOrphanedCaptureTasks[i];
            if (!orphan.IsCompleted) continue;
            if (orphan.IsCompletedSuccessfully) orphan.Result.Dispose();
            portraitOrphanedCaptureTasks.RemoveAt(i);
        }
        if (portraitSnapshotTask is { IsCompleted: true } snapshotTask)
        {
            portraitSnapshotTask = null;
            if (snapshotTask.IsCompletedSuccessfully)
                portraitSnapshot = snapshotTask.Result;
            else
                portraitStatus = "Game portrait capture failed: " +
                    (snapshotTask.Exception?.GetBaseException().Message ?? "capture cancelled");
        }
        if (portraitSaveTask is { IsCompleted: true } saveTask)
        {
            portraitSaveTask = null;
            if (saveTask.IsCompletedSuccessfully)
            {
                string key = MemberPortraitFiles.ContentKey(portraitTargetId);
                var oldPath = MemberPortraitFiles.Resolve(PortraitConfigFolder,
                    cfg.MemberPortraitFileByContentId, portraitTargetId);
                cfg.MemberPortraitFileByContentId[key] = portraitNewFileName;
                cfg.Save();
                string newPath = Path.Combine(MemberPortraitFiles.Directory(PortraitConfigFolder), portraitNewFileName);
                if (oldPath is not null && oldPath != newPath)
                {
                    try { File.Delete(oldPath); } catch { /* old cache may be locked; never discard the new picture */ }
                }
                portraitStatus = "Saved genuine in-game portrait for " + portraitTargetName + ".";
                CloseMemberPortraitSnapshot();
                portraitEditorOpen = false;
            }
            else
                portraitStatus = "Could not save Adventurer Plate portrait: " +
                    (saveTask.Exception?.GetBaseException().Message ?? "save cancelled");
        }
    }

    private static Vector2 ClampUv(Vector2 value) => new(
        Math.Clamp(value.X, 0f, 1f), Math.Clamp(value.Y, 0f, 1f));

    private void DrawMemberPortraitCaptureWindow()
    {
        if (!portraitEditorOpen && portraitSnapshotTask is null && portraitSaveTask is null) return;
        if (!portraitEditorOpen) return;
        ImGui.SetNextWindowSize(new Vector2(800f, 590f), ImGuiCond.FirstUseEver);
        if (ImGui.Begin("FFXIV ADVENTURER PLATE CAPTURE###WindboundPortraitCapture"))
        {
            ImGui.TextUnformatted("Capture for: " + portraitTargetName);
            ImGui.TextWrapped("Only use this if their Adventurer Plate is visible in FFXIV. " +
                "Drag a SQUARE directly on the screenshot to select the portrait. " +
                "The image itself is a crop surface; move this window by its title bar. " +
                "Full screenshots are never written to disk.");
            if (portraitSnapshotTask is not null)
                ImGui.TextDisabled("Taking a game-only snapshot...");
            else if (portraitSnapshot is { } screenshot)
            {
                float usableWidth = Math.Max(200f, ImGui.GetContentRegionAvail().X - 12f);
                float factor = MathF.Min(usableWidth / screenshot.Width,
                    380f / screenshot.Height);
                Vector2 size = new(screenshot.Width * factor, screenshot.Height * factor);
                // ImGui.Image only paints an image; it does not claim a mouse-drag ActiveId.
                // Dragging over a non-interactive image can therefore move the ImGui
                // window rather than adjust the portrait crop. InvisibleButton owns
                // the full image rectangle, preventing accidental window movement.
                // The capture window itself remains movable by its TITLE BAR.
                ImGui.InvisibleButton("##WindboundPortraitCropSurface", size);
                Vector2 imgMin = ImGui.GetItemRectMin();
                Vector2 imgMax = ImGui.GetItemRectMax();
                var cropDraw = ImGui.GetWindowDrawList();
                cropDraw.AddImage(screenshot.Handle, imgMin, imgMax);
                Vector2 mouse = ImGui.GetMousePos();
                bool hovered = ImGui.IsItemHovered();
                bool active = ImGui.IsItemActive();
                // Only begin when the interactive image surface owns this press.
                if (hovered && active && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    portraitDragStart = Vector2.Clamp(mouse, imgMin, imgMax);
                    portraitDragging = true;
                    portraitSelectionExists = false;
                }
                // Keep dragging if the cursor crosses the edge of the preview;
                // the coordinates are clamped to the image bounds below.
                if (portraitDragging && active && ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Vector2 end = Vector2.Clamp(mouse, imgMin, imgMax);
                    SetSquarePortraitCrop(portraitDragStart, end, imgMin, size);
                }
                if (portraitDragging && !ImGui.IsMouseDown(ImGuiMouseButton.Left))
                {
                    Vector2 end = Vector2.Clamp(mouse, imgMin, imgMax);
                    SetSquarePortraitCrop(portraitDragStart, end, imgMin, size);
                    portraitDragging = false;
                }
                if (portraitSelectionExists)
                {
                    Vector2 a = imgMin + portraitUvMin * size;
                    Vector2 b = imgMin + portraitUvMax * size;
                    uint color = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, .43f, .78f, 1f));
                    cropDraw.AddLine(a, new Vector2(b.X, a.Y), color, 3f);
                    cropDraw.AddLine(new Vector2(b.X, a.Y), b, color, 3f);
                    cropDraw.AddLine(b, new Vector2(a.X, b.Y), color, 3f);
                    cropDraw.AddLine(new Vector2(a.X, b.Y), a, color, 3f);
                }
            }
            ImGui.Separator();
            if (portraitSelectionExists && portraitSnapshot is not null && portraitSaveTask is null)
            {
                if (ImGui.Button("SAVE SELECTED GAME PORTRAIT")) StartSavingMemberPortrait();
                ImGui.SameLine();
            }
            else ImGui.TextDisabled("Select the in-game portrait to enable SAVE.");
            if (portraitSaveTask is not null) ImGui.TextDisabled("Saving thumbnail locally...");
            if (portraitSaveTask is null && ImGui.Button("CANCEL / CLOSE"))
            {
                // ImGui may still draw the screenshot this frame; release on
                // the following frame, rather than invalidating its handle now.
                portraitEditorOpen = false;
                portraitClosePending = true;
            }
            ImGui.TextWrapped(portraitStatus);
        }
        ImGui.End();
    }

    private void SetSquarePortraitCrop(Vector2 start, Vector2 end, Vector2 imageTop, Vector2 imageSize)
    {
        Vector2 delta = end - start;
        float side = MathF.Min(MathF.Abs(delta.X), MathF.Abs(delta.Y));
        if (side < 16f)
        {
            portraitSelectionExists = false;
            return;
        }
        Vector2 target = start + new Vector2(MathF.CopySign(side, delta.X), MathF.CopySign(side, delta.Y));
        Vector2 low = Vector2.Min(start, target);
        Vector2 high = Vector2.Max(start, target);
        portraitUvMin = ClampUv((low - imageTop) / imageSize);
        portraitUvMax = ClampUv((high - imageTop) / imageSize);
        portraitSelectionExists = portraitUvMax.X > portraitUvMin.X && portraitUvMax.Y > portraitUvMin.Y;
    }

    private void StartSavingMemberPortrait()
    {
        if (portraitSnapshot is not { } screenshot || !portraitSelectionExists ||
            portraitTargetId == 0 || portraitSaveTask is not null) return;
        try
        {
            portraitNewFileName = MemberPortraitFiles.NewName(portraitTargetId);
            string directory = MemberPortraitFiles.Directory(PortraitConfigFolder);
            System.IO.Directory.CreateDirectory(directory);
            string output = Path.Combine(directory, portraitNewFileName);
            portraitSaveTask = SaveMemberPortraitAsync(screenshot, portraitUvMin, portraitUvMax, output);
            portraitStatus = "Writing the cropped game portrait locally...";
        }
        catch (Exception ex) { portraitStatus = "Portrait save could not start: " + ex.Message; }
    }

    private static async Task SaveMemberPortraitAsync(
        IDalamudTextureWrap screenshot, Vector2 uvMin, Vector2 uvMax, string output)
    {
        // Copy to owned 128x128 texture; save ONLY the selected rectangle, not
        // the full game display. The source is kept alive by the editor while saving.
        using var thumbnail = await Plugin.Textures.CreateFromExistingTextureAsync(screenshot,
            new TextureModificationArgs
            {
                Uv0 = uvMin,
                Uv1 = uvMax,
                NewWidth = 128,
                NewHeight = 128,
                MakeOpaque = true
            }, leaveWrapOpen: true, debugName: "Windbound Bard captured member portrait");
        await Plugin.TextureReadback.SaveToFileAsync(thumbnail, PortraitPngCodec, output,
            leaveWrapOpen: true);
    }

    private void CloseMemberPortraitSnapshot()
    {
        portraitSnapshot?.Dispose();
        portraitSnapshot = null;
        portraitSelectionExists = false;
        portraitDragging = false;
        if (portraitSnapshotTask is { } pending)
        {
            portraitSnapshotTask = null;
            portraitOrphanedCaptureTasks.Add(pending);
        }
    }

    private void DisposeMemberPortraitCapture()
    {
        portraitEditorOpen = false;
        portraitClosePending = false;
        CloseMemberPortraitSnapshot();
        foreach (var task in portraitOrphanedCaptureTasks)
        {
            if (task.IsCompletedSuccessfully) task.Result.Dispose();
            else if (!task.IsCompleted)
            {
                // Best-effort cleanup after shutdown, without accessing UI state.
                _ = task.ContinueWith(finished =>
                {
                    if (finished.IsCompletedSuccessfully) finished.Result.Dispose();
                }, TaskScheduler.Default);
            }
        }
        portraitOrphanedCaptureTasks.Clear();
        // A running save can finish on disk, but no new config is persisted after unload.
    }
}
