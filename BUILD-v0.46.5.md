# Build a full Windbound Bard v0.46.5 installation package

Extract the full **source** ZIP. Open PowerShell in the extracted project root.

Build the v0.46.5 plugin and reuse the already working v0.46.4 CaptureEngine, FFmpeg and Relay assets:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-WindboundRelease.ps1 `
  -PreviousReleaseZip 'E:\path\to\FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip'
```

Validate the resulting v0.46.5 install ZIP:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-WindboundRelease.ps1 `
  '.\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.5.zip' -FullInstall
```

The output ZIP must contain **`NunuWindboundBard.dll`** and **`NunuWindboundBard.json`** immediately at ZIP root. The copied support folders are `CaptureEngine/`, `Relay/` and `ThirdParty/`. Do not zip the enclosing project folder or use the 0.46.4 DLL. The build script checks manifest version against v0.46.5.0.

The original working v0.46.4 ZIP had a single wrapper folder. The updated build script handles that when *importing extras*, but the new install ZIP it creates is flat at the root for Dalamud.

Upload only the generated `dist\FFXIVMIDIPLAYER-BY-NUNU-v0.46.5.zip` asset to the **0.46.5 GitHub release**, and commit the updated root `Plugin.json` so Dalamud follows the new URL. Do not publish the new manifest before the release asset exists.

The shared Render deployment does **not** require updating for this client-side change. Different conductor bands continue using separate room codes.
