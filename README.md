WindboundBardFFXIV-Complete-Manual.html

Nunu’s Windbound Bard FFXIV — Complete User Manual
Version 0.32.0.0 · Wiki reference · Updated November 2026 · Based on the inspected Visual Studio source ZIP.

Contents
- Home
- Installation & Setup
- Your First Performance
- Performance Keyboard & 37-Key Mapping
- Chat Commands & Controls
- FFXIV Optimiser & MIDI Tags
- Smart Playlists
- Native Ensemble Quick Start
- Conductor & Octet Operations
- Concert Director & Setlists
- Visual Stage Designer
- Concert Snapshots & Restore
- Soulweaver Studio
- MIDI Arrangement Inspector
- SoundFont Audio Preview
- Live MIDI Piano
- Compact Performance HUD
- Languages & Localisation
- Troubleshooting
- Frequently Asked Questions
- Build, Beta Testing & Updates
- Version & Feature Reference
- Glossary
- Octet Concert Checklist
<a id="Home"></a>
Nunu's Windbound Bard FFXIV — Official User Wiki
A MIDI performance and concert-management suite for Final Fantasy XIV
Documentation version: v0.32.0.0 (Dalamud API 15 / .NET 10)
Audience: New performers, solo players, duet partners, octet conductors, and beta testers.

Welcome to the Windbound Bard wiki! Whether you want to play a single MIDI on one Bard or coordinate an eight-character concert, this manual takes you from your first 37-key test note to full-stage preparation.
Beta-build note: These instructions are grounded in the v0.32 source package, not a promise that every feature is guaranteed after an FFXIV patch. Verify your installed plugin version; some menu names are translated in French/German. Certain features are experimental or require another Dalamud plugin. This is an unofficial third-party project.

Start here
I want to…	Read
Install or build the plugin	Installation & Setup
Play my first song	First Performance
Set the 37-note keyboard correctly	Keyboard & Note Mapping
Manage a MIDI library	Smart Playlists
Perform an FFXIV duet or octet	Native Ensemble Quick Start
Control everyone's tracks and instruments	Conductor & Octets
Prepare a full concert	Concert Director
Place performers on stage	Stage Formations
Save and reload a concert	Concert Snapshots
Hear a MIDI through speakers	SoundFont Preview
Play a USB MIDI piano live	Live MIDI Input
Fix errors or stuck playback	Troubleshooting


Main features
- Direct FFXIV Performance output: C3–C6 (37 notes), chord handling, octave adaptation, and safe in-game note callbacks rather than simulated typing.
- Native ensemble: conductor/follower roles, in-game Ready Check start, auto-accept, party file selection, track assignments, instrument switching, ARM ALL, and group STOP/unequip.
- Concert tools: Stage Manager, setlist preparation, individual-Bard retry, MIDI Inspector, snapshots, persistent octet lineups, and compact HUD.
- Creative tools: Soulweaver Studio, SoundFont speaker preview, live MIDI controller input, guitar-tone overrides, and original-MIDI-preserving exports.
- Localisation: English, Français, Deutsch.
What this plugin does not do
Windbound cannot create missing MIDI files on another PC through ordinary party-chat selection. Native Ensemble START depends on the current FFXIV client and Dalamud compatibility. SoundFont preview is a local audio playback feature, not a recording or FFXIV soundfont emulator. Stage navigation uses optional vnavmesh and walks, rather than teleporting. The experimental stage diagram does not automatically rotate all Bards to face the audience.
Recommended first-session order
1. Install Windbound and check the Dalamud version.
2. Configure the expanded 37-key Performance keyboard.
3. Play the bundled calibration MIDI in Solo mode.
4. Only after Solo works, try two-account native ensemble.
5. Then add the conductor controls and Concert Director.
Navigation
Use the wiki sidebar for detailed chapters. The complete single-file manual and offline HTML edition are included in the documentation download. Your own Config.json, MIDI files, SoundFonts, and saved snapshot JSON files should be backed up independently of this wiki.
Windbound Bard is a community/beta project for performers. Follow FFXIV's rules and respect music rights. No affiliation with Square Enix or the Dalamud developers is claimed.
<a id="Installation-and-Setup"></a>
Installation & Setup
Requirements
- A working installation of Final Fantasy XIV and a compatible Dalamud environment. After major FFXIV patches, allow Dalamud and the game's native interfaces to become compatible before testing.
- The Windbound Bard compiled DLL and its dependencies, or the source project and the tools needed to build it.
- For building the v0.32 source: .NET 10, Dalamud.NET.Sdk 15, and a suitable x64 build environment (the supplied project specifies Dalamud.NET.Sdk/15.0.0).
- A MIDI file in Standard MIDI File format (.mid or .midi, format 0 or 1).
- An FFXIV Performance-capable character and an expanded 37-note Performance keyboard for sending game notes.
Optional additions: a local .sf2 SoundFont for speaker preview, a USB MIDI controller for Live MIDI Piano, or the separate vnavmesh plugin on each client using saved stage navigation.
Installing a compiled/test build
If the project maintainer supplies a compiled Windbound release or a maintained Dalamud plugin repository, follow the instructions for that specific release. Do not assume a GitHub source ZIP is an installable DLL or that an older repository manifest contains v0.32.

For a developer build, use Dalamud's official Dev Plugin Locations feature:
1. Start FFXIV through your Dalamud-enabled launcher.
2. Open /xlsettings → Experimental → Dev Plugin Locations.
3. Add the folder containing the built NunuWindboundBard.dll and its adjacent dependency files, or select the DLL where supported.
4. Apply the settings and load/enable the development plugin.
5. In FFXIV chat, enter /windbound to open the main window.
See Dalamud's official Getting Started page for development-plugin instructions. The old devPlugins drop-in folder is no longer the recommended approach.
Building from the Visual Studio source ZIP
1. Extract the complete project ZIP to a writable folder; don't open files directly inside the ZIP.
2. Open NunuWindboundBard.sln in Visual Studio, or use PowerShell in the extracted root:
   .\Build-Plugin.ps1
3. The script builds Debug/x64. Resolve any package-restore or SDK errors reported by Visual Studio.
4. Confirm NunuWindboundBard.dll and the audio dependencies are in the output folder. The source project references MeltySynth 2.4.1 and NAudio 2.2.1; deployment may require MeltySynth.dll, NAudio.dll, NAudio.Core.dll, and NAudio.WinMM.dll beside the plugin.
5. Add the built plugin folder under Dalamud Dev Plugin Locations. Launch or hot-reload, then open /windbound.
Important: A successful .NET build confirms compilation, not compatibility with the latest game client. Verify in game using the calibration MIDI.
First-run choices
1. Open Songs & Playback and make sure a Solo performance is selected on the Ensemble tab.
2. Choose English, Français, or Deutsch from the language selector at the top.
3. Open Direct Output & Test and check the Performance mapping.
4. Open the full FFXIV Performance keyboard and follow First Performance.
Where settings live
Windbound uses Dalamud's saved plugin configuration. MIDI paths and many settings are per file or per client, while ARM state is intentionally not restored after restarting. Back up the relevant Dalamud plugin configuration directory before upgrading to a beta, and save exported Concert Snapshots separately if you want portable copies of your setlists. Your SoundFont and MIDI files are not packaged inside configuration backups.
Multiple accounts and Linux
Each running game client has its own plugin state and HUD. Native FFXIV party commands let clients on the same PC or different PCs coordinate without comparing local Windows timers. The older local shared-memory and private-TCP modes are available but are not necessary for recommended native ensemble operation.

The main MIDI engine has been exercised with testers on Windows and Linux/Wine. A physical MIDI controller or SoundFont audio backend on Linux depends on whether those Windows audio/MIDI APIs are exposed through Wine/Proton. Do not assume every audio device will be detected automatically.
Support and updates
- Prefer a tested release over a new experimental build during a concert.
- Keep the previous ZIP/project and a copy of plugin configuration before each upgrade.
- After an FFXIV maintenance update, check native Performance, instrument switching, Ready Check and vnavmesh separately.
- If a UI label differs from this guide, compare the installed version against the v0.32 wiki target.
<a id="Your-First-Performance"></a>
Your First Performance — Solo Quick Start
This walkthrough is for a single Bard, with no party or conductor required. Complete it before trying octet synchronisation.
Five-minute checklist
1. Type /windbound in FFXIV chat.
2. Open Ensemble and choose Solo.
3. Open Songs & Playback, then click Load sample to load the bundled Bard-37-Note-Calibration.mid. Alternatively click Browse MIDI… and select a .mid or .midi file.
4. Open Instruments. Choose a Performance instrument such as Piano or Lute. For an automatic switch, enable Automatically switch instrument before Play. If FFXIV doesn't open the Performance keyboard on its own, enter expanded Performance manually.
5. Ensure FFXIV's expanded 37-key Performance window is ready. Windbound should show a ready indicator.
6. In Songs & Playback, tick ARM: Direct FFXIV Performance notes.
7. Optionally open Direct Output & Test and click Test C4.
8. Click Play. Verify notes are audible and the progress bar advances from 0%.
9. Click STOP to stop. Use Reset MIDI to start (00:00) (or right-click the progress bar) to rewind.
ARM is a safety switch. Without ARM, the player may run as a preview of scheduling without sending notes into FFXIV. ARM is not saved across plugin restarts.

