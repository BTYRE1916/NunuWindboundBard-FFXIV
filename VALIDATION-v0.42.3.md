# Validation for 0.42.3

Environment: Linux, .NET SDK 10.0.100 and actual Dalamud API 15 references.

| Check | Result |
|---|---|
| Previous 0.42.2 Configuration loaded from memory | Reproduced the user's exact ArgumentNullException with parameter path1 |
| Fixed 0.42.3 plugin clean Release/x64 build | Passed; 0 errors and the same 6 unrelated existing warnings |
| Native Windows x64 self-contained engine publish | Passed |
| Actual fixed plugin DLL loaded from memory | 19 startup/configuration/path/deserialization checks passed |
| Custom engine/codec paths and other saved preferences | Preserved by the regression checks |
| Defaults after an install directory/version change | Resolve to the newly supplied plugin directory without persisting it |
| Missing/null/blank path values | Handled without constructing a path from null |
| Full Windows/Dalamud plugin startup and native device capture | Not executed on this Linux host |

The regression project is RegressionTests/StartupConfigurationSmoke. It loads the actual compiled Configuration rather than a duplicated implementation or stub configuration. Its --expect-old-failure option can reproduce the bug against a previous DLL. The old DLL is not included in this release.

The 0.42.3 fix changes the application version and capture-path initialization/resolution only. No music, ensemble or native recording/encoding behavior was changed. The previously reported Linux recording-finalization smoke-test timeout remains unresolved; these startup checks do not establish recording reliability.

Reference: https://dalamud.dev/news/2024/03/11/net8-upgrade/ documents the unavailable reflection assembly location and the supported AssemblyLocation property.
