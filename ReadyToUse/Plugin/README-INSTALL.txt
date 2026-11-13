Nunu Windbound Bard 0.42.3 - startup path fix
Windows x64 / Dalamud API 15

The plugin, dependencies and plugin JSON are at this ZIP's root.
The native engine, FFmpeg and codec notices are under CaptureEngine/.
Keep every supplied file and subfolder together.

Repository installation:
1. Upload this ZIP as FFXIVMIDIPLAYER-BY-NUNU-Latest.zip on the repository's
   latest published GitHub release, replacing the previous asset of that name.
2. Commit and push the matching companion Plugin.json at the repository root.
3. Refresh the repository in Dalamud and install/update the plugin.

Local development load:
Extract the ZIP to a folder, then select NunuWindboundBard.dll in Dalamud's
Experimental > Dev Plugin Locations. The DLL and JSON must stay together.

This release fixes the Configuration constructor's null path1 exception.
Default capture paths now resolve from Dalamud's actual plugin directory,
not reflection's empty Assembly.Location. Custom paths remain configurable.

Validation: clean Release build, self-contained Windows engine publish,
19 checks against the actual plugin DLL loaded from memory, and package
version/layout/dependency/CRC checks. The previous compiled version was
confirmed to reproduce the user's exact exception under the same loader.
Full in-game initialization and Windows capture have not been run here.
The earlier Linux recording finalization timeout remains unresolved;
recording reliability is not established by this startup fix.

The source and regression test are in the companion full-project archive.
