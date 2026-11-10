# WindboundBard v0.36 - FFXIV instrument icon selector (source release)

This is a complete **source** project built on the existing v0.35 WindboundBard project.
The feature is **visual only** and does not modify instrument actions, selection IDs,
track-to-Bard mappings, playback, stage formations, settings, or party protocol.

## What's new

- **Songs > Instruments:** FFXIV action icon beside chosen instrument and each dropdown item.
- **Conductor > Party Instruments:** FFXIV icon for each assigned Bard and every choice.
- **Soulweaver Studio:** Icons in instrument selection for each arranged MIDI part.
- The complete instrument label remains visible and the whole row, including the icon, is clickable.
- Uses `ITextureProvider.TryGetFromGameIcon` and `ISharedImmediateTexture.TryGetWrap`.
  **No icon PNGs are included**: icons come from the installed FFXIV game resources.
- If Dalamud cannot load an icon, the selector remains usable with plain text.

## Open in Visual Studio

1. Make a backup of your existing project and unzip **this full project** into a fresh folder.
2. Open `NunuWindboundBard.sln` in Visual Studio, using the same Dalamud SDK version as the solution.
3. Restore packages, select x64 and rebuild.
4. Load the output DLL as a Dalamud dev plugin (when the game's Dalamud update is available).
5. Inspect the 28 icons, especially Electric Guitar tones, in all three selectors.

The included `Plugin.json` is a **development manifest**. Its download URLs still point
at the existing published v1.32 release and must be updated by the maintainer
when publishing v0.36. This ZIP is **not** a binary release.

## Notes for testing

- The game's instrument icons are in the `750..779` asset range; v0.36.1
  fixes the off-by-one mistake so 1..28 map to 751..778 (750 is a generic Performance icon). Visually verify mapping on the current game update, since Square Enix can
  reorganise game data; icons are not bundled. Adjust `InstrumentCatalog.GameIconId`
  if the live game reveals a different order.
- Confirm `Keep current instrument` (ID 0) still has no icon and still does not switch.
- Confirm Guitar tones 24..28 retain distinct instrument IDs and keep selected values.
- Confirm instrument dropdown clicks, selections and Save/Play behave as before.
- The `InstrumentIconCatalogSmokeTest` checks metadata but **cannot prove game artwork**.
- This package has been checked statically. A full Dalamud build and live icon check have
  not been run in this environment.

## Main changed files

- `src/NunuWindboundBard/Core/InstrumentCatalog.cs`
- `src/NunuWindboundBard/Plugin.cs`
- `src/NunuWindboundBard/MainWindow.InstrumentIcons.cs` (new)
- `src/NunuWindboundBard/MainWindow.cs`
- `src/NunuWindboundBard/MainWindow.PartyInstruments.cs`
- `src/NunuWindboundBard/MainWindow.Soulweaver.cs`
- `src/NunuWindboundBard/NunuWindboundBard.csproj`
- `Tests/InstrumentIconCatalogSmokeTest.cs` and `.csproj` (new)

## Attribution
Final Fantasy XIV game icon resources are © SQUARE ENIX. The plugin loads the
user's installed game's own UI assets at runtime; it does not redistribute them.
