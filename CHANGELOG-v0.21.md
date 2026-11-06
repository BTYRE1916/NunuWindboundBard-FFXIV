# Windbound Bard v0.21.0.0 — French Localization

- Added **English / Français** dropdown at the top of the player.
- English defaults for all existing installations. French preference persists between launches and updates, with a non-destructive configuration migration to version 22.
- Live switching of main and HUD titles, tabs, controls, help, menus and status text.
- French terms and phrasing across the ensemble conductor/receiver, Smart Playlists, Live MIDI, Soulweaver Studio and guitar-tone controls.
- More than 700 translated phrases and exact label entries; external device errors and unmatched technical diagnostics deliberately retain their original content.
- Widget identifiers following `##` remain unchanged for ImGui stability; MIDI filenames, protocol, user data, and note values are never translated.
- Original v0.20 playback engine, native ensemble, instrument switcher and live MIDI core source files left unchanged.
- Added `Assets/i18n/fr-FR.json` for translation maintenance and `Tests/Run-v0.21-Tests.ps1` for language regression tests.

**Build/test status:** source checks and ZIP integrity only. Requires Windows .NET 10/Dalamud compile and live game testing.
