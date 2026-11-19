# Windbound Bard v0.46.9 — diagnostic update for a reported ~1-second relay duet offset

This update adds **diagnostics and a manual calibration workaround**. It is **not a proven fix for automatic synchronization**. The previous START ACK interval is a delivery measurement, not note or audio latency.

## What changed

- Conductor relay report and follower report now include `part first note at +N ms`, derived from the first scheduled MIDI note in that Bard's assigned part; alongside the existing `callback first/mean/worst` timing, RTT and clock spread.
- Compare the conductor and follower `part first note` values. A +1000 ms opening rest for one part is intentional MIDI arrangement timing, not 1000 ms of network lag.
- `Follower timing trim (ms)` now ranges from **-1500 to +1500 ms** rather than -250 to +250; a dedicated reset control restores 0 ms. A prominent warning displays for trim magnitudes greater than 250 ms.
- Negative trim advances only the *follower's scheduled playback*; it does not alter MIDI files or the other room members, and only takes effect on a new cue.
- No changes to Render relay service, room isolation, START messages, automatic clock offset estimator, or Capture Engine behaviour.

## Safe duet test

1. Both players install v0.46.9 and start with trim 0. Choose a song with clear, simultaneous first notes for both assigned parts, or verify whether either part starts after an opening rest.
2. Send START and play for at least 3 seconds, ideally through multiple bars. Copy conductor Relay diagnostics; compare `part first note` and `callback first/mean/worst` for each Bard.
3. If these MIDI starts and callback delays align, but *heard sound* remains consistently ~1 second late for the follower, check FFXIV Performance output/audio buffering. The plugin cannot directly timestamp when the remote audience hears a note.
4. Only after confirming a stable offset, trial a manual -1000 ms trim on the **late** follower (if the follower is early, choose +1000 ms). Re-cue; trim is not a clock-sync repair and can hide a different problem.
5. If the offset varies or drifts during the piece, reset trim to 0 and capture timing reports and an external audio recording for comparison.

## Build and packaging

Run `Build-WindboundRelease.ps1 -PreviousReleaseZip <path to working v0.46.4 full install ZIP>` after extracting the project. The script rebuilds the plugin and native engine, then stages support assets. Upload only `dist/FFXIVMIDIPLAYER-BY-NUNU-v0.46.9.zip` as Dalamud's install asset, not the source archive. Build/run using a Windows .NET SDK; this ZIP is source-only.
