# Windbound Bard v0.15.0.0 — Compact Performance HUD

This is a targeted quality-of-life update based on the tested **v0.14** source.

## New HUD

- Separate, movable Dalamud mini-window opened from the main player or `/windbound hud`.
- Permanently visible character name, role, song title, elapsed/duration, progress, status and ARM checkbox.
- Solo: Play, Pause/Resume, Stop, Next.
- Conductor: Play Ensemble (uses the configured local, native Ready Check, private TCP or one-click party start), Stop.
- Follower: no independent Play button, to avoid breaking ensemble synchronisation.
- During one-click instrument PREPARE: READY/expected follower counter.
- Full/compact modes; additional details in expanded view include instrument, playlist, next song and status.
- Lock position, adjustable opacity, optional next-song preview, hide/reopen control.
- Right-click progress bar to reset MIDI **locally**. A fresh conductor cue is required to restart a group.
- Explicit `/windbound hud show` and `/windbound hud hide` commands.
- HUD visibility/layout/position preference survives reload; output ARM and follower consent are still session-only.

The HUD is **per game client**. It cannot draw on top of every other FFXIV client simultaneously when its own client window is in the background. Multiple clients can each have their own HUD, and the leader controls synced ensemble playback from one client.

No requirement to focus the game for playback. No Windows keyboard injection. The native Performance callbacks, network and local ensemble transport, optimisation, instrument switching, Soulweaver, saved playlists and song transition code are carried over without modification.

## Validation

`Tests/Check-v0.15-Hud.ps1` checks HUD integration, that followers cannot start independently, the version and settings wiring. `Build-Plugin.ps1` builds with .NET 10 and Dalamud SDK 15. Actual Dalamud build and FFXIV gameplay need checking on Windows/Linux because the SDK/runtime is not installed in the packaging environment.
