# Windbound Bard v0.40.2 — Visible OBS Recording Controls

1. Extract this complete project ZIP to a **new folder** and keep your previous source/build as backup.
2. Open `NunuWindboundBard.sln` in Visual Studio 2022/2026 with the compatible Dalamud SDK. Build **Release | x64**.
3. Start OBS Studio. Under **Tools > WebSocket Server Settings**, enable the server and note the password/port (default 4455).
4. In FFXIV open `/windbound` > **SHOW** > **Concert Capture**; enter OBS password, click **CONNECT TO LOCAL OBS**.
5. At the top of the Concert Capture controls you'll now see **START RECORDING**, **PAUSE**, **PLAY / RESUME**, **STOP & SAVE** at all times. A button is greyed out unless that action makes sense.
6. Start recording, pause, then resume. The record timer and paused status should change; OBS continues in the **same** recording session/file. Stop through the two-step confirmation.
7. Optional: enable the **Performance HUD**; it also has REC, PAUSE, PLAY/RESUME controls. Stop/Save remains in SHOW > Concert Capture for safety.
8. If OBS is recording but the controls still say STANDBY, check connection status and the OBS diagnostics. Recording state comes from `GetRecordStatus`, not the optional replay buffer.

**Recording is unlimited in WindboundBard.** Use OBS MKV/Hybrid MP4 for long performances and monitor disk space. OBS owns the video/audio output; WindboundBard does not play recorded videos in the plugin. To watch a completed recording, open its saved file in your preferred video player.

This is a source-only experimental update; MIDI, instruments, profiles, emotes, portraits, song overlays and other project files are preserved. No compiled DLL or video is included.