Main playback controls
Control	Purpose
Play	Start the currently prepared MIDI; may first request the selected instrument
Pause / Resume	Pause or resume local Solo playback
STOP	Stop MIDI playback and release scheduled notes
Next	Move to the next song in the active Solo playlist
Playback speed	Adjust playback speed, 0.5×–2×
Auto next	Advance automatically when a Solo song finishes
Reset MIDI to start	Stop and rewind the local MIDI to 00:00
Show piano roll	Show a visual lane/note preview in the player
Show Performance HUD	Open the compact, movable performance panel


Working with MIDI files
Windbound supports Standard MIDI Files of format 0 and 1, including multi-track files. In Songs & Playback you can Browse MIDI, load a typed file path, add several files, or import a whole folder with optional subfolders. After a song loads, Windbound displays its track count, initial BPM, duration, final note count, chord groups and optimiser statistics.

If the notes sound too high/low, adjust the relevant track octave by -12 or +12 in Songs & Playback or the FFXIV Optimiser tab. A MIDI containing explicit #+12, #-12, #Lute or similar metadata can set these defaults automatically; see MIDI Instruments and Tags.
About chords
The default Full chords / original timing mode preserves note start times and durations. Other reduction modes (melody/highest, lowest, loudest and gentle arpeggio) are helpful when dense chords do not sound cleanly through the game. FFXIV may limit the number or duration of simultaneous notes; hearing every note is not guaranteed simply because the plugin sent callbacks.
If nothing plays
Before changing Windows or Linux keyboard settings, check: full Performance UI, ARM, a nonempty arrangement, the selected track checkboxes, and the Instruments status. If playback is stuck, try /windbound recover; it is an emergency local reset and requires ARM again. See Troubleshooting.
<a id="Performance-Keyboard-and-Mapping"></a>
FFXIV Performance Keyboard & 37-Key Mapping
This is the most important setup step. If FFXIV is set to a reduced/mini keyboard or to different bindings, a MIDI can appear to progress while no correct notes are heard.
Set FFXIV to expanded Performance
1. Enter Bard Performance mode in FFXIV with a Performance instrument.
2. Use the expanded/full keyboard (37 notes), not the mini keyboard.
3. In FFXIV's Performance keyboard settings, configure the 37 keys in chromatic pitch order using the mapping below.
4. Open /windbound → Direct Output & Test. The window should say Expanded Performance addon detected or FULL 37-KEY READY.
5. Tick ARM in Songs & Playback and use Test C4 (only when armed) to verify output.
Windbound uses the Performance addon's native callbacks, not simulated Windows keypresses. The mapping is still valuable as a reference and must match the game's full layout for players also using normal keyboard Performance controls.
Default 37-note binding table
The supplied PerformanceKeyMap.DefaultKeys is exactly:
ZXCVBNMASDFGHJKLQWERTYUIOP1234567890;
MIDI	Note	Key	MIDI	Note	Key	MIDI	Note	Key
48	C3	Z	61	C♯4	J	74	D5	1
49	C♯3	X	62	D4	K	75	D♯5	2
50	D3	C	63	D♯4	L	76	E5	3
51	D♯3	V	64	E4	Q	77	F5	4
52	E3	B	65	F4	W	78	F♯5	5
53	F3	N	66	F♯4	E	79	G5	6
54	F♯3	M	67	G4	R	80	G♯5	7
55	G3	A	68	G♯4	T	81	A5	8
56	G♯3	S	69	A4	Y	82	A♯5	9
57	A3	D	70	A♯4	U	83	B5	0
58	A♯3	F	71	B4	I	84	C6	;
59	B3	G	72	C5	O			
60	C4	H	73	C♯5	P			


The range is MIDI 48 through 84 inclusive, C3 to C6. This is 37 chromatic notes, not 37 white piano keys.
Why the note range matters
A concert MIDI may contain notes below C3 or above C6. Windbound's FFXIV Optimiser can shift parts by octaves and optionally repair remaining out-of-range notes without changing the source MIDI. Review every track individually; one global transpose is not always the best solution for a full orchestra.
Troubleshooting keyboard problems
Symptom	Check
Progress advances, no sound	ARM enabled, instrument equipped, expanded keyboard open, FFXIV Performance available
Wrong notes	Confirm the full 37-key setup and MIDI octave/transpose settings
Follower silent	Native Ensemble role, matching MIDI, assigned lanes and output ARM
“Performance not ready”	Open the 37-key keyboard; wait for instrument switch to settle
A song starts halfway through	Right-click the progress bar → Reset MIDI to start (00:00)


<a id="Commands-and-Shortcuts"></a>
Chat Commands & Controls Reference
The commands below are verified in the v0.32 Plugin.cs command handler. Enter them in the FFXIV chat box.
Command	Effect
/windbound	Open/close the main player window
/windbound stop	Stop local MIDI playback (when used as a conductor, follow the explicit group-STOP workflow for party-wide actions)
/windbound recover	Emergency reset of local MIDI scheduler/pending start/stage state; requires ARM again
/windbound resetmidi	Alias for local emergency recovery
/windbound midipanic	Live MIDI Piano: release all held controller notes
/windbound hud	Toggle Compact Performance HUD
/windbound hud show	Show the HUD
/windbound hud hide	Hide the HUD


Important UI actions that are not chat commands
- SET PARTY TRACKS, SET PARTY INSTRUMENTS, ARM ALL BARDS, DISARM ALL and START ENSEMBLE are buttons in the Ensemble / Concert Director interface.
- STOP ENSEMBLE + UNEQUIP ALL is the party-wide stop/put-away workflow; don't confuse it with simply closing a local HUD.
- SET POSITIONS & MOVE PARTY is in the Stage Formations / Visual Stage Designer area and does not start music.
- Reset MIDI to start (00:00) is also available via right-click on the playback progress bar; it rewinds only the local scheduler.
Recommended keyboard/mouse shortcuts
Windbound doesn't need a global keyhook for MIDI playback. For fast work, leave the compact HUD movable/locked and use the on-screen controls. If your Windows keybindings or FFXIV Performance bindings differ from the supplied defaults, refer to Performance Keyboard & Mapping.
After a failed command
Check the status message in the plugin rather than assuming the operation succeeded. Group commands must be received and acknowledged by the expected characters; one sent message is not delivery proof. See Troubleshooting.
<a id="FFXIV-Optimiser-and-MIDI-Tags"></a>
FFXIV Optimiser, Instruments & MIDI Track Tags
The optimiser adapts a MIDI to FFXIV's C3–C6 Performance note range. It does this at playback time; the source .mid is not rewritten when you change a setting.
Track-by-track settings
In Songs & Playback or FFXIV Optimiser, each MIDI track/channel appears as its own lane. You can:
- Enable or mute the lane.
- Shift it by octave steps (-12, +12) from -48 to +48 semitones.
- Enable Repair remaining high/low notes by octaves.
- Apply a playback-only global transpose of up to ±24 semitones.
- Choose full chords, highest/melody, lowest, loudest, or gentle arpeggio handling.
- Adjust minimum note gap, arpeggio spacing, and note hold duration.
Use the MIDI Inspector to see whether a shift actually improves the playable note count.
Detecting instruments automatically
Windbound reads MIDI Program Change data and instrument metadata, including track names, instrument names, text and marker/cue events. When possible it recommends a matching FFXIV instrument; General MIDI programs without an exact equivalent are approximated. Turn on Auto-detect instruments from MIDI tracks / GM programs under MIDI Instruments & Octave Tags.

