# Windbound Bard v0.31.2 — C# compiler errors CS0165 + CS0177

This is a small compiler-only hotfix of **v0.31.1 Emergency Playback Recovery**, not a rewrite of the MIDI engine or stage designer.

- **CS0165** in `MainWindow.StageFormations.cs`: Use `StageSafe(out string why)` as the first (always evaluated) condition operand. The stage-safety check supplies a useful error reason if movement isn't allowed. Native navigation still runs only after this safety check passes.
- **CS0177** in `Core/GamePartyCueProtocol.cs`: Initialise `zone` to zero before the `ValidPose` input-validation chain. All early returns now assign the `out uint zone` parameter.
- Bumped project version to **0.31.2.0** and updated the existing v0.31.1 source regression script to expect the new version.

No changes to MIDI playback, note callbacks, instrument switching, native Ready Check, Solo/Ensemble settings, French language resources, or stage movement protocol semantics.

## Build and test

1. Open `NunuWindboundBard.sln` with Visual Studio / .NET 10 + Dalamud SDK 15.
2. Restore, rebuild, and confirm the two CS errors are gone.
3. Run `py Tests/Check-v0.31.2-CSharp-Definite-Assignment.py` and `py Tests/Check-v0.31.1-Playback-Recovery.py` for source-level assertions.
4. Test Solo playback with stage editor still off, then test the native Ensemble.

**Limits:** Source checks and ZIP validation have been performed; this environment has no .NET SDK or live FFXIV for actual compilation and gameplay tests.
