# Windbound Bard v0.20 — Solo USB MIDI Piano + native group-STOP stow

**Based on the latest v0.19.1 native unequip source** (which had been prepared as a focused fix to v0.19). The v0.19 ensemble, Soulweaver, 37-note callback output, track assignments, conductor sync and Smart Playlists are preserved.

## New: live USB MIDI input in Solo mode

Use a MIDI-capable synth piano, keyboard controller or USB-MIDI interface to play FFXIV instruments directly with the full 37-note Performance layout.

- `Live MIDI Piano` tab, hardware port refresh, saved device choice, Connect/Reconnect, Disconnect.
- `Equip FFXIV Piano` button using Windbound's existing native instrument switcher.
- Solo only. NoteOn/NoteOff, velocity-zero NoteOff, per-channel MIDI CC64 sustain, CC120/123 all-off, and `PANIC` control.
- Octave shift by 12 semitones (-48..+48); optional octave fold into the FFXIV MIDI 48–84 (C3–C6) note range; otherwise outside notes are ignored.
- Safely handles multiple physical notes mapping to the same 37-key note (reference-counted to avoid early release).
- Driver callbacks only queue compact MIDI messages; all Performance addon callbacks remain on Dalamud Framework.Update.
- Live input is paused/cleared if you disarm output, disconnect the device, enter an ensemble, close full Performance, switch instruments or play a MIDI file. STOP releases live keys.
- Compact Performance HUD identifies an active live MIDI device even without a loaded `.mid` file.
- `/windbound midipanic` emergency All Notes Off.
- Uses Windows `winmm.dll` (no additional NuGet packages). Wine/Proton may expose Windows MIDI input ports via winmm depending on its ALSA configuration; native Linux/ALSA backend is not yet included.
- The plug-in remembers device preferences but starts output **disarmed** on each load.

**Velocity:** the FFXIV Performance addon callback has no standard velocity parameter; note-on velocity is used only to distinguish NoteOn from velocity-zero NoteOff.

## STOP + native instrument unequip

The previously prepared v0.19.1 correction is retained: group STOP first releases notes, then requests the native FFXIV Performance instrument action with instrument ID `0` to stow instruments. Merely closing the Performance keyboard was insufficient. This path is game-version-dependent and **needs in-game testing**.

## Verification

Run `Tests/Run-v0.20-Tests.ps1` with .NET 10 for pure MIDI mapping and existing native ensemble tests. Build the project using `Build-Plugin.ps1` in the normal Dalamud/Visual Studio SDK environment, then verify with an actual USB piano.

Static source and ZIP integrity checks were performed in this environment. No .NET compiler, Windows MIDI hardware or live FFXIV client was available here; the package is **source-only, not game-tested**.
