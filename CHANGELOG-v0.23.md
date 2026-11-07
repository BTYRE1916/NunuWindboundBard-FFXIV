# Nunu's Windbound Bard 0.23 — Persistent Octet Track Assignments

**Keep your eight Bards assigned across songs, rather than resetting every time a new MIDI loads.**

## New behavior

- **Keep party track assignments across songs** is enabled by default on new installations and upgrades.
- Your conductor's first existing per-song track map automatically becomes the reusable lineup for the current saved Ensemble playlist. You do not have to remake an already-configured octet.
- Assign track 1 to the pianist, track 2 to the violinist, and so on; those *performer slots* are reused automatically for every new MIDI in **that Ensemble playlist** (even if later MIDI track/channel numbers differ).
- Saved per-song assignments still override the lineup for unusual arrangements. You can explicitly **Apply saved lineup to this MIDI** to replace an old per-song override.
- **Save current as default lineup** and **Forget saved lineup** give you control over each saved Ensemble playlist. Solo playlists do not alter the conductor lineup.
- If an arrangement contains fewer or more tracks, match the overlapping slots in order; leave any additional tracks **unassigned** and show a reminder to review them. No arbitrary member is selected for new tracks.
- Changing a track's party member updates the saved reusable layout, ready for the next song. Clearing assignments on this MIDI does not destroy the default lineup.
- FFXIV native file selection / SET PARTY TRACKS continue using the existing authenticated party-chat protocol and same native Ready Check start. Followers never start music because of a lineup update.
- English/French UI entries for the new controls are included in both the runtime catalogue and `Assets/i18n/fr-FR.json`.

## Compatibility and safety

Preserves v0.22.2 MIDI metadata, `#Lute`, `#+12`/`#-12` tags, Soulweaver, Live MIDI Piano, Smart Playlists, native ensemble hook, group STOP+unequip and original note output. No MIDI on disk is modified. Templates store local per-character ContentIDs only in the plugin configuration; party messages continue using the existing roster-relative assignment map.

Run `Tests/Run-v0.23-Tests.ps1` on Windows with .NET 10/Dalamud dev SDK. No .NET compiler or FFXIV runtime is available here: the archive is a source release and needs your in-game verification.
