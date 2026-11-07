# Windbound Bard v0.32 — Deutsche Übersetzung / German UI

**Based on the working v0.31.2 playback and stage-formation hotfix.**

- Added **Deutsch** to the existing English / Français selector; language changes take effect immediately and are saved per Dalamud client.
- Provided **942 professionally worded German UI entries** matching the existing French catalogue: playback, ensemble, conductor, concert snapshots, stage formations and visual dot editor, Soulweaver, MIDI audio preview, Live MIDI Piano, diagnostics, instrument names, Smart Playlists and settings.
- Existing **English remains the default**. Existing English/French preferences are preserved. Switching language does not activate stage movement, alter party settings, reset ARMed state or start MIDI playback.
- Corrected the UI dictionary lookup for labels containing Dear ImGui `##` IDs. The visible label translates, while the original invisible widget ID is retained.
- German translation includes the expanded reusable octet-lineup messages and the MIDI-file dialog filter.
- No MIDI data, note keys, filenames, character names, private protocol strings, saved playlists or game callbacks are translated.
- Included editable `GERMAN-TRANSLATIONS.tsv`, standalone German localisation tests, and the original French regression tests. A Windows build with the .NET 10 / Dalamud API 15 SDK is still required.

### Beta test suggestions

Check the language selector, Stage Formations, conductor controls, Smart Playlists, audio preview and instrument diagnostics on both English and German clients. Confirm that English and French remain unchanged and that ensemble MIDI playback continues to work. Report any UI strings still shown in English; external plugin/driver/game messages and user-authored MIDI titles can retain their original language.

**No native ensemble, playback, instrument-switching, stage navigation or SoundFont rendering code was intentionally changed.**
