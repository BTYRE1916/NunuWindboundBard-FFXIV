# Nunu's Windbound Bard v0.38 — Getting started

## Build
1. Extract the entire project ZIP into a NEW folder, away from your working v0.37.3 checkout.
2. Open `NunuWindboundBard.sln` using Visual Studio on your Windows machine.
3. Restore NuGet / Dalamud.NET.Sdk 15; build **Release | x64**.
4. Load the resulting DLL through Dalamud's local developer plugin loader, NOT through a modified live release feed.
5. Close the game Performance keyboard before running emote choreography.

## Piano Academy
1. Load a MIDI under **Songs & Playback**, or use **Piano Academy → Browse lesson MIDI**.
2. Open the new **Piano Academy** tab. The ORIGINAL MIDI notes are used, not FFXIV's limited C3–C6 mapping.
3. **Refresh MIDI ports**, select your USB synth, then **CONNECT**. Turn on **Read my USB MIDI keyboard for lessons**.
4. Select **Wait for correct notes** or **Play Along**. In Wait mode, the timeline pauses at each chord; in Play Along, pitches within 200 ms score hits.
5. Choose practice range, note split, speed and keyboard size; press **PLAY / RESUME LESSON**.
6. Press **OPEN LARGE PRACTICE VIEW** for a detachable falling-note piano that stays visible after the main plugin menu closes.

Notes: Full-size 88-key roll is horizontally scrollable on narrow monitors. Windows WinMM is currently the MIDI-IN backend (Wine/ALSA availability is not guaranteed). The synthesizer itself produces audio; this module does not play the MIDI through FFXIV or provide standalone built-in audio synthesis. Disconnect in one app before connecting the same hardware port in another app, if the MIDI driver is exclusive.

## Showmaster
1. Stop MIDI and **stow instruments**; wait for the game to leave Performance mode. The Showmaster tab has **STOW MY INSTRUMENT** for convenience.
2. Open **Showmaster**, choose a preset such as Group Bow, or **Create custom routine**.
3. Edit the name, up to 12 approved emote steps, and each step's delay in milliseconds.
4. Click **Run local** for your own character. Only click **GROUP SHOW** when you are the actual in-game party leader in Native Ensemble **Conductor** role.
5. Followers select Native Ensemble **Follower** and explicitly enable **Allow verified party leader's group emotes** on the Showmaster tab. Default is OFF.
6. The conductor's single cue includes the exact emote sequence. Followers do not need matching local custom presets or an external connection. All members must be opted in, in the same party, nearby enough for party roster verification, and stowed.
7. Use **STOP SHOW ROUTINE** to cancel. Showmaster automatically cancels if song playback/Performance mode/combat begins.

Only fixed emotes are accepted: bow, clap, wave, cheer, dance, pose, salute, joy, laugh, flex, victory, goodbye. They are submitted with `motion` to suppress emote chat text. The game may still reject an unavailable emote/action depending on the character and game patch. FFXIV party chat transports short, visible `!windbound` choreography cues; it carries no screenshots, code or files.

## Regression check (Windows, .NET 10)
```powershell
python .\Tests\Check-v0.38-PianoAcademy-Showmaster.py
dotnet run --project .\Tests\PianoAcademyShowmasterSmokeTest.csproj
```

## Things still to validate in-game
- Release x64 project compilation against your current Dalamud 15 install.
- Hardware MIDI connection and its input timing (test C4/G4 and chords).
- Play Along accuracy, Wait mode chord advancement and full-size piano scroll.
- Follower opt-in and party chat group cue reliability under ordinary game conditions.
- Native in-game emote behaviour after the Performance keyboard is completely closed.

This archive is **source**, not a precompiled Dalamud release. Keep your last known working plugin DLL handy. The development `Plugin.json` is deliberately not pointed to an unverified public download.
