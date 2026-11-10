# Windbound Bard v0.35 — Solo Stage & Automatic Facing (source preview)

- Added a self-contained **Solo Stage** editor (no party or conductor required).
- Solo Bard can walk via optional vnavmesh to a saved nearby spot, then attempt to face the saved direction.
- Added a **Face Forward Now** button that does not require walking or vnavmesh.
- Added stage formation **automatic facing** after arrival, including follower/leader completion acknowledgements.
- Added performer-facing guide arrows and a **Face Audience** multi-member action.
- Added separate saved solo layout, automatic-facing setting, and game-config service registration.
- Stage stop/recovery and MIDI-start cancellation cover both walking and facing.
- Built from the full existing Visual Studio source tree instead of relying on partial patch application.

**Validation:** source-only integration checks passed. Full C# compilation and in-game runtime testing have not been performed in this environment.
