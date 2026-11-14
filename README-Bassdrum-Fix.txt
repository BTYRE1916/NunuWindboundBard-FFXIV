Windbound MIDI Suite: Bassdrum source fix

Apply to the source project and rebuild the plugin.

1. Extract this archive into the project root (the folder containing src and Tests).
2. Replace src/NunuWindboundBard/Core/MidiInstrumentInference.cs and the matching test file.
3. Rebuild the plugin in Release/x64, then package the resulting plugin output for installation.

This patch was prepared against GitHub master commit
c2d731673f57753ca016c94bc138e0e3fff5f259.

Cause
The old name matcher recognised "bass drum" but missed "Bassdrum".
It then matched the generic "drum" fallback, which selected Bongo (ID 11).
The correct Bass Drum instrument ID is 12; the instrument catalog already had it right.

Changes
- Recognise Bassdrum, BassDrum, Bass Drum, bass-drum and bass_drum.
- Preserve an explicitly named percussion part on MIDI channel 10 instead of
  treating an already arranged FFXIV note pitch as a General MIDI drum identifier.
- Keep General MIDI note voting for generic track names such as Drums and Kit.
- Retain the existing instrument IDs and note pitches.

Validation
Reproduced the old Bassdrum-to-Bongo failure with the actual C# regression test.
The updated Tests/MidiTrackMetadataSmokeTest.csproj compiles and passes under .NET 10.
Coverage includes spelling variants, track names, FF04 instrument names, FF01/FF06
tags, channel 10 parts, party assignment and preservation of note pitches.
The test build reports two pre-existing warnings in MidiOptimization.cs.
The complete Dalamud plugin was not rebuilt or exercised in FFXIV for this source patch.

Run the regression test from the source project root:
dotnet run --project Tests/MidiTrackMetadataSmokeTest.csproj --configuration Release

For a temporary workaround with an existing build, put #Bass Drum (with a space)
in the MIDI track name. Existing builds recognise that explicit tag before drum-note inference.
