using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Threading.Tasks;

namespace NunuWindboundBard.Core;

/// <summary>
/// Local-only fallback for OBS Text (GDI+) / Read from file. The FFXIV thread
/// only queues changes; disk I/O happens on one background worker. This remains
/// available when OBS WebSocket is disconnected and never touches the MIDI engine.
/// </summary>
public sealed class ObsSongTitleFileBridge : IDisposable
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    private readonly object gate = new();
    private readonly string outputPath;
    private string desired = "";
    private string status = "Waiting for the first song-title file update.";
    private long version;
    private long completedVersion = -1;
    private bool workerRunning;
    private long nextRetryAtQpc;
    private bool disposed;

    public ObsSongTitleFileBridge(string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(configDirectory))
            throw new ArgumentException("Plugin configuration directory is missing.", nameof(configDirectory));
        outputPath = Path.Combine(configDirectory, "Windbound-NowPlaying.txt");
    }

    public string FilePath => outputPath;
    public string Status { get { lock (gate) return status; } }

    public void SetText(string? value)
    {
        string normalized = ObsSongTitleOverlay.CleanTitle(value);
        bool start = false;
        lock (gate)
        {
            if (disposed) return;
            if (desired == normalized && (workerRunning || completedVersion == version ||
                Stopwatch.GetTimestamp() < nextRetryAtQpc)) return;
            desired = normalized;
            version++;
            if (!workerRunning) { workerRunning = true; start = true; }
        }
        if (start) _ = Task.Run(WriteLoopAsync);
    }

    public void Retry()
    {
        bool start = false;
        lock (gate)
        {
            if (disposed) return;
            version++;
            if (!workerRunning) { workerRunning = true; start = true; }
        }
        if (start) _ = Task.Run(WriteLoopAsync);
    }

    private async Task WriteLoopAsync()
    {
        while (true)
        {
            string text;
            long attempt;
            lock (gate)
            {
                if (disposed) { workerRunning = false; return; }
                text = desired;
                attempt = version;
            }
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                await File.WriteAllTextAsync(outputPath, text, Utf8WithoutBom).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                        ArgumentException or System.Security.SecurityException)
            {
                lock (gate)
                {
                    status = "Title file could not be written (" + ex.GetType().Name + "). " +
                             "Check the plugin configuration folder, then Retry.";
                    workerRunning = false;
                    // Retry no more than once every three seconds if a file is locked.
                    nextRetryAtQpc = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 3;
                }
                return;
            }
            lock (gate)
            {
                completedVersion = attempt;
                nextRetryAtQpc = 0;
                status = text.Length == 0 ? "Title file updated (blank between songs)." :
                    "Title file updated: " + text;
                if (attempt == version || disposed) { workerRunning = false; return; }
            }
        }
    }

    public void Dispose()
    {
        lock (gate) disposed = true;
    }
}
