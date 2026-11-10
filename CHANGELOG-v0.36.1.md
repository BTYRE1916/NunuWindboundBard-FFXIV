# v0.36.1 — Instrument Icon Alignment Hotfix

- Fixed the Performance instrument icon offset in the shared `InstrumentCatalog.GameIconId` helper.
- Previously icon 750 (the generic Performance icon) appeared for Harp and all subsequent rows appeared one instrument behind.
- Correct mapping is 1=>751 (Harp) through 28=>778 (Electric Guitar Special). `Keep current` (0) remains iconless.
- Tightened icon catalog smoke test to assert each instrument ID's exact icon ID, not merely uniqueness and a broad icon range.
- Bumped package/assembly version to 0.36.1.0 without altering user configuration version or existing release URLs.
- No changes to playback, instrument switching, party assignments, stage movement or network protocols.

**Local C# build and in-game visual verification remain required.**
