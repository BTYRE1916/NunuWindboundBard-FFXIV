> Historical libobs prototype. Superseded by the native backend in [v0.42.0](START-HERE-v0.42.0.md), which does not require OBS.

# Windbound Bard v0.41.0 — background capture studio (development preview)

The plugin is the studio cockpit. A separate Windows x64 process hosts libobs and performs capture, scene composition, mixing, encoding and output. There is no separate engine editor window. The existing OBS WebSocket integration remains the default capture option.

This package includes source and compiled development binaries. Both projects compile against .NET 10 / Dalamud API 15. Live Windows capture, preview, recording, streaming and device compatibility have **not** been verified in this environment. See `VALIDATION-v0.41.0.md` before using it for a concert.

## Quick start

1. Extract the entire ZIP into a new folder. Open that folder's `NunuWindboundBard.sln` when building from source; avoid mixing the previous standalone recorder with this project. In Dalamud's development plugin locations, select `ReadyToUse/Plugin/NunuWindboundBard.dll`. Keep its accompanying audio libraries beside it.
2. Have an OBS **32.x Windows x64** installation or extracted portable runtime available. Keep its `bin`, `data` and `obs-plugins` folders together. OBS's application does not need to run. OBS 33.x is intentionally rejected until its ABI is verified.
3. Open **SHOW → Concert Capture**, choose **Background studio engine**. The engine path defaults to `CaptureEngine/WindboundCaptureEngine.exe` beside the plugin. Set the runtime root, usually `C:\Program Files\obs-studio`.
4. Click **Launch Background Engine**. This starts a hidden process. Use **Connect to Running Engine** after reloading the plugin. Startup failures are recorded in the plugin configuration folder's `CaptureStudio/engine-error.log`.
5. Add a scene. Add **FFXIV window**, **Text**, **Image**, desktop audio, microphone, or another available source type. Select a layer to configure its properties. File properties have a Browse button.
6. Select a scene to preview/edit it. **Take Selected Scene** sends it to Program with a fade; set fade duration to zero for a cut. Editing the Program scene changes the current recording/stream immediately.
7. Configure output resolution, H.264 encoder, bitrate, recording folder and replay limits while all outputs are stopped. Start recording explicitly. **Stop & Save** waits for output finalization before reporting a saved MKV path.

The self-contained engine includes its .NET runtime. OBS runtime files are not bundled. An OBS installation normally supplies the required native dependencies. Source builds require the .NET 10 SDK and a Dalamud API 15 installation.

## Editing from the plugin

- Add, rename and remove scenes. The last scene and current Program scene cannot be deleted.
- Add and remove sources, change their visibility, and move layers up/down.
- Edit text, fonts, colors, file paths, device/window choices, media lists and other native source properties. Apply properties, then Refresh Choices when a device change affects the available options.
- Edit position, scale, rotation, crop and fit-to-canvas. Lock layers against transform changes. Drag an unrotated selected layer in the edit preview to move it; drag its lower-right handle to resize it. Numeric transform controls also support rotation, flipped scaling and off-canvas placement.
- Add, configure, enable, disable, reorder and remove the available audio/video filters.
- Preview a selected scene without taking it live. Switch the preview to Program to monitor the composed output. Preview is 384×216 at roughly 4 FPS; full output resolution is independent.
- Control Program-scene audio gain, mute, meters, synchronization offset, monitoring and six recording track buses. Streaming uses bus 1. Audio belongs to its scene; add audio sources to every scene that needs them.
- Record, pause/resume the same recording, stop/save, buffer/save replays, and stream to a custom RTMP/RTMPS server. Broadcasting requires explicit confirmation. Stream credentials are held only for the current session and are not saved in the collection or plugin config.

Scene changes are saved atomically to `CaptureStudio/studio.json` with the previous version at `studio.json.bak`. Corrupt or unsupported collections fail visibly rather than overwriting the previous collection. To restore the backup, shut down the engine first, then replace `studio.json` with a copy of the backup. These are Windbound collections, not OBS Studio profile imports.

## Process lifetime

Closing the tab, disconnecting, reloading the plugin or exiting FFXIV does not stop the engine or its outputs. Reconnect to regain control. Stop all outputs before using **Shut Down Idle Engine**. The engine enforces this rule too. Force-ending the engine in Task Manager can leave an unfinished recording.

One engine runs per Windows user. Multiple clients connect to the same studio. Commands are serialized through a current-user-only named pipe. There is no TCP listener. libobs is never loaded into FFXIV's process. Selecting the OBS fallback does not stop a native output, or vice versa; the tab reports active outputs in both options when connected.

## Current boundaries

This is an OBS-style studio implementation, not feature parity with the entire OBS desktop application. It currently provides the native sources listed by its loaded modules, cut/fade transitions, H.264 + AAC, MKV recording, six audio buses, bounded replay, and custom RTMP/RTMPS output.

Qt/frontend-dependent extensions, browser/web overlays, virtual camera, stingers, nested scene/group editing, OBS profile import, audio plugin hosting and every third-party OBS extension are not implemented. Use the preserved OBS connection for those workflows. Automatic MIDI title updates remain available in the existing OBS option; native text overlays are manually editable in this version.

Window capture uses libobs's Windows Graphics Capture option for the FFXIV preset. Game Capture, Display Capture and cameras can be added through the source-type list when their modules are present. Background capture still depends on Windows and FFXIV continuing to render. Minimized, protected or exclusive-fullscreen windows can stop providing frames. The preset matches the game's window title/class/executable; with several identical FFXIV windows, review the source's window selection before recording.

Desktop audio captures the selected Windows output device, including other applications using it. Audio monitoring can feed back if routed into a desktop-audio source. Encoder availability depends on the OBS runtime, installed drivers and GPU. There is no recording time cap; storage and system performance remain the limits. Test playback and A/V synchronization on your machine before a long session.

## Build and test

- `Build-Plugin.ps1` builds the plugin against the local Dalamud installation.
- `Build-CaptureEngine.ps1` publishes a self-contained Windows x64 engine to `artifacts/CaptureEngine`.
- `Tests/Run-v0.41-Tests.ps1` runs the studio protocol/controller/persistence tests and the existing OBS protocol/transport checks on Windows.
- `Tests/Run-v0.41-Tests.ps1 -ObsRuntimeRoot 'C:\Program Files\obs-studio'` additionally runs the native smoke test. It creates a separate temporary collection with synthetic color/text sources, verifies composition/preview, records a short MKV with pause/resume, saves a replay and checks restoration. It does not capture your desktop or start a livestream. Inspect the saved MKV with a video player afterwards.

OBS's runtime and modules retain their own licenses. No OBS binaries are redistributed by this package. If distributing an OBS runtime with a future release, provide its required license notices and corresponding source obligations.

## Build fix for leftover MainForm.cs

If Visual Studio reports missing `Form`, `Label`, `Color` or `System.Windows.Forms` in `WindboundCaptureEngine/MainForm.cs`, it is compiling files from the earlier standalone recorder. This background engine has no `MainForm.cs`.

The corrected engine project explicitly lists its six source files and pins `WindboundCaptureEngine.Program` as its entry point. It excludes leftover form code and resources from its build. Extract this package into a fresh directory and open its solution, then Clean Solution and Rebuild Solution. For an existing v0.41.0 source tree that already contains all the new engine files, replacing `src/WindboundCaptureEngine/WindboundCaptureEngine.csproj` with the corrected copy is sufficient.

Verification reproduced the Forms compile errors with a deliberately added legacy `MainForm.cs`; the same folder built with zero errors and zero warnings after the project-file correction, while that legacy file remained present.
