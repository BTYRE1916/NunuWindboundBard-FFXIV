# Nunu's Windbound Bard v0.10 — Reliable Auto Next & Song Switching

**Dalamud API 15 · .NET 10 · full Visual Studio source**

## What's fixed since v0.9

- **Auto Next works while the Windbound UI is closed or hidden**, including when you alt-tab. The end-of-song transition now runs on Dalamud's framework update loop, rather than the ImGui `Draw()` method.
- **Manual playlist/file changes during a performance are safely queued.** Windbound stops the old note stream, lets the game-thread scheduler release all held notes, loads the chosen MIDI, and (in Solo mode) starts it automatically.
- **Play pressed immediately after Stop isn't lost:** it queues a one-shot start until the old performance has stopped.
- **Next button** uses the same safe stop-and-release handoff.
- **Broken, missing, or empty playlist entries are skipped** by Auto Next; the previous arrangement is never replayed accidentally after a failed file load. Songs with muted parts can still be loaded manually for editing.
- **Instrument preparation can't silently wait forever:** a guarded 55-second timeout prints the switcher's diagnostics. Windbound still attempts the instrument choice automatically; it doesn't require a manual reset if the native switch succeeds.
- **Deliberate Stop cancels outstanding song changes.** Ensemble sessions remain conductor-cued; Solo Auto Next doesn't run during ensembles.

Nothing has changed in the FFXIV direct Performance note callbacks, chord scheduling, optimizer, guitar tones, party assignment, or multi-device transport. Your Solo and Ensemble playlist profiles and per-file instrument/octave choices stay intact.

## Test in FFXIV

1. Build this source as a development plugin, or publish the built output in a **new** release. In `/windbound`, select **Solo** and enable **Auto next**. Add two or three short MIDIs to the same saved playlist.
2. Open full 37-key Bard Performance, ARM output, then play the first MIDI. **Close `/windbound` and alt-tab**; it should automatically continue to the next song after the first finishes.
3. While a MIDI is playing, reopen Windbound and click a different song in the playlist. It should release the old notes and switch to the selected MIDI without you manually resetting the instrument.
4. Try Piano in one song and Violin in the next, each set in the Instruments tab. Native instrument switching remains game-build-sensitive; if it fails, send the **Instrument switching diagnostics** instead of repeatedly resetting.
5. Test the Next and Stop buttons. Stop must not trigger Auto Next.
6. Test Ensemble mode; it must **not** auto-advance without the conductor cue.

Run `Tests\Run-v0.10-Tests.ps1` in PowerShell on Windows with .NET 10 for source and deterministic song-flow tests. Existing v0.9 network/playlist tests can still be run with `Tests\Run-v0.9-Tests.ps1`.

**Testing limitation:** This source update was statically checked and packaged in a non-Windows environment. It has **not** been compiled in Visual Studio or tested inside FFXIV here; confirm both transitions and instrument switching on your PC. Instrument switching depends on FFXIV native UI behaviour, which may change across game updates.

---

## Previous v0.9 features — Multi-Device Ensemble / Duo Edition

**Dalamud API 15 · .NET 10 · FFXIV Performance MIDI Player**

This version extends the working v0.8 plugin. **All v0.8 features remain:** chord-aware native Performance output, alt-tab and chat-safe performances, C3–C6 MIDI optimiser, per-track octaves, automatic instrument selection, guitar tone overrides, scrollable playlist, saved Solo/Ensemble playlist profiles and same-PC conductor/follower mode.

## What's new

**Opt-in Multi-Device mode** lets one player (the conductor) host a private TCP stage while other Windbound Bard players join from different PCs. Once paired:

- The conductor's loaded MIDI is transferred automatically to each follower (max **4 MB**; `.mid` / `.midi` only, SHA-256 verified).
- The conductor's **track/channel → party member** assignments are transferred as part of the same session revision.
- The conductor's octave shifts, note-repair option, chord strategy, transpose, gap and arpeggio settings travel with the MIDI, so the duet plays the intended arrangement.
- The follower automatically loads the song into a separate plugin cache (does **not** add it to or overwrite saved playlists).
- The follower reports **READY** only when the right session is loaded, the assigned tracks are playable, an expanded Performance keyboard is open, output is ARMed, **Wait for conductor cue** is checked and network clocks are synchronised.
- Host sends a coordinated START cue with a **minimum 5-second count-in**. Clients estimate cross-PC clock differences from periodic round-trip samples; the game note scheduler still starts on its own framework thread. STOP is distributed too.
- If multi-device is **OFF**, the existing memory-mapped same-PC ensemble behaves exactly as before.

**No mandatory foreground/window focus; no chat keystrokes; no direct MIDI into other applications.**

## Quickstart: Father's duet with Edea

