# Nunu's Windbound Bard v0.18 — MidiBard-style Leader File and Instrument Sync

**Built on v0.17's native FFXIV Ready Check start mechanism, with all previous music-engine and playlist features.**

## 1. Auto-Accept Ready Check ON by default

New installations start with the **FFXIV native ensemble option enabled** and **Automatically accept FFXIV Ensemble Ready Check ON**. Each account still must select *Conductor* or *Follower*, join the FFXIV party, open the expanded **37-key** Performance keyboard, and ARM game output. A solo player is not silently enrolled in someone else's performance. Auto-Accept is a visible checkbox and can be turned off; future updates do not override that choice.

On upgrade from v0.17, native ensemble setting and user's auto-accept toggle are preserved. The new leader file/instrument sync option defaults on. Existing Solo, Ensemble, playlists, settings, song presets, track assignments and instrument presets remain intact.

## 2. Conductor chooses file — followers load it automatically

The leader selecting a MIDI from Songs & Playback triggers one **leader-only** party-chat command (`!windbound file...`). Every active Windbound follower searches its own local saved Solo/Ensemble MIDI entries by a short SHA-256 hash; playlist order and Windows/Linux file paths don't need to match. At most four candidates are hashed per framework tick to avoid blocking FFXIV while scanning large libraries.

If a matching `.mid` exists, the follower loads and rewinds it automatically, and also applies the leader's *track-to-character assignments* if included. It sends a `loaded` reply so the conductor can see how many followers have the correct song. If the song cannot be found, loaded, or the roster differs, the follower reports failure and native START remains blocked rather than playing the wrong MIDI. A manual **SYNC SELECTED MIDI TO PARTY** button allows retrying a lost command. This transfers **selection metadata, never raw MIDI bytes**. Each follower must own the exact file on disk.

## 3. Conductor SET PARTY INSTRUMENTS button

Choose a Performance instrument for each character from the party dropdowns. The presets are saved per-MIDI. Press **SET PARTY INSTRUMENTS — auto-switch everyone**. The plugin sends a short roster-relative instrument map; followers automatically use the existing in-game instrument selector and report their READY/failed state to the conductor. Native callbacks occur only on FFXIV's framework update thread. The conductor also switches locally, and the READY counter shows all identified party members, not only nearby clients. The button does **not** automatically start the song.

Once the file and instruments are READY, the conductor presses **START ENSEMBLE — FFXIV Ready Check**. The game's own synchronised ensemble START remains the **only** event starting MIDI playback. Our older chat/UTC timed start is still retired. This works across multiple accounts on one Windows/Linux machine or across PCs, assuming the users are all in the same FFXIV party.

## 4. Fixes and diagnostics

- v0.17's leader client only listened to chat commands in legacy mode: this release **enables conductor-side ACK reception** for both file and instrument readiness.
- Prevented FFXIV auto-accept while a follower's exact MIDI or assigned instrument is still unresolved.
- Auto-Accept now requires evidence that the **expanded 37-key** UI has been active recently, while tolerating the brief UI disappearance caused by the game Ready Check dialog.
- Fixed Smart Playlists' **Dalamud API 15 CS1503 drag/drop** issue by passing bytes and `ImGuiCond.Once` rather than a pointer/size pair.
- MIDI / Instrument SET status and timeouts are visible in the conductor's ensemble panel; file sync can be retried.
- Song selection, party-instrument SET, and native Ready Check are separate commands. No extra playback scheduler or audio engine was introduced.

## Installation and validation

Use `Build-Plugin.ps1` with the .NET 10 SDK and Dalamud API 15. Then run `Tests/Run-v0.18-Tests.ps1` for v0.18 protocol, instrument map, native start-gate and Smart Playlist tests. Source-only structural checks passed here, but a **Windows/Dalamud build and real in-game test are still necessary**. An unofficial Dalamud plugin may be against FFXIV terms.
