# Windbound Bard v0.28.0.0 — Concert Snapshots & Restore

## New: Named, portable Concert Snapshots

The **Concert Snapshots** tab saves a complete local concert setup under a name. These snapshots survive plugin restarts and can be exported to/imported from `.json` files for backups on your PC.

A snapshot captures:

- The active Solo/Ensemble playlist name, song order and selected MIDI.
- Solo/Conductor/Follower role and the native ensemble options (including Auto-Accept).
- Playback speed/tempo, global transpose, chord/hold/spacing and automatic MIDI detection settings.
- Reusable party performer lineup (including up to eight Bards or more), per-song party track assignments, part inclusions and party instrument choices.
- Per-lane octave shifts, manual override tags, guitar tones, and relevant Soulweaver presets.
- Smart Playlist favourites, tags, shuffle/repeat mode and Play Next queue.
- Concert Director next-song preparation preference, audience announcements, HOLDs and encore cues.

Snapshots do **not** embed MIDI files. Files must remain accessible on the computer where you restore them. Exported JSON includes local paths and party ContentIDs, so treat it as a personal backup rather than publishing it openly.

## Safe restoration

Select a snapshot, inspect its contents, and press **Restore Selected Snapshot → Confirm Restore**. Windbound:

1. Refuses restoration during live MIDI playback, held MIDI-controller notes, pending instrument switches or active ensemble preparation.
2. Saves a persistent **Before restore** snapshot of your current concert for recovery.
3. Creates a **new** saved Solo/Ensemble playlist profile rather than overwriting your existing one.
4. Restores per-MIDI track/instrument/octave settings, playlist, lineup and concert settings into the local plugin configuration.
5. Resets the local MIDI cursor and optionally reloads the selected local MIDI **without broadcasting** a party file-change, instrument, or start cue.
6. Leaves **ARM output disabled**. Native Ready Check and any group preparation require an intentional conductor action.

Because the current plugin stores certain per-MIDI settings **globally by MIDI path**, restoring a snapshot intentionally updates those preferences for other playlists pointing to the exact same file. The automatic Before restore snapshot preserves those older per-MIDI settings for recovery.

Legacy private TCP auto-connect is explicitly disabled on restore. Saved SoundFonts, MIDI controller configuration, UI language and session follower consent are not replaced.

## French interface

The new tab, buttons, confirmations, preview help text and export/import commands have English/French translations; ImGui internal IDs remain stable.

## Tests

Run `Tests/Run-v0.28-Tests.ps1` under .NET 10/PowerShell. The standalone JSON regression project checks snapshot deep copies, instrument and track maps, octave shifts, setlist and invalid data. Source checks verify the UI and the no-broadcast guard.

**Source-only preview:** This ZIP was statically checked, but a Windows/.NET/Dalamud build and in-game tests are still needed. Existing native MIDI scheduling, Auto-Accept, group STOP/unequip, SoundFont audio preview and Soulweaver engines are unchanged.
