# Windbound Bard v0.37.2 — Dedicated Band Portraits Tab

## Changes

- Added a **Band Portraits** tab directly beside **Songs & Playback**, so it is easy to find even on smaller plugin windows.
- Portrait management no longer hides behind Ensemble track assignments or requires Conductor/Follower mode. In Solo mode, the signed-in character appears automatically, even outside a party.
- The portrait list is expanded by default rather than hidden in a collapsed tree, with clear CAPTURE and Remove actions for each visible member.
- Moved the floating Adventurer Plate screenshot/crop editor to the main window's draw path, allowing it to remain available even when changing tabs.
- Track-assignment, instrument-assignment and follower-roster portrait thumbnails are unchanged; portraits are still captured and saved locally only, keyed by character Content ID.
- Preserved v0.37.1 native instrument unequip/switch logic, game icon alignment, stage features, playlists, MIDI output, and all prior functionality.

## Use

Open **WindboundBard > Band Portraits** (second tab). With the member's Adventurer Plate visible in FFXIV, press **CAPTURE**, drag a square around the portrait, then **SAVE SELECTED GAME PORTRAIT**. A character's own entry works without being in a party. For other characters, join a party with them to show their names in the list.

## Notes

- Capturing requires a live FFXIV client and Dalamud game-viewport texture service. It is **manual**, not an automatic remote Adventurer Plate fetch.
- The preview is an explicit game screenshot. The plugin saves only the cropped 128x128 PNG thumbnail. Portraits are not sent to other clients.
- A successful local source audit cannot guarantee live game correctness. Rebuild and test on a compatible Dalamud installation.
- This full project source is not a pre-built DLL. The release manifest's download links intentionally remain unchanged until the maintainer publishes a new version.
