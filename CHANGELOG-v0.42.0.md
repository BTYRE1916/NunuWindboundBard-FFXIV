# v0.42.0 — native recording-suite backend

Replaces the libobs prototype with Windbound's own backend. No OBS runtime or OBS module is loaded by the capture engine.

- Added our Windows Graphics Capture / D3D11 capture driver and own scene compositor for window/display, images, text, color panels, media and camera sources.
- Added our WASAPI mixer, per-source gain/mute/metering/delay, monitoring, effects and six recording buses.
- Added our recording timeline, pause/resume transport, output lifecycle, bounded replay RAM ring and replay save logic.
- Bundled FFmpeg as a codec/muxer worker for our composed pixels/mixed PCM; it does not control scenes or capture windows.
- Retained the plugin cockpit: scene/source/overlay editing, properties, transforms/crop, filters, preview, cut/fade takes, outputs and engine lifecycle.
- Added native MIDI-title text-file binding. Made the native backend the default and changed launch configuration to engine/codec paths.
- Bumped controller/collection protocol to version 2 and used a separate native profile directory.
- Retained the existing OBS WebSocket connection as a separate optional legacy workflow.
- Kept the explicit headless build input list so leftover Forms recorder files cannot enter the engine build.

Validated builds, core protocol/persistence tests, preserved OBS regression checks, and an actual synthetic codec test covering six AAC tracks, pause/resume timing, decoded pixels, replay save and staging cleanup. Windows live capture/visual/device tests remain outstanding.