For a group, the conductor assigns tracks to Bards first. Then SET PARTY INSTRUMENTS uses detected instrument suggestions for the assigned tracks, unless a saved manual choice overrides them.
Track-name notation
Windbound recognises explicit instrument and octave instructions such as:
#+12 #Lute
#-12 #Piano
#Violin
#+24 #Flute
- #+12 means transpose that lane up one octave; #-12 means down one octave.
- #Lute selects/recommends the FFXIV Lute instrument.
- The tags affect that track, not the whole orchestra.
- A track containing both a tag and a General MIDI patch gives priority to the explicit tag.
- If a performer owns multiple conflicting instrument tracks, the conductor gets a suggestion/warning and should choose one instrument for that performer.
Manual overrides and re-reading tags
Manual octave changes are saved per MIDI path and take precedence over subsequently imported tags. If a tag seems ignored, open MIDI Instruments & Octave Tags and try:
1. Re-read MIDI tags now (keep manual overrides) — rescans metadata without overriding your edits.
2. Restore MIDI octave tags (clear manual octave overrides) — clears saved manual octave settings for the current MIDI and reapplies tags.
3. Use MIDI instrument suggestions (clear manual instrument overrides) — tells the conductor to use detected instruments again.
4. Copy detected MIDI metadata report — provides raw labels, GM programs, tag values and actual applied octaves for troubleshooting.
Do not press reset buttons casually during a prepared concert if you need specific custom octaves; save a Concert Snapshot first.
Guitar tone overrides
For an electric guitar instrument, open Guitar Tones and enable track-based overrides. Select Clean, Overdriven, Muted, Power Chords or Special (where applicable). Changes are remembered per MIDI/track/channel and applied on the next performance. FFXIV supports one active guitar tone per performer at a time, so simultaneous tone changes across multiple tracks may conflict.
Useful sample files
- Bard-37-Note-Calibration.mid — basic pitch test.
- Bard-Chord-Callback-Test.mid — overlapping note and chord test.
- Windbound-MIDI-Instrument-and-Octave-Tags.mid — tagged instrument/octave test.
- Windbound-MIDI-FF01-Marker-and-GM-Detection.mid — metadata format test.
<a id="Smart-Playlists"></a>
Smart Playlists & MIDI Library Management
Separate libraries for Solo and Ensemble
Windbound keeps distinct Solo and Ensemble playlist profiles. Changing the Ensemble role/library saves the current playlist and restores the appropriate one. A named playlist stores its own song order and Smart Playlist settings.

In Songs & Playback you can:
1. Click Add MIDI files… or Add MIDI folder… to populate a playlist.
2. Choose Solo library or Ensemble library.
3. Use Create playlist to make a new named profile; choose whether to copy the current songs.
4. Use Rename, Save playlist, Export playlist JSON or Import playlist JSON to manage backups.
Search, favourites and tags
Use the Smart Playlist Manager's search to filter by filename, path or custom tag. Toggle Favourites only, or assign comma-separated tags such as duet, piano, classic. Favourites and tags are scoped to the saved playlist and are included when exporting a playlist JSON file.
Drag-and-drop ordering
Drag a song row onto another to change order. Alternatively right-click a song for Move one place up/down, Play NEXT, Add to favourites, or Remove from playlist. Removing a playlist entry does not delete the actual MIDI file.
Play Next, shuffle and repeat
- Play NEXT adds a song to a separate request queue. It does not rearrange the main playlist.
- Shuffle draws from a no-repeat cycle, subject to the currently prepared song and repeat mode.
- Repeat Off stops at the end; Repeat All wraps/restarts; Repeat One repeats the current track after completion.
- Manual Next bypasses Repeat One, while queued songs take priority over the ordinary order.
- Auto next works for Solo playback; ensemble song transitions remain controlled by the conductor.
Recent songs
The recently played panel keeps up to 50 recent starts per playlist profile (the UI shows the latest 20). You can use the panel to reload a familiar MIDI.
Missing file cleanup
Click Scan for missing files, review the missing entries and confirm removal. This deletes broken playlist references only. Your MIDI files on disk remain untouched.
Recommended concert-library structure
Consider separate profiles such as:
Playlist	Purpose
Nunu - Solo Piano	Personal practice / live keyboard pieces
Nunu & Edea - Duets	Two-Bard concert order
Full Octet - Main Set	Eight-player concert with saved lineup
Full Octet - Encores	Optional requested songs


A Song Playlist JSON export stores paths and metadata but not the underlying MIDI bytes. On another PC, make sure those files exist at the paths expected by the receiving client, or import the same songs into its local library.
<a id="Native-Ensemble-Quick-Start"></a>
Native FFXIV Ensemble — Duet Quick Start
Recommended ensemble mode: FFXIV Native Ensemble Ready Check. This uses FFXIV's in-game start mechanism. Windbound's retired party-chat time-delay start is not the recommended mode; ordinary party commands can prepare clients, but the native game START triggers the music.
What every Bard needs
- The same Windbound version and a working Dalamud installation.
- Characters in the same in-game FFXIV party.
- Their own copy of the exact MIDI locally (file paths can differ by computer).
- An assigned musical part, the required instrument and the expanded 37-key Performance interface.
- Windbound's ARM enabled, either locally or via the conductor's ARM ALL BARDS control.
Example: Nunu and Edea duet
On the conductor's client
1. Open /windbound → Ensemble → select Conductor.
2. Enable FFXIV native Game Ensemble and Automatically follow the party leader's MIDI and instrument choices (leader sync).
3. Be the actual FFXIV party leader. Load the desired MIDI into the Ensemble playlist.
4. In Party-Roster Track Assignments, select which track/channel goes to Nunu and which goes to Edea.
5. Click SET PARTY TRACKS and confirm follower acknowledgements.
6. Select instruments and click SET PARTY INSTRUMENTS (or PREPARE ENTIRE ORCHESTRA in Concert Director).
7. Click ARM ALL BARDS. Wait for responses and check that both have their instruments and 37-key Performance keyboard ready.
8. Press START ENSEMBLE — FFXIV Ready Check. The game start cue triggers each client's assigned notes together.
On Edea's follower client
1. Select Follower in Ensemble; enable FFXIV native Game Ensemble.
2. Leave Automatically follow the party leader's MIDI and instrument choices on.
3. Leave Automatically accept FFXIV Ensemble Ready Check on (this is enabled by default, but may be disabled manually).
4. Leave Allow my party leader to ARM this Bard automatically enabled if the conductor should operate ARM for this account.
5. The conductor's file and SET commands should load the matching local MIDI and prepare the selected tracks/instrument. Confirm the 37-key Performance UI is active.
6. Wait for the native START; the follower should not use its own Solo Play button.
How song selection is synchronised
Windbound's conductor sends a MIDI fingerprint and selection command, not the MIDI file contents. A follower scans its local known MIDI entries for the matching file. If it cannot find the identical file, it reports an error: add the same MIDI to that client's library and retry.
What Auto-Accept does
The auto-accept option can confirm an incoming FFXIV Performance Ensemble Ready Check when the client is configured and sufficiently ready. It is not a substitute for loading the right MIDI or entering Performance. If a client cannot participate safely, Windbound may refuse the check instead of pretending it is ready.
Stop, disarm and unequip
- STOP ENSEMBLE + UNEQUIP ALL: conductor request to stop notes, close Performance and request the native instrument-stow action on all clients.
- DISARM ALL: turn output ARM off without necessarily putting instruments away.
- STOP my playback: local stop; not the same as conductor-wide group stop.
Compatibility notes
Native Game Ensemble is designed for same-PC multiple game clients and parties across different machines, including mixed Windows/Linux setups. Private TCP / Tailscale is a separate legacy mode, not required for native FFXIV ensembles. Network party commands travel over normal in-game party chat and may be visible to party members; START timing itself comes from the game.
<a id="Conductor-and-Octet-Operations"></a>
Conductor Controls & Octet Operations
This chapter is for anyone running a duet, quartet, octet, or multiple game clients on one PC. All preparation commands use Windbound's party-leader coordination; the FFXIV native Ready Check remains the only start trigger.
Roles and prerequisites
- Solo: plays locally; has no conductor role.
- Conductor: chooses the ensemble's MIDI, performer assignments and instruments. Must be the actual leader of the FFXIV party for leader commands to be accepted.
- Follower: accepts matching file/track/instrument/ARM messages and waits for the native ensemble start.
All performers need the same MIDI bytes locally, a compatible Windbound version, and expanded Performance. Give each character enough time for instrument switching before START.
Assigning performers to tracks
1. Load an Ensemble MIDI into the conductor's active playlist.
2. Open Ensemble → Party-Roster Track Assignments.
3. For each Track / Channel, select the Bard who should play its notes. Choose Unassigned / silent for unused parts.
4. Click SET PARTY TRACKS — send to every Bard.
5. Wait for Tracks: READY acknowledgements. A follower with no assigned notes may correctly be silent; that is not automatically an error.
Keep your assignments across songs
Enable Keep party track assignments across songs (on by default in recent builds). The saved lineup slots carry to the next song in the same Ensemble playlist; per-song overrides can still take precedence. Use Save current as default lineup, Apply saved lineup to this MIDI, and Forget saved lineup as needed. New MIDI tracks that exceed the saved lineup need checking.

