# Windbound Bard v0.43.1

The main Windbound window has a **Hide Game Performance HUD** button below the language selector. It makes the native Performance keyboard, guitar tone selector, and metronome transparent while their game windows stay open for playback. **Show Game Performance HUD** restores them. The toggle starts off each time the plugin loads.

The developer build passed an in-game test with the new toggle. This release uses assembly version `0.43.1.0` and Dalamud API 15.

## Publish to GitHub

1. In `BTYRE1916/NunuWindboundBard-FFXIV`, create a public release with tag **`0.43.1`**.
2. Attach **`FFXIVMIDIPLAYER-BY-NUNU-v0.43.1.zip`** as a release asset and publish. Use this filename exactly. The tag begins with `0` and the filename contains `v0.43.1`.
3. Open the installer link below and confirm it downloads. Then copy the full project's root `Plugin.json` to the GitHub repository root on `master` and push. Both Dalamud install and update links point to this asset.

https://github.com/BTYRE1916/NunuWindboundBard-FFXIV/releases/download/0.43.1/FFXIVMIDIPLAYER-BY-NUNU-v0.43.1.zip

The installer ZIP contains `NunuWindboundBard.dll`, its `NunuWindboundBard.json` manifest, audio dependencies, and `CaptureEngine/` at the ZIP root. Keep that structure when using the package. Publish the compiled installer as the release asset. Commit the source and repository manifest as ordinary Git changes. Both `CaptureEngine` executables are already bundled inside the installer and should be distributed through Releases to avoid GitHub's large-file limit.

## Build from the full project

Open `NunuWindboundBard.sln` in Visual Studio, select **Release | x64**, and rebuild with the .NET 10 SDK and Dalamud API 15 references. The PowerShell equivalent is:

```powershell
.\Build-Plugin.ps1 -Configuration Release
```

The full project's `ReadyToUse/Plugin/` folder contains the same compiled payload as the installer ZIP. The native capture worker and FFmpeg are the previously shipped Windows binaries.
