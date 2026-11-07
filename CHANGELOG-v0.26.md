# v0.26 — MIDI Arrangement Inspector (Concert Suite)

This release builds on v0.25's Concert Director, automated next-song preparation and targeted single-member recovery.

**New MIDI Inspector tab:** analyses the loaded Standard MIDI File against the current C3–C6 per-lane optimisation. Shows each track/channel's source-note count, playable-note count, source pitch span, current octave shift, explicit `#+12/#-12` tag, inferred FFXIV instrument, assigned performer, out-of-range notes, duplicate same-pitch overlaps, dense chord onsets, missing track assignments and instrument conflicts for multi-track Bards. The full report can be copied for debugging.

**Optional Auto-fit untagged octaves** is explicitly triggered by the user. It leaves MIDI files, explicit octave tags, disabled lanes and saved manual octave shifts untouched. Approved lane shifts are saved to Windbound's configuration and re-analysed. No original MIDI bytes are overwritten.

Run `Tests/Run-v0.26-Tests.ps1` on Windows with .NET 10 SDK for source-level smoke tests, then build using `Build-Plugin.ps1`. Live Dalamud/FFXIV testing is necessary; no .NET compiler or running game was available during this packaging pass.
