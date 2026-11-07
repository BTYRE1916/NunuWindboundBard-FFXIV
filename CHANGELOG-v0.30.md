# Windbound Bard v0.30 — Stage Formations (Bard Toolbox-style convenience)

## Additions

- **Stage Formations tab** for a conductor to save the current positions of 2–15 Bards as a named, persistent stage preset. Captures member positions and headings relative to the saved conductor anchor. The selected preset and lineup are remembered across plugin restarts.
- **MOVE PARTY INTO SAVED FORMATION**: conductor sends individual party-verified destination requests, paced to avoid FFXIV party-chat flooding. Each follower uses the optional **vnavmesh** plugin to walk safely to its assigned saved spot. Reports arrivals or reasons for failure.
- **STOP STAGE MOVEMENT** from the conductor; also a local stop button for followers and stopping navigation on plugin unload.
- **Opt-in per follower**, default OFF. A user need only enable it once per client, then the conductor can recall as many saved formations as desired. The feature does NOT move characters at login, while music is playing, while performing, or while in combat.
- **Strict protections**: verified party leader only, sorted ContentId roster fingerprints, per-request nonces, targeted members, territory verification, short-range movement limits and world-object proximity checks to avoid placing characters in a different housing ward or instance.
- **No direct character coordinate writes**. Navigation happens through `vnavmesh.SimpleMove.PathfindAndMoveTo`. The feature is unavailable rather than unsafe if vnavmesh isn't installed or its navigation mesh is not ready.
- **English and French UI**. Formation data stays local in Dalamud's plugin configuration; party messages contain only current in-game XYZ stage coordinates and command status.

## Limitations

- Requires separate `vnavmesh` plugin installed and enabled on **every** participating FFXIV client, including Linux/Wine clients. It may need time to build a navmesh for a housing area. Windbound does not install vnavmesh for you.
- **Automatic facing is not implemented**. Saved headings are displayed as a guide only; each player's final orientation must be adjusted manually. This does not claim full Bard Toolbox parity.
- Designed for short walking distances on the same physical stage. Everyone must be in the same party, territory and physical instance/ward and near the saved anchor. Max 32m capture radius, 40m movement path distance, and leader within 20m of the stage anchor for recall. No teleports, cross-zone movement or combat actions.
- Stage movement is NOT tied to music playback, automatic ARM, Native FFXIV Ready Check or concert setlist transitions. It is initiated only by clicking Recall.
- Game client updates and navmesh availability may affect reliability; test in a quiet area using two accounts before trusting an octet show.

## Validation

- `Tests/Run-v0.30-Tests.ps1` tests 2-8 member geometry, message parsing, invalid coordinates, roster tags, capture/move/stop/ACK and runs the v0.29 regression suite.
- Core playback, instrument switching, Soulweaver, Concert Director and MIDI-file processing were not edited.
- Source-level checks and archive CRC were completed here. No Windows/.NET SDK or live game client was available to build or test the plugin against FFXIV. Third-party plugins may be contrary to the game's rules.
