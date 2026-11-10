# Windbound Bard v0.37.3 — Full Project (Portrait Crop Fix)

This ZIP is a **complete Visual Studio source project**, based on v0.37.2, not a patch and not a compiled DLL.

1. Extract the ZIP into its **own new folder**; retain your currently working build as a backup.
2. Open `NunuWindboundBard.sln` in Visual Studio. Choose **Release | x64**, restore, and **Rebuild Solution**. The project uses `Dalamud.NET.Sdk/15.0.0` and .NET tooling.
3. Load the newly built DLL in FFXIV through Dalamud's developer plugin settings.
4. Open **Band Portraits**, show an Adventurer Plate in-game, and press **CAPTURE** next to its owner.
5. **Left-click the portrait INSIDE the preview image, hold and drag** to select a square. The whole window should **not** move now. The window remains draggable by the title bar.
6. Press **SAVE SELECTED GAME PORTRAIT**. The locally saved thumbnail should appear beside the character in track assignment.
7. Repeat with another member or your character to verify updated thumbnail loading.

To run source-level regression checks from the project root:

```powershell
python .\Tests\Check-v0.37.3-Portrait-Crop-Drag.py
python .\Tests\Check-v0.37.1-Auto-Instrument-Switch.py
```

The second script may contain historical version assertions; any such assertions pertain to the old release rather than this new v0.37.3 build. The new v0.37.3 checker verifies current preservation of the switch and portrait systems.

**Scope/limitations:** Actual mouse interaction, game screenshot capture, native Performance switching and Dalamud runtime compatibility require a live Windows/FFXIV test. This package was not compiled inside FFXIV here. The existing release manifest's download links are retained unchanged until you publish a new compiled build.
