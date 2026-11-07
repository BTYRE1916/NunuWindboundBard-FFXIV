# Windbound Bard v0.31 — Visual Stage Designer (Draggable Bard Dots)

This update builds on v0.30 Saved Stage Formations. It changes **how formations are designed**, without modifying the existing party-message transport, MIDI playback, native ensemble start, SoundFont preview, or vnavmesh movement backend.

## Bard Toolbox-style stage editor

- **Top-down square stage canvas** inside `/windbound` → **Stage Formations**, created automatically for the current party (2–15 Bards). Each character appears as a numbered coloured dot with a name.
- **Drag individual dots** to the desired stage position using the mouse. Optional **0.5 m snap** and an adjustable **8–64 m canvas width**. Targets are limited by the existing stage-formation maximum radius (32 m). The 64 m canvas width is for viewing large existing stages; it does not enlarge the allowed movement distance.
- **Select a dot and assign a Bard to it**. Assigning a performer already on another dot swaps the two, rather than duplicating a character.
- **Quick layouts:** straight line, arc, circle or two rows, followed by manual fine-tuning.
- **SAVE DIAGRAM AS NEW PRESET** records your plan without moving anyone. **SAVE CURRENT DIAGRAM** updates the current preset. **SET POSITIONS & MOVE PARTY** saves the plan and immediately begins the existing safe movement protocol.
- **Re-anchor at my position** centres the layout on the conductor's current world location and orientation (explicit button; never changes anchor merely because a player walked).
- **Load saved formation into diagram** lets you edit or fine-tune earlier v0.30 presets, including formations captured from characters already positioned manually.
- French and English UI entries are included with stable ImGui IDs.

## Performer movement and safety

- Followers still **opt in** to stage movement. Every client needs the existing optional **vnavmesh** navigation plugin. No teleportation, direct XYZ memory writes, keyboard simulation, musical START, or automatic movement at login.
- Reuses v0.30's verified leader, party roster, territory, proximity, combat/performance and movement-distance checks. The conductor's SET button sends individually targeted movement requests and displays progress / failures.
- The square is oriented to the saved conductor **anchor and heading**. Stage top is backstage (-Z), bottom audience side (+Z) in this local coordinate frame. If the source preset came from another stage/ward, re-anchor explicitly, then validate in an empty stage area.
- **Facing remains a saved guide**. This version automatically positions Bards but does not claim to rotate them automatically (Bard Toolbox has additional facing control).
- Design edits are local drafts until the conductor presses Save or Set. Existing presets aren't changed while dragging. Save As New creates a separate copy.
- Non-participating/unopted followers and unsafe movement states are reported; movement can be stopped from the conductor or locally.

## Build and test

`Tests/Run-v0.31-Tests.ps1` runs a pure .NET 10 test for canvas-to-world geometry, 2–15 Bard preset creation, dot dragging, templates, roster swaps, safe radius and save/reload, followed by all v0.30 tests. The project also includes the existing full Windows/Dalamud build script.

This source ZIP was statically checked and CRC-verified; **no .NET SDK or FFXIV runtime is available in the packaging environment**, so compilation and live vnavmesh movement must be verified on the user's system. Start with two clients in a clear, flat stage area.
