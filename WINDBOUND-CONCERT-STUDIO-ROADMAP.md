# Windbound Concert Studio — Native Capture Roadmap

A separate Windows companion process keeps video capture and encoding isolated from Dalamud and FFXIV.

## Milestone 1 included in this source
- Windows Forms companion shell (`src/WindboundCaptureEngine`)
- Local JSON named-pipe endpoint (`WindboundCaptureEngine`)
- FFmpeg Windows Desktop Duplication capture (`ddagrab`)
- NVENC H.264 / HEVC / AV1 selection, with x264 fallback
- Resolution, frame rate and bitrate controls
- MKV output and explicit clean stop/finalise
- Engine log at `%LOCALAPPDATA%\\NunuWindboundBard\\CaptureEngine.log`

## Honest limitations
- Requires a Windows FFmpeg build with `ddagrab`; place `ffmpeg.exe` beside the engine executable or add it to PATH.
- This first milestone captures the selected desktop/output, not an individual game window.
- Audio mixing, scene composition, RTMP/SRT streaming, replay buffer, plugin UI integration, and robust pause/resume are not implemented yet.
- Pause/resume requests are explicitly rejected rather than pretending to pause. The planned implementation is segmented capture plus final remux.

## Build on Windows
Open `NunuWindboundBard.sln` in Visual Studio 2022 with the .NET 10 Windows Desktop workload, or run:

```powershell
dotnet build .\\src\\WindboundCaptureEngine\\WindboundCaptureEngine.csproj -c Release
```

Verify FFmpeg support:

```powershell
ffmpeg -filters | findstr ddagrab
ffmpeg -encoders | findstr nvenc
```

The engine uses a local named pipe, not a network listener. It does not store stream keys.
