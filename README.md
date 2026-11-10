> **v0.39 — Dark Stage UI:** The plugin now has six grouped workspaces,
> a concert header, status chips, custom accents and Showmaster/Piano Academy
> display polish. For installation and test instructions see
> [START-HERE-v0.39.md](START-HERE-v0.39.md) and
> [CHANGELOG-v0.39.md](CHANGELOG-v0.39.md).
>
> **Note:** This is a source-only experimental UI build pending a live Dalamud test.


## v0.38.1 — Full Showmaster Emote Library

The Showmaster tab now reads all player emotes from FFXIV's game data and offers the **current character's unlocked actions** in its searchable picker, with real emote icons. A conductor can compose up to 12 emote steps and broadcast their game emote IDs to opted-in followers. Use **CHECK BAND EMOTES** before the show to see which selected emotes are unlocked on each participating character. Missing emotes can fall back to a bow or be skipped; ownership is **never** granted across accounts. See [START-HERE-v0.38.1.md](START-HERE-v0.38.1.md).

> **v0.38 preview source (unverified in-game):** New **Piano Academy** tab offers falling notes, full-range 88-key keyboard lessons, a detachable practice window and live USB piano input. **Showmaster** adds between-song emote macro buttons, custom timed routines and opt-in group choreography. See [START-HERE-v0.38.md](START-HERE-v0.38.md). Keep a backup of the prior working build.

# Windbound Bard v0.37.3 — Portrait Crop Fix 🖤🎶

**The Band Portraits tab is now second in the main WindboundBard menu and works in Solo, Conductor, and Follower modes.** It no longer hides inside the Ensemble track-assignment section. You can capture your own character's in-game Adventurer Plate picture without joining a party, and capture other current party members with their Adventurer Plates open. Captured thumbnails remain available in track/instrument assignment rows on this client only.

**Full Visual Studio source project, not a compiled plugin or an upstream GitHub release.** Build via `NunuWindboundBard.sln`. See [START-HERE-v0.37.3.md](START-HERE-v0.37.3.md) for setup, capture steps and testing limitations. Includes the earlier v0.37.1 instrument-switching fix and v0.36.1 icon alignment.

---

# Windbound Bard v0.36.1 — Performance Instrument Icon Alignment Fix 🎻

**Complete Visual Studio source ZIP** for the 28 Bard Performance icons. The v0.36 icon lookup had an off-by-one error: icon 750 (generic Performance) was displayed beside Harp. This release maps Harp to 751, Piano to 752, through Electric Guitar Special at 778. All UI selectors use the shared catalog; no instrument action IDs or MIDI behavior are changed.

**Build:** Open `NunuWindboundBard.sln`, select x64 and rebuild. See [START-HERE-v0.36.1-Icon-Alignment.md](START-HERE-v0.36.1-Icon-Alignment.md) for testing and compatibility notes.

---

# Windbound Bard v0.35 — Solo Stage & Automatic Facing 🖤🎹

**Full Visual Studio source ZIP:** The v0.35 changes are already merged into this copy. Open `NunuWindboundBard.sln` and read **[START-HERE-v0.35.md](START-HERE-v0.35.md)** for no-party stage movement, facing controls, build instructions and testing limitations. The older release notes below are kept for history.

---

# Windbound Bard v0.32 — German / Deutsche Benutzeroberfläche 🇩🇪

**Based on stable-test v0.31.2**. Adds **Deutsch** to the existing **English / Français** language selector at the top of `/windbound`. Switch instantly without restarting; the language is saved per client. English is the default for new testers and previous settings remain intact.

The new catalog contains **951 German interface translations** covering the existing French localization scope: songs, Solo/Ensemble, Concert Director, stage dots, Set Party Tracks/Instruments, SoundFont preview, Live MIDI Piano, snapshots and diagnostics. Technical engine output, MIDI track titles, arbitrary filenames and messages from the game or third-party drivers may remain untranslated. English and French continue to work.

No MIDI/ensemble game logic was changed. The original German UI widget IDs (`##`) are stable, so translations don't change how buttons are identified. The full translation table is provided in `GERMAN-TRANSLATIONS.tsv` for beta-test corrections.

### Install / test

1. Build `NunuWindboundBard.sln` with .NET 10 + Dalamud API 15, then load the plugin.
2. Open `/windbound` and select **Deutsch** in **Interface language / Sprache der Oberfläche**.
3. Test tabs including **Bühnenformationen**, **Konzert-Schnappschüsse**, **Songs & Wiedergabe** and **Audiovorschau**.
4. Run `Tests/Run-v0.32-Tests.ps1` to check German/French translation coverage, stable ImGui IDs, and old playback source checks.

**This is a source package, not a compiled plugin.** No Windows/.NET compiler or live FFXIV client was available during packaging, so please build and test before distributing to your beta testers.

---

# Windbound Bard v0.31.2 — CS0165 / CS0177 compiler hotfix

