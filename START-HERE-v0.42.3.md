# Windbound Bard 0.42.3 - startup fix

This full source package fixes the reported `ArgumentNullException` (`path1`) in the Configuration constructor. It includes rebuilt Release/x64 plugin binaries, the self-contained Windows capture engine and FFmpeg under `ReadyToUse/Plugin`.

## What changed

Dalamud loads plugin assemblies from memory, so reflection's `Assembly.Location` is empty. The configuration constructor previously used it to construct capture paths, causing the reported exception before the plugin could finish loading.

`CaptureEnginePath` and `CaptureFfmpegPath` now default to empty override strings. The plugin obtains the physical plugin directory from `PluginInterface.AssemblyLocation.DirectoryName` and resolves bundled paths at runtime. Custom overrides are preserved. Default paths also follow the current installation folder after a version update rather than saving an old version folder. The older capture migration no longer uses reflection paths.

## Install the supplied binaries

For a local development load, select `ReadyToUse/Plugin/NunuWindboundBard.dll` in Dalamud's development plugin locations. Keep all files and the `CaptureEngine` subfolder together. Open SHOW > Concert Capture > Native Windbound capture suite and verify the resolved engine and codec worker paths.

For the repository installer, upload the companion `FFXIVMIDIPLAYER-BY-NUNU-Latest.zip` unchanged as an asset of the latest published GitHub release, replacing the previous asset. Commit and push the root `Plugin.json` from this source package. The repository, ZIP manifest and assemblies must all advertise **0.42.3.0**. Repository download links use the latest release's named ZIP asset.

## Build source

Open `NunuWindboundBard.sln` with .NET 10 and Dalamud API 15 installed. Use Release/x64, select Build for both projects in Configuration Manager and rebuild the solution. `Build-Plugin.ps1 -Configuration Release` rebuilds both projects. `Build-CaptureEngine.ps1` publishes the standalone Windows engine.

The root NuGet.Config retains the package-source mapping fix. The plugin has no assembly/project reference to the engine, which runs outside the game.

## Verify the exact startup failure

The test loads the actual compiled plugin DLL from a stream and verifies that reflection's assembly location is empty. It constructs and deserializes real Configuration instances before a plugin service is assigned, checks bundled/custom/null paths and confirms other saved preferences are retained.

```powershell
dotnet run --project RegressionTests/StartupConfigurationSmoke/StartupConfigurationSmoke.csproj -- "ReadyToUse/Plugin/NunuWindboundBard.dll" "$env:APPDATA/XIVLauncher/addon/Hooks/dev"
```

The built DLL passed **19 checks**. The previous 0.42.2 DLL reproduced the exact null `path1` crash in the same loader. Full in-game initialization and Windows capture execution remain unverified here; see VALIDATION-v0.42.3.md.
