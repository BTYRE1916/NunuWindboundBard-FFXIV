# v0.41.0 — plugin-controlled background capture studio

- Added a separate Windows x64 headless libobs 32.x engine and versioned, bounded current-user named-pipe protocol.
- Added scenes, composed edit/Program preview, cut/fade takes, sources and overlays, file browsing, native source properties, transforms/crop, layer controls and filter editing in Concert Capture.
- Added Program-scene audio meters, gain/mute, offset, monitoring and six recording buses.
- Added MKV record/pause/resume/stop, replay buffer/save, custom RTMP streaming, encoder/video/storage settings and output lifecycle acknowledgement.
- Added atomic collection persistence, previous-version backup, explicit idle shutdown and reconnect support. Plugin disposal never stops engine outputs.
- Retained the original OBS option as the default; its client and transport implementation are unchanged. Updated the HUD to control the selected capture option and open the cockpit directly.
- Corrected two pre-existing OBS test harness issues: a missing title-overlay compile dependency and an outdated disconnect-status text assertion.
- Added offline validation and a Windows native smoke test. Compiled against actual Dalamud API 15 references; Windows hardware/live capture testing is outstanding.

See `START-HERE-v0.41.0.md` for installation, supported features and current boundaries.

## Build correction

The background engine now compiles an explicit source list and specifies its headless entry point. Leftover `MainForm.cs`, designer code and form resources from an older standalone recorder no longer enter the engine build. Verified against a folder containing a legacy form that reproduces the reported errors. No Windows Forms dependency is added.
