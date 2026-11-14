# Windbound MIDI Workbench — v0.43.0.0

Open **STUDIO → MIDI Workbench** in the plugin.

1. Choose **Import MIDI…**, or **Copy current player MIDI**.
2. Click a part to make it active. Use the Select checkboxes for multi-part edits;
   Use controls which parts are included in export and full preview.
3. Set instruments and octave tags. Source instrument / octave directives are
   read from MIDI metadata. The piano roll shows stored pitches; local audio
   applies the octave tags once. **Bake octave tags into notes** makes those
   shifts permanent in the working copy and clears the directives.
4. Use the piano roll to select notes and edit pitch, velocity, start and duration,
   add notes or delete them. Windbound's reader uses 35–60,000 ms note durations.
5. Track tools support rename, reorder, duplicate, remove, merge using the active
   instrument, split into non-overlapping voices, transpose, C3–C6 adaptation,
   duplicate removal, same-pitch overlap trimming, bounded sustain extension,
   beat-based quantization and sustained voice limiting. Undo / Redo retain up
   to 20 editing snapshots.
6. **Drum split and custom pitch map** separates a General MIDI drum kit into
   named Bass Drum, Snare Drum, Bongo, Cymbal and Other Percussion parts. Edit
   destinations / target pitches before splitting. Unmapped notes are retained;
   unchecking Keep deliberately removes that source hit. The default map keeps
   hi-hats and rides in Other Percussion for manual review. Choose whether to
   retain GM pitches or convert to playable FFXIV pitches. Already named FFXIV
   parts such as Bassdrum are preserved. Enable the GM drum-kit checkbox only
   when the selected part really contains GM drum identifiers.
7. **Preview enabled parts** / **Solo selected** use your existing SoundFont.
   Preview uses General MIDI approximations of FFXIV instruments. Choose a
   SoundFont using the button if none is configured. Game playback must be
   stopped before preview starts.
8. **Export MIDI as…** saves a new file and reopens it to validate its notes.
   **Export, add to library & load** saves into WorkbenchArrangements under the
   plugin configuration directory and adds it to the Solo or Ensemble library.
   An active performance continues; stop it before loading the new arrangement.
9. **Export source lyrics (.lrc)** saves timestamped MIDI lyric events when the
   source has them. MIDI export preserves tempo changes, meter, key and lyrics.

The source MIDI and existing output files are preserved. Working copies live
for this plugin session; export before unloading or importing another song.
Track instrument settings are stored in explicit MIDI tags for Windbound.
Standard MIDI export can represent up to 15 distinct melodic program sounds
plus drums; excessive overlapping same-pitch copies can exhaust those channels.
The exporter explains these cases instead of silently losing notes.

The native capture engine, MP4 output, HD preview, Soulweaver and Bassdrum fix
are retained from the preceding release.

## Publishing the repository update

Use **FFXIVMIDIPLAYER-BY-NUNU-v0.43.0.zip** as the GitHub release asset unchanged.
Create a public release with the exact tag **0.43.0**. Publish the ZIP asset
before updating the public repository's root **Plugin.json**. The supplied
manifest points to this exact release tag and filename. Publishing the manifest
first advertises a download that does not exist yet and results in HTTP 404.

The full-project source archive is for development. Its ReadyToUse/Plugin
directory contains the same compiled files as the installer ZIP.

## Building

Install the .NET 10 SDK and Dalamud API 15 reference files. On Windows with a
standard XIVLauncher installation, run:

```powershell
.\Build-Plugin.ps1 -Configuration Release
```

This builds both solution projects serially. To publish the standalone native
capture engine, use Build-CaptureEngine.ps1. The supplied capture EXE remains
the existing validated 0.42.4 engine; the Workbench changes are in the plugin.

```text
dotnet run --project Tests/MidiWorkbenchTests.csproj --configuration Release
dotnet run --project Tests/MidiTrackMetadataSmokeTest.csproj --configuration Release
```

The full solution builds with zero errors. Workbench regression tests include
real MIDI export / re-import, tempo changes, lyrics, named percussion, octave
metadata, undo / redo and preservation of the source file. The compiled DLL is
also checked for x64, version, memory-loaded configuration and actual Workbench
MIDI export. The in-game UI and audio device playback still need verification
in FFXIV on Windows.
