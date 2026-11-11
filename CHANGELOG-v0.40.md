# Windbound Bard v0.40 — OBS Concert Capture Studio (source-only)

- New **SHOW → Concert Capture** page and a small recording quick control in the existing Performance HUD.
- Localhost-only OBS WebSocket 5 client with challenge/salt SHA256 authentication, request ID verification and background status updates.
- Manual **Start / Stop + confirmation / Pause / Resume / Split file** recording control, **no plugin-imposed duration limit**.
- Optional replay buffer Start / Stop / Save, and separate, confirmed Start Stream / Stop Stream.
- OBS-reported recording status, file path, elapsed hours (including >24h), actual output resolution, and disk-free-space advisory.
- No OBS passwords or streaming keys saved to configuration. Recording starts only with a manual button; recording continues in OBS after plugin unload/disconnection.
- Per-concert file splitting automation and actual encoder/resolution must be set in OBS, not by the plugin.
- Optional Now Playing overlay: updates an existing OBS Text (GDI+) source with the loaded MIDI title, can hide the caption between songs, and lets performers retry the sync after configuring OBS. Changes use the localhost connection, do not alter the source style or MIDI timing.
- New standalone OBS protocol smoke test and local source checks.
- Existing MIDI engine, ensemble protocol, stage movement and Showmaster business logic unchanged.

## Caveat

This is an **experimental source ZIP**, not a verified or published plugin DLL. No real Dalamud/OBS runtime build was possible in this environment. Test first on a backup / developer install.
