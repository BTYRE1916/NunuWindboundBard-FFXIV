# Live MIDI Piano — in-game hardware checklist

1. **Start with Windbound Solo mode**, stop any loaded MIDI file and confirm no other software owns your synth's MIDI-IN port.
2. Connect a USB MIDI keyboard or piano with a USB-MIDI interface. Open Windbound's `Live MIDI Piano` tab, Refresh MIDI devices, select the keyboard, then Connect.
3. Equip FFXIV's Piano using the new dedicated button. Confirm FFXIV is configured for the **full 37-key C3–C6 Performance layout**, not the mini keyboard.
4. Enable live controller input and ARM. Press C4 (MIDI 60) on the physical keyboard; expected: one FFXIV C4 note. Releasing the key should release FFXIV C4.
5. Hold several keys at once, release them individually, and test a sustain pedal (CC64); check note-offs when the pedal is lifted.
6. Test -12 and +12 transpose and range fold. For an 88-key controller test C2 and C7. With fold disabled, outside C3–C6 should be ignored.
7. While holding a key, click **PANIC All Notes Off** or run `/windbound midipanic`. Verify no notes remain held.
8. While holding a note, disable ARM; then reopen or close the 37-key Performance keyboard. No note should resume automatically.
9. Start playback of a saved MIDI file while Live MIDI is enabled. The hardware notes should be suspended and MIDI-file playback should work normally. Stop file playback to resume live MIDI input.
10. Switch into Ensemble, and back to Solo. Live controller output must stay silent in Ensemble.
11. Disconnect and reconnect USB MIDI; use Refresh and Reconnect if your OS changes the device index.
12. **Native group STOP / unequip test** (separate): on two characters, have conductor start ensemble, press STOP, verify notes stop, Performance keyboard closes and the instrument actually disappears on both characters.

## If MIDI hardware is not listed

- Ensure the MIDI controller appears in your operating system's MIDI device list.
- Close DAWs, MuseScore, or other MIDI apps if the device is locked. Some MIDI ports cannot be opened by two clients at the same time.
- Check that you're selecting the **controller's MIDI OUT into the PC** (which WinMM enumerates as a MIDI input port).
- The initial MIDI-IN backend uses **Windows winmm.dll**. Linux FFXIV through Wine/Proton may or may not expose MIDI ports through Wine; native ALSA MIDI input is not yet implemented.
- There is **no real-velocity control** in FFXIV's Performance-mode key callbacks.

Note: source and standalone mapping tests are included, but live device/FFXIV behaviour still needs manual Windows + FFXIV validation.
