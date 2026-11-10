# Windbound Bard v0.37.1 — Automatic Instrument Switching Hotfix

## What was wrong

The existing instrument-change path invoked `PerformanceModeWide.Close(true)` before trying to equip a new instrument. Closing that window can leave the previous instrument model equipped. Separately, the STOP/unequip feature already used the native FFXIV Performance action with instrument ID zero. This mismatch explains why manual stowing was sometimes required between different instrument models.

## Changes

- Use the existing native `PerformAction(0)` path during ordinary instrument changes, including guitar ↔ piano, harp, lute, violin ↔ cello, and double bass.
- Confirm that **both** the character's Performance mode has exited **and** Dalamud's `ConditionFlag.Performing` is false before equipping another instrument.
- Add a brief 650 ms settling interval after exit confirmation, with re-check if Performance becomes active again.
- Permit up to two bounded native stow attempts and two bounded equip dispatches. Never perform endless retries.
- If native action resolution fails or the game refuses stow, keep a manual exit fallback with diagnostics. Do **not** bypass FFXIV/Dalamud version protections.
- Retain the direct electric-guitar tone-change path when switching among the five guitar tones; it does not require leaving Performance mode.
- Preserve MIDI, ensemble, stage-formation, instrument icon, and in-game portrait subsystems. No new config permissions or overwrites.

## Windows validation checklist

1. Open `NunuWindboundBard.sln` in Visual Studio and **Rebuild Solution (Release | x64)**.
2. In FFXIV, stop MIDI output and start with an instrument already equipped.
3. Test Guitar Clean → Piano → Lute → Harp → Guitar Overdriven.
4. Test Violin → Cello → Double Bass → Violin.
5. Test Electric Guitar Overdriven → Clean without unequipping (tone change only).
6. Repeat using the conductor's SET PARTY INSTRUMENTS if the solo test succeeds.
7. If a switch fails, open Instruments → Instrument switching diagnostics → Copy instrument diagnostics, and share the **Status + Diagnostics** with us. The text does not include account credentials.

IMPORTANT: The source check is not a native FFXIV compatibility test. The Win32 native signature must be verified on the currently running game/Dalamud build before relying on automatic stow; use the safe manual fallback if unavailable.
