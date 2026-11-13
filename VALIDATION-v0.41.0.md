# Validation — v0.41.0 development preview

Validated on 13 November 2026 in a Linux build environment with .NET SDK 10.0.100. This is a compiled development package, not a Windows-live-tested concert release.

| Check | Result |
|---|---|
| Plugin Release build using actual Dalamud API 15 (15.0.4) reference assemblies | Passed, 0 errors; 6 existing warnings outside the new studio code |
| Windows x64 self-contained background engine publish | Passed, 0 errors |
| Studio framing, invalid-input guards, x64 ABI layouts, collection/backup restoration, offline opt-in behavior | 16 checks passed |
| Existing OBS capture protocol, authentication, title file and offline behavior | 18 checks passed |
| Existing OBS recording transport state transitions and guards | 9 checks passed |
| Windows native smoke test project compilation | Passed, 0 errors |
| Original OBS client, transport, title overlay and title file bridge | Byte-for-byte unchanged from the supplied archive |
| Legacy MainForm.cs overlay reproduction | Original project reproduced missing Forms errors; corrected project builds with the legacy file still present, 0 errors / 0 warnings |
| Whitespace/conflict-marker review | Passed |
| Real Windows named-pipe controller integration | Not run; local socket creation is restricted in this environment |
| Real libobs 32.x initialization, GPU composition and preview | Not run; Windows runtime/GPU unavailable |
| Real FFXIV capture, devices, pause/resume MKV, replay save and stream | Not run |
| In-game visual layout and mouse/file-picker interaction | Not run |

The Windows test runner includes a real named-pipe peer test. Its optional native smoke test checks every imported libobs entry point before initializing the runtime, then exercises synthetic scenes/overlays, auxiliary preview, recording/pause/resume/finalization, replay save and restoration. It rejects an unconfirmed stream request without contacting a streaming server.

Before a concert, run the Windows tests, play back the saved synthetic MKV, then test your actual window/audio sources and encoder. Check A/V synchronization, preview behavior, output finalization, disk use, and reconnect after a plugin reload. Test streaming separately using your chosen service. Native-source availability and capture behavior depend on the runtime, Windows version, devices and drivers.

The older OBS protocol fixture had a stale wording assertion; it now checks the current disconnect message. Its transport test project was missing the existing title-overlay dependency; that compile reference is restored. Neither change alters the OBS client implementation.