This is the complete **v0.31.1** project with two C# definite-assignment compiler errors corrected. Everything else, including the emergency `/windbound recover` command and experimental visual stage designer toggle, is preserved. See `CHANGELOG-v0.31.2.md` for the exact changes.

---

# Windbound Bard v0.31.1 — Emergency Playback Recovery Hotfix

**Status:** Source patch / needs Visual Studio build and live FFXIV testing. This hotfix is not proof that the reported “already running” problem is fully solved; the precise source depends on the in-game diagnostics.

The v0.31 Stage Designer added a navigation subsystem but did **not** modify the MIDI scheduler. The exact text **“Another stage movement is already running”** came from the vnavmesh stage-navigation component. The MIDI scheduler's `IsPlaying` guard is separate. To prevent any stage exception from blocking MIDI note dispatch, stage polling is now executed **after** the music scheduler, with explicit error isolation.

## Recover a stuck local player

1. Run **`/windbound recover`** (or Songs & Playback → **EMERGENCY: Reset stuck playback**). This stops local stage navigation, discards pending MIDI starts, resets the local scheduler, and disarms output. It does **not** delete MIDI files or saved presets or start any music.
2. Allow a game frame for held notes to be released. If FFXIV itself is currently in an Ensemble, end it using the **in-game** Ensemble UI; a plugin-local reset cannot cancel the server's state.
3. For a solo test, set Windbound to **Solo**, load `Samples/Bard-37-Note-Calibration.mid`, open the expanded 37-note Performance keyboard and **ARM** again, then press Play.
4. If the error persists, copy **Songs & Playback → Song switching diagnostics**. The report now distinguishes local MIDI Playing/StopPending from active stage navigation and pending FFXIV native Ready Check.

## Visual stage editor safe mode

The experimental dot editor is **OFF by default on upgrade** to v0.31.1. Previous **Save Current Party Positions / Move Party Into Saved Formation** controls remain available and existing presets are preserved. To test the new square-and-dots editor, open Stage Formations → **Enable Visual Stage Designer (experimental)**. Disabling it doesn't change your saved layouts.

If you need a reliable concert fallback, restore the **v0.30 project** from the backup ZIP and keep the plugin configuration backup. All changes here are scoped to recovery, stage feature isolation and versioned settings; the native note scheduler and 11 other core music/concert source files are unchanged.

---

# Nunu's Windbound Bard v0.31 — Visual Stage Designer 🖤🎻

**Dalamud API 15 · .NET 10 · Stage formations, octet conductor, MIDI player**

## New: drag the dots, then MOVE the Bards

Open `/windbound` → **Stage Formations** on the FFXIV party leader's **Conductor** client. Instead of walking all Bards into place manually, Windbound now draws a **square, top-down stage** with a numbered dot for each character.

1. Put the conductor at the approximate **centre of the actual stage** (ideally facing towards the audience). All Bards should be nearby in the same housing ward/instance and FFXIV party.
2. In **Stage Formations**, a **Visual Stage Designer** appears automatically for the active party. Create a fresh layout, or choose a saved preset and click **Load saved formation into diagram**.
3. Choose **Line, Arc, Circle or Two Rows** as a starting layout. Drag the coloured dots to your preferred stage spots. Click a dot and use **Assign Bard to selected spot** if you want another Bard at that position; existing assignments are swapped safely.
4. Adjust the stage-width scale and optional 0.5m snap. Use **Re-anchor stage at my position** if the venue location changed or you need the conductor's current location as the centre.
5. Press **SET POSITIONS & MOVE PARTY**. Windbound **saves your diagram** and sends the selected destinations to participating Bards, which walk there using **vnavmesh**. You can also save without moving anyone using **Save Current Diagram** or **Save As New Preset**.
6. Review the movement statuses below, and use **Stop Stage Movement** to cancel. The MIDI player never starts because of moving a formation.

On each Follower, enable **Allow my party leader to save my position and move me on stage** and install/enable **vnavmesh**. Followers must leave Performance and combat before movement is allowed. The editor supports same-PC multibox ensembles and parties on separate computers, subject to FFXIV world/party proximity and the game's existing party-chat transport.

**Important**: This uses the saved conductor anchor and heading for the dot coordinate system; it is not a literal map of collision/obstacles. Navmesh determines how to walk, and may reject unreachable dots. Automatic facing is not implemented yet; the heading slider is a guide.

v0.30's **Save Current Party Positions** and **Move Party Into Saved Formation** remain available. All Concert Director, one-click ARM, native ensemble, Smart Playlist, Soulweaver, French localisation and audio-preview features are preserved.

See `CHANGELOG-v0.31.md` and `Tests/Run-v0.31-Tests.ps1`. Source-only checks were performed here; compile with .NET 10 and test in-game before using during a live concert.

---

# Nunu's Windbound Bard v0.30 — Saved Stage Formations 🖤🎻

