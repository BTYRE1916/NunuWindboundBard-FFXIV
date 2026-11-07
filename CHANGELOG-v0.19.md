# Nunu's Windbound Bard v0.19 — Octet Conductor Tracks + Group STOP/Unequip

This release is based directly on v0.18.2 (the tested cross-world party-message receiver).

## SET PARTY TRACKS (conductor)

- In the **Ensemble** tab select Conductor, Native FFXIV Ensemble and Follow Party Leader; load a MIDI.
- Under **Party-Roster Track Assignments** choose the performer for every MIDI track/channel. For an octet, assign up to eight party members different tracks (or multiple tracks per member).
- Press **SET PARTY TRACKS — send to every Bard**. The same action is available near **SET PARTY INSTRUMENTS** above.
- Windbound sends a compact `!windbound tracks` message to the **FFXIV party**, including SHA-256 MIDI fingerprint, roster fingerprint, assignments and nonce. Each follower automatically checks its loaded MIDI and party roster, applies only its own notes, then reports `trackok` or `trackfail`. The conductor sees a READY count.
- `Unassigned / silent` sends an explicit zero-map, **not** the previous `keep manual selections` shortcut. This means you can deliberately mute every part or a selected Bard.
- No MIDI bytes or character IDs are transmitted. The existing song-selection and instrument-selection commands still work. Native FFXIV START remains the only musical start signal.
- Followers on the same PC (multiple logged-in game clients) and other PCs use the same FFXIV party transport. No VPN or remote-port dependency. Every client must install the same version, join the same FFXIV party, and have the exact MIDI file.

## Leader STOP = stop notes + stow every Performance instrument

- The conductor's **STOP ENSEMBLE + UNEQUIP ALL** button, the main player STOP and `/windbound stop` now send a verified leader-only `!windbound stow` cue in Native Game Ensemble mode.
- The conductor and each receiving follower stop/release every active MIDI note *first*, cancel pending start/switch requests, then attempt to close their own Performance keyboard using the existing native addon close callback. Instruments are reported unequipped only once the game confirms leaving Performance mode.
- Failed or unsupported native game close requests are **not silently ignored**. Windbound reports the failure and users can manually close Performance in game.
- Solo STOP still behaves like an ordinary MIDI stop; it does **not** automatically unequip the soloist.
- The old native ensemble timing hook, keyboard/note output, Smart Playlists, performance HUD and MIDI optimisation are otherwise unchanged.

## Testing and limitations

Run `Tests/Run-v0.19-Tests.ps1` with Windows PowerShell + .NET 10 SDK. This covers protocol round-trips, eight-member/24-lane track maps, explicit empty assignments, invalid rosters/nonces, track acknowledgements, group STOP and legacy commands. Source-only checks are provided separately.

**Important:** This archive was not compiled in this environment (no .NET/Dalamud SDK installed) and native Performance closing was not tested in FFXIV. Check on two accounts first, then on all eight before a live concert. The commands are visible to party members and could be throttled by the game's party chat system; a local submit does not guarantee server delivery. During a duty or unsafe player state the game can reject Performance switching.
