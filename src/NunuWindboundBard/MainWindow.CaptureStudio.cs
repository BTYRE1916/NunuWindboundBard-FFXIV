using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using NunuWindboundBard.Core;

namespace NunuWindboundBard;

public sealed partial class MainWindow
{
    private bool openCaptureCockpit;
    internal void OpenCaptureCockpit() { openCaptureCockpit = true; IsOpen = true; }
    private string studioSceneId = "", studioSourceId = "", studioFilterId = "";
    private string studioNewScene = "Concert", studioNewSource = "Overlay", studioSourceType = "", studioFilterType = "";
    private string studioRenameScene = "", studioRenameSource = "";
    private string studioStreamServer = "", studioStreamKey = "";
    private bool studioStreamConfirmed, studioStopConfirmed, studioRemoveSourceConfirmed, studioRemoveSceneConfirmed;
    private int studioTransitionMs = 300;
    private long studioPreviewAt, studioTextureSequence = -1;
    private IDalamudTextureWrap? studioTexture;
    private string studioPreviewError = "";
    private StudioOutputSettings? studioOutputDraft;
    private StudioTransform? studioTransformDraft;
    private StudioInspector? studioInspectorSeen, studioInspectionSelection;
    private readonly Dictionary<string, JsonElement> studioPropertyDraft = [];
    private bool studioPreviewProgram;
    private string studioTextureScene = "";
    private bool studioDragging, studioResizing;
    private StudioTransform? studioDragOriginal;
    private void DisposeCaptureStudioPreview() { studioTexture?.Dispose(); studioTexture = null; studioStreamKey = ""; }