This update adds a new **Stage Formations** tab to v0.29, inspired by the formation features described in Bard Toolbox. It does not copy Bard Toolbox's source or affect Windbound's existing MIDI playback, Smart Playlists, Soulweaver, Concert Director, native Ensemble Ready Check, party-track allocation, ARM or SoundFont Preview.

## Setup for your octet

1. Install **Windbound v0.30** and **vnavmesh** on every participating game client. vnavmesh is a separate FFXIV Dalamud navigation plugin; Windbound only uses its published optional IPC endpoints. Wait until vnavmesh has built the navmesh for the stage area.
2. Join the same party and bring every Bard to the **same actual stage, housing ward and instance**. On each follower open `Stage Formations` and enable **Allow my party leader to save my position and move me on stage**. This is a saved permission and can be disabled anytime.
3. Manually arrange the performers once. On the party leader/conductor open `Stage Formations`, type a name such as `Tenacious Bar'ds Octet`, and click **SAVE CURRENT PARTY POSITIONS**. Windbound asks every opted-in follower to report its position; the saved formation appears once *all* have replied.
4. On a later concert return everyone to that same stage area. Select the named formation and click **MOVE PARTY INTO SAVED FORMATION**. Each Bard automatically walks to the saved location, with arrival/failure reports shown by the conductor. This does not trigger Performance or arm output.
5. If anything goes wrong, press **STOP STAGE MOVEMENT** in the conductor, or the local STOP button on an individual follower.

**Important differences from Bard Toolbox:** Current v0.30 restores **positions**, not automatic facing. Saved facing angles are displayed as a reference. No direct actor-coordinate memory writes are used. All movement is ordinary walking through optional vnavmesh and requires the party leader to be visible nearby; this avoids treating another housing ward with identical coordinates as the same stage. Move requests require explicit conductor action, not automatic movement every time the plugin loads.

## FFXIV party-message protocol

Party commands `fcap`, `fpos`, `fgo`, `fok`, `fbad`, and `fstop` carry only in-game stage coordinates, member positions by verified sorted roster slot, and completion status. The actual party leader is verified by Windbound's existing sender-matching code. The message transport is deliberately visible in FFXIV party chat. Use this only with a trusted ensemble and don't assume party-chat delivery is instantaneous.

## Build and testing

Open `NunuWindboundBard.sln` using .NET 10 and Dalamud API 15. Run `Tests/Run-v0.30-Tests.ps1` to execute the new headless formation tests and the earlier party and MIDI regression suite. The source wasn't built or exercised in FFXIV by this environment; verify with two clients before moving an octet.

---

# Windbound Bard v0.29 — One-Click Conductor Party ARM

**Dalamud API 15 · .NET 10 · Windows/Linux FFXIV clients**

Adds **ARM ALL PARTY BARDS** for native FFXIV Ensembles. The conductor can prepare output for an entire duo, quartet or octet in one click without opening the follower windows. This is a preparation command only — it never starts music, changes instruments, or invokes the native Ensemble Ready Check.

## How to use

1. Install **v0.29 on every participating account**, join the same FFXIV party, and select Native Ensemble. Conductor chooses the **Conductor** role; the other accounts choose **Follower** with **Follow party leader** enabled.
2. Leader loads the same MIDI on followers using Windbound's existing **SYNC SELECTED MIDI** and **SET PARTY TRACKS / SET PARTY INSTRUMENTS** controls (or **PREPARE ENTIRE ORCHESTRA**).
3. Click **ARM ALL PARTY BARDS** in the Conductor panel or Stage Manager. The leader ARMs itself and broadcasts one small `!windbound arm` party-chat command. Active Followers with matching MIDI, enabled parts, and permission will ARM automatically and send an `armok` reply.
4. Review **Party ARM: X/Y acknowledged**. It reports missing members, rejections, silent/trackless Bards, and followers that were ARMed but still need the full 37-key Performance keyboard. Open the keyboard/equip instruments before playing if needed.
5. The conductor presses **START ENSEMBLE**, using FFXIV's native Ready Check exactly as before. The party ARM cue itself never plays notes.

**DISARM ALL** is also available when the ensemble is stopped. It disables output on every opted-in follower without unequipping instruments or deleting MIDI files. The existing **STOP ENSEMBLE + UNEQUIP ALL** stays unchanged and separate.

### Consent, limits and safety

- **Allow my party leader to ARM this Bard automatically** is enabled by default for native Ensemble Followers, with a checkbox to disable it. ARM *state* is never persisted across plugin restarts; a new session starts disarmed until the leader clicks ARM ALL again.
- Only a real verified FFXIV party leader can issue an ARM command. Clients verify a party-roster fingerprint and exact MIDI SHA256 prefix; invalid, stale, mismatched, or playing-client commands are rejected. The per-cue random nonce prevents replay.
- Followers still need the same MIDI locally, selected parts and a compatible **37-key Performance** window. The button does not equip an instrument. A client without assigned notes acknowledges as *silent* rather than pretending to have active notes.
- The sender's chat API accepting a command is **not delivery proof**. Missing replies are shown after a 25-second timeout, including accounts without Windbound or those using older plugin versions. Try the button again after resolving any issue.
- No credentials, MIDI bytes, file paths or ContentIDs are sent in chat. Short party messages may be visible to players in the party.
- There is no effect on the Solo MIDI player, audio previews, Concert Snapshots, saved octet lineups, Live MIDI Piano, or the native ensemble START timing.

