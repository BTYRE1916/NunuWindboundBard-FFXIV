# Validation — native capture suite v0.42.0

Build/test environment: Linux, .NET SDK 10.0.100, actual Dalamud API 15 reference assemblies. The Windows x64 package is self-contained and includes FFmpeg 9.0.2 from the Gyan Windows distribution.

| Check | Result |
|---|---|
| Native engine compile/publish | Passed |
| Plugin Release compile against actual Dalamud API 15 | Passed; six pre-existing warnings outside the new capture code |
| Core framing, bounds, validation, collection/backup and offline opt-in checks | 14 passed |
| Preserved OBS protocol/title/offline checks | 18 passed |
| Preserved OBS recording transport state checks | 9 passed |
| Real codec recording test | 30 frames at 10 FPS, six AAC tracks, zero dropped frames, finalized MKV |
| Pause/resume timeline | Passed; a wall-clock pause did not increase the three-second saved timeline |
| Encoded video inspection | Passed; saved MKV decoded back to the supplied scene pixel colors within encoding tolerance |
| Real replay test | Passed; RAM block ring saved a concatenated MKV with all six audio tracks and cleaned its disk staging blocks |
| Windows synthetic composition smoke project | Compiled; not executed in this environment |
| Real Windows named-pipe integration | Not run; local socket creation restricted in this environment |
| Windows Graphics Capture / D3D11 / WASAPI device execution | Not run; Windows devices unavailable |
| Actual FFXIV background capture, camera/media synchronization and in-game tab layout | Not run |
| Live RTMP/RTMPS streaming and hardware encoder execution | Not run |

The codec smoke test executes the new output/replay code with the host's Linux FFmpeg and POSIX FIFO transport. The shipped Windows build uses current-user named pipes and bundled Windows FFmpeg. It validates our timeline/data flow and file handling; it does not substitute for testing the Windows transport, capture driver, devices or bundled executable on Windows.

Native engine source contains no libobs import, OBS plugin loader, OBS runtime path or OBS frontend call. The earlier OBS native interop file has been removed. The preserved optional OBS WebSocket client, recording transport and title-file helper are byte-for-byte unchanged from the original archive.

Use `Tests/Run-v0.42-Tests.ps1` on Windows, inspect the saved clips in a player, then test actual capture/audio sources and your chosen encoder. High-resolution/high-FPS performance remains unmeasured; the current compositor uses CPU composition rather than a GPU scene renderer.
