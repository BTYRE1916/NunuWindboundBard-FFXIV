<img width="256" height="256" alt="image" src="https://github.com/user-attachments/assets/ce695b91-69a6-4a60-8b56-13251d1a98ec" />
                 
                   

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


https://raw.githubusercontent.com/BTYRE1916/NunuWindboundBard-FFXIV/master/Plugin.json