    private void DrawCaptureBackendSelector()
    {
        int backend = cfg.CaptureBackend;
        if (ImGui.Combo("Capture option##wbCaptureBackend", ref backend, "Optional OBS connection\0Native Windbound capture suite\0"))
        {
            cfg.CaptureBackend = backend; cfg.Save(); studioStopConfirmed = false; studioStreamConfirmed = false;
        }
        bool showHud = cfg.ObsShowCaptureHud;
        if (ImGui.Checkbox("Capture controls on Performance HUD##wbCaptureHud", ref showHud)) { cfg.ObsShowCaptureHud = showHud; cfg.Save(); }
        ImGui.TextDisabled("Switching views never starts or stops either capture option.");
        var obs = plugin.ObsCapture.Snapshot; var native = plugin.StudioCapture.Snapshot;
        if (obs.Recording || obs.Streaming) ImGui.TextColored(new Vector4(1,.7f,.3f,1), "OBS output active: " + (obs.Recording ? "recording " : "") + (obs.Streaming ? "streaming" : ""));
        if (native.Recording || native.Streaming || native.Replay) ImGui.TextColored(new Vector4(1,.7f,.3f,1), "Background output active: " + (native.Recording ? "recording " : "") + (native.Streaming ? "streaming " : "") + (native.Replay ? "replay buffer" : ""));
        ImGui.Separator();
    }
    private void DrawCaptureStudio()
    {
        var client = plugin.StudioCapture; var state = client.Snapshot;
        ImGui.TextColored(WindboundStageStyle.Accent(cfg.StageUiAccent), "WINDBOUND CAPTURE STUDIO");
        ImGui.TextWrapped("Build scenes and overlays here. The Windows background engine captures, mixes and encodes; MIDI transport remains independent.");
        if (!state.Connected)
        {
            var paths = plugin.CapturePaths;
            string exe = paths.EngineExecutable, root = paths.FfmpegExecutable;
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("Engine executable##wbStudioExe", ref exe, 1024)) { cfg.CaptureEnginePath = exe; cfg.Save(); }
            ImGui.SetNextItemWidth(-1);
            if (ImGui.InputText("Codec worker (ffmpeg.exe)##wbStudioRoot", ref root, 1024)) { cfg.CaptureFfmpegPath = root; cfg.Save(); }
            ImGui.TextDisabled("The native suite uses Windows capture and its bundled codec worker. No OBS installation is required.");
            ImGui.BeginDisabled(state.Busy);
            if (ImGui.Button("LAUNCH BACKGROUND ENGINE##wbStudioLaunch")) client.Launch(exe, root, Path.Combine(Plugin.PluginInterface.GetPluginConfigDirectory(), "CaptureStudioNative"));
            ImGui.SameLine(); if (ImGui.Button("CONNECT TO RUNNING ENGINE##wbStudioConnect")) client.Connect();
            ImGui.EndDisabled();
            ImGui.TextWrapped(state.Message);
            if (client.LastError.Length > 0) ImGui.TextWrapped(client.LastError);
            ImGui.TextWrapped("Build and publish the engine with Build-CaptureEngine.ps1. Start once, then reconnect after a plugin reload. One studio engine runs per Windows user.");
            return;
        }
        if (ImGui.SmallButton("Disconnect controller##wbStudioDisconnect")) { client.Disconnect(); DisposeCaptureStudioPreview(); return; }
        ImGui.SameLine(); ImGui.TextDisabled(state.RuntimeVersion);
        ImGui.TextWrapped(state.Message);
        if (client.LastError.Length > 0) ImGui.TextColored(new Vector4(1,.55f,.35f,1), client.LastError);
        if (client.Inspector is { } inspected && !ReferenceEquals(inspected, studioInspectionSelection)) {
            studioInspectionSelection = inspected;
            var scene = state.Scenes.FirstOrDefault(x => x.Sources.Any(y => y.Id == inspected.SourceId));
            if (scene != null) {
                var source = scene.Sources.First(x => x.Id == inspected.SourceId);
                if (studioSourceId != source.Id) { studioSceneId = scene.Id; studioSourceId = source.Id; studioRenameSource = source.Name; studioTransformDraft = source.Transform; }
                studioFilterId = inspected.FilterId;
            }
        }
        DrawStudioOutputs(state);
        ImGui.Separator();
        DrawStudioPreview(state);
        ImGui.Separator();
        ImGui.BeginDisabled(state.Busy);
        if (ImGui.BeginTable("##wbStudioEditor", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableNextColumn(); DrawStudioScenes(state);
            ImGui.TableNextColumn(); DrawStudioSourceEditor(state);
            ImGui.EndTable();
        }
        ImGui.EndDisabled();
        ImGui.Separator();
        DrawStudioMixer(state);
        ImGui.Separator();
        DrawStudioOutputSettings(state);
        ImGui.Separator();
        ImGui.BeginDisabled(state.Busy || state.Recording || state.Streaming || state.Replay);
        if (ImGui.SmallButton("SHUT DOWN IDLE ENGINE##wbStudioShutdown")) { client.Command("Shutdown"); }
        ImGui.EndDisabled();
        ImGui.TextDisabled("Closing this tab, logging out, or reloading the plugin leaves the engine running. Stop outputs before shutting it down.");
    }
    private void DrawStudioOutputs(StudioSnapshot s)
    {
        var c = plugin.StudioCapture;
        ImGui.TextUnformatted((s.RecordingStarting ? "STARTING RECORDING" : s.Recording ? s.Paused ? "RECORDING PAUSED" : "RECORDING" : "RECORDING STANDBY") + "  " + ObsRecordingClient.FormatDuration(s.RecordingMilliseconds));
        ImGui.SameLine(); ImGui.TextUnformatted(s.StreamingStarting ? "STREAM CONNECTING" : s.Streaming ? "STREAM LIVE" : "STREAM OFF");
        if (s.FreeDiskGiB.HasValue) ImGui.TextDisabled($"Disk {s.FreeDiskGiB:0.0} GiB free | Recorded {s.RecordingBytes / 1048576.0:0} MiB | Stream dropped frames {s.DroppedFrames}");
        if (s.FreeDiskGiB < cfg.ObsDiskWarningGiB) ImGui.TextColored(new Vector4(1,.55f,.3f,1), "LOW DISK SPACE — stop and save or free storage.");
        ImGui.BeginDisabled(s.Busy || s.Recording);
        if (ImGui.Button("START RECORDING##wbStudioRecord")) c.Command("StartRecord"); ImGui.EndDisabled(); ImGui.SameLine();
        ImGui.BeginDisabled(s.Busy || !s.Recording || s.RecordingStarting || s.OutputStopping);
        if (ImGui.Button(s.Paused ? "RESUME##wbStudioResume" : "PAUSE##wbStudioPause")) c.Command(s.Paused ? "ResumeRecord" : "PauseRecord");
        ImGui.SameLine(); if (ImGui.Button("STOP & SAVE...##wbStudioStop")) studioStopConfirmed = true;
        ImGui.EndDisabled();
        if (!s.Recording) studioStopConfirmed = false;
        if (studioStopConfirmed)
        {
            ImGui.TextUnformatted("Finish the recording and save the MKV file?");
            ImGui.BeginDisabled(s.Busy);
            if (ImGui.Button("YES, STOP & SAVE##wbStudioStopYes")) { c.Command("StopRecord"); studioStopConfirmed = false; }
            ImGui.SameLine(); if (ImGui.Button("Keep recording##wbStudioStopNo")) studioStopConfirmed = false;
            ImGui.EndDisabled();
        }
        if (s.OutputStopping) ImGui.TextDisabled("Finalizing output... wait for the saved path or stopped status.");
        if (s.LastSavedFile.Length > 0) { ImGui.TextWrapped("Saved: " + s.LastSavedFile); if (ImGui.SmallButton("Copy recording path##wbStudioCopyRecord")) ImGui.SetClipboardText(s.LastSavedFile); }
        ImGui.BeginDisabled(s.Busy);
        if (ImGui.Button(s.Replay ? "STOP REPLAY BUFFER##wbStudioReplayStop" : "START REPLAY BUFFER##wbStudioReplayStart")) c.Command(s.Replay ? "StopReplay" : "StartReplay");
        ImGui.SameLine(); ImGui.BeginDisabled(!s.Replay);
        if (ImGui.Button("SAVE LAST MOMENTS##wbStudioReplaySave")) c.Command("SaveReplay"); ImGui.EndDisabled(); ImGui.EndDisabled();
        if (s.LastReplayFile.Length > 0) ImGui.TextWrapped("Replay: " + s.LastReplayFile);
        if (ImGui.CollapsingHeader("Live streaming##wbStudioStreaming"))
        {
            ImGui.InputText("RTMP / RTMPS server##wbStudioServer", ref studioStreamServer, 2048);
            ImGui.InputText("Stream key (session only)##wbStudioKey", ref studioStreamKey, 2048, ImGuiInputTextFlags.Password);
            ImGui.Checkbox("I want to broadcast the program scene##wbStudioStreamConfirm", ref studioStreamConfirmed);
            ImGui.BeginDisabled(s.Busy || s.Streaming || s.StreamingStarting || s.OutputStopping || !studioStreamConfirmed || studioStreamKey.Length == 0);
            if (ImGui.Button("GO LIVE##wbStudioLive")) {
                c.Command("StartStream", new { Server = studioStreamServer, Key = studioStreamKey, Confirmed = true }); studioStreamKey = ""; studioStreamConfirmed = false;
            }
            ImGui.EndDisabled(); ImGui.SameLine(); ImGui.BeginDisabled(s.Busy || (!s.Streaming && !s.StreamingStarting) || s.OutputStopping);
            if (ImGui.Button("END STREAM##wbStudioEndLive")) c.Command("StopStream"); ImGui.EndDisabled();
            ImGui.TextDisabled("Server and key are not saved in the scene collection or plugin configuration.");
        }
    }
    private void DrawStudioPreview(StudioSnapshot s)
    {
        string sceneId = studioPreviewProgram ? "" : (studioSceneId.Length == 0 ? s.ProgramScene : studioSceneId);
        string label = s.Scenes.FirstOrDefault(x => x.Id == (sceneId.Length == 0 ? s.ProgramScene : sceneId))?.Name ?? "Scene";
        ImGui.TextUnformatted((studioPreviewProgram ? "PROGRAM PREVIEW — " : "EDIT PREVIEW — ") + label);
        ImGui.Checkbox("Preview Program instead of selected scene##wbStudioPreviewProgram", ref studioPreviewProgram);
        bool previewOn = cfg.CapturePreviewEnabled;
        if (ImGui.Checkbox("Show composed preview##wbStudioPreview", ref previewOn)) { cfg.CapturePreviewEnabled = previewOn; cfg.Save(); }
        if (!previewOn) return;
        if (Stopwatch.GetTimestamp() > studioPreviewAt) { plugin.StudioCapture.RequestPreview(sceneId); studioPreviewAt = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 4; }
        var frame = plugin.StudioCapture.Preview;
        if (frame.Bytes != null && frame.SceneId == sceneId && (frame.Sequence != studioTextureSequence || frame.SceneId != studioTextureScene))
        {
            try {
                var next = Plugin.Textures.CreateFromRaw(new RawImageSpecification { Width = StudioProtocol.PreviewWidth, Height = StudioProtocol.PreviewHeight, Pitch = StudioProtocol.PreviewWidth * 4, DxgiFormat = 28 }, frame.Bytes, "Windbound studio preview");
                studioTexture?.Dispose(); studioTexture = next; studioTextureSequence = frame.Sequence; studioTextureScene = frame.SceneId; studioPreviewError = "";
            } catch (Exception e) { studioPreviewError = "Preview unavailable: " + e.Message; }
        }
        if (studioTexture != null && studioTextureScene == sceneId) {
            float width = Math.Min(768, ImGui.GetContentRegionAvail().X);
            Vector2 origin = ImGui.GetCursorScreenPos(), size = new(width, width * 9 / 16);
            ImGui.Image(studioTexture.Handle, size);
            if (!studioPreviewProgram) DrawStudioCanvasHandles(s, sceneId, origin, size);
        } else ImGui.TextDisabled("Waiting for composed frames...");
        if (studioPreviewError.Length > 0) ImGui.TextWrapped(studioPreviewError);
        ImGui.TextDisabled("Preview is 384 x 216 at about 4 FPS; recording uses the configured full resolution and frame rate.");
        ImGui.TextDisabled("Select and edit a scene, then Take to send it to Program. Editing Program changes the live output.");
    }
    private void DrawStudioCanvasHandles(StudioSnapshot s, string sceneId, Vector2 origin, Vector2 size)
    {
        var source = s.Scenes.FirstOrDefault(x => x.Id == sceneId)?.Sources.FirstOrDefault(x => x.Id == studioSourceId);
        if (source == null || source.Width <= 0 || source.Height <= 0 || source.Transform.Locked || source.Transform.Rotation != 0) return;
        var t = studioTransformDraft ?? source.Transform;
        Vector2 factor = new(size.X / s.Output.Width, size.Y / s.Output.Height);
        Vector2 position = origin + new Vector2(t.X, t.Y) * factor;
        Vector2 extent = t.Fit ? size : new Vector2(Math.Max(1,source.Width-t.CropLeft-t.CropRight)*Math.Abs(t.ScaleX), Math.Max(1,source.Height-t.CropTop-t.CropBottom)*Math.Abs(t.ScaleY)) * factor;
        uint color = ImGui.ColorConvertFloat4ToU32(WindboundStageStyle.Accent(cfg.StageUiAccent));
        ImGui.GetWindowDrawList().PushClipRect(origin, origin + size, true);
        ImGui.GetWindowDrawList().AddRect(position, position + extent, color);
        if (!t.Fit) ImGui.GetWindowDrawList().AddRectFilled(position + extent - new Vector2(8), position + extent, color);
        ImGui.GetWindowDrawList().PopClipRect();
        if (!s.Busy && ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
            Vector2 mouse = ImGui.GetMousePos();
            if (mouse.X >= position.X && mouse.Y >= position.Y && mouse.X <= position.X+extent.X && mouse.Y <= position.Y+extent.Y) {
                studioDragging = true; studioResizing = !t.Fit && mouse.X >= position.X+extent.X-10 && mouse.Y >= position.Y+extent.Y-10;
                studioDragOriginal = t;
            }
        }
        if (studioDragging && studioDragOriginal != null) {
            Vector2 delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left) / factor;
            var start = studioDragOriginal;
            studioTransformDraft = studioResizing
                ? start with { ScaleX = Math.Clamp(start.ScaleX + delta.X / Math.Max(1,source.Width-start.CropLeft-start.CropRight), .01f,100), ScaleY = Math.Clamp(start.ScaleY + delta.Y / Math.Max(1,source.Height-start.CropTop-start.CropBottom), .01f,100) }
                : start with { X = start.X + delta.X, Y = start.Y + delta.Y };
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                plugin.StudioCapture.Command("Transform", new { SourceId = source.Id, Transform = studioTransformDraft }); studioDragging = false;
            }
        }
    }
    private void DrawStudioScenes(StudioSnapshot s)
    {
        var c = plugin.StudioCapture;
        if (!s.Scenes.Any(x => x.Id == studioSceneId)) studioSceneId = s.ProgramScene;
        ImGui.TextUnformatted("SCENES");
        foreach (var scene in s.Scenes)
            if (ImGui.Selectable((scene.Id == s.ProgramScene ? "[PROGRAM] " : "") + scene.Name + "##" + scene.Id, studioSceneId == scene.Id)) {
                studioSceneId = scene.Id; studioRenameScene = scene.Name; studioSourceId = ""; studioTransformDraft = null; studioRemoveSceneConfirmed = false;
            }
        ImGui.InputText("New scene##wbStudioNewScene", ref studioNewScene, 128);
        if (ImGui.Button("ADD SCENE##wbStudioAddScene")) c.Command("AddScene", new { Name = studioNewScene });
        ImGui.InputText("Scene name##wbStudioRenameScene", ref studioRenameScene, 128);
        if (ImGui.SmallButton("Rename scene##wbStudioSceneRename")) c.Command("RenameScene", new { SceneId = studioSceneId, Name = studioRenameScene });
        ImGui.InputInt("Fade duration (ms)##wbStudioTransition", ref studioTransitionMs); studioTransitionMs = Math.Clamp(studioTransitionMs, 0, 5000);
        if (ImGui.Button("TAKE SELECTED SCENE##wbStudioTake")) c.Command("TakeScene", new { SceneId = studioSceneId, Duration = studioTransitionMs });
        ImGui.TextDisabled("0 ms = cut; otherwise fade.");
        ImGui.BeginDisabled(s.Scenes.Length < 2 || studioSceneId == s.ProgramScene);
        if (ImGui.SmallButton("Delete scene...##wbStudioDeleteScene")) studioRemoveSceneConfirmed = true;
        if (studioRemoveSceneConfirmed) {
            ImGui.TextWrapped("Delete this scene and all its layers?");
            if (ImGui.Button("Delete scene##wbStudioDeleteSceneYes")) { c.Command("RemoveScene", new { SceneId = studioSceneId }); studioRemoveSceneConfirmed = false; }
            ImGui.SameLine(); if (ImGui.Button("Cancel##wbStudioDeleteSceneNo")) studioRemoveSceneConfirmed = false;
        }
        ImGui.EndDisabled();
        ImGui.Separator(); ImGui.TextUnformatted("LAYERS — TOP FIRST");
        var selected = s.Scenes.FirstOrDefault(x => x.Id == studioSceneId);
        if (selected == null) return;
        foreach (var source in selected.Sources.AsEnumerable().Reverse())
        {
            bool visible = source.Visible;
            if (ImGui.Checkbox("##wbVisible" + source.Id, ref visible)) c.Command("Visibility", new { SourceId = source.Id, Visible = visible });
            ImGui.SameLine();
            if (ImGui.Selectable(source.Name + (source.Transform.Locked ? " [locked]" : "") + "##" + source.Id, source.Id == studioSourceId)) SelectStudioSource(source);
        }
        ImGui.Separator(); ImGui.TextUnformatted("ADD SOURCE / OVERLAY");
        ImGui.InputText("Source name##wbStudioSourceName", ref studioNewSource, 128);
        DrawStudioTypePicker("Source type##wbStudioInput", s.Inputs, ref studioSourceType);
        ImGui.BeginDisabled(studioSourceType.Length == 0);
        if (ImGui.Button("ADD SELECTED SOURCE##wbStudioAddInput")) c.Command("AddSource", new { SceneId = studioSceneId, Name = studioNewSource, Type = studioSourceType });
        ImGui.EndDisabled();
        if (ImGui.SmallButton("+ FFXIV window##wbStudioAddGame")) AddStudioPreset(s, "window_capture", "FFXIV", true);
        ImGui.SameLine(); if (ImGui.SmallButton("+ Text##wbStudioAddText")) AddStudioPreset(s, "text", "Title", false, new { text = "Windbound Bard", font = new { face = "Arial", size = 72, flags = 0 }, color = 0xFFFFFFFFu });
        if (ImGui.SmallButton("+ Image##wbStudioAddImage")) AddStudioPreset(s, "image", "Image overlay");
        ImGui.SameLine(); if (ImGui.SmallButton("+ Desktop audio##wbStudioAddAudio")) AddStudioPreset(s, "desktop_audio", "Desktop audio", false, new { device_id = "default" });
        if (ImGui.SmallButton("+ Microphone##wbStudioAddMic")) AddStudioPreset(s, "microphone", "Microphone", false, new { device_id = "default" });
        ImGui.TextDisabled("Choose files, windows, devices and text in the source properties. Capture requires an active rendering window; minimized games may stop producing frames.");
    }
    private void AddStudioPreset(StudioSnapshot s, string prefix, string name, bool game = false, object? settings = null)
    {
        string type = s.Inputs.FirstOrDefault(x => x.Id == prefix)?.Id ?? s.Inputs.LastOrDefault(x => x.Id.StartsWith(prefix, StringComparison.Ordinal))?.Id ?? "";
        if (type.Length == 0) return;
        plugin.StudioCapture.Command("AddSource", game
            ? new { SceneId = studioSceneId, Name = name, Type = type, GameProcessId = Environment.ProcessId }
            : (object)new { SceneId = studioSceneId, Name = name, Type = type, Settings = settings ?? new { } });
    }
    private void SelectStudioSource(StudioSource source)
    {
        studioSourceId = source.Id; studioFilterId = ""; studioRenameSource = source.Name;
        studioTransformDraft = source.Transform; studioRemoveSourceConfirmed = false;
        plugin.StudioCapture.Command("Inspect", new { SourceId = source.Id, FilterId = "" });
    }
    private static void DrawStudioTypePicker(string label, StudioType[] types, ref string selected)
    {
        string currentType = selected;
        if (!types.Any(x => x.Id == currentType)) selected = types.FirstOrDefault()?.Id ?? "";
        currentType = selected;
        if (ImGui.BeginCombo(label, types.FirstOrDefault(x => x.Id == currentType)?.Name ?? "Unavailable")) {
            foreach (var type in types) if (ImGui.Selectable(type.Name + "##" + type.Id, selected == type.Id)) selected = type.Id;
            ImGui.EndCombo();
        }
    }
    private void DrawStudioSourceEditor(StudioSnapshot s)
    {
        var c = plugin.StudioCapture;
        var source = s.Scenes.FirstOrDefault(x => x.Id == studioSceneId)?.Sources.FirstOrDefault(x => x.Id == studioSourceId);
        ImGui.TextUnformatted("SOURCE EDITOR");
        if (source == null) { ImGui.TextWrapped("Select a layer to edit its properties, transform, crop and filters."); return; }
        ImGui.TextUnformatted(source.Name);
        if (source.Status.Length > 0) ImGui.TextWrapped(source.Status);
        if (source.Type == "text") {
            using var titleSettings = JsonDocument.Parse(source.Settings);
            bool bind = titleSettings.RootElement.TryGetProperty("read_from_file", out var reading) && reading.ValueKind == JsonValueKind.True &&
                titleSettings.RootElement.TryGetProperty("text_file", out var titlePath) && titlePath.GetString() == obsTitleFile.FilePath;
            if (ImGui.Checkbox("Use current MIDI title for this overlay##wbNativeTitle", ref bind)) {
                if (bind) { cfg.ObsSongTitleEnabled = true; cfg.Save(); }
                c.Command("UpdateProperties", new { SourceId = source.Id, FilterId = "", Settings = new { read_from_file = bind, text_file = obsTitleFile.FilePath } });
            }
        }
        ImGui.InputText("Layer name##wbStudioRenameSource", ref studioRenameSource, 128);
        if (ImGui.SmallButton("Rename##wbStudioSourceRename")) c.Command("RenameSource", new { SourceId = source.Id, Name = studioRenameSource });
        ImGui.SameLine(); if (ImGui.SmallButton("Layer up##wbStudioUp")) c.Command("Layer", new { SourceId = source.Id, Delta = 1 });
        ImGui.SameLine(); if (ImGui.SmallButton("Layer down##wbStudioDown")) c.Command("Layer", new { SourceId = source.Id, Delta = -1 });
        if (ImGui.CollapsingHeader("Position, scale and crop##wbStudioTransform", ImGuiTreeNodeFlags.DefaultOpen))
        {
            studioTransformDraft ??= source.Transform;
            var t = studioTransformDraft;
            float x = t.X, y = t.Y, sx = t.ScaleX, sy = t.ScaleY, rot = t.Rotation;
            int left = t.CropLeft, top = t.CropTop, right = t.CropRight, bottom = t.CropBottom; bool fit = t.Fit, locked = t.Locked;
            if (ImGui.Checkbox("Lock layer##wbStudioLock", ref locked)) { studioTransformDraft = t with { Locked = locked }; c.Command("Transform", new { SourceId = source.Id, Transform = studioTransformDraft }); }
            ImGui.BeginDisabled(source.Transform.Locked);
            ImGui.InputFloat("X (pixels)##wbStudioX", ref x); ImGui.InputFloat("Y (pixels)##wbStudioY", ref y);
            ImGui.InputFloat("Scale X##wbStudioScaleX", ref sx); ImGui.InputFloat("Scale Y##wbStudioScaleY", ref sy);
            ImGui.InputFloat("Rotation (degrees)##wbStudioRotation", ref rot);
            ImGui.Checkbox("Fit within canvas##wbStudioFit", ref fit);
            ImGui.InputInt("Crop left##wbStudioCropL", ref left); ImGui.InputInt("Crop top##wbStudioCropT", ref top);
            ImGui.InputInt("Crop right##wbStudioCropR", ref right); ImGui.InputInt("Crop bottom##wbStudioCropB", ref bottom);
            studioTransformDraft = t with { X = x, Y = y, ScaleX = sx, ScaleY = sy, Rotation = rot, CropLeft = Math.Max(0,left), CropTop = Math.Max(0,top), CropRight = Math.Max(0,right), CropBottom = Math.Max(0,bottom), Fit = fit, Locked = locked };
            if (ImGui.Button("APPLY TRANSFORM##wbStudioApplyTransform")) c.Command("Transform", new { SourceId = source.Id, Transform = studioTransformDraft });
            ImGui.SameLine(); if (ImGui.SmallButton("Reset##wbStudioResetTransform")) { studioTransformDraft = new(); c.Command("Transform", new { SourceId = source.Id, Transform = studioTransformDraft }); }
            ImGui.EndDisabled();
        }
        if (ImGui.SmallButton("Source properties##wbStudioInspectSource")) { studioFilterId = ""; c.Command("Inspect", new { SourceId = source.Id, FilterId = "" }); }
        ImGui.Separator(); ImGui.TextUnformatted("FILTER STACK");
        foreach (var f in source.Filters)
        {
            bool enabled = f.Enabled;
            if (ImGui.Checkbox("##wbFilterEnable" + f.Id, ref enabled)) c.Command("FilterEnabled", new { SourceId = source.Id, FilterId = f.Id, Enabled = enabled });
            ImGui.SameLine(); if (ImGui.Selectable(f.Name + "##" + f.Id, studioFilterId == f.Id)) { studioFilterId = f.Id; c.Command("Inspect", new { SourceId = source.Id, FilterId = f.Id }); }
        }
        DrawStudioTypePicker("Filter type##wbStudioFilterType", s.Filters, ref studioFilterType);
        if (studioFilterType.Length > 0 && ImGui.SmallButton("Add filter##wbStudioFilterAdd")) {
            string name = s.Filters.First(x => x.Id == studioFilterType).Name;
            c.Command("AddFilter", new { SourceId = source.Id, Type = studioFilterType, Name = name });
        }
        if (source.Filters.Any(f => f.Id == studioFilterId)) {
            if (ImGui.SmallButton("Filter up##wbStudioFilterUp")) c.Command("FilterOrder", new { SourceId = source.Id, FilterId = studioFilterId, Delta = -1 });
            ImGui.SameLine(); if (ImGui.SmallButton("Filter down##wbStudioFilterDown")) c.Command("FilterOrder", new { SourceId = source.Id, FilterId = studioFilterId, Delta = 1 });
            if (ImGui.SmallButton("Remove selected filter##wbStudioFilterDelete")) { c.Command("RemoveFilter", new { SourceId = source.Id, FilterId = studioFilterId }); studioFilterId = ""; }
        }
        ImGui.Separator(); DrawStudioProperties(source.Id);
        ImGui.Separator();
        if (ImGui.SmallButton("Delete layer...##wbStudioDeleteSource")) studioRemoveSourceConfirmed = true;
        if (studioRemoveSourceConfirmed) {
            ImGui.TextUnformatted("Remove this layer and its filters?");
            if (ImGui.Button("Delete layer##wbStudioDeleteSourceYes")) { c.Command("RemoveSource", new { SourceId = source.Id }); studioRemoveSourceConfirmed = false; }
            ImGui.SameLine(); if (ImGui.Button("Cancel##wbStudioDeleteSourceNo")) studioRemoveSourceConfirmed = false;
        }
    }
    private void DrawStudioProperties(string sourceId)
    {
        var c = plugin.StudioCapture; var inspected = c.Inspector;
        if (inspected == null || inspected.SourceId != sourceId || inspected.FilterId != studioFilterId) return;
        if (!ReferenceEquals(studioInspectorSeen, inspected)) {
            studioInspectorSeen = inspected; studioPropertyDraft.Clear(); foreach (var p in inspected.Properties) studioPropertyDraft[p.Name] = p.Value;
        }
        ImGui.TextUnformatted(studioFilterId.Length == 0 ? "SOURCE PROPERTIES" : "FILTER PROPERTIES");
        foreach (var p in inspected.Properties)
        {
            ImGui.PushID(p.Name); ImGui.BeginDisabled(!p.Enabled);
            JsonElement v = studioPropertyDraft[p.Name]; object? next = null;
            string label = p.Label + "##property";
            if (p.Type == 1) { bool b = v.ValueKind == JsonValueKind.True; if (ImGui.Checkbox(label, ref b)) next = b; }
            else if (p.Type == 2) { int n = v.TryGetInt32(out int number) ? number : 0; if (ImGui.InputInt(label, ref n)) next = Math.Clamp(n, (int)p.Min, (int)p.Max); }
            else if (p.Type == 3) { float n = (float)v.GetDouble(); if (ImGui.InputFloat(label, ref n)) next = Math.Clamp(n, (float)p.Min, (float)p.Max); }
            else if (p.Type == 6) {
                string chosen = p.Choices.FirstOrDefault(x => x.Value.GetRawText() == v.GetRawText())?.Label ?? v.ToString();
                if (ImGui.BeginCombo(label, chosen)) {
                    foreach (var choice in p.Choices) { ImGui.BeginDisabled(choice.Disabled); if (ImGui.Selectable(choice.Label)) next = choice.Value; ImGui.EndDisabled(); }
                    ImGui.EndCombo();
                }
            }
            else if (p.Type is 7 or 13) {
                uint packed = v.TryGetUInt32(out var color) ? color : uint.MaxValue;
                var rgba = new Vector4((packed & 255) / 255f, ((packed >> 8) & 255) / 255f, ((packed >> 16) & 255) / 255f, p.Type == 7 ? 1f : ((packed >> 24) & 255) / 255f);
                if (ImGui.ColorEdit4(label, ref rgba)) next = (uint)(rgba.X*255) | ((uint)(rgba.Y*255)<<8) | ((uint)(rgba.Z*255)<<16) | ((uint)(rgba.W*255)<<24);
            }
            else if (p.Type == 8) { if (ImGui.Button(label)) c.Command("PropertyButton", new { SourceId = sourceId, FilterId = studioFilterId, Name = p.Name }); }
            else if (p.Type is 4 or 5) {
                string text = v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                bool changed = p.Multiline ? ImGui.InputTextMultiline(label, ref text, 8192, new Vector2(-1, 100)) : ImGui.InputText(label, ref text, 8192, p.Password ? ImGuiInputTextFlags.Password : ImGuiInputTextFlags.None);
                if (changed) next = text;
                if (p.Type == 5 && ImGui.SmallButton("Browse...##browsePath")) {
                    string propertyName = p.Name; StudioInspector captured = inspected;
                    if (p.Directory) fileDialogs.OpenFolderDialog("Choose " + p.Label, (bool ok, string folder) => {
                        if (ok && ReferenceEquals(c.Inspector,captured)) studioPropertyDraft[propertyName] = StudioProtocol.Json(folder);
                    });
                    else fileDialogs.OpenFileDialog("Choose " + p.Label, ".*", (bool ok, string file) => {
                        if (ok && !string.IsNullOrWhiteSpace(file) && ReferenceEquals(c.Inspector,captured)) studioPropertyDraft[propertyName] = StudioProtocol.Json(file);
                    });
                }
            }
            else if (p.Type == 9) {
                string face = v.TryGetProperty("face", out var f) ? f.GetString() ?? "Arial" : "Arial";
                int points = v.TryGetProperty("size", out var z) ? z.GetInt32() : 72;
                int flags = v.TryGetProperty("flags", out var fl) ? fl.GetInt32() : 0;
                bool bold = (flags & 1) != 0, italic = (flags & 2) != 0;
                ImGui.TextUnformatted(p.Label);
                bool changed = ImGui.InputText("Font family##font", ref face, 128);
                changed |= ImGui.InputInt("Size##fontSize", ref points); points = Math.Clamp(points, 6, 512);
                changed |= ImGui.Checkbox("Bold##fontBold", ref bold); ImGui.SameLine(); changed |= ImGui.Checkbox("Italic##fontItalic", ref italic);
                if (changed) next = new { face, size = points, flags = (flags & ~3) | (bold ? 1 : 0) | (italic ? 2 : 0), style = bold ? italic ? "Bold Italic" : "Bold" : italic ? "Italic" : "Regular" };
            }
            else if (p.Type == 10) {
                string lines = v.ValueKind == JsonValueKind.Array ? string.Join("\n", v.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Object && x.TryGetProperty("value",out var path) ? path.GetString() : x.ToString())) : "";
                ImGui.TextUnformatted(p.Label + " (one entry per line)");
                if (ImGui.InputTextMultiline("##editableList", ref lines, 32768, new Vector2(-1,100))) next = lines.Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x => new { value = x.Trim(), hidden = false, selected = false }).ToArray();
            }
            else if (p.Type == 11) {
                int numerator = v.TryGetProperty("numerator",out var n) ? n.GetInt32() : 30, denominator = v.TryGetProperty("denominator",out var d) ? d.GetInt32() : 1;
                ImGui.TextUnformatted(p.Label); bool changed = ImGui.InputInt("FPS numerator##fpsNum",ref numerator); changed |= ImGui.InputInt("FPS denominator##fpsDen",ref denominator);
                if (changed) next = new { numerator = Math.Max(1,numerator), denominator = Math.Max(1,denominator) };
            }
            else ImGui.TextDisabled(p.Label);
            if (next != null) studioPropertyDraft[p.Name] = StudioProtocol.Json(next);
            ImGui.EndDisabled(); ImGui.PopID();
        }
        if (ImGui.Button("APPLY PROPERTIES##wbStudioApplyProperties")) c.Command("UpdateProperties", new { SourceId = sourceId, FilterId = studioFilterId, Settings = studioPropertyDraft });
        ImGui.SameLine(); if (ImGui.SmallButton("Refresh choices##wbStudioRefreshProperties")) c.Command("Inspect", new { SourceId = sourceId, FilterId = studioFilterId });

    }
    private void DrawStudioMixer(StudioSnapshot s)
    {
        ImGui.TextUnformatted("AUDIO MIXER — PROGRAM SCENE");
        var scene = s.Scenes.FirstOrDefault(x => x.Id == s.ProgramScene);
        if (scene == null) return;
        ImGui.BeginDisabled(s.Busy);
        foreach (var source in scene.Sources.Where(x => x.Audio))
        {
            ImGui.PushID(source.Id); ImGui.TextUnformatted(source.Name);
            if (source.Status.Length > 0) ImGui.TextWrapped(source.Status);
            ImGui.ProgressBar(Math.Clamp((source.PeakDb + 60)/60,0,1), new Vector2(-1,8), $"{source.PeakDb:0} dB");
            float db = source.Volume <= 0 ? -60 : 20 * MathF.Log10(source.Volume); bool mute = source.Muted; int offset = (int)source.SyncOffsetMs, monitor = source.Monitor; uint tracks = source.Tracks;
            bool changed = ImGui.SliderFloat("Gain (dB)##gain", ref db, -60, 12, "%.1f dB");
            changed |= ImGui.Checkbox("Mute##mute", ref mute);
            if (ImGui.TreeNode("Advanced audio##advanced")) {
                changed |= ImGui.InputInt("Audio delay (ms)##sync", ref offset); offset = Math.Clamp(offset,0,20000);
                changed |= ImGui.Combo("Monitoring##monitor", ref monitor, "Off\0Monitor only\0Monitor and output\0");
                ImGui.TextDisabled("Recorded audio tracks (stream uses track 1)");
                for (int i = 0; i < 6; i++) { bool on = (tracks & (1u << i)) != 0; if (ImGui.Checkbox((i+1)+"##track"+i, ref on)) { tracks = on ? tracks | (1u<<i) : tracks & ~(1u<<i); changed = true; } if (i < 5) ImGui.SameLine(); }
                ImGui.TreePop();
            }
            if (changed) plugin.StudioCapture.Command("Audio", new { SourceId = source.Id, Volume = db <= -60 ? 0 : MathF.Pow(10,db/20), Muted = mute, SyncOffsetMs = offset, Monitor = monitor, Tracks = tracks });
            ImGui.PopID();
        }
        ImGui.EndDisabled();
        ImGui.TextDisabled("Add desktop audio and microphone sources explicitly. Audio follows the active scene. Monitoring uses the Windows default playback device.");
    }
    private void DrawStudioOutputSettings(StudioSnapshot s)
    {
        if (!ImGui.CollapsingHeader("Video, encoding and storage##wbStudioOutputSettings")) return;
        studioOutputDraft ??= s.Output;
        var o = studioOutputDraft;
        int width = o.Width, height = o.Height, fps = o.Fps, bitrate = o.VideoBitrate, audio = o.AudioBitrate, seconds = o.ReplaySeconds, memory = o.ReplayMegabytes;
        string encoder = o.Encoder, folder = o.Folder;
        ImGui.BeginDisabled(s.Busy || s.Recording || s.Streaming || s.StreamingStarting || s.Replay || s.OutputStopping);
        ImGui.InputInt("Canvas/output width##wbStudioWidth", ref width); ImGui.InputInt("Height##wbStudioHeight", ref height); ImGui.InputInt("FPS##wbStudioFps", ref fps);
        DrawStudioTypePicker("H.264 encoder##wbStudioEncoder", s.Encoders, ref encoder);
        ImGui.InputInt("Video bitrate (kbps)##wbStudioBitrate", ref bitrate); ImGui.InputInt("Audio bitrate per track (kbps)##wbStudioAudioBitrate", ref audio);
        ImGui.InputText("Recording folder##wbStudioFolder", ref folder, 1024);
        if (ImGui.SmallButton("Browse recording folder##wbStudioFolderBrowse")) fileDialogs.OpenFolderDialog("Choose recording folder", (bool ok, string chosen) => {
            if (ok && studioOutputDraft != null) studioOutputDraft = studioOutputDraft with { Folder = chosen };
        });
        ImGui.InputInt("Replay duration (seconds)##wbStudioReplaySeconds", ref seconds); ImGui.InputInt("Replay RAM limit (MiB)##wbStudioReplayMemory", ref memory);
        studioOutputDraft = o with { Width = width, Height = height, Fps = fps, VideoBitrate = bitrate, AudioBitrate = audio, Encoder = encoder, Folder = folder, ReplaySeconds = seconds, ReplayMegabytes = memory };
        if (ImGui.Button("APPLY OUTPUT SETTINGS##wbStudioApplyOutput")) plugin.StudioCapture.Command("OutputSettings", studioOutputDraft);
        ImGui.SameLine(); if (ImGui.SmallButton("Reload saved settings##wbStudioOutputReload")) studioOutputDraft = s.Output;
        ImGui.EndDisabled();
        ImGui.TextDisabled("Stop all outputs before applying settings. MKV recording, stereo 48 kHz AAC, six recording tracks. Choose NVIDIA, Intel or AMD hardware encoding when supported by your GPU.");
    }
}
