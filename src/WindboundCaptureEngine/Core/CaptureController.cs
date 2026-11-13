using System.Diagnostics;

namespace WindboundCaptureEngine.Core;

/// <summary>Isolated FFmpeg/DDAGrab capture backend. It never runs inside Dalamud.</summary>
public sealed class CaptureController : IDisposable
{
    private readonly object gate = new();
    private Process? process;
    private DateTimeOffset startedAt;
    private string? outputPath;
    private string state = "Idle", message = "Ready.";

    public (string State, string Message, string? OutputPath, long ElapsedSeconds) Snapshot
    {
        get
        {
            lock (gate)
            {
                if (process is { HasExited: true })
                {
                    state = process.ExitCode == 0 ? "Saved" : "Error";
                    message = $"FFmpeg exited with code {process.ExitCode}. See CaptureEngine.log.";
                    process.Dispose(); process = null;
                }
                long seconds = state == "Recording" ? (long)(DateTimeOffset.UtcNow - startedAt).TotalSeconds : 0;
                return (state, message, outputPath, seconds);
            }
        }
    }

    public async Task<EngineResponse> HandleAsync(EngineRequest request)
    {
        switch (request.Command.Trim().ToLowerInvariant())
        {
            case "status": return Response(true);
            case "start": return Start(request);
            case "stop": return await StopAsync();
            case "pause": return Response(false, "Pause is not available yet; it will be implemented using segmented capture and final remux.");
            case "resume": return Response(false, "Resume is not available until segmented pause/resume is implemented.");
            default: return Response(false, "Unknown command.");
        }
    }

    private EngineResponse Start(EngineRequest r)
    {
        lock (gate)
        {
            if (process is { HasExited: false }) return Response(false, "Capture is already running.");
            if (r.Width is < 640 or > 7680 || r.Height is < 480 or > 4320 || r.Fps is < 24 or > 120 || r.BitrateMbps is < 5 or > 150)
                return Response(false, "Capture settings exceed supported limits.");
            if (string.IsNullOrWhiteSpace(r.OutputPath)) return Response(false, "Choose an output path.");
            string ffmpeg = FindFfmpeg();
            if (ffmpeg.Length == 0) return Response(false, "ffmpeg.exe not found beside this app or on PATH.");
            outputPath = Path.GetFullPath(r.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            if (File.Exists(outputPath)) return Response(false, "Output file already exists; choose another filename.");
            string encoder = r.Encoder is "h264_nvenc" or "hevc_nvenc" or "av1_nvenc" ? r.Encoder : "libx264";
            string args = $"-hide_banner -f lavfi -i \"ddagrab=framerate={r.Fps}:draw_mouse=1\" " +
                $"-vf \"scale={r.Width}:{r.Height}:force_original_aspect_ratio=decrease,pad={r.Width}:{r.Height}:(ow-iw)/2:(oh-ih)/2\" " +
                $"-c:v {encoder} {(encoder == "libx264" ? "-preset veryfast" : "-preset p5 -tune hq")} " +
                $"-b:v {r.BitrateMbps}M -maxrate {r.BitrateMbps}M -bufsize {r.BitrateMbps * 2}M -pix_fmt yuv420p -f matroska \"{outputPath}\"";
            var psi = new ProcessStartInfo(ffmpeg, args) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            process = new Process { StartInfo = psi, EnableRaisingEvents = true };
            process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log(e.Data); };
            try
            {
                if (!process.Start()) return Response(false, "FFmpeg did not start.");
                process.BeginErrorReadLine(); process.BeginOutputReadLine();
                startedAt = DateTimeOffset.UtcNow; state = "Recording";
                message = $"Desktop capture running with {encoder}; recording to crash-resilient MKV.";
                return Response(true);
            }
            catch (Exception ex) { state = "Error"; message = ex.Message; process.Dispose(); process = null; return Response(false, message); }
        }
    }

    private async Task<EngineResponse> StopAsync()
    {
        Process? p;
        lock (gate)
        {
            p = process;
            if (p is null || p.HasExited) { state = "Idle"; return Response(false, "No active recording."); }
            state = "Stopping"; message = "Finalising recording…";
        }
        try
        {
            await p.StandardInput.WriteLineAsync("q");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
            await p.WaitForExitAsync(timeout.Token);
            lock (gate) { process?.Dispose(); process = null; state = "Saved"; message = "Recording finalised."; return Response(true); }
        }
        catch (Exception ex) { lock (gate) { state = "Error"; message = "Stop failed: " + ex.Message; return Response(false, message); } }
    }

    private EngineResponse Response(bool ok, string? text = null)
    { var s = Snapshot; return new(ok, s.State, text ?? s.Message, s.OutputPath, s.ElapsedSeconds); }

    private static string FindFfmpeg()
    {
        string local = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(local)) return local;
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        { string p = Path.Combine(dir.Trim(), "ffmpeg.exe"); if (File.Exists(p)) return p; }
        return "";
    }

    private static void Log(string line)
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NunuWindboundBard");
        Directory.CreateDirectory(folder);
        File.AppendAllText(Path.Combine(folder, "CaptureEngine.log"), $"[{DateTimeOffset.Now:O}] {line}{Environment.NewLine}");
    }

    public void Dispose()
    {
        try { if (process is { HasExited: false }) { process.Kill(true); process.WaitForExit(3000); } } catch { }
        process?.Dispose();
    }
}
