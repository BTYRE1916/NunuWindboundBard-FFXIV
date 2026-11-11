# WindboundBard v0.40.2 — OBS Recording Transport Controls

- **START, PAUSE, PLAY/RESUME and STOP & SAVE** are always visible in SHOW > Concert Capture. Buttons are disabled when OBS is not connected or the requested action is unavailable.
- The Performance HUD shows REC, PAUSE and PLAY/RESUME side by side, even before recording begins. STOP & SAVE is confirmed in the full Concert Capture window.
- The recorder updates its UI as soon as OBS accepts a recording command, without waiting for the next polling interval.
- OBS `GetRecordStatus` updates recording and pause state **before** optional streaming/replay-buffer queries. A disabled replay buffer no longer hides recording controls.
- START, PAUSE and RESUME are operations on the **same OBS recording session/file**, not a new recording each time.
- Recordings are not limited by WindboundBard: OBS, available disk and filesystem limits apply.
- The existing song title overlay, 4K recording options, streaming, MIDI, Showmaster, Piano Academy and Stage Formations remain unchanged.
- No OBS login details are saved in plugin config, no automatic recording/streaming starts, and no new permissions are required.

## Verification

Source-only patch. We could not run a native Dalamud/.NET build or a live OBS/FFXIV session here. Run the new `Tests/Check-v0.40.2-OBS-Transport.py` and `Tests/ObsRecorderTransportSmokeTest.csproj`, then test in OBS manually.

To verify: connect OBS, press START, PAUSE, PLAY/RESUME, then STOP & SAVE. Verify the *same* recording file grows after resuming. Start a recording directly from OBS and verify WindboundBard shows recording status within a few seconds.
