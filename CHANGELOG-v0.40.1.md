# WindboundBard v0.40.1 — OBS Now Playing reliability fix

**Local development source; not a verified compiled release.** Based on the v0.40 Concert Capture + Now Playing project.

## What changed

- **Independent overlay updates:** OBS text changes are now checked ~350 ms apart, regardless of failures from optional recording/replay/status requests. Recording status continues approximately every two seconds.
- **Discover OBS text sources:** reads `GetInputList` on connection and on demand; pick an existing Text source by its actual name instead of guessing.
- **Explicit CREATE SOURCE action:** creates a Windows OBS Text (GDI+) source in the *current program scene*, but only when the user presses the button; existing inputs are not overwritten.
- **Test title (10 seconds):** quickly confirms whether OBS can receive and show a message even without a MIDI playing.
- **Read from file diagnostics:** detect `inputSettings.read_from_file=true` and explain why setting the OBS `text` property would not display an overlay.
- **Local text-file fallback:** always maintains `Windbound-NowPlaying.txt` within the Dalamud plugin config directory when the title feature is enabled. OBS Text (GDI+) can point at it using **Read from file**. Writes happen asynchronously and only when the title changes; no Internet or WebSocket connection needed for this option.
- **Better diagnostics:** displays the current MIDI basename, whether this client is actually playing, current OBS scene, source name/type issues, and a copyable diagnostics summary.
- **Text source kind compatibility:** accepts `text_ft2_source_v2` where available. Existing GDI+ v2/v3 support is retained.
- **Unchanged recorder:** Start/Stop, pause/resume, stream, file split, recording duration, and all MIDI, instruments, ensemble, and stage logic are unchanged.

## Limits

- The title comes from the **currently loaded MIDI filename**, with `.mid`/`.midi` removed. A conductor with no local MIDI output may show `IDLE`; turn off **Hide title between songs** in this case if you still want the selected title displayed.
- The OBS source must be in the visible recording/program scene. Creating or configuring an OBS source cannot guarantee an existing scene/layout actually includes it.
- This repository has not been compiled against Dalamud or exercised with a running OBS/FFXIV client in the packaging environment. Perform the Release/x64 build and two OBS test paths described in `START-HERE-v0.40.1.md`.
- The project's `Plugin.json` release links are intentionally left pointing to the published build until you upload a new release.