Best practice: If a special song uses a different performer arrangement, temporarily turn reuse off, edit that song, then re-enable reuse. The master lineup is not replaced unless you explicitly save it.
Set Party Instruments
Under Conductor File + Instrument Sync, pick an instrument for each party member. Press SET PARTY INSTRUMENTS — auto-switch everyone. Followers automatically use the game's Performance instrument switching and report READY/failure. If MIDI Instruments & Octave Tags is enabled, detected instrument suggestions can be used unless you saved a manual instrument choice for this MIDI.

If a player has multiple parts requiring different instruments, choose a sensible main instrument for that character or split the parts among Bards. One Bard cannot simultaneously hold Piano and Violin.
Set Party MIDI
Selecting an Ensemble song can automatically tell followers to load the matching MIDI. SYNC SELECTED MIDI TO PARTY resends the file-selection command when necessary. The file is looked up by identity/fingerprint; a same-named MIDI with different bytes is not necessarily a match.
ARM ALL / DISARM ALL
ARM ALL BARDS arms the conductor and the opted-in followers. It never starts notes or equips instruments. Look for a response count such as 7/7 acknowledged in an eight-character group. A follower may be ARMed but still show 37-key keyboard needed; prepare the instrument before starting.

DISARM ALL revokes ARM; the instruments can stay equipped. ARM state is deliberately not saved across restart. Follower opt-in is controlled separately by Allow my party leader to ARM this Bard automatically.
Recommended conductor sequence
Choose Ensemble playlist and MIDI
            ↓
Assign / reuse performer slots
            ↓
SYNC SELECTED MIDI → SET PARTY TRACKS
            ↓
SET PARTY INSTRUMENTS (or PREPARE ENTIRE ORCHESTRA)
            ↓
ARM ALL BARDS → check READY status
            ↓
START ENSEMBLE — native FFXIV Ready Check
            ↓
STOP ENSEMBLE + UNEQUIP ALL at the end
Same-PC eight-account setup
Running many game clients does not require eight Windows keystroke bots; Windbound's notes use direct Performance callbacks. Give each client the right Follower setting and 37-key Performance layout, keep the MIDI library available per account, and verify the correct client name appears in the compact HUD. The conductor's stage readiness panel helps identify a missed client without focusing each window.
When only one client responds
1. Confirm every client runs the same plugin version.
2. Confirm Conductor is the real in-game party leader and the other accounts are Follower.
3. Inspect the Follower Party command receiver diagnostics for messages seen, accepted, and rejected.
4. Verify the MIDI fingerprint and party roster are the same.
5. Try SYNC SELECTED MIDI, SET PARTY TRACKS, SET PARTY INSTRUMENTS separately.
6. If a client doesn't equip, test the Instruments tab locally. Its issue may be native Performance readiness rather than party messaging.
<a id="Concert-Director-and-Setlists"></a>
Concert Director, Stage Manager & Setlist Automation
Concert Director is the conductor's rehearsal and live-show control centre. It sits in the Ensemble tab when native Game Ensemble and leader synchronisation are enabled.
Prepare Entire Orchestra
1. Load the intended Ensemble MIDI.
2. Confirm performer slots, instrument choices and track assignments.
3. Click PREPARE ENTIRE ORCHESTRA.
4. Windbound runs the existing file/track/instrument preparation and requests status reports from the party.
5. Review Stage READY: X/Y. A yellow or missing row identifies a specific Bard who needs attention.
6. If one Bard fails, click Retry beside that member instead of resetting the other seven.
7. Once ready, click the separate START ENSEMBLE button to use FFXIV's native start mechanism.
Preparation does not start music. Song selection and instrument switching can occur before you address the audience.
What a readiness report checks
Stage Manager can identify an invalid/missing MIDI, mismatched file fingerprint, absent track assignments, missing or wrong instrument, unready full Performance keyboard, output not ARMed, or unacknowledged follower state. These reports depend on party-chat delivery and can be refreshed. A message being sent is not proof every client received it; wait for each acknowledgement.
Stage Manager buttons
Control	Effect
PREPARE ENTIRE ORCHESTRA	Coordinate song, tracks and instrument preparation
ARM ALL BARDS	Enable output across participating clients (separate from START)
Refresh Stage Status	Request new status reports
Retry beside a Bard	Re-send preparation to that specific performer
Cancel Preparation	Cancel the current preparation cycle, not launch a song
Copy Stage Report	Copy a diagnostic summary to share with a tester


Concert Setlist Automation
Open the Concert Setlist Automation section in Concert Director.
- Automatically prepare next Ensemble song (never auto-start): optional; OFF by default. Preloads the next MIDI and prepares the group after the current piece finishes. You still press native START for the next performance.
- PREPARE NEXT SONG NOW: manually stage the next song when you are ready.
- Introduction / audience announcement: a saved note for what you plan to say to your audience; treat it as a cue, not an automatic public-chat announcement.
- Break after song (seconds): an intentional pause before the following preparation.
- HOLD after this song: pause the setlist workflow so you can speak, adjust instruments or take requests.
- Mark as Encore: annotate optional encore songs.
The next-song choice follows the Smart Playlist's saved order/Play Next logic; the conductor remains in control of every native Ensemble START.
Example: Tenacious Bar'ds concert
Order	Song	Concert cue	Action
1	Opening Theme	Welcome the audience	Prepare orchestra, ARM, native START
2	Piano & Violin Duet	Introduce Edea	Prepare next; check two allocated parts
3	Full Octet	All eight players	Wait for 8/8 readiness; native START
4	Finale	Audience thanks	Mark as encore/HOLD after


Tips for a reliable show
- During a live concert, avoid enabling the experimental stage editor at the same time as active playback.
- Leave Auto next enabled only when you intend unattended Solo sequencing; ensembles are different.
- Use Concert Snapshots to preserve a complete show configuration.
- Keep Audio Preview local to avoid confusing rehearsal sound with game Performance output.
- Send a stage report if only some Bards become READY.
<a id="Visual-Stage-Designer"></a>
Stage Formations & Visual Stage Designer
Windbound offers saved stage positions and an experimental draggable dot editor. These are two ways to create the same kind of formation preset.
Stage Designer is experimental and OFF by default in v0.32, because v0.31 introduced a navigation/playback regression that was addressed in v0.31.1–0.31.2. First verify ordinary MIDI playback, then enable the visual editor for a controlled rehearsal.

Prerequisites
- Same in-game FFXIV party, at the same actual stage and housing ward/instance.
- The actual party leader must select Conductor.
- Every participating character needs vnavmesh installed and working locally. Its navmesh needs to be ready for the area.
- Each Follower must enable Allow my party leader to save my position and move me on stage.
- All performers must be out of Performance and out of combat before movement.
Movement uses vnavmesh's normal path-walking. It does not teleport characters, bypass collision, or force a facing angle.
Method A: Capture positions you've already placed
1. Position each Bard manually on the stage.
2. In the conductor's Stage Formations tab, enter a formation name.
3. Click SAVE CURRENT PARTY POSITIONS. Follower clients report their positions after their permission checks.
4. Select the saved preset in Saved formations.
5. During a later concert, return everyone to the correct stage/ward, then click MOVE PARTY INTO SAVED FORMATION.
6. Review arrival/failure results. Use STOP STAGE MOVEMENT to cancel navigation.
Method B: Design a formation using dots
1. In Stage Formations, enable Enable Visual Stage Designer (experimental).
2. Stand the conductor at approximately the stage centre and face toward your intended audience. Click Re-anchor stage at my position if the diagram's origin needs updating.
3. Create a diagram or load a saved formation.
4. Choose a starting shape — Line, Arc, Circle, or Two Rows.
5. Drag each numbered Bard dot around the square. The dot represents a position relative to the selected stage centre and heading.
6. Select a dot to assign the appropriate party member. Selecting someone already assigned swaps their spots, rather than duplicating a Bard.
7. Set the Stage width (metres); optionally enable Snap 0.5 m.
8. Save the diagram without moving anyone, or click SET POSITIONS & MOVE PARTY to save and request movement.
9. Check acknowledgements, distances and failed path reports before preparing MIDI playback.
Stage layout example

                    ┌──────────── STAGE (TOP VIEW) ────────────┐
                    │       [4]   [5]   [6]   [7]              │
                    │   [2]                          [8]       │
                    │                [1]    [3]                │
                    └──────────────────────────────────────────┘
                                     AUDIENCE
