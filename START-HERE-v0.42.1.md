# Windbound Bard v0.42.1 - full project build fix

This is the complete matching native-suite project, not a patch. It includes the solution, plugin source, native engine source, tests, assets, bundled FFmpeg and rebuilt Windows binaries.

## NuGet restore fix included

Keep the supplied `NuGet.Config` beside `NunuWindboundBard.sln`. It clears inherited package sources/mappings for this project and maps all project packages, including `Microsoft.Windows.SDK.NET.Ref` version `10.0.19041.57`, to nuget.org. Your user-wide NuGet configuration is not edited.

For the previously reported NU1100 error, close Visual Studio, extract this complete package to a fresh folder, reopen the included solution and choose **Restore NuGet Packages**, then **Rebuild Solution**. If working in an existing folder, copying only this package's `NuGet.Config` beside that folder's solution is sufficient for this restore configuration change. If a cached restore error remains, close Visual Studio and delete only `src/WindboundCaptureEngine/obj` before restoring again.

This was verified with a fresh package directory: the old mappings reproduced the same NU1100 failure, and the included solution configuration restored the exact SDK package successfully. The complete Release/x64 solution then rebuilt with 0 errors and 6 existing warnings.

## Open and build

1. Extract this ZIP into a fresh folder. Open the solution inside the extracted `NunuWindboundBard-v0.42.1-FullProject` folder rather than the older solution on E:.
2. Open `NunuWindboundBard.sln` in Visual Studio with .NET 10 and the compatible Dalamud API 15 installation.
3. Select **Release | x64** and **Rebuild Solution**.

The solution already includes both projects. Do not add `WindboundCaptureEngine` as an assembly/project reference to the plugin. The native engine runs as a separate process; the plugin uses `Core/CaptureStudioClient.cs` and the shared protocol source to communicate with it.

There is no Apply-CaptureStudioBuildFix.ps1 step. The source has already been corrected.

## Both projects must be selected for build

In Visual Studio, use **Build > Configuration Manager**. Choose **Release** and **x64**. For both `NunuWindboundBard` and `WindboundCaptureEngine`, select **Release**, platform **x64**, and check **Build**. Repeat for Debug/x64 if you use Debug. Close the dialog and choose **Build > Rebuild Solution**.

The supplied solution already contains these mappings for both projects in both configurations. The original repository's older solution maps the capture engine to Any CPU; when using the newer x64-only engine, use the matching solution from this package or update those mappings in Configuration Manager. Building just the plugin project does not build the separate engine.

`Build-Plugin.ps1` now rebuilds the **whole solution**, checks that both output assemblies exist and prints both paths. Run it from a PowerShell terminal with `./Build-Plugin.ps1 -Configuration Release` (or Debug). No engine assembly reference is needed.

To build only the native engine, independently of Visual Studio's selected projects, run:

```powershell
dotnet build .\src\WindboundCaptureEngine\WindboundCaptureEngine.csproj -c Release -p:Platform=x64 --no-incremental
```

If Visual Studio still reports a skipped build, its exact next output line distinguishes an excluded project from an unsupported configuration or a project already up to date.

## Supplied binaries

Use `ReadyToUse/Plugin/NunuWindboundBard.dll` as the development plugin location in Dalamud. Keep all its accompanying dependencies and the entire `CaptureEngine` subfolder together. Both the plugin and the self-contained Windows x64 engine were rebuilt from this source.

Open **SHOW > Concert Capture > Native Windbound capture suite**. The engine path should point to the supplied `CaptureEngine/WindboundCaptureEngine.exe`, and the codec worker path to `CaptureEngine/ffmpeg.exe`. The native backend does not require OBS. After rebuilding just the solution, use `Build-CaptureEngine.ps1` to publish a standalone engine if needed, then keep the published engine and codec worker beside the built plugin under a `CaptureEngine` subfolder.

## Reported build errors addressed

- `Configuration` contains `CaptureBackend`, `CaptureEnginePath`, `CapturePreviewEnabled`, and the native codec-worker setting `CaptureFfmpegPath`.
- The matching Capture Studio screen uses `CaptureFfmpegPath`; there is no remaining `CaptureObsRuntimeRoot` reference from the old OBS runtime prototype.
- `Plugin` owns and initializes `StudioCapture` as the real `CaptureStudioClient`, and disposes the controller on unload.
- The capture configuration migration appears once, after the earlier migrations. New configuration defaults use version 39.
- The engine uses an explicit source list and entry point; stale WinForms recorder files are excluded.

## Validation

The full solution compiled in Release | x64 with .NET SDK 10.0.100 and actual Dalamud API 15 references: **0 errors, 6 warnings outside Capture Studio**. The native engine also published successfully as a self-contained Windows x64 executable. The 14 offline Capture Studio checks passed.

The Linux recording/replay smoke test timed out during recording finalization with the host's FFmpeg 7.1.5. Recording reliability is not established by this compiler fix. Windows capture, devices, in-game preview and live outputs have not been run here. See `VALIDATION-v0.42.1.md`; historical v0.42.0 validation describes an earlier preparation environment.