1. Both of you load **v0.9** as a Dalamud development plugin, join the **same FFXIV party**, and arrange a private network connection between your computers. For different home networks, use [Tailscale](https://tailscale.com/docs/how-to/connect-to-devices) or an equivalent private encrypted network. The plugin itself does **not encrypt** transport. Do **not** port-forward its TCP listener onto the public Internet.
2. On **Father's PC**: `/windbound` → **Ensemble** → **Conductor** → check **Enable Multi-Device**. Set a stage name such as `NunuEdeaDuo`, leave TCP port `47542`, and click **Generate code**. Keep the code private; send it to Edea by a private channel. Click **HOST private ensemble**.
3. On **Edea's PC**: `/windbound` → **Ensemble** → **Follower** → check **Enable Multi-Device**. Enter the **same stage name, port and code**, and paste Father's **Tailscale IP / private LAN IP** into **Conductor LAN / Tailscale IP**. Click **JOIN conductor**.
4. Father loads a MIDI and uses the existing **Party-roster Track Assignments** dropdowns to give the melody to Nunu and the accompaniment to Edea. Click **Send updated MIDI + assignments** if necessary. The song and arrangement are delivered automatically; Edea does not need to find a matching local file.
5. Edea chooses her own **instrument** using the Instruments tab and enters **expanded 37-note Bard Performance**. On the Songs page she enables **ARM output**, and on Ensemble she checks **Follow conductor's party-track assignments** and **Wait for conductor cue**.
6. Father does the same for his own instrument and ARM if he is playing. Look for **Edea Uematsu — READY** on the conductor's stage. Click **Cue ensemble start**. Both clients start the assigned parts on their synchronised clocks. To stop both, use **STOP connected bards**.

### Troubleshooting connection and timing

- **Connection refused / timeout:** Verify both PCs are online in Tailscale, use the conductor's private Tailscale IPv4 address, confirm port `47542`, and allow an inbound TCP rule for FFXIV or the port on the conductor's Windows firewall. Use `tailscale ping <host>` to confirm private reachability. Public port forwarding is not necessary or recommended.
- **Session code wrong:** Both need the identical, case-sensitive stage name and secret code; the code isn't saved and must be re-entered after game restart.
- **Edea stuck at WAITING:** Her game must show expanded Performance, ARM and Wait for cue. Check if her actual FFXIV character ContentId is in Father's party list and has at least one MIDI track assigned. Both clients must use **Follow conductor's party-track assignments** for automatic parts.
- **Clock sync waiting:** Leave both clients connected for a couple of seconds. The plugin collects at least three latency samples. Network jitter or a slow relay can affect precision; it isn't guaranteed frame-perfect.
- **Song fails to transfer:** Network MIDI files are limited to 4 MB (by raw file size). No remote file paths are executed; the bytes are checked for MIDI signature and SHA-256 before saving to the plugin's `NetworkMIDICache` directory.
- **Edea needs another instrument:** Each player selects their own instrument in their own plugin. Change it **before** cueing. Multi-device controls the song, track assignments and timing, not another person's UI actions.

## Security and limits

The code is verified by **HMAC-SHA256 challenge/response** before accepting session data. **This is authentication, not encryption**: MIDI and commands travel in plaintext TCP once paired. Use a trusted LAN or encrypted overlay such as Tailscale. Generated secrets are 24 hex characters (96 bits); secrets are never written to plugin config or logs. Only the network address and TCP port are saved. Authentication does not independently verify a claimed FFXIV ContentId; only connect with trusted friends. No public relay server, NAT punch-through, in-game party-chat communication, remote command execution or auto-connect on launch. The built-in FFXIV Ensemble Ready Check is **not** hooked.

This is a **preview build**, not yet compiled or exercised in a live FFXIV client. Cross-device clock synchronisation is approximate and needs real duet testing. FFXIV and Dalamud updates can affect native Performance controls. As always, third-party plugins may be against the game's terms.

## Building

Open `NunuWindboundBard.sln` in Visual Studio on Windows (with the appropriate Dalamud dev setup and .NET 10 SDK) or run `Build-Plugin.ps1`. The project SDK is `Dalamud.NET.Sdk/15.0.0`. Load `src/NunuWindboundBard` output as a Dalamud development plugin.

## Source and test notes

- `src/NunuWindboundBard/Core/MultiDeviceEnsemble.cs` — pairing, frame-bounded private TCP, challenge/response auth, session transmission, follower readiness and NTP-style offset measurement (monotonic stopwatch clocks).
- `src/NunuWindboundBard/MainWindow.cs` — Multi-Device tab controls, stage/host/join, song/cache handling, part assignment, readiness gating and START/STOP integration.
- `Tests/MultiDeviceTransportSmokeTest.csproj` — standalone localhost conductor/follower test for authentication, MIDI byte transfer, saved octave maps, READY, synced START/STOP. Run `Tests/Run-v0.9-Tests.ps1` on Windows. It also runs previous saved-playlist and party-assignment regression tests.
- All core audio, instrument, solo playlist, local ensemble and MIDI/optimiser files are unchanged from the tested v0.8 source.
