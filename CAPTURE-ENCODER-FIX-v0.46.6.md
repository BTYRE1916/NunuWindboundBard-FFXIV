# Windbound Bard v0.46.6 — Capture Studio encoder fix

## Diagnosis

The Capture Studio video/audio command builder in `src/WindboundCaptureEngine/OutputPipeline.cs`
prepended `-thread_queue_size 8` before `-i pipe:0` and `-thread_queue_size 32`
before each audio input. Recent FFmpeg builds interpret that option as output-only,
and abort with `Encoder failed (-22)` / `Option thread_queue_size ... cannot be applied to input url pipe:0`.

**Fix:** remove input-side `-thread_queue_size`; retain existing bounded .NET video/audio queues.
The full native engine executable **must be rebuilt** for the change to take effect.
Rebuilding only `NunuWindboundBard.dll` does not fix the error.

## Full Windows release using your v0.46.4 working asset

From this **v0.46.6 source** folder run in PowerShell:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Build-WindboundRelease.ps1" `
    -PreviousReleaseZip "E:\NunuWindboundBard-FFXIV-v0.1.1-Background\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip"
```

The script now builds the plugin AND `src/WindboundCaptureEngine/WindboundCaptureEngine.csproj`.
Only unchanged FFmpeg and relay assets are retained from the old release; its old
`WindboundCaptureEngine.exe` is overwritten by the freshly published executable.
It also moves the nested `CaptureEngine\ffmpeg\ffmpeg.exe` to `CaptureEngine\ffmpeg.exe`.

Validate with:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File ".\Test-WindboundRelease.ps1" `
    ".\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.6.zip" -FullInstall
```

Upload the new `dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.6.zip` as GitHub tag `0.46.6`, and
publish the updated root-level repository `Plugin.json` to point to it.
Do not upload the full source ZIP as the Dalamud install asset.

## If your custom Capture Engine is already published

Pass `-CaptureEngineDirectory "C:\path\to\NEW\publish"` alongside `-PreviousReleaseZip`.
This avoids building the engine twice. The directory must contain the patched `WindboundCaptureEngine.exe`
and its full publish dependencies, not a copied v0.46.4 executable.

## Verification

After installing v0.46.6, restart the Capture Engine process (restarting the game
also works). Re-test **Software H.264 (libx264)** before hardware encoder options.
A successful start should no longer report the `thread_queue_size` input error.
Other failures should be inspected separately using the diagnostics panel.
Run `dotnet run --project RegressionTests/RecordingPreviewSmoke/RecordingPreviewSmoke.csproj`
for the argument regression assertions; pass the path to ffmpeg.exe to run the real
container and replay checks.

Note: Source packaging and static checks can be verified without Windows, but native
recording still needs testing on the target Windows machine.
