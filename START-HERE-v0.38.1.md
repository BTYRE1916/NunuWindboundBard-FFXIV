# Nunu's Windbound Bard v0.38.1 — Full In-Game Emote Library

A complete Visual Studio source project. No fork or push to any repository is required.

## Build

1. Extract the ZIP to a **new** folder.
2. Open `NunuWindboundBard.sln` in Visual Studio.
3. Set **Release | x64**, install/restore the matching Dalamud.NET.Sdk and NuGet dependencies, then **Build > Rebuild Solution**.
4. Run `Tests/Run-v0.38.1-Tests.ps1` from PowerShell for the source check and offline core smoke test. A local .NET 10 SDK is required.
5. Load the compiled plugin as a Dalamud development plugin, after backing up your currently working version.

## Conductor setup

1. In **Showmaster**, click **Refresh unlocked emotes**. The list uses FFXIV's own emote sheet and your *actual* per-character unlocks.
2. Search for any emote; choose it under **Add unlocked emote**, then **ADD STEP**. Edit waits as needed.
3. Set **Native Ensemble Conductor**, enable the existing party-leader sync, and form a party with the full band.
4. Click **CHECK BAND EMOTES** on the selected routine. Each opted-in follower reports a per-step unlock mask, not their whole inventory.
5. Check readiness by member. You can adjust the routine to use shared emotes, or let followers bow/skip missing ones.
6. Between songs, after all instruments are stowed, click **GROUP SHOW**.

## Follower setup

1. Install **v0.38.1** and select **Native Ensemble Follower**.
2. Enable **Allow verified party leader's group emotes** in Showmaster (off by default).
3. Leave **Bow instead of a missing/locked emote** enabled if desired. Disable it to skip unavailable emotes silently.
4. Stow your instrument between songs. No emotes are played during Performance or combat.

## Important boundaries

- The **conductor's unlocked library defines the routine**, but other accounts do not acquire the conductor's unlocks. FFXIV checks every character separately.
- Follower consent is required, and the leader is verified using the game's party roster.
- Only a compact list of emote row IDs and timings is sent. Text commands, user-entered macros and unlock state modifications are never transmitted.
- A member not running v0.38.1 won't understand the new `show2` group cue. Keep all clients on the same version.
- Party compatibility results are a planning aid; some emotes may still be blocked by the game's current animation state or area.
