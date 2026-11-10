# v0.38.0 — Piano Academy + Showmaster

## Windbound Piano Academy
- Original MIDI timing and full 88-key note range (A0–C8, MIDI pitches 21–108); no C3–C6 fold/transpose from FFXIV optimizer.
- Falling-note timeline, piano keyboard highlighting, left/lower and right/upper split colours.
- Play Along scored by a 200 ms hit window; Wait for Notes holds at chords until all pitches are played.
- Lesson speed 0.25–2.0×, track filter, lower/upper-range practice, split key, practice segment loop (Play Along).
- Reuses Windbound's existing Windows MIDI input, without opening the same MIDI port twice.
- Large detachable practice window remains open if the main WindboundBard window is closed.
- Piano practice monitors hardware MIDI on Dalamud's framework thread but never emits FFXIV Performance notes.

## Windbound Showmaster
- Separate tab with custom named intermission routine buttons and sequence editor.
- Allowed emotes only; commands are /bow motion, /wave motion etc (no arbitrary commands).
- Optional conductor group show cue via existing verified FFXIV party chat, with a bounded synchronised start time.
- Follower opt-in defaults to OFF. 12 steps maximum, 12 saved routines maximum, emote rate limited.
- Automatically cancels if FFXIV Performance, MIDI playback, instrument switching or combat begins.
- Includes Stop routine and Stow Instrument buttons; does not auto-start songs or send notes.

## Compatibility
- Based on complete v0.37.3 project, including portrait drag fix, in-game portraits, stage formations, instrument icons and native instrument-switch repair.
- No changes to existing PlaybackEngine, MIDI optimisation, native Ensemble Ready Check or instrument switching implementation.
- Configuration upgraded additively to 36, developer assembly version to 0.38.0.0.

## Validation status
- v0.38 source integration and archive check passes in this environment.
- `Tests/PianoAcademyShowmasterSmokeTest.csproj` included for Windows .NET 10 build/runtime test.
- Not compiled against the current Dalamud SDK and not verified in the live FFXIV client here. Use a test character before going live with a full band.