### Build and test

Open `NunuWindboundBard.sln` in Visual Studio with the .NET 10 / Dalamud API 15 SDK. Run `Tests/Run-v0.29-Tests.ps1` for protocol checks. The new code has been statically checked and packed, but a **Windows build and a real two-client FFXIV test are still required** before relying on it for a concert.

---

# Nunu's Windbound Bard v0.28 — Concert Snapshots & Restore 🖤🎹

This update builds on v0.27.1 MIDI Audio Preview and adds **named, exportable Concert Snapshots** for Solo, duet, quartet and octet performances. Existing native ensemble, automatic party instruments, persistent track mapping, Soulweaver, Concert Director, MIDI Audio Preview, Live MIDI Piano, Smart Playlists and French UI are preserved.

**Quickstart:** `/windbound` → **Concert Snapshots**. Type a concert name, click **SAVE CURRENT CONCERT SNAPSHOT**, choose it from the list and **Export snapshot JSON...** to keep an independent backup. To use it later, select the snapshot, open **Preview snapshot contents**, click **RESTORE SELECTED SNAPSHOT...** and **CONFIRM RESTORE**. A restored snapshot creates a **new saved playlist profile**, and Windbound automatically saves a *Before restore* backup of your previous setup.

The snapshot contains playlist/song order, conductor role and FFXIV native ensemble options, track/performer lineup, per-song instruments, octave/transpose overrides, playback tempo and settings, setlist annotations, favourites and queue. Individual MIDI files themselves are **not** included in the JSON. The interface shows missing file references before restoration; repair MIDI paths on another PC if needed. JSON backups include local file paths and character IDs; keep them private.

Restoration is **local only and never auto-starts a concert**. No party chat/FFXIV Ready Check is sent; ARM is turned off. After restoring a saved octet, ensure the same characters are in the in-game party, ARM the performers, and press **PREPARE ENTIRE ORCHESTRA** (Concert Director) to resynchronise everyone before the normal native START. Existing per-MIDI preferences in the plugin are shared by path; the automatic Before restore snapshot allows rollback of any overwritten per-song preferences.

Run `Tests/Run-v0.28-Tests.ps1` for the snapshot JSON and source regression checks on a Windows PC with .NET 10. The bundle has not been compiled or tested inside FFXIV in this environment.

---

# Windbound Bard v0.27.1 — Audio Preview compiler hotfix

Fixes `CS1061: MidiFileSequencer does not contain a definition for MidiFile` in `Core/AudioPreviewPlayer.cs`. Preview length is now taken from the existing `MeltySynth.MidiFile.Length`, which is supported by the package used in this project. All v0.27 audio-preview features, Concert Director, French localisation and ensemble behaviour are preserved. See `CHANGELOG-v0.27.1.md` and run `Tests/Check-v0.27.1-API.ps1` for the static compatibility check.

---

# Nunu's Windbound Bard v0.27 — Local MIDI Audio Preview 🎹

**Dalamud 15 · .NET 10 · complete source from v0.26 Concert Director**

## Quick start

1. Build `NunuWindboundBard.sln` with .NET 10 and Dalamud SDK 15. The
   `MeltySynth` and `NAudio` NuGet dependencies are restored during build;
   include their dependency DLLs when installing the compiled plugin.
2. Open `/windbound`, load a `.mid`/`.midi` in **Songs & Playback**.
3. Select **Audio Preview** (French: **Aperçu audio**), browse for your own
   licensed `.sf2` SoundFont, then click **Play Audio Preview**.
4. Select **Original MIDI** for the full GM instrument/tempo arrangement,
   **Windbound arrangement** for the presently enabled C3–C6 parts, or
   **Selected track** to hear one track, optionally with its octave shift.
5. Adjust volume and tempo. Preview audio goes to local PC speakers only.
   It never sends MIDI into FFXIV, changes instruments, queues notes for the
   live performance, or sends commands to other accounts.

**You must provide your own `.sf2` SoundFont.** No third-party copyrighted
samples are packaged. Output uses WinMM on Windows; Wine/Proton audio output is
not yet confirmed. The original MIDI mode preserves program changes and tempo;
the reconstructed arrangement modes use notes and approximated GM instruments.

The complete MIDI file player, Concert Director, ensemble sync, octet lineups,
French translations, Smart Playlists, Live MIDI Piano, HUD and Soulweaver remain
in this project. See `CHANGELOG-v0.27.md` for details.

---

