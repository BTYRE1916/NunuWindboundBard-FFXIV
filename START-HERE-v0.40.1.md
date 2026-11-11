# WindboundBard v0.40.1 — Fix your OBS song title overlay

This package is the **complete Visual Studio source**, not a precompiled DLL.

## Rebuild

1. Extract this ZIP into a fresh folder (do not overwrite your known-good build).
2. Open `NunuWindboundBard.sln` in Visual Studio; select **Release | x64**.
3. Rebuild, then load the resulting DLL through Dalamud developer plugins.
4. Start OBS Studio and enable its WebSocket server under **Tools > WebSocket Server Settings** (default port 4455) if using the WebSocket route.

## Option A: direct OBS WebSocket title (recommended)

1. In WindboundBard open **SHOW > Concert Capture** and connect to OBS.
2. Enable **Show current MIDI title on OBS video**.
3. Press **Refresh OBS text sources**; select your OBS text source from the dropdown, or press **CREATE TEXT SOURCE IN OBS SCENE**. The latter explicitly creates a new Windows Text (GDI+) input in OBS's current *program* scene.
4. Check that the OBS source is visible in the scene being recorded. In its properties **Read from file must be OFF**.
5. Press **SHOW TEST TITLE (10 seconds)**. You should see `WINDBOUND BARD — TEST TITLE` in the OBS preview.
6. Load a MIDI into WindboundBard and play it. OBS should update to the filename without its extension, usually within a second.
7. If you want the title displayed even before playback, **uncheck Hide title between songs**. This is also useful for a conductor not assigned a local MIDI part.

Read the detailed **Title overlay** status message under the controls. If it reports the source is not found, select the exact name. If it reports **Read from file**, either turn that option off or use Option B below.

## Option B: local text-file title (no WebSocket required)

1. Enable **Show current MIDI title on OBS video** in WindboundBard. OBS connection itself is optional.
2. In **SHOW > Concert Capture**, copy the displayed `Windbound-NowPlaying.txt` path with **COPY TITLE FILE PATH**.
3. In OBS, create/edit a **Text (GDI+)** source, enable **Read from file**, and paste that exact path into the text-file field. Ensure the source is visible in the concert scene.
4. Press **SHOW TEST TITLE (10 seconds)**. OBS should display the test message from the text file.
5. Play your MIDI normally. The title file changes only when the song/display state changes. No recording stop is triggered.

**Do not enable both modes on the same OBS source:** if **Read from file** is enabled, the WebSocket `text` property will not be displayed. This is expected OBS behaviour.

## If the title is still blank

- Confirm **Current MIDI** in WindboundBard shows your selected file, not **No song loaded**.
- Confirm **Local MIDI output** says `PLAYING`, or uncheck **Hide title between songs** for a silent conductor.
- Confirm the OBS source is visible, correctly positioned and unhidden in the recorded *program* scene, and that its font/text extents have enough space.
- Press **SHOW TEST TITLE**, inspect **Title overlay** status, then press **COPY TITLE DIAGNOSTICS** and share the text without any passwords.
- The recording keeps running if the title overlay fails. The title controls do not affect MIDI note output, recording duration or stream state.

## Source tests

Run from the project root:

```powershell
py -3 .\Tests\Check-v0.40.1-NowPlaying.py
```

If your system has the required .NET 10 SDK, you can also run:

```powershell
dotnet run --project .\Tests\ObsCaptureProtocolSmokeTest.csproj
```

These checks cannot replace a live OBS/FFXIV test.
