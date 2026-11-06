# Nunu's Windbound Bard v0.16 — Smart Playlist Manager

This update is built directly on v0.15 Compact Performance HUD, with the original MIDI note engine, instrument switching, Soulweaver, and same-PC / cross-PC ensemble code kept unchanged.

## New

- Drag songs to reorder the saved playlist, or use Move Up / Move Down (also accessible by right-click).
- Favourites per saved playlist and a Favourites-only filter.
- Comma-separated tags; search filenames, paths, and tags. Export/import carries favourites and tags while remaining compatible with old playlist JSON.
- A persistent **Play Next** queue that takes priority over the ordered list without rearranging its saved songs.
- Per-playlist Shuffle and Repeat Off / All / One. Defaults to Repeat All to retain earlier Auto Next wraparound. Repeat One is bypassed when Next is clicked manually; queued requests are always honoured first.
- Recently played history (last 50 starts, visible last 20), separated by Solo/Ensemble playlist ID.
- An explicit missing-file scan with confirmation before removing references. Actual `.mid` and `.midi` files are never deleted.
- Compact HUD's Next display respects the Play Next queue, repeat mode, and shuffle status.
- Persistent profile state and a non-destructive v0.15→v0.16 configuration upgrade.

## Behaviour notes

- Automatic advancement remains **Solo-only**. The conductor still cues Ensemble songs as a group.
- ARM and follower-consent settings remain session-only, unaffected by playlist management.
- Song queue entries are removed when successfully started, not when the user merely clicks to load one.
- Shuffle is a no-repeat bag until the cycle is exhausted; Repeat Off stops, Repeat All refills.
- Old playlists and MIDI arrangement profiles are preserved.

## Verification

Run `Tests/Run-v0.16-Tests.ps1` on Windows with .NET 10 SDK. The core test project has no Dalamud dependencies. Build the plugin with `Build-Plugin.ps1`, then test in-game.