# Windbound Bard v0.26 — Concert Suite + MIDI Inspector

Concert Director Stage Manager, automatic next-song preparation, single-Bard retry, and MIDI Arrangement Inspector are included in this cumulative full-source build. The **MIDI Inspector** tab reports song/track diagnostics and offers manual auto-fitting of untagged octaves without changing MIDI files. See `CHANGELOG-v0.24.md`, `CHANGELOG-v0.25.md`, `CHANGELOG-v0.26.md` and `Tests/Run-v0.26-Tests.ps1`. Existing solo instruments, native ensemble, Live MIDI Piano, Smart Playlists, HUD and French translations remain.

---

# Windbound Bard v0.25 — Concert Setlist & Targeted Retry

New `CHANGELOG-v0.25.md`: opt-in automatic **next-song preparation** (never auto-start), per-song concert cues and audience breaks, targeted single-Bard retry via party chat. Stage Manager v0.24, native Ready Check, full music engine and all other existing features retained.

---

# Windbound Bard v0.24 — Concert Director

See `CHANGELOG-v0.24.md`. The new Concert Director pane appears on the Ensemble tab. Use **PREPARE ENTIRE ORCHESTRA** then native **START ENSEMBLE**; stage status requests/replies are party-chat metadata only. Existing local, cross-PC, solo, Soulweaver, Live MIDI Piano, French and Smart Playlist features are retained.

---

# Nunu's Windbound Bard v0.23 — Reuse Your Octet Across Songs 🎹🎻

**New:** In `/windbound` → **Ensemble → Party-Roster Track Assignments**, leave **Keep party track assignments across songs** checked. As conductor, assign the eight performer parts just once. Windbound remembers the lane-order lineup in your active named Ensemble playlist and automatically applies it to the next MIDI file. Its existing native leader file-sync message also carries the resulting assignments to follower clients, so you still only need native Ready Check for playback.

Existing per-song maps take priority when explicitly saved. Use **Apply saved lineup to this MIDI** if you want to overwrite an older song-specific choice; **Save current as default lineup** and **Forget saved lineup** manage the shared template. Mismatched track counts produce a warning; extra lanes are silent until assigned. Clearing a song's map does not erase the default lineup.

English/French language selection, MIDI instrument and octave tag detection, direct note callbacks, group STOP, MIDI keyboard input, Smart Playlists and Soulweaver are preserved. `Tests/Run-v0.23-Tests.ps1` covers the persistent octet lineup and previous MIDI tests. **This source has not been compiled or tested in FFXIV here.**

---

# Nunu’s Windbound Bard v0.22.2 — MIDI Instrument / Octave Detection Fix 🖤🎹

**Built on v0.22.1 with the native ensemble engine, party/file sync, group STOP/unequip, live MIDI piano, Smart Playlists, Soulweaver and French/English interface preserved.**

## Why some songs showed no instrument or octave

Earlier v0.22 releases only read `#Lute` / `#+12` from FF03 track titles, FF04 instrument names and (for solo files only) filenames. Many MIDI editors instead export these tags as **FF01 Text**, **FF06 Marker** or **FF07 Cue** events, and may overwrite an earlier tagged track title later in the same track. Our reader did not retain those tags. Also, the Solo instrument dropdown did not use the detected instrument unless it had been manually selected.

v0.22.2 fixes both paths:

- Reads `#+12`, `#-12`, `#Lute`, `#Violin`, `#Piano` etc. from **FF01 text, FF03 name, FF04 instrument name, FF06 marker and FF07 cue**, preserving even tags that appear before later renamed track titles.
- Reads standard **MIDI Program Change**, including unambiguous declarations located in a different MIDI track on the same channel.
- Gives human-readable musical track names (e.g., `Violin Melody`) priority over generic, often incorrect GM Piano defaults.
- Uses an inferred instrument in **Solo** when no explicit manual instrument has been saved; the conductor still infers the dominant instrument for each assigned party member before **SET PARTY INSTRUMENTS**.
- Treats a saved **Keep current** as a true manual override. **Use detected instrument for this MIDI** removes that override and resumes automatic detection.
- Applies octave tags per track/channel on load, respecting saved manual octave edits. The controls **Restore MIDI octave tags** and **Re-read MIDI tags now** are provided in Songs & Playback / Instruments.
- Shows both the **file octave** and **APPLIED octave** per track and includes **Copy detected MIDI metadata report** for diagnosing real-world files.

## Test your arrangement

1. Load your MIDI in `/windbound` → **Songs & Playback**. Open **MIDI INSTRUMENTS & OCTAVE TAGS**.
2. Leave **Auto-detect instruments** and **Read #+12 / #-12 octave tags** enabled.
3. Expand **Detected MIDI tracks, instruments and octaves**. Look at **file octave** and **APPLIED**. If a saved manual octave override is overriding the tag, press **Restore MIDI octave tags**. For a saved instrument preference, press **Use detected instrument for this MIDI** or **Use MIDI instrument suggestions** (the latter clears conductor instrument overrides too).
4. If tags still are not detected, click **Copy detected MIDI metadata report** and inspect the FF03, FF04, FF01/marker, Program Change and APPLIED fields. The report includes local file paths: review before sharing it.
5. In an ensemble, assign each part to a Bard, then press **SET PARTY INSTRUMENTS**. This still uses our existing reliable FFXIV native Ensemble Ready Check for START.

