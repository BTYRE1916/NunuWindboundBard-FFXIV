# NunuWindboundBard v0.46.4 — source-only relay START fix

Based on the uploaded complete v0.46.3 project source.

## Changed

- Every connected participant receives the same numbered scheduled START cue.
- Conductor tracks individual follower START acceptance/rejection in the connected-performers roster and relay diagnostics.
- A follower reports if START was armed or why it could not be armed.
- Only unacknowledged followers are re-sent the *same* START cue (three attempts at 650 ms intervals, while the downbeat is still in the future).
- Duplicate START cues return the original acknowledgement without starting the Bard again.
- Includes an extended eight-follower relay smoke test covering shared timing, a simulated rejection and an acknowledgement retry.
- Plugin project and manifest version updated to 0.46.4.0. Existing relay server source is included and unchanged.

## Build and deploy

1. Build the solution/plugin and required companion projects on your Windows development machine with the project's required .NET SDK and Dalamud references.
2. If practical, run the `Tests/OrchestraRelaySmokeTest.csproj` relay smoke test against your actual relay server (requires the relay URL and a MIDI test file).
3. Package your compiled Dalamud release as your normal release ZIP. The `Plugin.json` points to `releases/download/0.46.4/FFXIVMIDIPLAYER-BY-NUNU-v0.46.4.zip`, so the GitHub release tag and uploaded asset must match exactly, or adjust the manifest before publishing.
4. Deploy v0.46.4 to the conductor and all followers before retesting for individual START results; older clients can join but cannot acknowledge START.

## Verification limitations

Source patch and archive-integrity checks only. No .NET SDK is available in this execution environment, so the plugin, tests and release binaries were **not** compiled or runtime-tested here. The reported original bug may have an additional game-side cause; the new START results help identify it.
