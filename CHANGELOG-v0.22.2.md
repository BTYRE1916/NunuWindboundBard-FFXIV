# Windbound Bard 0.22.2 — Metadata detection and application hotfix

- Capture FF01 text, FF06 marker and FF07 cue tags in addition to existing FF03/FF04.
- Preserve earlier tags even after later FF03 track-name edits.
- Inherit unambiguous channel Program Changes from conductor/meta-only tracks in format-1 files.
- In Solo, infer an instrument from selected MIDI lanes when no manual preference exists.
- Treat explicit Solo Keep Current as a manual override, not a request to resume auto detection.
- Prefer meaningful track names to generic GM Piano patches; unchanged explicit tags and FF04 take precedence.
- Add per-track effective octave and instrument source diagnostics plus a copyable report.
- Add an FF01/FF06 sample and regression coverage for these cases.
- Retain solo, native ensemble, group STOP/stow, Live MIDI Piano, Soulweaver, HUD, Smart Playlists and translation.
- No background MIDI-byte modifications or note engine changes.

Run Tests/Run-v0.22.2-Tests.ps1. Linux/Windows FFXIV runtime tests are required.