The bundled `Samples/Windbound-MIDI-FF01-Marker-and-GM-Detection.mid` demonstrates Text `#+12 #Lute`, Marker `#-12 #Violin` and a named Violin track with a conflicting Piano program.

**Note:** A `.mid` file cannot automatically convey instrument and octave markings unless those markings are genuinely embedded in its metadata. Tags only in a shared filename are intentionally not applied to every track of a multi-part octet. General MIDI programs are suggestions when no explicit tag exists. Optimisation may fold notes back into C3–C6, so an applied shift isn't always an audible whole octave for every note.

## Verification

Run `Tests/Run-v0.22.2-Tests.ps1` on Windows with .NET 10, then build the project with `Build-Plugin.ps1` and test in FFXIV. The source/ZIP package was statically checked in the packaging environment, but could not be compiled or tested with a live game client here.

---

# v0.22.1 CS8510 compiler fix

This revision removes the redundant invalid-program arm in `MidiInstrumentInference.MapGeneralMidi`. All 128 valid General MIDI instrument mappings, MIDI track tags like `#+12 #Lute`, automated conductor assignments and existing features are preserved. See `CHANGELOG-v0.22.1.md`.

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

---

# Windbound Bard v0.21 — French / English Interface 🇫🇷

**Dalamud API 15 · .NET 10 · built on v0.20 Live MIDI Piano and Native Stow**

## How to change language

Open `/windbound`. At the top of the main player use **Interface language / Langue de l'interface** and select **English** or **Français**. Changes take effect immediately in the main player and compact Performance HUD, and the language is saved in Windbound's configuration. No game restart is required. English remains the default for existing and new installations.

The French localization covers the main tabs, controls, status messages, tooltips and help text for **Solo, native FFXIV Ensemble, conductor instruments/tracks/files, Smart Playlists, Soulweaver Studio, Guitar Tones, 37-key FFXIV Optimiser, Live MIDI Piano, and Performance HUD**. The French resource is kept as an editable JSON catalogue (`Assets/i18n/fr-FR.json`), with an embedded C# version for offline use in FFXIV. Unrecognised technical messages fall back to English rather than inventing an error translation. Some exception messages originating in the game, Windows or third-party drivers can remain in their original language.

The localization system keeps ImGui's `##` internal IDs identical in both languages. It **does not translate user MIDI file names, track/channel data, command identifiers, TCP/party protocol messages or stored source paths**. Changing language does not alter instrument assignments or concert settings.

## Verification

Run `Tests/Run-v0.21-Tests.ps1` with the .NET 10 SDK to test localisation, stable widget IDs, dynamic status translations, switching languages and preserving MIDI file names. Build the full plugin with `Build-Plugin.ps1`, and test English and French in the game's UI. Static validation and archive integrity have been checked in this environment; a Windows/Dalamud compile and live FFXIV test have not been performed here.

---

# Nunu's Windbound Bard v0.20 — Live USB MIDI Piano (Solo)

## Play a real synthesizer or MIDI piano in FFXIV

Windbound v0.20 adds **live MIDI-IN** to its Solo Performance tools. Connect a MIDI keyboard to Windows (USB or through a MIDI interface), open `/windbound` → `Live MIDI Piano`, press **Refresh MIDI devices**, select the device and **Connect keyboard**. Press **Equip FFXIV Piano** (or choose another instrument manually), use FFXIV's expanded **37-key Performance layout**, and tick **ARM live notes to FFXIV**. Then play on the actual keyboard! No MIDI file is required.

The new tab includes octave transpose ±12, octave folding into C3–C6, channel selection, sustain-pedal CC64, a **PANIC All Notes Off** control and hardware diagnostics. Console command `/windbound midipanic` is an emergency release if a key gets stuck. The full HUD displays "Live MIDI" when a controller is connected and output is armed. Your existing **Songs & Playback ARM** checkbox is shared: it is deliberately OFF each plugin launch.

Windbound sends direct FFXIV Performance callbacks from the game thread, not simulated keyboard input, so you can chat or alt-tab while playing. Live keys are intentionally suspended during MIDI-file playback or Ensemble mode. The same note engine and MIDI optimiser used for saved songs remain unchanged. Direct FFXIV Performance cannot express physical-key velocity; it plays the notes with the game's own sound.

**Platform:** hardware capture is implemented using Windows `winmm.dll`, with no NuGet dependency. A Wine/Proton setup can sometimes expose MIDI ports through winmm but this is not guaranteed; native Linux ALSA MIDI-IN isn't implemented yet. If your controller isn't listed, confirm it appears in Windows' MIDI devices and isn't held by another program.

