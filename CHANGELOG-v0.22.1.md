# Windbound Bard v0.22.1 — CS8510 GM instrument switch hotfix

**Fixed:** `Core/MidiInstrumentInference.cs`, GM program mapping: removed the redundant `< 0 or > 127 => 0` switch arm. Also narrowed the first piano range from `<= 23` to `>= 0 and <= 23`, so negative values cannot accidentally become Piano. Every valid program from 0 to 127 retains its mapping; the remaining `_ => 0` arm correctly handles invalid integer values. The former duplicate coverage made the final arm unreachable and caused C# error CS8510 (line 117).

**Preserved:** All GM-to-FFXIV instrument mappings, MIDI instrument-tag precedence, octave tags, automatic party assignment, native ensemble, French UI, live keyboard, Smart Playlist and HUD logic are unchanged.

The plugin version is now `0.22.1.0`. Configuration migration remains at version 23, as no settings schema changed. Added six assertions for valid and invalid GM mappings to `Tests/MidiTrackMetadataSmokeTest.cs` and updated the static source checker to v0.22.1.

**Testing:** Run `Tests/Run-v0.22-Tests.ps1` on Windows with .NET 10, then build with `Build-Plugin.ps1`. Static source inspection and ZIP checks were run in the packaging environment, which has no .NET SDK.
