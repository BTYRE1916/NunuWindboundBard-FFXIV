# v0.29 — One-Click Party ARM for Concert Conductors

- Added **ARM ALL PARTY BARDS**, **DISARM ALL**, and an optional opt-out for native Ensemble Followers.
- Added verified leader `arm` / follower `armok` / `armfail` party-chat command protocol and 25-second response monitoring.
- Acknowledges silent followers and 37-key UI missing separately from missing or incorrect MIDI and rejected commands.
- Added conductor HUD + Stage Manager shortcuts, English/French controls, profile-preserving upgrade migration (v31).
- Uses the existing per-session ARM field. Doesn't start FFXIV Performance or save live ARM state.
- Leaves all existing playback, instrument, file-sync and native ensemble timing implementations unchanged.

Tests: `Tests/Run-v0.29-Tests.ps1` includes pure .NET command parsing / safety preflight and source integration.

**Not compiled or game-tested in the packaging environment.**