**Also included:** the previously prepared **native group STOP instrument stow** change from v0.19.1, which sends the game's instrument=0 action after releasing notes rather than just closing the 37-key window. Group stow is game-version dependent; test with two clients before an octet.

Run `Tests/Run-v0.20-Tests.ps1` for pure note-router and existing ensemble tests, then build the plugin with `Build-Plugin.ps1` on your Windows/.NET 10 Dalamud development machine. No live FFXIV/USB testing was possible in the packaging environment.

---

# Nunu’s Windbound Bard v0.19 — Conductor-controlled octets 🎼

**New:** Select a performer for each MIDI track/channel, then press **SET PARTY TRACKS**. Every participating FFXIV party member running Windbound v0.19 automatically applies the conductor's assigned parts. The conductor can **Set Party Instruments** separately, and START still uses native FFXIV Ensemble Ready Check with Auto-Accept enabled by default.

**New STOP:** The conductor's **STOP ENSEMBLE + UNEQUIP ALL**, the main STOP button, or `/windbound stop` asks every Bard (including the conductor) to release notes, then close the FFXIV Performance keyboard and unequip the instrument. An unsupported native close is reported; no false success.

**Quick test:** On two clients, select Conductor/Follower and Native FFXIV Ensemble, open the same MIDI, assign track 1 to the conductor and track 2 to the follower, then click SET PARTY TRACKS. Verify `Tracks: READY (1/1)` on the conductor and the correct parts on the follower. Then click SET PARTY INSTRUMENTS, start with native FFXIV Ready Check, and press STOP ENSEMBLE + UNEQUIP ALL. If this works, scale to an octet. Solo playback, Soulweaver, HUD and Smart Playlists remain present.

See `CHANGELOG-v0.19.md` for exact behaviour, limitations and test instructions. Build using `Build-Plugin.ps1`; a full Windows SDK/in-game test is still required.

---

# Nunu's Windbound Bard v0.18.2 — Party Instrument Sync Hotfix

**If Edea does not switch when the conductor clicks SET PARTY INSTRUMENTS:** install this build on both clients, ensure Edea chooses Follower with native Game Ensemble and Follow leader enabled, and expand **Party command receiver diagnostics** in the Ensemble panel. The report identifies whether the command arrived, was rejected, or failed in the instrument switcher. See `CHANGELOG-v0.18.2.md` for troubleshooting and validation.

---

# Nunu's Windbound Bard v0.18 — Conductor File + Instrument Sync 🖤🎻

**Dalamud API 15 · .NET 10 · FFXIV Windows / Linux**

New in v0.18: a MidiBard-style **conductor SET instrument button**, **automatic follower MIDI selection and track maps**, and **Auto-Accept Ready Check ON by default for new installations**. Music itself still starts **only** on FFXIV's native Ensemble START event; there is no separate chat-based start timer.

## Two-minute setup (Nunu + Edea + Veits)

1. Install **v0.18 on every FFXIV account**, including additional clients on the same computer. Join the same **FFXIV party**; the Windbound Conductor must be the actual FFXIV party leader.
2. `/windbound` → **Ensemble** → choose **Conductor** on the leader and **Follower** on everybody else. **Use FFXIV native Ensemble** is on for first-time installers. Auto-Accept Ready Check is ON by default. Everyone enables **ARM output** and opens the *expanded 37-key* in-game Bard Performance keyboard.
3. Conductor selects a MIDI from **Songs & Playback**. Windbound broadcasts its hash and (if available) conductor party-track assignments. Followers automatically switch to the identical local MIDI, regardless of local folder names or playlist order. See MIDI `READY x/y` in Ensemble. If missing, use **SYNC SELECTED MIDI TO PARTY** after installing the exact file locally on that client.
4. Conductor chooses an instrument for each Bard in **CONDUCTOR FILE + INSTRUMENT SYNC**, then clicks **SET PARTY INSTRUMENTS — auto-switch everyone**. Wait for the Instruments `READY x/y` status. No follower clicks or in-game Ready Check confirmations needed for each song.
5. Conductor presses **START ENSEMBLE — FFXIV Ready Check**. Windbound automatically confirms the native Ready Check on ready clients, and the game delivers synchronized START to all. To change the song later, pick the next MIDI on the conductor and repeat.

If a client isn't ready, the conductor sees a failure or timeout instead of beginning an incomplete ensemble. Party-chat commands are visible to party members and send only file hashes, per-player instrument codes and part mappings, **not MIDI bytes**. The exact MIDI must exist locally. To disable the leader's automatic file/instrument control, untick **Automatically follow the party leader's MIDI and instrument choices**; to disable Ready Check auto-confirmation, untick its separate checkbox.

### Validation

