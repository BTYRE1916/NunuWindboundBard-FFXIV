# Build Windbound Bard v0.47.1 on Windows

Open PowerShell in the extracted folder containing `NunuWindboundBard.sln`. The build uses the .NET 10 SDK and Dalamud API 15 development files.

## Build and check the full installer

Set `$previousRelease` to the location of your working v0.46.4 full **installer ZIP**. The build compiles the v0.47.1 plugin, publishes the v0.47.1 native capture engine and takes FFmpeg, relay support and third-party files from that installer.

```powershell
$previousRelease = 'E:\path\to\FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build-WindboundRelease.ps1 -PreviousReleaseZip $previousRelease
```

After the build succeeds, check the completed archive.

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Test-WindboundRelease.ps1 .\dist\FFXIVMIDIPLAYER-BY-NUNU-v0.47.1.zip -FullInstall
```

For GitHub distribution, attach `dist\FFXIVMIDIPLAYER-BY-NUNU-v0.47.1.zip` to tag `0.47.1` before publishing the updated repository manifest `Plugin.json` and the contents of `docs\`. The repository manifest points Dalamud to that exact asset.

## Build just the plugin

```powershell
dotnet build .\src\NunuWindboundBard\NunuWindboundBard.csproj -c Release -p:Platform=x64
```

The Cloudflare Worker is a separate deployment. See [the relay guide](CloudflareRelay/README.md) for its setup commands.
