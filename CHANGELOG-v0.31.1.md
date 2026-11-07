# Windbound Bard v0.31.1 — Emergency Playback Recovery and Stage Isolation

Hotfix based directly on the **v0.31 Visual Stage Designer** release.

**What the source comparison shows:** From v0.30 to v0.31, the underlying MIDI scheduling and native FFXIV note-sending source did *not* change. The only exact `already running` text added to the stage subsystem is raised by **StageFormationNavigator** while a movement is active; this is not proof of a MIDI scheduler failure. However, errors in an optional stage poll could interrupt the framework update before the MIDI scheduler ticked. That situation is now prevented.

## Fixes and recovery

- Stage formation polling runs **after** `PlaybackEngine.Update`, in a guarded block. If vnavmesh / stage state throws, the plugin logs the problem and resets the stage state; MIDI note dispatch has already been processed.
- Stage navigation reports explicitly when **movement** (rather than MIDI playback) is already running.
- The music start path stops **active stage movement** before starting MIDI, rather than letting both run together.
- New **EMERGENCY: Reset stuck playback** button under **Songs & Playback**, plus `/windbound recover` or `/windbound resetmidi` in chat.
- Emergency recovery stops local navigation, abandons pending native/playlist/instrument starts, clears the local MIDI scheduler, releases held notes on the next game update, cancels local native Ready Check bookkeeping and **disarms** output. It does not overwrite playlists, songs, SoundFonts or stage presets and does not send any musical START message.
- If FFXIV itself is already in an Ensemble, the reset cannot end that server-side state: end the native ensemble in-game first.

## First-run procedure

1. Back up your working setup. Test **v0.30** if you need a reliable immediate rollback for a concert.
2. Install/build v0.31.1 and run `/windbound recover` if the player displays a stuck Playing/Running message.
3. Choose Solo if testing a solo song. Load the bundled 37-key calibration MIDI, open the wide Performance keyboard, ARM, and Play.
4. If an active stage movement was the culprit, use **STOP STAGE MOVEMENT** first, then retry the song.
5. If the error recurs, copy **Songs & Playback → Song switching diagnostics**. The full text and whether `Playing=true` will distinguish MIDI playback from stage navigation and native Ensemble state.

## Validation limitations

Source-diff and ZIP integrity checks have been performed. This environment has no Windows .NET 10 or FFXIV runtime, so this project has **not** been compiled or verified inside the game. `Tests/Check-v0.31.1-Playback-Recovery.py` is a source-level smoke test, not a gameplay test.

### Experimental visual editor safe mode

The visual-dot stage editor is now **disabled by default on upgrade**. The established v0.30 Stage Formations controls still work. To use the new editor, open Stage Formations and enable **Enable Visual Stage Designer (experimental)**. A runtime exception in the editor disables it locally instead of leaving the normal player inaccessible. Saved stage presets are not deleted.
