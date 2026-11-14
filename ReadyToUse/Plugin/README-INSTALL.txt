Windbound MIDI Suite 0.43.0.0 — Windows x64 / Dalamud API 15

New: STUDIO > MIDI Workbench
Import MIDI; edit tracks and notes in the piano roll; undo / redo; merge or
split voices; adapt ranges; clean duplicates and overlaps; quantize to beats;
split GM drums with custom pitch maps; preview with your SoundFont; export MIDI
and LRC lyrics. Exported MIDI can be added to the library and loaded in Windbound.
Source MIDI files and existing exports are preserved.

The native capture engine, MP4 output, HD preview and Bassdrum fix are retained.
Keep the CaptureEngine folder beside NunuWindboundBard.dll.

PUBLISHING:
Create a public GitHub release with the EXACT tag 0.43.0.
Attach FFXIVMIDIPLAYER-BY-NUNU-v0.43.0.zip unchanged.
Publish that release before replacing the repository root Plugin.json.
The manifest points to that exact tag and asset name.

VALIDATION:
Full solution Release/x64 build: 0 errors, 6 existing warnings.
43 Workbench regression checks, including real MIDI export / re-import, passed.
Existing MIDI metadata / Bassdrum regression passed.
Actual compiled DLL version, x64, memory-loaded configuration, drum-map
serialization and Workbench MIDI export verified.
In-game UI interaction and Windows audio-device playback need verification.
