# Windbound Bard v0.18.1 — C# CS0136 compiler hotfix

**Issue:** `Core/GamePartyCueProtocol.cs:140` used `out int index` inside the NativeFile branch while the same `TryParse` method declared `out int index` again for the legacy PLAY branch around line 204. C# rejects the overlapping local declarations with CS0136.

**Fix:** Rename only the NativeFile branch's local to `fileIndex` in its `ParseIndex` call and subsequent `GamePartyCue` constructor. No protocol bytes or behavior change.

**Also updated:** project version to `0.18.1.0` and v0.18 static check to accept the hotfix and verify that rename.

Everything else is identical to v0.18, including instrument SET, automatic MIDI file-selection, default-on Auto Accept Ready Check, native game ensemble START, Soulweaver and the Smart Playlist Manager.

Run `Tests/Run-v0.18-Tests.ps1` and `Build-Plugin.ps1` in PowerShell with the .NET 10 SDK and Dalamud.NET.Sdk 15. This environment does not have .NET, so compilation and live FFXIV verification remain necessary.
