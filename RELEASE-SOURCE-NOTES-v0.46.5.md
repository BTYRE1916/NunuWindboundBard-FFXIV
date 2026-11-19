# Windbound Bard v0.46.5 — source update (not precompiled)

Based on the packaged v0.46.4 source and the user-supplied known-working v0.46.4 Windows release.
The old binaries have **not** been modified or relabeled. Compile this source on Windows.

## Hosted-band Ensemble START safety

- START still broadcasts the same sequence, MIDI revision and future clock timestamp to all connected Bards.
- Hosted START is blocked if a connected follower does not advertise START acknowledgements.
- Each participant replies **ARMED** or **FAILED** after attempting to queue local playback.
- The first **FAILED** reply immediately cancels the take for **every Bard**, including the conductor, and explains why.
- If any acknowledgement remains missing 1.25 seconds before the downbeat, START is cancelled for everybody rather than allowing a partial band performance.
- Existing targeted retries and duplicate-START suppression are preserved.
- Each follower still receives STOP over its own encrypted band stream; MIDI sharing, instrument setup and other conductor controls are unchanged.

This is a safety fix for incomplete starts, **not** a claim of tested simultaneous playback on a live octet. The relay smoke-test source now includes success, retry, rejection and missing-ack scenarios.

## Release packaging

Build the new plugin as v0.46.5.0. Use the full-install build command in BUILD-v0.46.5.md to copy the native capture engine/FFmpeg/relay assets from the last working v0.46.4 ZIP while replacing the plugin binary and local manifest with newly compiled 0.46.5 files. Do **not** ship a source ZIP or a release ZIP with a wrapper folder as the Dalamud install asset.

Test in a controlled band first, with all Bards updated. On any cancelled START, check the per-Bard START result in the conductor's Hosted Band roster and the Copy Relay Report diagnostic.

## Validation state

Static source, JSON and ZIP-layout checks performed. Full .NET compilation and Dalamud live-game testing cannot be performed in this environment.
