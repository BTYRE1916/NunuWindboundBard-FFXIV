# Windbound Bard v0.42.0 — our native capture suite

Windbound owns the recording suite. The plugin tab is its control cockpit, and a separate hidden Windows process does the capture, scene composition, audio mixing and output work. **OBS and libobs are not dependencies of this backend.**

| Component | Implementation |
|---|---|
| Controller and editor | Windbound's in-game Concert Capture tab |
| Window/display capture | Our Windows Graphics Capture / Direct3D11 driver |
| Scenes, overlays, transforms, fades and video effects | Our compositor |
| Desktop audio, microphone, gain/mute, delay, effects and track routing | Our WASAPI mixer |
| Recording timeline, pause/resume, replay memory ring and process lifecycle | Our engine |
| Video/audio codecs and file/network muxing | Bundled FFmpeg worker |
| Local control | Versioned current-user-only named pipe; no TCP control server |

FFmpeg is a codec/muxer component of our engine, not an OBS replacement application that the plugin remotely controls. It receives our composed BGRA pixels and our mixed PCM tracks. It does not own the scene graph or capture sessions.

## Run the supplied binaries

1. Extract the entire package into a **fresh folder**. Avoid mixing older standalone-recorder or libobs-prototype files into this project.
2. In Dalamud's development plugin locations, select `ReadyToUse/Plugin/NunuWindboundBard.dll`. Keep its accompanying audio libraries and the entire `CaptureEngine` subfolder together.
3. Open **SHOW → Concert Capture → Native Windbound capture suite**. This is now the default capture option. Check that the engine and codec paths point to **this package's** `WindboundCaptureEngine.exe` and `ffmpeg.exe` under `ReadyToUse/Plugin/CaptureEngine`.
4. Click **Launch Background Engine**. The process has no editor window. After a plugin reload, use **Connect to Running Engine**.
5. Add scenes and sources in the tab. Configure the selected source's properties, then Apply. Text/image/file selection, camera/window/device choices and filters are all edited here.
6. Select a scene to preview/edit it. **Take Selected Scene** sends it to Program. Use a zero-duration cut or a timed fade. Editing Program changes the live recording/stream.
7. Configure output resolution, FPS, encoder, bitrate, recording folder and replay limits while outputs are stopped. Start recording explicitly. **Stop & Save** finalizes the MKV and then reports its path.

Requires Windows 10 version 2004/build 19041 or newer and Dalamud API 15. The engine is a self-contained Windows x64 build with its .NET runtime. The FFmpeg worker is included. No OBS installation, OBS directory, obs.dll or OBS plugins are needed.

Startup errors are written under the plugin configuration directory at `CaptureStudioNative/engine-error.log`. Native collection files live in `CaptureStudioNative/studio.json`, with an atomic previous-version backup at `studio.json.bak`. Protocol/collection version 2 is separate from the earlier libobs prototype; its old collection is not overwritten or silently imported.

## Controls in the plugin tab

- Scenes: add, rename, remove, select, preview, cut/fade to Program.
- Sources: window, display, image, text, solid-color panel, video/media file, camera, desktop output audio and microphone.
- Overlays: text/font/color/padding, transparent images, media loop and decode dimensions. A text source can read a local text file or bind to **Use current MIDI title for this overlay**.
- Layers: visibility, ordering, position, scale, rotation, crop, fit and lock. Drag an unrotated selected overlay to move it; drag its lower-right handle to resize it. Numeric controls handle the remaining transforms.
- Video filters: color correction/opacity and chroma key. Filter enable, order, settings and removal are controlled in the editor.
- Audio: Program-scene gain, mute, meters, positive delay (0–20 seconds), monitoring and six recording buses. Simple gain, noise gate, compressor and limiter filters are provided. Stream output uses bus 1.
- Recording: H.264 + AAC in MKV, start, pause/resume the same file, stop/finalize, size/time/disk status and saved path.
- Replay: bounded RAM ring of encoded blocks, configurable duration/memory, save to one MKV. Completed two-second blocks are buffered, so a replay can end up to roughly two seconds behind live capture. Short disk staging files are deleted as blocks enter the RAM ring.
- Streaming: custom RTMP/RTMPS server and session-only key, explicit broadcast confirmation, start/stop, encoder errors and dropped-frame status.
- Engine: launch, reconnect, disconnect and idle shutdown. No capture/output command is tied to MIDI Play/Stop.

Audio follows Program; add the audio sources required by each scene. Edit-scene preview does not mix that scene's audio into Program. Monitoring a device that is also being captured can feed back; choose devices/routes deliberately.

## Lifetime and persistence

Closing the tab, disconnecting, unloading the plugin or exiting FFXIV does not send an output-stop or engine-shutdown command. Reconnect to regain control. Stop all outputs before **Shut Down Idle Engine**. One native engine runs per Windows user; plugin clients on that user share the studio.

Scenes and settings are saved; recordings, replay and streams never auto-start when the engine restarts. Stream server/key are not written to the scene collection or plugin configuration. Codec diagnostics redact the key. Every command uses a bounded framed message and request ID.

The existing OBS WebSocket option is retained solely as an optional legacy connection. It is not involved in the native backend. The native backend can run on a machine without OBS installed.

## Current engineering boundaries

This is a development build of our own suite, not verified production parity with all OBS desktop features. Windows-native capture, source devices, in-game visual behavior and live streaming still need testing on Windows. See `VALIDATION-v0.42.0.md`.

The current compositor uses CPU/GDI+ composition after D3D11 capture readback. Hardware video encoding is available through compatible NVIDIA/Intel/AMD encoders, but the compositor itself is not yet a GPU scene compositor. Start at 1080p30; 4K60 performance has not been established. Encoder names advertised by FFmpeg do not guarantee compatible installed GPU hardware; startup errors are shown in the tab.

Minimized, closed, protected or non-rendering windows may stop supplying frames. The engine reports source problems in the editor/mixer. Window selection uses the actual handle with title/process identity and rejects ambiguous matches on restoration. The native backend uses Windows capture; it does not inject a game-capture hook.

Browser/web overlays, virtual camera, HDR processing, nested scenes/groups, stingers, negative audio offset, third-party audio plugin hosting and OBS profile import are not implemented. Media file audio/video use separate decoder workers; verify synchronization for your actual media and devices. There is no software recording-duration cap; disk, memory and system throughput remain the limits. Finished replays use completed encoded blocks rather than the last unfinished live block.

## Build and verify

Open the supplied `NunuWindboundBard.sln` using the .NET 10 SDK and your Dalamud API 15 installation. The engine has an explicit source list and entry point, so stale `MainForm.cs`/designer files are excluded.

- `Build-Plugin.ps1` builds the plugin.
- `Build-CaptureEngine.ps1` publishes the native self-contained engine and copies the supplied codec worker. `-FfmpegPath` can select another compatible Windows FFmpeg executable.
- `Tests/Run-v0.42-Tests.ps1 -FfmpegPath '<path to ffmpeg.exe>'` runs protocol/controller checks, preserved OBS regression checks and the actual native codec/replay smoke test.
- Add `-NativeWindows` to run the synthetic Windows composition/preview smoke test. It uses a separate temporary profile, creates color/text layers, edits them, records with pause/resume, saves a replay and checks restoration. It does not start a stream or capture your desktop.

Play back the generated test files, then test your actual FFXIV window, microphone/output devices and encoder before a long concert. Inspect A/V timing, drop counters, source status, finalization, reconnect and available disk space.

The original FFmpeg distribution license and build/source information are in `ThirdParty/FFmpeg`. Other dependency notices are listed in `ThirdParty/NOTICE.md`.
