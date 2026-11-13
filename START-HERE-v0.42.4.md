# Windbound Bard 0.42.4 - MP4 and HD preview

## Update and activate the new engine

1. Stop recording, streaming and replay in Concert Capture, then choose **SHUT DOWN IDLE ENGINE**. The engine normally keeps running across plugin updates, so this step activates the new engine features.
2. Upload the companion **FFXIVMIDIPLAYER-BY-NUNU-v0.42.4.zip** as an asset of the latest published GitHub release. Keep that exact filename. Commit/push the matching root **Plugin.json** from this package.
3. Refresh the custom repository in Dalamud and update the plugin to **0.42.4.0**.
4. Open **SHOW > Concert Capture > Native Windbound capture suite** and launch the updated engine. Its status identifies version 0.42.4. Keep the plugin files and CaptureEngine subfolder together.

For a local development load, use ReadyToUse/Plugin/NunuWindboundBard.dll and its accompanying files. The published Windows x64 engine is self-contained and the FFmpeg codec worker is included.

## MP4 output

Expand **Video, encoding and storage**, choose **MP4** under **Recording format**, then click **APPLY OUTPUT SETTINGS** with outputs stopped. Start recording normally and use **Stop & Save** to finalize the MP4 before opening or sharing it. Saved replays also use the selected format. MKV remains available and is the default for existing profiles.

Both formats use H.264 video and six stereo AAC tracks. Streaming remains RTMP/RTMPS. Replay buffering still uses short internal MKV blocks, then saves to the selected MKV/MP4 format without re-encoding. MP4 includes faststart metadata for playback.

## Sharper preview

Choose Economy (up to 960x540), HD (up to 1280x720, default), or Full HD (up to 1920x1080). Choose 5, 10 (default) or 15 preview FPS. These controls affect preview only; recording still uses its output settings.

Click **OPEN LARGE PREVIEW** for an independent, resizable preview window. It stays available when the main plugin window is closed. Edit/Program selection is shared with the editor. Canvas aspect ratio is preserved, including ultrawide and 4:3. A smaller recording canvas is not artificially enlarged before transfer.

Old running engines remain usable with their legacy preview and MKV capability. The new controls tell you to restart the idle engine for MP4 and HD preview. Existing scenes/configuration are retained.

## Source and testing

Build NunuWindboundBard.sln using .NET 10, Dalamud API 15 and Release/x64. Build-Plugin.ps1 builds both projects. Build-CaptureEngine.ps1 publishes the standalone Windows engine. The existing startup path fix and NuGet configuration are retained. The source package also retains LFS tracking for the bundled engine executables.

Portable format/preview checks and real codec fixtures:

```powershell
dotnet run --project RegressionTests/RecordingPreviewSmoke/RecordingPreviewSmoke.csproj -- "ReadyToUse/Plugin/CaptureEngine/ffmpeg.exe"
```

Actual recording pipe/pause/finalization/replay test:

```powershell
dotnet run --project Tests/NativeOutputPipelineSmoke.csproj -c Release -- "ReadyToUse/Plugin/CaptureEngine/ffmpeg.exe" mp4 30
```

Use `mkv 30` to exercise the retained MKV path. The Windows native composition/JPEG smoke project can be run separately with the bundled FFmpeg path after outputs are stopped. See VALIDATION-v0.42.4.md for the checks executed here and the remaining Windows-specific validation.
