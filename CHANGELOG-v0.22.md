# Windbound Bard v0.22 — MIDI track instruments & octave tags

**Based on v0.21 (English/French edition). Existing one-click/native ensemble, group STOP/unequip, live MIDI Piano, Soulweaver and Smart Playlist code are untouched.**

## Read instruments directly from MIDI

The MIDI reader now preserves the per-track/channel General MIDI **Program Change (Cn)** data, MIDI track names (FF 03), instrument names (FF 04) and channel-10 percussion note information. When a file is loaded and conductor track assignments are present, the assigned Bard's suggested FFXIV instrument is determined automatically. Explicit `#Lute`, `#Piano`, `#Violin` etc. tags beat General MIDI programs, which are mapped approximately onto FFXIV's 28 Performance instruments.

For each player assigned several lanes, Windbound picks the recognised instrument with the highest assigned **note count** and warns about conflicts. An explicit manual instrument selection always wins. Existing leader **SET PARTY INSTRUMENTS** sends the calculated choices to followers and equips them, using the already-working native/party protocol. **SET PARTY TRACKS** remains a separate action and FFXIV native Ensemble START is unchanged.

Use **Instruments → MIDI Instruments & Octave Tags** to inspect detected programs, suggestions and conflicts. Use **Use MIDI instrument suggestions** to clear old manual instrument choices on the *current file* when you want the MIDI tags to take precedence again.

## Octave tags (`#+12 #Lute`, `#-12 #Piano`)

A track's **FF 03 name**, or FF 04 instrument-name metadata, can contain `#+12`, `#-12`, `#+24`, `#-24`, `#+36`, `#-36`, `#+48`, `#-48` or `#0`. These are semitone shifts, in full octaves only. `#+12 #Lute` means equip **Lute** for the Bard assigned to that part, and shift that **track/channel's pitches** up twelve semitones. A single-track solo MIDI can also contain filename tags. Filename tags are deliberately ignored for multi-part octet files to avoid changing every part.

Tags act as initial defaults **without editing the original MIDI**. The existing C3–C6 range optimiser/repair still applies. Manual octave edits made with the buttons on v0.22 are saved and win over later file reloads, while legacy auto-optimiser saves won't suppress a new `#+12` tag. Use **Restore MIDI octave tags** to clear manual octave override flags for the current file.

The same track metadata is read independently from matching MIDI bytes on every follower PC (including Linux). No extra cross-PC clock or instrument-name messages are required: the conductor's existing SET commands carry the chosen FFXIV instrument IDs.

## Validation

Run `Tests/Run-v0.22-Tests.ps1` with Windows PowerShell and .NET 10. Unit tests build real Standard MIDI File fixtures and check GM Program Change capture, priority of `#Lute` over GM Piano, FF04 names, percussion mapping, multiple-track party conflicts, per-track octave shift and manual override rules.

**Not compiled/game-tested in the packaging environment**; the tests and in-game two-Bard/octet validation must be run before release.
