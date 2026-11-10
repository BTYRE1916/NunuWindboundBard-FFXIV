# Nunu's Windbound Bard v0.36.1 — Icon Alignment Fix

This is a **full source project**, not a patch. It is based on the previous v0.36 source archive and retains all existing WindboundBard features (MIDI playback, solo/ensemble, stage formations, Soulweaver, instrument switching, translations, etc.).

## Issue seen in FFXIV

The first icon was the generic **PERFORMANCE** icon, rendered beside **Harp**. Every following icon was one instrument behind its label because `InstrumentCatalog.GameIconId` used `749 + instrumentId`.

## Fix

Only the shared icon mapping was corrected to `750 + instrumentId` for IDs 1–28. **Keep current instrument** (ID 0) and invalid IDs still have no icon. The correct expected sequence begins with Harp=751, Piano=752, Lute=753, Fiddle=754 and ends with Electric Guitar Special=778. The fix applies automatically to solo, party and Soulweaver dropdowns because all three call the same helper.

No changes to in-game **Perform action IDs**, switching, native callbacks, party protocol, playlists, stored instrument choices, or stage movement. No icon files are distributed; Dalamud loads them from the installed game.

## Build & test

1. Extract this ZIP to **a new folder** to avoid mixing v0.36 and v0.36.1 files.
2. Open `NunuWindboundBard.sln` in Visual Studio with the .NET 10 SDK and Dalamud API 15 development dependencies available.
3. Build **x64 / Debug** or **x64 / Release**.
4. Optionally run the standalone catalog smoke test: `dotnet run --project Tests/InstrumentIconCatalogSmokeTest.csproj`.
5. In the game, inspect `Harp`, `Piano`, `Lute`, `Fiddle`, `Violin`, `Cello`, and all 5 guitar tones in the solo instrument dropdown. Also check the conductor party list and Soulweaver list if those features are in use.

**Verification:** The source diff, metadata test expectations, solution/project consistency and ZIP contents were checked without compiling. This environment cannot run the Dalamud SDK or a live FFXIV client, so verify the actual artwork in game before publishing.

**Development manifest:** `Plugin.json` has AssemblyVersion `0.36.1.0`, but its GitHub download URLs deliberately remain pointed to the user's previous published release. Update the URLs/LastUpdate only when publishing a new release.
