# v0.43.0.0 validation

- Both solution projects built in Release/x64 with zero errors (6 pre-existing warnings).
- 43 Workbench tests passed, including beat-aware quantization across tempo changes,
  UTF-8 and legacy accented metadata, real MIDI round-trip, lyric export, instrument /
  octave metadata, drum splitting, source preservation, polyphony, editing and undo.
- Existing MIDI metadata and Bassdrum regression passed.
- The actual memory-loaded DLL passed x64 / version / entry-point checks, default
  configuration construction, settings serialization, Bassdrum mapping and Workbench export.
- Capture engine / FFmpeg bytes are unchanged from the previously validated release.
- In-game UI and actual Windows SoundFont output remain unverified in this environment.