The v0.18 package contains the full Visual Studio project, samples, `Tests/Run-v0.18-Tests.ps1`, and `CHANGELOG-v0.18.md`. Core note performance and Soulweaver remain unchanged from v0.17. **No real Windows compilation or FFXIV game session was possible here**. Test both accounts with the bundled 37-note calibration MIDI before a full concert.

---

# Nunu's Windbound Bard v0.17 — Native Ensemble Reliability Edition

**Dalamud API 15 · .NET 10 · FFXIV Performance MIDI player**

This release replaces the previous unreliable party-chat/UTC-timed ensemble START with a **single, native FFXIV Ensemble Ready Check + game START workflow**. The sequence is deliberately modelled on [MidiBard 2's documented ensemble behaviour](https://github.com/reckhou/MidiBard2/tree/v3-api15-stable). The code is independently written against FFXIVClientStructs and Dalamud; it is not a copy of MidiBard's AGPL implementation.

### What changed

1. The **conductor clicks START ENSEMBLE** once, which queues opening the FFXIV metronome, starts the game's Ready Check, and submits the conductor's confirmation.
2. All participating, ARMed clients with the same MIDI loaded **automatically confirm the native receiving Ready Check** through the in-game Performance callback (not via Windows keystrokes). Auto-accept is on when Native Ensemble mode is selected and can be disabled per Bard.
3. **Playback begins ONLY after FFXIV emits its native ensemble-start signal**. Both Windows and Linux clients use their **own local timestamp of that game signal**; no attempt to compare OS clocks, party chat delivery times or shared memory across PCs.
4. **Every native UI callback is now issued on the FFXIV framework update thread**, never directly from the ImGui Draw thread. The listener runs even while the Windbound window is hidden or FFXIV has been alt-tabbed.
5. Upgrading from v0.16 explicitly turns off the legacy timed party-chat OneClick START setting. It retains the source MIDI, stored MIDI arrangements, per-track octaves, chosen instruments, guitar tones, saved Solo/Ensemble playlists, Soulweaver Studio, Compact HUD and Smart Playlist Manager. Legacy local/TCP transport classes remain in the code but are not used while native mode is on.

### First duet / octet test

1. Install the **same v0.17 build** on every FFXIV client. Do not play the same MIDI through MidiBard 2 simultaneously with Windbound; that would send duplicate notes.
2. Join the same FFXIV party. Choose **Conductor** on the leader and **Follower** on all other performers.
3. On every client, enable **Use FFXIV native Ensemble (MidiBard-style; same PC or cross-PC)**. Leave **Automatically accept FFXIV Ensemble Ready Check** ON.
4. Load the **same MIDI file** on each client, select different **track checkboxes** locally and prepare each Bard's desired **instrument**. Remote track assignment and instrument switching via chat are not part of the start handshake in this reliability build; prepare these before the Ready Check.
5. Open FFXIV Performance using the **full 37-note C3–C6 layout** and tick **ARM** on every performer with assigned notes. If conducting silently, enter Performance mode but MIDI output need not be ARMed.
6. Press **START ENSEMBLE — FFXIV Ready Check** on the conductor. Do **not** click Play on individual followers. Once the game has gathered all native confirmations, it sends an ensemble-start signal to each client and Windbound starts their prepared parts.
7. Use the **Copy native diagnostics** button on both the conductor and a follower if there is a problem. Check that every client reports a non-zero native START count, and whether the follower's auto-confirm attempt counter has increased. These are callback attempts, not proof the game accepted them.

### Expected limitations

- All participants must have the appropriate MIDI file and local track assignments; **this mode does not transfer files over party chat**.
- **Automatic guitar/party instrument choices from the retired chat-timed OneClick path are not transmitted by this native start mode.** You can still choose instruments in Windbound before starting, and all related per-song settings remain saved.
- Native readiness, metronome callbacks and start hooks are **game-version-sensitive**. If the listener cannot find the signature, the UI reports it rather than launching a half-synchronised performance.
- The game's own confirmation and START event decide timing; Windbound does **not** claim frame-perfect sync or that all notes are audibly emitted. Other participants who haven't configured Windbound may still need to accept the game's Ready Check manually.
- Dalamud and automated music performance are third-party modifications; use at your own discretion with respect to FFXIV's terms.

### Build / verify

Open `NunuWindboundBard.sln` in Visual Studio on Windows, with .NET 10 and a configured Dalamud API 15 development environment. Build `src/NunuWindboundBard/NunuWindboundBard.csproj` for x64. `Build-Plugin.ps1` builds a Debug development assembly.

The new `Tests/Run-v0.17-Tests.ps1` performs structural checks and runs the existing native ensemble start-gate smoke tests where .NET is available. `Tests/Check-v0.17-Native-Ensemble.py` performs structural checks on Windows or Linux with Python 3. These **do not replace an actual FFXIV in-game ensemble test**.

This archive is a **Visual Studio source release**, not a compiled plug-and-play Dalamud plugin. It has been source-checked but not compiled or exercised in a live FFXIV client here.
