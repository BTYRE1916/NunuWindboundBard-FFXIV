# v0.42.1 - coherent full-project build fix

- Packaged matching native Capture Studio UI, configuration, client, protocol and engine sources together.
- Removed a duplicated capture migration nested in the stage-formation migration; retained one correctly ordered migration and aligned new configuration version to 39.
- Rebuilt both plugin and Windows native engine as version 0.42.1.
- No engine reference or source patch script is required.
- Full solution compilation passed with 0 errors. See VALIDATION-v0.42.1.md for warnings and the unresolved Linux output smoke-test timeout.

- Added a solution-local NuGet.Config to resolve NU1100 caused by inherited package-source mappings; verified exact SDK package restore against a restrictive parent configuration and rebuilt the full solution.

- Fixed Build-Plugin.ps1 to rebuild both projects via the solution in Debug/x64 or Release/x64, verify both output assemblies and print their paths. Confirmed both solution Build selections and x64 mappings; documented the Visual Studio Configuration Manager correction for an older solution.
