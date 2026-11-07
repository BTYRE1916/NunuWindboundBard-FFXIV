# Nunu's Windbound Bard v0.27 — Local SoundFont MIDI Audio Preview

Builds on v0.26 Concert Director Complete Suite with **no changes to native
FFXIV Performance, auto-accept, ensemble timing, party control or MIDI input**.

## Features
- New **Audio Preview / Aperçu audio** tab, available in Solo or Ensemble mode.
- Choose a local, user-supplied `.sf2` SoundFont with a file picker or full path.
- Three modes: **Original MIDI** with full program changes, controller events
  and MIDI tempo map; **Windbound C3–C6 arrangement** as currently prepared for
  this client; **individual MIDI track** with optional saved octave shift.
- Local Play / Pause / Resume / Stop, volume 0–100%, tempo 50–200%, elapsed
  time/progress. Original MIDI and generated preview audio are independent of
  FFXIV game output and do not open Performance or send party-chat commands.
- Uses **MeltySynth 2.4.1** (MIT) for SoundFont rendering and **NAudio 2.2.1**
  (MIT) for Windows WinMM playback, with float-to-16-bit PCM conversion.
- Loads SoundFont data on a background thread to avoid stalling the game UI.
  Preview uses a dedicated audio thread, never the FFXIV native note callback.
- Automatically cancels local preview when the source MIDI/arrangement changes
  or live FFXIV MIDI-file playback begins. It will NOT interfere with Live MIDI
  Piano hardware input; disable live hardware ARM if you want only speaker sound.
- English / French UI translations and persistent local SoundFont path, preview
  speed and preview volume. No user SoundFont file is included in the ZIP.

## Audio and platform limitations
- Local preview is **not** the same as FFXIV's actual instrument sounds; it
  depends on the chosen SoundFont and original MIDI GM programs.
- Original MIDI mode preserves the file, including tempo changes and most GM
  controls. Arranged and single-track modes rebuild MIDI notes from Windbound's
  note model; expression, pitch bends and other non-note controller events are
  therefore not reproduced in those two modes.
- Multiple MIDI lanes sharing an output channel (more than 15 melodic lanes)
  may share GM patches in generated preview.
- Current speaker output uses WinMM/NAudio. It works on Windows; Wine/Proton
  audio device support is environment-dependent, **not verified** on Linux.
- Download an appropriately licensed `.sf2` SoundFont yourself. Do not bundle
  copyrighted SoundFont samples without permission.
- NuGet restore requires connectivity on the Windows build PC.

## Test/verification
- `Tests/Run-v0.27-Tests.ps1` runs a standalone MIDI-preview-builder test and
  structural integration checks; tests for the earlier concert suite remain.
- The packages were selected against published MeltySynth/NAudio API docs.
- This release was packaged in a Linux workspace with no .NET SDK/FFXIV client,
  so compiling and actual local speaker output remain to be verified on Windows.
