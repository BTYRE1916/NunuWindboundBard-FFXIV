# Component information

The capture backend does not use OBS, libobs, OBS modules or the OBS frontend.

- Windows Graphics Capture, Direct3D11, GDI+ and WASAPI are Windows system APIs used by Windbound's own backend code.
- Vortice.Direct3D11 3.8.3 / SharpGen bindings: https://github.com/amerkoleci/Vortice.Windows
- NAudio.Wasapi / NAudio.Core 2.2.1: https://github.com/naudio/NAudio
- System.Drawing.Common and the self-contained .NET runtime: https://github.com/dotnet/runtime
- Windows SDK .NET projections / C#/WinRT: https://github.com/microsoft/CsWinRT
- FFmpeg 9.0.2 Windows essentials build: https://www.gyan.dev/ffmpeg/builds/

The supplied FFmpeg binary is copied from the original Gyan essentials distribution. Its original `LICENSE` and `README.txt`, including the build configuration and source information, are retained in `ThirdParty/FFmpeg`. The README identifies the FFmpeg source commit as https://github.com/FFmpeg/FFmpeg/commit/946fcce07b.

The plugin's existing MIDI/audio-preview dependencies remain unchanged.
