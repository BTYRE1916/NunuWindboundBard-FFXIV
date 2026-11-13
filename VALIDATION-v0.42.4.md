# Validation for 0.42.4

Environment: Linux, .NET SDK 10.0.100, actual Dalamud API 15 references and host FFmpeg 7.1.5. The bundled Windows codec worker remains the supplied Gyan distribution with its notices.

| Check | Result |
|---|---|
| Complete plugin/engine Release/x64 build | Passed, 0 errors; six existing plugin warnings outside the capture changes |
| Windows x64 self-contained engine publish | Passed |
| Format/preview validation, JPEG header bounds, framing, persistence, aspect ratio and real muxer/replay checks | 35 passed |
| Existing offline controller/protocol/collection checks | 14 passed |
| Actual memory-loaded Configuration regression | 19 passed; startup fix retained |
| Old coupled pipe writer at MP4/30 FPS | Reproduced recording finalization timeout |
| New actual pipe recording at MP4/30 FPS | Passed pause/resume, 3-second saved timeline, six AAC tracks, decoded scene pixel check, replay save and staging cleanup |
| New actual pipe recording at MKV/30 FPS | Same recording/pause/finalization/replay checks passed |
| Windows native composition and HD JPEG preview smoke test | Compiled; not executed on this Linux host |
| Actual Windows capture devices, in-game visual behavior, hardware encoders and live streaming | Not executed here |

The 35-check regression suite creates real MKV and MP4 fixtures using production muxer arguments, inspects all six AAC streams, decodes supplied video frames, verifies faststart atom ordering and remuxes replay blocks to MP4. The separate actual pipeline smoke test uses the production output transport, bounded writers and pause logic with POSIX FIFOs on this host. Windows uses current-user-only named pipes and still needs device/runtime validation.

Preview generation uses high-quality bicubic downsampling and bounded JPEG encoding on a worker. The controller validates the encoded dimensions and allocates textures asynchronously. Only one encode and one texture upload can be pending; late uploads are disposed when the preview is reset or the plugin unloads. No preview operation is tied to MIDI scheduling.

Windows-only JPEG rendering/decoding is covered by the updated CaptureStudioWindowsSmoke source and compile check; visual quality/FPS and Windows native capture performance were not measured here. Requested preview FPS is an upper bound and depends on canvas/output settings and machine throughput.

The earlier Linux finalization timeout is superseded by the reproduced 30 FPS startup case and the passing independent-writer pipeline tests in this build. This does not establish long-duration, high-resolution or hardware-encoder performance.