This diagram is illustrative; the real layout is a draggable square inside Windbound. A dot is a relative target, not a guarantee that vnavmesh will find a walkable route.
Safe operation
- Same housing ward matters. Different wards can have identical local coordinates; Windbound also checks that the party leader is actually nearby.
- No automatic movement on login/song load. The conductor explicitly clicks a move action.
- No automatic facing in v0.32. Facing is saved as a guide only.
- No movement while playing or fighting. Stop the MIDI and leave Performance before repositioning.
- If navigation becomes stuck, use STOP STAGE MOVEMENT or /windbound recover locally, then disable the experimental editor until tested.
Common problems
Problem	Suggested check
A Bard does not move	vnavmesh installed and its mesh built; follower stage permission enabled
Wrong stage/coordinates	All in same ward/instance; conductor re-anchor position/heading
Navigation times out	Dot may be unreachable; move it closer or change layout width
“Already running” or stuck state	Distinguish stage navigation from MIDI playback; stop stage movement and use recovery if needed
Facing incorrect	Auto-rotation isn't included in this version


<a id="Concert-Snapshots-and-Restore"></a>
Concert Snapshots & Restore
A Concert Snapshot is a named backup of a concert's configuration. You can save a Solo programme, duet, quartet, or full octet and restore it later without manually rebuilding every track or instrument selection.
What's included
Snapshots can capture the active playlist and order, selected MIDI, per-file instrument and octave settings, track assignments, default octet lineup, playback tempo/settings, Smart Playlist details, Concert Director cues, and related rehearsal preferences. They do not contain the MIDI audio bytes or SoundFont file itself.
Save a snapshot
1. Open /windbound → Concert Snapshots.
2. Type a useful name such as Full Octet – Mist Stage – Friday.
3. Click SAVE CURRENT CONCERT SNAPSHOT.
4. Verify it appears in the Saved concert drop-down with the correct number of MIDIs and performer slots.
5. For a PC backup, select it and click Export snapshot JSON.... Save the file somewhere outside the plugin folder.
Restore a snapshot
1. Stop playback and any pending stage movement.
2. Go to Concert Snapshots and choose the saved entry.
3. Expand Preview snapshot contents. Check the selected MIDI, playlist name, performer slots and Missing MIDI files count.
4. Click RESTORE SELECTED SNAPSHOT... → CONFIRM RESTORE.
5. Windbound creates a new saved playlist profile and saves a Before restore safety snapshot.
6. Review the settings; ARM and follower consent are not restored.
7. For an ensemble, rejoin the correct party, Prepare Entire Orchestra, ARM everybody and send the normal native Ready Check.
Restore is local and does not auto-start music, equip instruments, ARM output or broadcast party commands. It can, however, replace some shared local per-MIDI override settings; review the automatically created Before restore backup if necessary.
Import and export
- Export snapshot JSON stores data for backup/sharing, not the MIDI files themselves.
- Import snapshot JSON brings saved concert settings back to Windbound.
- On another computer, file paths may differ; add/copy the matching MIDI files locally and repair references as needed.
- Snapshots may include local path details and performer identifiers. Treat exported files as private to your ensemble.
Recommended snapshot names
Name	Use case
Nunu Solo Piano	Single Bard, piano settings and Solo playlist
Edea Duet	Piano/violin tracks, two-Bard order and instruments
Full Octet – Main Set	Eight-person track lineup and concert cues
Festival Encore	Special songs and a different prepared performer set


<a id="Soulweaver-Studio"></a>
Soulweaver Studio — Create Solo, Duet, Quartet & Octet Arrangements
Soulweaver is Windbound's arrangement generator. It analyses a loaded MIDI and creates separate musician parts for FFXIV Performance, using heuristic melody, bass and harmony grouping. It is not a generative audio model or a guarantee of perfect orchestration; audition and refine the results.
Create an arrangement
1. Load the source MIDI in Songs & Playback.
2. Review and adjust octave shifts in FFXIV Optimiser if necessary.
3. Open Soulweaver Studio.
4. Select Solo, Duet, Quartet or Octet (1, 2, 4 or 8 performers).
5. Decide whether to Analyse all non-percussion tracks or respect the currently muted/enabled lanes.
6. Optionally set a Per-performer chord limit; 0 allows unlimited prepared chord notes.
7. Click Analyse & Generate Parts.
8. Examine the estimated key, generated note counts, range, warnings, timeline and per-part role descriptions.
9. For each generated part, choose its FFXIV instrument and, if applicable, a party member. The Auto-assign button uses party order.
10. Click Export arrangement MIDI... or Export & add to Solo/Ensemble playlist.
Generated output
The exported MIDI is a new file; the original source is not overwritten. Each musician receives their own output track/lane, with original tempo changes preserved where possible. The file can then be loaded back into Windbound for native Ensemble preparation, SoundFont preview or inspection.
Rehearsal workflow
- Use SoundFont Audio Preview in Windbound Arrangement mode to listen without sending FFXIV notes.
- Use MIDI Inspector to catch out-of-range notes, missing playable parts and conflicts.
- Use the Party-Roster Track Assignments panel to verify each Bard is responsible for the intended output lane.
- If the arrangement is too dense, reduce the chord limit or adjust melody/bass allocations; don't assume every MIDI sounds good in full chords.
Saving and rerunning
Soulweaver presets are saved per source MIDI. If you change octave settings, regenerate the arrangement before exporting. A previously exported MIDI doesn't automatically change just because its source preset changed.
Limitations
Soulweaver uses deterministic musical heuristics and cannot guarantee the desired lead melody, drum handling or instrument blend. Automatic instrument suggestions should be reviewed before equipping performers. Remember the game only supports a limited C3–C6 range and sometimes restricts audible polyphony.
<a id="MIDI-Inspector"></a>
MIDI Arrangement Inspector — Pre-Concert Quality Check
The MIDI Inspector tab analyses the currently prepared MIDI, highlighting issues before you bring a party onto the stage. Inspection is read-only unless you explicitly ask Windbound to make an octave adjustment.
Inspect an arrangement
1. Load the MIDI and any desired lane settings in Songs & Playback.
2. Open MIDI Inspector.
3. Click INSPECT MIDI ARRANGEMENT.
4. Review the summary: playable notes / source notes, errors, warnings, and per-lane pitch ranges.
5. Inspect each row for track/channel, current octave shift, suggested instrument, note count and party assignment.
6. Expand Inspection findings to see warnings such as out-of-range notes, suspicious overlaps or unused parts.
7. Click Copy Inspection Report to share a plain-text report with beta testers or your concert team.
Auto-fit untagged octaves
Auto-fit untagged octaves (manual action) suggests in-range shifts for MIDI lanes that do not already have explicit #+12/`#-12` tags or saved manual octave overrides. This does not rewrite the original MIDI file. It also skips disabled tracks.

If you want the original file's tagged octave back, use the MIDI Instruments & Octave Tags controls in Songs & Playback rather than blindly changing everything.
What to look for in an octet
Finding	Action
Zero playable notes assigned to a Bard	Check track assignment and lane enable state
Many notes outside C3–C6	Review octave shifts and repair options
Several tracks call for different instruments on one Bard	Choose a main instrument or split tracks
Excessive overlaps / duplicate pitches	Audition and consider chord-handling adjustments
Marked octave conflicts with manual shift	Confirm whether the manual override is intentional
Wrong inferred instrument	Check embedded MIDI metadata and saved instrument overrides


Exporting and audibly verifying
Inspect → edit lane settings → inspect again → Audio Preview → short Solo test → full Ensemble rehearsal. This is safer than trying a new dense MIDI live for the first time in front of an audience.
<a id="SoundFont-Audio-Preview"></a>
SoundFont Audio Preview (Local Speakers)
SoundFont preview lets you hear a MIDI through your local PC speakers without equipping an FFXIV instrument or broadcasting notes to the party. It uses MeltySynth to render SoundFont instruments and NAudio for playback.
What you'll need
- An existing .mid or .midi loaded in Songs & Playback.
- A General MIDI .sf2 SoundFont. Windbound does not bundle any SF2 bank; choose one whose licence allows your intended use.
- Working Windows speaker output or suitable Wine/Proton audio compatibility; the v0.32 backend uses the Windows audio stack.
Play a preview
1. Select and load a MIDI in Songs & Playback.
2. Open Audio Preview.
3. Click Browse SoundFont... and select an extracted .sf2 file. You can also enter its path and click Save SoundFont path.
4. Choose Preview source:
   - Original MIDI (all tracks and GM instruments) — hear the original file, tempo changes and GM program information.
   - Windbound C3–C6 arrangement (enabled local parts) — hear notes after the current Windbound optimisations, lane mutes and octave shifts.
   - Selected MIDI track (solo preview) — listen to one lane; optionally Apply this track's saved octave shift.
