# v0.46.4 — fix for Dalamud install `NunuWindboundBard.json` not found

## Cause

The **full project source** ZIP cannot be given to the Dalamud plugin installer. Its entire tree is under a project folder and it has no compiled plugin DLL or *local install* manifest at archive root. The root-level `Plugin.json` is the **repository index** (an array); it is not the plugin's local installation manifest.

For Dalamud API 15, the *downloaded plugin release ZIP* must have these files **at ZIP root** (not in a containing folder):

```
NunuWindboundBard.dll
NunuWindboundBard.json
[other required plugin runtime DLLs and assets]
CaptureEngine/                    # optional for native concert recording
  WindboundCaptureEngine.exe     # if you publish the capture engine
  ffmpeg.exe                     # if you publish your licensed FFmpeg binary
```

The zip's `NunuWindboundBard.json` must be a **single JSON object** matching the DLL/version; the remote repository `Plugin.json` remains an **array** and should be published separately as your repository index.

## What changed in the full source

- Added `src/NunuWindboundBard/NunuWindboundBard.json`: the local **flat** manifest *template* with metadata. The `Dalamud.NET.Sdk` (15.0.0) uses this template to generate the install manifest, including `InternalName`, `AssemblyVersion` and `DalamudApiLevel`.
- Added `Build-WindboundRelease.ps1`, which builds the **plugin** in Release/x64, finds the SDK's packaged `latest.zip` (or its loose build files), checks that the DLL and local manifest are beside each other at the root, optionally adds your separately published studio engine and ffmpeg, and emits `dist/FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip`.
- Added `Test-WindboundRelease.ps1` so a manually produced ZIP can be checked before publishing.
- **No changes** to relay, MIDI or playback code in this packaging-only correction. The previously delivered v0.46.4 START acknowledgement patch is preserved.

## Windows build / release procedure

Open PowerShell in the **extracted project root** (where `NunuWindboundBard.sln` is located), then run:

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindboundRelease.ps1
powershell -ExecutionPolicy Bypass -File .\Test-WindboundRelease.ps1 .\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip
```

For studio capture, first build/publish `WindboundCaptureEngine` for Windows. Supply the directory with `WindboundCaptureEngine.exe` and your own `ffmpeg.exe` path (not supplied in this source project):

```powershell
powershell -ExecutionPolicy Bypass -File .\Build-WindboundRelease.ps1 `
  -CaptureEngineDirectory 'C:\your-published-engine-folder' `
  -FfmpegExe 'C:\your-ffmpeg-folder\ffmpeg.exe'
```

Upload **only** `dist/FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip` as the *install/download asset* for the **0.46.4** GitHub release tag. Do **not** upload the source ZIP as the install asset. The repository index `Plugin.json` already points to this exact release tag and asset filename.

If you already uploaded a broken asset under that name, delete/replace the broken release asset, and retry the install after the new asset is publicly downloadable. A packaging-only correction at the same version does not force an update for already-installed clients; bump the version if you need existing installations to auto-update.

## Verification limits

This source artifact was checked for its manifest template, repository version consistency and ZIP integrity. A Windows/.NET SDK is not available here to run the PowerShell build or the Dalamud installer. Run the build and validation commands on your PC before publishing; only the real compiled release can be fully validated.
