# Windbound Bard v0.46.7 — Hosted duet timing diagnosis

- The `(+1064.2s)` values in earlier relay logs were connection uptime, not musical latency. The label now says `connected +...s`.
- `START queued` → `START armed` is a delivery/acknowledgement interval, **not** measured note synchronization.
- Clock synchronization uses a median of nearby low-RTT samples where available (with its previous lowest-RTT fallback), to reduce offset jitter.
- The conductor now shows each follower’s first/mean/worst *game callback dispatch delay relative to that follower's scheduled MIDI note*. It also reports the follower's clock RTT and spread of low-RTT offset samples.
- The conductor logs its own corresponding first/mean/worst callback delay into Relay diagnostics.
- Followers can adjust `Follower timing trim (ms)` while connected. Start at zero; use **negative** values to move a follower earlier and **positive** values to move it later. Move in 20 ms increments only after a repeatable listening test.
- This trim is local to that follower, saved per FFXIV plugin configuration. It is not broadcast and does not alter other rooms or Render.

Important caveats:
- Clock spread is an estimate of variability, **not** a guaranteed absolute accuracy measurement; asymmetric network routing can bias the offset.
- Note callback timing does not measure FFXIV's audible playback delay or output buffering. A follower with similar callback delays can still sound delayed because of rendering/audio transport.
- Build both plugin and engine from the current source; `Build-WindboundRelease.ps1` can import support files using `-PreviousReleaseZip`.
- All members should update to v0.46.7 to send and display new timing diagnostics.
- The Render relay code/protocol remains compatible; no relay server redeployment is required.

Duet test procedure:
1. Conductor and follower both run v0.46.7, connect with join code, and load identical assigned MIDI.
2. Follower checks Clock synchronisation READY / RTT / spread and sets trim to 0.
3. Conductor START; after notes begin, open Relay diagnostics on both systems.
4. Compare `conductor callbacks` and `follower ... dispatched` rather than the session uptime or START ACK interval.
5. If a repeatable audible offset remains, adjust Edea's trim in 20ms increments. Negative makes Edea earlier, positive later.
6. Share both Relay reports when investigating persistent drift, especially during different parts of the song.

Build limitations: source archive is statically checked here; the Windows .NET toolchain, Dalamud and live duet are not available in this environment. Test on your PC before public release.