5. Adjust Preview volume and Preview tempo (0.5×–2×).
6. Click Play Audio Preview. Use Pause/Resume and Stop Audio Preview normally.
Which SoundFont should I use?
For a full band/octet with mixed instruments, choose a complete GM SoundFont with at least 128 GM melodic programs and percussion. Options to audition include GeneralUser GS, Arachno SoundFont and FluidR3 GM where licensing permits. Different SoundFonts will render the same MIDI with different timbres.
A SoundFont is a speaker approximation, not the exact timbre of FFXIV Performance instruments. Keep the original MIDI as a reference and audition the Windbound arrangement before a public concert.

Common problems
Symptom	Check
No audio	Selected .sf2 exists, volume above zero, Windows default output device works
“SoundFont not found”	Extract the archive; select the .sf2 file, not .7z/`.zip`
All parts sound like Piano	Original MIDI may contain few GM program changes; try Windbound arrangement mode and track metadata
Preview stops when pressing Play in FFXIV	Intentional safety behaviour: live FFXIV file playback stops local speaker preview
Compiler error about MidiFileSequencer.MidiFile	Use the corrected v0.27.1+ build; don't mix old Audio Preview files
Linux audio unavailable	Windows WinMM/NAudio backend may need Wine configuration; this path hasn't been proven universally


<a id="Live-MIDI-Piano"></a>
Live MIDI Piano — Play a USB Keyboard in FFXIV
Live MIDI Piano turns a physical MIDI keyboard or synthesizer into a live FFXIV Performance controller. This is a separate, Solo-only input path, not a MIDI file player or group conductor feature.
Set up a keyboard
1. Connect the USB MIDI keyboard or MIDI interface to the PC.
2. In Windbound's Ensemble tab, choose Solo.
3. Open Live MIDI Piano.
4. Enable Enable live MIDI controller input and click Refresh MIDI devices.
5. Choose the MIDI port from the MIDI input device list.
6. Click Connect keyboard. The status should show the connected device name.
7. Click Equip FFXIV Piano (instrument 2) if needed. Open the expanded 37-key Performance interface.
8. Tick ARM live notes to FFXIV and play a few notes on the physical keyboard.
A MIDI file does not need to be loaded for live piano input.
Live settings
Setting	Purpose
Controller octave transpose	Shift incoming pitches by ±12-step octave increments (up to ±48)
Fold other octaves into C3–C6	Bring notes from an 88-key controller into FFXIV's limited range
Use sustain pedal (CC64)	Interpret a typical MIDI sustain pedal
MIDI channel	All channels (omni) or one selected channel
PANIC: All Notes Off	Release the router's held notes immediately
Reconnect / Disconnect	Recover from a disconnected or occupied MIDI device


Safety and limitations
- The live input router pauses itself while MIDI-file playback is running, to prevent two sources fighting over FFXIV keys.
- Turning ARM off, disconnecting, leaving Performance or switching to Ensemble stops/renders the live notes inactive.
- FFXIV's Performance interface accepts C3–C6; velocity and fully expressive piano dynamics aren't available through the game's 37-key callbacks.
- The input service uses Windows WinMM. Some controllers work through Wine; others require driver configuration or may be in use by another application.
- If notes become stuck, press PANIC or type /windbound midipanic.
Diagnose a quiet keyboard
Check the Connected label, total Received notes, Held notes, Last MIDI note, Bard note, Out-of-range count and driver errors. If the plugin receives notes but FFXIV is quiet, check output ARM, Performance mode, C3–C6 mapping and instrument selection before reinstalling drivers.
<a id="Compact-Performance-HUD"></a>
Compact Performance HUD
Windbound has a small per-game-client window for solo playing, multibox octets and concert monitoring. It is separate from the main player and can be shown while the main window is closed.
Open the HUD
Type /windbound hud to toggle it, /windbound hud show to display it, or /windbound hud hide to dismiss it. You can also use Show Performance HUD in the main player.
What it shows
- The logged-in character name and player role (Solo/Conductor/Follower).
- Current MIDI title and progress, elapsed/duration and basic status.
- ARM state, whether the 37-key Performance window is ready, and key playback controls.
- In expanded mode: instrument, current playlist, next song and detailed status.
- During conductor preparation: READY X/Y follow-up acknowledgements where available.
Controls by role
Role	HUD controls
Solo	Play, Pause/Resume, Stop, Next, ARM, Full player
Conductor	Play Ensemble/Ready Check, Stop, ARM, Full player
Follower	Monitoring, ARM, Stop and Full player; no independent Play button


Right-click the HUD progress bar to reset the MIDI locally. For an ensemble, send a fresh conductor cue rather than rewinding only one Bard during a show.
Customise appearance
- Drag the title bar to reposition the HUD.
- Switch between Compact and Expanded modes.
- Use Lock to prevent accidental movement.
- Adjust Opacity and optionally display the next song.
- Click Full player to open the main Windbound window.
Display choices are saved between sessions. ARM state is not.
Multi-account note
Each FFXIV process renders its own HUD inside its game client. A follower HUD will not become a universal desktop overlay on top of seven other client windows. The conductor can still control the overall native ensemble and check acknowledgements from one client.
<a id="Languages-and-Localisation"></a>
Languages & Localisation
Windbound v0.32 provides English, French (Français) and German (Deutsch) UI options.
Change language
1. Open /windbound.
2. At the top of the main window, locate Interface language.
3. Select English, Français, or Deutsch.
4. The UI changes immediately, and the selected language is saved for that client. A restart is not required.
The language setting affects interface labels, buttons, hints, tab names, most status messages, the performance HUD, concert/snapshot features and Stage Formations. Filenames, arbitrary MIDI track names, party-chat protocol commands and third-party driver/game errors may remain in their original language.
Examples of key controls
English	Français	Deutsch
Songs & Playback	Morceaux et lecture	Songs & Wiedergabe
Stage Formations	Formations de scène	Bühnenformationen
Concert Snapshots	Instantanés de concert	Konzert-Schnappschüsse
Audio Preview	Aperçu audio	Audiovorschau


UI labels can vary slightly depending on the installed translation build; the underlying functions do not change.
Help improve translations
The v0.32 source package includes GERMAN-TRANSLATIONS.tsv, intended for reviewers to flag grammar, truncation, tone and musical vocabulary. A useful beta report contains: language, exact tab/control, current wording, suggested wording, source version, and screenshot where possible.

