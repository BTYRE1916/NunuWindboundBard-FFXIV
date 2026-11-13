# 0.42.4 - MP4 and HD preview

- Added a persisted MKV/MP4 recording-format selector. Recording and saved replay filenames/muxers follow that selection; MP4 gets faststart metadata. Existing profiles default to MKV.
- Added bounded high-quality JPEG preview transfer at 540p, 720p or 1080p, selectable 5/10/15 FPS, aspect-preserving scaling and asynchronous image encoding/texture uploads.
- Added an independent, resizable preview window that remains usable when the main window is closed.
- Added explicit engine capability reporting, with legacy RGBA preview compatibility and restart guidance for older running engines.
- Removed startup coupling between video and audio pipe writers. The old arrangement reproduced a finalization timeout at MP4/30 FPS; independent bounded writers now pass actual pipe recording, pause/resume, finalization and replay for MP4 and MKV.
- Retained the memory-loaded configuration startup fix and LFS rules for the bundled executables.
