# Full project — Windbound Bard v0.37.1

This is a **full, self-contained Visual Studio source project**, based on v0.37 (in-game band portraits) with a targeted fix to automatic instrument switching. It does **not** include a compiled DLL or push anything to GitHub.

1. Extract the entire ZIP into a NEW folder; don't overwrite your previous working project.
2. Open `NunuWindboundBard.sln` in Visual Studio.
3. Select **Release | x64**, restore dependencies and choose Build → Rebuild Solution.
4. With FFXIV and a compatible Dalamud version running, load your newly built DLL as a developer plugin.
5. Run `Tests/Run-v0.37.1-Auto-Instrument-Switch.ps1` for static source checks (optional; requires Python 3).
6. Test the instrument changes listed in `CHANGELOG-v0.37.1.md` **before** using the new build on a concert stage.

The main change is in `src/NunuWindboundBard/Core/InstrumentSwitcher.cs`. The name/image UI, song files and party permissions remain unchanged. When testing, check that the **Instruments** tab shows that the old instrument is stowed before the next appears.

If switching falls back to manual stow, the native Performance action may not be available on this FFXIV build: use the **Copy instrument diagnostics** button and send us that output. We cannot certify game-version signatures without live local testing.