Do not translate ## suffixes in C#/ImGui UI control identifiers or literal MIDI filename/tag syntax. The UI uses those IDs internally, so changing them can break widget state even when the displayed text looks correct.
Translation compatibility
Language selection is local to each Bard: a German-speaking Follower and an English-speaking Conductor can participate in the same native ensemble. MIDI data, note mapping, hashes and assignment messages do not change with the interface language.
<a id="Troubleshooting"></a>
Troubleshooting & Recovery
This chapter focuses on problems observed during Windbound's development and beta tests. Always record the exact displayed error and plugin version before making large configuration changes.
A. No sound / progress increases but Bard stays silent
1. Confirm the game is in expanded 37-key Performance, not mini mode.
2. Ensure ARM is checked.
3. Check that a Performance instrument is actually equipped and the game's Performance UI is READY.
4. Verify the MIDI has playable notes in C3–C6, and the track/channel is enabled.
5. Use Direct Output & Test → Test C4 and the bundled calibration MIDI.
6. Check the instrument switcher's diagnostics if the equipment changed but the 37-key keyboard didn't open.
Known tester finding: A Linux client appeared silent until its FFXIV 37-key setup was corrected. That was a configuration issue, not evidence that Windbound generally fails on Linux.
B. “Already running” / stuck playback
- Stop any active MIDI and stage movement.
- Type /windbound recover, or use Songs & Playback → EMERGENCY: Reset stuck playback.
- Allow one game update for held notes to release. ARM again manually.
- If FFXIV itself is still in a native Ensemble, end it through the game's UI; the plugin reset cannot override server state.
- Disable Visual Stage Designer (experimental) while confirming Solo playback works.
- Copy Song switching diagnostics if the error persists; distinguish MIDI scheduler state from vnavmesh's “Another stage movement is already running.”
C. Conductor switches instrument locally but follower doesn't
- Check that both use the same plugin build.
- The sender must be the actual FFXIV party leader.
- Follower role, native Game Ensemble and Automatically follow party leader's MIDI and instrument choices must be enabled.
- Use Party command receiver diagnostics on the follower. Look for messages seen, accepted/rejected counts and a reason.
- If the command was accepted but no switch occurs, inspect Instrument switching diagnostics, not the party transport.
- A full plugin reload and reapplying follower options can clear stale runtime state, but save configuration before resets and don't rely on reloading as the only fix.
D. Instrument equipped but 37-key UI isn't open
This is a distinct readiness problem. Wait for the switch state to settle, open the expanded Performance keyboard and check the ready indicator. The party may have selected the correct instrument while the UI is not yet usable. Use Instruments → Switch to selected instrument NOW for a local diagnostic.
E. FFXIV native Ensemble fails to start everybody
- Don't use the retired party-chat countdown option; use native FFXIV Ready Check.
- Confirm all Bards have the identical MIDI, correct assigned lanes, instruments, ARM state and full Performance keyboard.
- On the conductor, run Prepare Entire Orchestra and Refresh Stage Status; resolve every missing/failed performer.
- Auto-Accept is on by default, but may be unchecked on a client. There is a manual confirm fallback under Native Ensemble diagnostics / manual fallback.
- If only one account is ready, verify party leader, role and receiver diagnostics; don't just spam START.
F. Bad notes / misplaced octaves / wrong instruments
- Use the MIDI Inspector and inspect individual lane pitches.
- Check #+12, #-12, #Lute and General MIDI program data.
- Inspect Copy detected MIDI metadata report for applied vs detected octave.
- Saved manual overrides take priority. Use Restore MIDI octave tags or Use MIDI instrument suggestions only when you want to replace those overrides.
G. SoundFont preview problems
- Select a real, extracted .sf2 SoundFont, not a ZIP or SFZ file.
- Verify speaker output and preview volume.
- Remember this is local audio only, and FFXIV file playback stops preview automatically.
- If building from old source and seeing MidiFileSequencer.MidiFile CS1061, use the v0.27.1+ corrected source files.
H. Stage-dot movement fails
- Confirm vnavmesh on every participating client and follower stage permission.
- Check same housing ward/instance, actual nearby party leader, and that everyone is idle/out of combat.
- Move the dot nearer or adjust stage width; a dot may fall inside an obstacle even if it fits on the 2D diagram.
- Use STOP STAGE MOVEMENT rather than letting a bad route run.
- In v0.32 the visual editor is OFF by default and automatic facing is not supported.
I. Problems immediately after an FFXIV patch
A native hook, Dalamud build or vnavmesh dependency may need updating. Check version compatibility before reinstalling Windows drivers or deleting Windbound settings. Keep a previous known-good project build and a configuration/snapshot backup.
Copying the right diagnostic report
Problem	Copy report from
MIDI doesn't load	Songs & Playback → Copy MIDI loading diagnostics
MIDI transitions/stuck state	Songs & Playback → Copy song switching diagnostics
Instrument switch	Instruments → Copy instrument diagnostics
Party leader/follower command	Ensemble → Copy party-command report
Native start	Ensemble → Copy native diagnostics
Group preparation	Concert Director → Copy Stage Report
Octave/instrument tags	MIDI metadata → Copy detected MIDI metadata report
Arrangement quality	MIDI Inspector → Copy Inspection Report


Privacy: Reports can include local filesystem paths, character names and party identifiers. Review the clipboard text before posting it publicly.
<a id="Frequently-Asked-Questions"></a>
Frequently Asked Questions
Can Windbound play an entire MIDI automatically?
Yes, in Solo mode it can play loaded MIDI notes through FFXIV's expanded Performance keyboard, after you ARM output. It does not synthesize audio inside the game by itself; the game Performance instrument is required.
Can I run an octet on one PC?
The native party-based ensemble is designed for multibox parties and separate computers. Each FFXIV client needs the plugin configured as a Follower and must have the matching MIDI; the leader can sync files, instruments, tracks and ARM state from one conductor window.
Does everyone need the exact same file path?
No. Native conductor file-sync uses a MIDI fingerprint to identify the matching local file. Everyone still needs the actual MIDI bytes on their computer; party chat does not transfer MIDI files.
Will Windows and Linux Bards play together?
The main native ensemble/Performance features were tested on both systems during development. Third-party dependencies and the Windows-specific Live MIDI Piano and SoundFont audio backends may require Wine configuration. Each client also needs the right in-game 37-key Performance layout.
Do followers have to click Ready Check?
With Automatically accept FFXIV Ensemble Ready Check enabled and the client ready, Windbound can accept automatically. The default is ON for new installs. It doesn't bypass a missing instrument, missing MIDI, wrong role or other readiness problem.
Does ARM ALL actually start the song?
No. ARM ALL prepares note output. START ENSEMBLE separately uses FFXIV's native Ready Check and start event.
Can I change which instrument a follower uses?
Yes. In the conductor's Ensemble panel, choose a per-Bard instrument and press SET PARTY INSTRUMENTS. Windbound can also infer instruments from MIDI track metadata when manual overrides don't take precedence.
Can I keep my performer assignments between songs?
Yes. Enable Keep party track assignments across songs and save the default lineup. Special songs can have per-file overrides.
Is the SoundFont preview audible to everyone in the party?
No. It is local speaker playback only, entirely separate from FFXIV Performance output and the ensemble start mechanism.
Can I use an 88-key MIDI keyboard live?
Yes, with Live MIDI Piano in Solo mode. Notes outside C3–C6 can be folded into range, or skipped. The device must be accessible through the Windows MIDI input system/Wine equivalent.
Can the stage dots move an entire party?
The conductor can design and save a formation then request movement for opted-in characters using optional vnavmesh. This walks, not teleports, and is experimental in v0.32. Auto-facing is not implemented.
Will a snapshot include the MIDI files themselves?
No. Snapshots contain configuration and paths, not MIDI binary content, SoundFonts or secret connection codes. Save MIDI files separately.
Can I switch language while performing?
The UI can switch between English, Français and Deutsch without a restart. It doesn't alter MIDI notes, party commands or performance timing. Avoid unnecessary UI interaction during a demanding live performance.
Does “Stop” automatically unequip the whole party?
The STOP ENSEMBLE + UNEQUIP ALL conductor control requests that workflow. A local STOP or DISARM ALL action is different. The native stow request can fail if FFXIV refuses it; check the status instead of assuming it worked.
Is this official or supported by Square Enix?
No. Windbound Bard is a third-party/beta plugin. Game updates can change native interfaces and unofficial plugin use may be against FFXIV's terms. Follow the game's rules and respect the rights of music creators.
<a id="Build-Beta-Testing-and-Updates"></a>
Building, Beta Testing, Backups & Game Updates
Build checklist (v0.32)
- Source project: NunuWindboundBard.sln.
- SDK: Dalamud.NET.Sdk 15.0.0, .NET 10, x64 target.
- Managed audio dependencies in .csproj: MeltySynth 2.4.1, NAudio 2.2.1.
- Build from the extracted project root with:
  .\Build-Plugin.ps1
- The script builds Debug x64 and lists the output DLL; use /xlsettings → Experimental → Dev Plugin Locations to load the folder/DLL.
- Keep adjacent NuGet audio DLLs when distributing a compiled test build. A source ZIP is not itself a compiled plugin.
- Never swap unrelated .cs files from older versions into a new build; many source files evolved together.
Recommended beta-test matrix
Test	One client	Two Bards	Octet	Windows/Linux
Plugin loads, commands, translations	✓			✓
37-key test C4, calibration MIDI	✓			✓
Polyphonic note + chord test	✓			✓
Instrument select / stow	✓	✓		✓
MIDI fingerprint / file sync		✓	✓	✓
SET PARTY TRACKS / INSTRUMENTS		✓	✓	✓
ARM ALL / native Ready Check START		✓	✓	✓
Concert Director PREPARE / Retry		✓	✓	✓
SoundFont preview	✓			Device-specific
Stage navigation / visual dots		✓	✓	vnavmesh-dependent
Live MIDI controller input	✓			WinMM-dependent
Snapshot export/import	✓			File-path-dependent


