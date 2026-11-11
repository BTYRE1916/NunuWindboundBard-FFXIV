# Nunu's Windbound Bard v0.40 — Concert Capture Studio

**Source-only experimental build.** Compile against Dalamud API 15 / .NET 10 on Windows. The project includes the v0.39 suite and a local OBS WebSocket 5 control panel. It has NOT been built or run against OBS / FFXIV in this environment.

## What the plugin does / does not do

- **Does** connect manually to OBS Studio on `ws://127.0.0.1:4455`, authenticate with the OBS WebSocket 5 handshake, and send official recording / pause / resume / stop / file split / replay-buffer / stream requests.
- **Does** show OBS's recording status and elapsed recording duration. The displayed timer handles multiple days (no 24-hour wraparound).
- **Does** monitor the local recording drive's free space *on a worker task*, and report low-space warnings in the SHOW panel. The displayed free space is advisory, not a guarantee that a long concert will fit.
- **Does not** record game frames, add an encoder in the FFXIV process, modify OBS's encoder, audio sources, recording format, file path or 4K resolution, or stop a recording when MIDI playback ends.
- **Does not** start recording automatically when loading or playing a song. No automatic stream starts. The OBS password is **not persisted**.
- **Does not** split files at a time threshold itself. Optional **automatic splitting** is configured in OBS Settings > Output (if available); the plugin has a **manual Split File Now** button.

## Set up OBS (once on the recording PC)

1. Install **OBS Studio 28 or newer** (includes obs-websocket v5). Launch OBS yourself.
2. In OBS, use **Tools > WebSocket Server Settings**. Enable the server, keep **Authentication enabled**, choose a strong password, and leave the port at **4455** unless you changed it intentionally. Keep the server bound to localhost. Do **not** forward this port on your router.
3. Create a scene with **Game Capture** targeting FFXIV and add the appropriate audio sources. Game audio and microphone audio come from OBS, not Dalamud.
4. Set **Settings > Video**: Output resolution **3840×2160** for 4K, and frame rate **30** initially. Move to **60** only after verifying performance.
5. Set **Settings > Output**: hardware NVENC encoder if available, and **MKV** or **Hybrid MP4** recording. In OBS, choose the destination on a drive with plenty of free space. Check OBS's output-mode and codec options for any file-splitting setting.
6. **Open WindboundBard → SHOW → Concert Capture**, enter the OBS WebSocket password and click **CONNECT TO LOCAL OBS**. The password field is cleared immediately after connecting. The password needs to be entered again after a restart or reconnect.

## Add a live Now Playing overlay to the recording

WindboundBard can update an **OBS Text (GDI+) source** with the title of the currently loaded MIDI, even when its own window is closed.

1. In OBS, open your **concert recording scene**. Under **Sources**, add **Text (GDI+)** and name it **`Windbound Song Title`** exactly. Leave **Read from file** off.
2. In OBS, set the font, size, colour, shadow/background and position (for example, a tasteful lower-third). Keep that source **visible** in every OBS scene where you want the title recorded. You can share one title source among scenes using OBS source references/nested scenes.
3. Open **WindboundBard → SHOW → Concert Capture** and connect to OBS. Turn on **Show current MIDI title on OBS video**. Keep the default Text source name or type your exact source name.
4. With **Hide title between songs** enabled (default), it displays a MIDI filename without `.mid`/`.midi` only while the player is playing, and clears the text after the song stops. Disable the option to keep the loaded song title shown between pieces.
5. Press **SYNC SONG TITLE TO OBS NOW** after creating/renaming the source. Check the **OBS preview** to confirm it appears before making a real recording. Changes are sent in the background, normally within about 2 seconds.

**Important:** The overlay is an OBS scene source that becomes part of OBS's recorded/streamed video. It is **not burned directly into FFXIV game frames** or the bare Game Capture input. It does not change OBS recording duration, start/stop the output, or upload any song files. Only the MIDI filename's display title is sent over the localhost WebSocket; the file path stays local. A source that is missing or is not an OBS Text source is reported, and can be retried with the Sync button.

## Record a concert of any length

1. Check OBS says **Connected** and the capture preview shows the correct scene and audio levels.
2. Click **START FULL CONCERT RECORDING**. Recording runs indefinitely until you manually stop it or OBS itself stops; **song changes do not stop recording**.
3. Use **PAUSE** / **RESUME** if you want to omit an intermission, and **SPLIT FILE NOW** if you want to close one file and continue into a new file, where supported by OBS recording mode.
4. Watch the displayed duration and free-space warning. Actual high-resolution recordings can use many gigabytes per hour. Storage, encoder stability, operating-system, and filesystem limits still apply.
5. After the entire encore, click **STOP RECORDING... → YES, STOP & SAVE**. OBS finalizes the file and returns its path (when supplied by OBS).
6. Optional **Replay Buffer** and **SAVE LAST MOMENTS** controls require replay buffer configured in OBS.

## Livestreaming safety

- Streaming controls are **separate** from recording. Go Live remains disabled until you tick **I understand this will broadcast my OBS scene publicly** for that action.
- The streaming service/stream key remain configured **only inside OBS**. The plugin does not transmit, log or store stream keys.
- The plugin never automatically streams when songs start or a follower joins. Broadcasting a concert to the public may require consent from other performers and adherence to platform / music rules.
- Stopping OBS streaming is a separate confirmation and does **not** stop recording.

## Disk monitor

The OBS WebSocket `GetRecordDirectory` request supplies the configured recording directory. Free-space monitoring works for local mounted drives. If OBS is on a different drive or using a network mount, supply a local **Folder override** for warnings only. That field does not move or rename OBS recordings. OBS can run out of space or encounter hardware faults: **unlimited duration is a lack of time cap, not a guarantee**.

## Test/build commands

From the extracted project folder, use Visual Studio **Release | x64** and **Rebuild Solution**. Alternatively:

```powershell
py -3 .\Tests\Check-v0.40-Concert-Capture.py
dotnet run --project .\Tests\ObsCaptureProtocolSmokeTest.csproj -c Release
```

Optional Windows runner: `Tests\Run-v0.40-Tests.ps1 -Build` (requires local PowerShell script execution permission). It runs source + protocol tests and optionally the plugin build. **These tests do not prove it works in your live FFXIV or OBS setup.**

Test steps with OBS before opening FFXIV: connect / authenticate, start recording, status, pause, resume, split, stop and inspect file. Then test at 1080p / 30 FPS in FFXIV before going to 4K / 60 FPS. Keep your v0.39 ZIP and your own config backup in case of regressions.

## Version / release manifest

`Plugin.json` retains existing **published release download URLs** rather than inventing a v0.40 compiled release. Update those URLs and publish an actual compiled build **only after** verifying the 0.40 project locally. No GitHub push has been performed.
