# Validation for this v0.42.1 package

Prepared on Linux on 13 November 2026 using .NET SDK 10.0.100. Dalamud references were taken from the official distribution for API 15; the SDK package is Dalamud.NET.Sdk 15.0.0.

| Check | Result |
|---|---|
| Complete solution Release / x64 build | Passed; 0 errors, 6 warnings outside Capture Studio |
| Windows x64 self-contained native engine publish | Passed |
| Plugin and engine supplied binaries | Rebuilt from this package's source |
| Capture Studio offline protocol/controller/persistence checks | 14 passed |
| Preserved optional OBS protocol/title/offline regression checks | 18 passed |
| Native output smoke using host FFmpeg 7.1.5 | Failed: recording did not finalize within the test's 15-second timeout; replay portion was not reached |
| Windows native capture, WASAPI devices, composition, named pipes, FFXIV UI, hardware encoding and live streams | Not executed on this Linux host |

The six build warnings are CS8604 in Localization.cs, CS0675 in SoulweaverMidiExport.cs and MidiOptimization.cs, CS8600 in MidiOptimization.cs, and CS8602 in PartyChatSenderMatcher.cs and MainWindow.StageFormations.cs. None is a missing Capture Studio member.

The successful build used `dotnet build NunuWindboundBard.sln -c Release -m:1 -nr:false -p:UseSharedCompilation=false -p:Platform=x64 -p:EnableWindowsTargeting=true` with DALAMUD_HOME pointing to the downloaded references. Single-node/no-shared-compilation flags accommodate this host and are not required changes to the project.

The publish used `dotnet publish src/WindboundCaptureEngine/WindboundCaptureEngine.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=None` with the output directed into ReadyToUse/Plugin/CaptureEngine.

Compiler fixes and a successful publish do not establish recording reliability. The failed host smoke test is unresolved; it must not be reported as passed. Earlier v0.42.0 validation remains historical and does not override this result.

## NuGet source-mapping correction

Added a root `NuGet.Config` with a single nuget.org source, cleared inherited disabled-source settings and mappings, and mapped every project package to that source. This is scoped to this solution.

Reproduced the user's exact `Microsoft.Windows.SDK.NET.Ref (= 10.0.19041.57)` NU1100 with a restrictive parent NuGet configuration and a separate empty packages directory. After placing the included NuGet.Config in the child project, restore downloaded that SDK reference package from nuget.org and completed with 0 errors. The restrictive parent configuration was still loaded, demonstrating that the local configuration overrode it.

Rebuilt the full solution with restore and `--no-incremental`: passed with 0 errors and the same 6 existing warnings. C# sources and supplied binaries are unchanged from the prior v0.42.1 package; this update changes restore configuration and documentation only.

Reference: https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping

## Whole-solution build correction

`Build-Plugin.ps1` now targets NunuWindboundBard.sln rather than just the plugin project. It forces a non-incremental x64 build for the chosen Debug or Release configuration, stops on build failure and verifies both output assemblies. Both projects have ActiveCfg and Build.0 mappings for Debug/x64 and Release/x64 in the supplied solution.

Executed the same dotnet command used by the updated script in both configurations: Debug/x64 and Release/x64 each compiled both projects successfully with 0 errors and the same 6 existing warnings. Both expected output assemblies were checked after each build. PowerShell execution itself was not performed on this Linux host; the script was checked lexically.

This correction changes the build script and documentation only. Application sources and supplied binaries remain those from the validated v0.42.1 package. The prior unresolved recording smoke-test timeout remains unresolved.