Start with the smallest safe rehearsal before running an eight-account concert. For language feedback, ask native French/German users to check long or clipped strings, and verify they can still use the same commands.
Reporting a bug
A useful report includes:
1. Windbound version and FFXIV/Dalamud build after the latest patch.
2. Windows or Linux/Wine/Proton and whether the issue happens on one or several clients.
3. Solo / Conductor / Follower role and in-game party size.
4. The exact steps and error text, including whether the 37-key keyboard is open.
5. Which MIDI sample was used, or (if allowed to share) the failing MIDI file.
6. The most relevant Copy diagnostics report from Troubleshooting.
7. A screenshot if the issue is visual (never include passwords or sensitive chat information).
Back up before upgrading
Keep a versioned backup folder on your PC, for example:
Windbound Backups/
  v0.31.2-known-working/
  v0.32-german-beta/
  Config-backup-YYYY-MM-DD/
  Concert-Snapshots/
  MIDI-Library/
  SoundFonts/
Keep the old source ZIP, the compiled plugin that worked, the Dalamud plugin configuration, exported Concert Snapshot JSON files and the actual MIDI files. A code ZIP alone does not preserve your custom character/track assignments if those settings live in Dalamud configuration.
After an FFXIV patch
1. Wait for a compatible Dalamud build; don't assume native hooks survive maintenance.
2. Check that Windbound loads without signature errors.
3. Test Solo calibration and chords first.
4. Test instrument switching, full 37-key Performance UI and native START with two accounts.
5. Then check group STOP/unequip, Stage Manager and Stage Formations (including vnavmesh compatibility).
6. If something breaks, stop using that feature and keep a known-good build for comparison. Don't delete your saved lineups and snapshots as the first fix.
Useful references: Dalamud Getting Started, Dalamud troubleshooting, and vnavmesh source.
Attribution and project boundaries
Windbound is an independent FFXIV performance plugin. Some ensemble behaviours are inspired by MidiBard 2's public workflow; this wiki does not claim MidiBard's code or branding. Any features using FFXIV native internals or other plugins can be affected by game patches. Respect the game's current terms and music-performance restrictions.
<a id="Version-and-Feature-Reference"></a>
Version & Feature Reference
This wiki targets Windbound Bard v0.32.0.0, the latest project source inspected for the documentation. Its release archive is a source package intended for compilation/testing, not a guarantee that an up-to-date binary is hosted at a particular website.
Approx. version	Major feature introduced
v0.1–0.4	Basic FFXIV MIDI and chord-aware direct Performance callbacks
v0.5–0.8	Instruments, track octaves, same-PC party setups and saved playlists
v0.9–0.12	Private TCP, song transitions, Soulweaver and early native ensemble support
v0.13–0.19	Party-controlled starts and later MidiBard-style native Ready Check, file/track/instrument synchronisation
v0.20	Live USB MIDI Piano; group native instrument-stow refinements
v0.21–0.22	French UI; MIDI instrument/`#+12` tag detection
v0.23	Persistent octet performer lineups
v0.24	Concert Director, readiness monitoring and ensemble preparation
v0.25	Automated setlist preparation (not automatic music START), targeted single-Bard retry
v0.26	MIDI Arrangement Inspector
v0.27 / v0.27.1	SoundFont speaker preview / compiler compatibility fix
v0.28	Concert Snapshots & Restore
v0.29	ARM ALL and DISARM ALL party controls
v0.30	Saved stage positions with optional vnavmesh walking
v0.31 / .1 / .2	Visual Stage Designer, local playback recovery and compiler fixes
v0.32	German interface translations alongside English and French


Current recommended feature configuration
- For native ensemble: conductor + followers, leader sync enabled, FFXIV native Ready Check, matching local MIDI file, 37-key Performance, and session ARM.
- For Solo: full keyboard, a loaded file, selected instrument, ARM, Play.
- For stage dots: after Solo and ensemble are confirmed stable, enable experimental Visual Stage Designer; install/configure vnavmesh separately.
- For SoundFont preview: load local .sf2 and keep speaker preview separate from in-game notes.
Where new instructions belong
When editing the plugin after v0.32, update the affected wiki page and this reference table. Keep a known-good screenshot of the UI and a tested key-mapping sample alongside each tagged release; avoid claiming a release is “stable” until it has passed a real in-game rehearsal.
<a id="Glossary"></a>
Glossary
Term	Meaning in Windbound
ARM	Enables direct MIDI note output to FFXIV Performance; intentionally session-only
Auto-Accept	Option for an eligible follower to accept FFXIV's native Ensemble Ready Check automatically
Bard lane	A MIDI track/channel pairing that can be enabled, shifted and assigned independently
Chord strategy	How simultaneous MIDI notes are preserved, reduced or arpeggiated for Performance
Conductor	The party leader's plugin role used to prepare files, assignments, instruments and native START
C3–C6	The 37-note FFXIV Performance pitch span used by Windbound (MIDI 48–84)
FFXIV Optimiser	Track-by-track octave and dense-note adaptation without editing the original MIDI
Follower	Party client that accepts prepared parts, instrument changes and native START
GM / General MIDI	Standard MIDI instrument program assignments; SoundFont preview can render these patches
HUD	Compact per-client overlay for status and basic transport controls
Native Ensemble START	The game-origin FFXIV synchronisation event that begins ensemble playback
Party fingerprint	Compact hash/check used to match MIDI identity or roster state between conductor and followers
PerformanceModeWide	Expanded in-game Performance keyboard UI needed for the current 37-note output adapter
Play Next	Solo request queue separate from the saved playlist's normal order
Snapshot	Saved concert configuration and paths, but not the binary MIDI/SoundFont content
Soulweaver	Windbound's heuristic tool for generating Solo/Duet/Quartet/Octet arrangements
SoundFont (.sf2)	Sample bank used for local audio preview through a synthesiser
Stage anchor	Conductor's chosen reference point/heading for a saved stage formation
vnavmesh	Separate optional Dalamud plugin providing pathfinding/navigation for stage movement
Wine/Proton	Compatibility layers that may run FFXIV and some Windows-based plugin dependencies on Linux


<a id="Octet-Concert-Checklist"></a>
Octet Concert Checklist — Print This Before a Show
Use this as a quick pre-concert checklist for an eight-character native ensemble. It can also be adapted for duets, quartets and other party sizes.
Before opening the venue
- [ ] All clients have a known-working Windbound version and compatible Dalamud.
- [ ] Every Bard has the same MIDI files in its local library (not necessarily the same path).
- [ ] All accounts are in the same FFXIV party; conductor is the actual party leader.
- [ ] All eight performers use expanded 37-note Performance and the correct key mapping.
- [ ] Follower options: native Game Ensemble, leader sync, Auto-Accept, and Allow leader ARM (if desired).
- [ ] Stage movement is complete and stopped; optional vnavmesh is idle.
- [ ] Each instrument can be equipped locally; no switching state is stuck.
- [ ] Latest MIDI and Concert Snapshot backups exist.
Five conductor buttons, in order
Step	Button	Expected result
1	SYNC SELECTED MIDI TO PARTY	Everyone loads the same MIDI fingerprint
2	SET PARTY TRACKS	Each Bard keeps only its assigned part(s)
3	SET PARTY INSTRUMENTS	Everyone equips the requested Performance instrument
4	ARM ALL BARDS	Seven followers acknowledge ARM; keyboard readiness confirmed
5	START ENSEMBLE	FFXIV native Ready Check synchronises the actual note start


Shortcut: PREPARE ENTIRE ORCHESTRA combines the preparation/status process. Check Stage Manager readiness and the group ARM state before START.
During the show
- Keep the Conductor HUD visible. Watch song progress and READY/failure alerts.
- If a Bard doesn't respond, use Retry for that member instead of resetting everyone.
- If preparing the next song automatically, remember it never auto-starts: the conductor must send the next native Ensemble START.
- Use HOLD after this song when speaking to the audience or rearranging performers.
- Do not click a local follower Play button or rewind only one client during a synchronised song.
End the performance
1. Press STOP ENSEMBLE + UNEQUIP ALL on the conductor.
2. Confirm held notes stop and instruments are put away; note that a native stow can fail if the game refuses it.
3. If leaving the stage, stop vnavmesh/stage movement before starting another song.
4. Save or export the final Concert Snapshot and playlist as a backup.
Emergency card
If…	Do this
Player says “already running”	/windbound recover on the affected local client; ARM again
MIDI advances but no notes	Verify ARM + 37-key keyboard + instrument + assigned tracks
Conductor commands reach only some Bards	Copy Party command receiver diagnostics from a silent follower
A performer is missing	Concert Director → Retry only that Bard
Native Ready Check fails	Check readiness, Auto-Accept and FFXIV game status; don't use retired chat timer
Stage dots don't move	Stop movement; verify vnavmesh, party permission, housing ward and walkable destination


More detail: Conductor & Octets · Concert Director · Troubleshooting
