# Windbound Bard v0.38.1 — Full Emote Library / Band Compatibility

- Showmaster discovers real player emotes from FFXIV's current `Emote` Excel sheet using Dalamud's `IDataManager`.
- The emote picker lists all commands unlocked by the local character, with search and the original game emote icons.
- The conductor can build and broadcast up to 12 Emote-sheet IDs per routine; the compact `!windbound show2` cue fits within the 130-character party command limit.
- Added `CHECK BAND EMOTES` to request a per-step unlock availability mask from opted-in followers. The conductor sees readiness by band member, not their entire emote inventory.
- Followers retain explicit consent and use their own character unlocks; locked emotes are **not** unlocked or forced. A follower may bow instead (default) or skip an unavailable step.
- Original v0.38 Showmaster routines are retained, including fixed text-command presets; old `show` cues remain supported when received from an older conductor.
- New `show2` and `schk` cues require v0.38.1 on all participating clients. No arbitrary macros, scripts, text from a remote conductor, file paths or other game commands can be transmitted by this feature.
- Does not change Piano Academy, MIDI playback, instrument actions, Stage Formations, portraits, or existing libraries.

## Limitations

A conductor's ownership of an emote **does not grant it to other characters or accounts**. Even on the same PC, FFXIV unlocks and restrictions are enforced by the individual client. Availability says the client has the emote unlocked; it does not guarantee the game permits its animation in every context. Emotes run only when Performance has fully ended and the instrument is stowed.

## Verification

Run `Tests/Run-v0.38.1-Tests.ps1` to check the pure-core protocol and source wiring. A Dalamud SDK build and a live FFXIV group test are still required.
