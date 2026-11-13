using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace NunuWindboundBard.Core;

/// <summary>Controller only. Capture and encoding run entirely outside the game.</summary>
public sealed class CaptureStudioClient : IDisposable
{
    private readonly object sync = new();
    private readonly SemaphoreSlim wire = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource? connection;
    private StudioSnapshot current = new();
    private StudioInspector? inspector;
    private byte[]? preview;
    private long previewSequence;
    private string previewSceneId = "";
    private string error = "";
    private int busy, previewPending, generation;
    public StudioSnapshot Snapshot { get { lock (sync) return current with { Busy = Volatile.Read(ref busy) != 0 || current.Busy }; } }
    public StudioInspector? Inspector { get { lock (sync) return inspector; } }
    public string LastError { get { lock (sync) return error; } }
    public (long Sequence, byte[]? Bytes, string SceneId) Preview { get { lock (sync) return (previewSequence, preview, previewSceneId); } }
    private bool disposed;
    public void Launch(string enginePath, string ffmpegPath, string profile)
    {
        if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        _ = Task.Run(async () => {
            try {
                if (!Path.IsPathFullyQualified(enginePath) || !File.Exists(enginePath) || !enginePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Select the published WindboundCaptureEngine.exe using its full path.");
                if (!Path.IsPathFullyQualified(ffmpegPath) || !File.Exists(ffmpegPath))
                    throw new ArgumentException("Select the bundled ffmpeg.exe codec worker.");
                var start = new ProcessStartInfo(enginePath) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(enginePath)! };
                start.ArgumentList.Add("--ffmpeg"); start.ArgumentList.Add(ffmpegPath);
                start.ArgumentList.Add("--profile"); start.ArgumentList.Add(profile);
                using var process = Process.Start(start);
                lock (sync) { error = ""; current = current with { Message = "Engine launched. Waiting for capture runtime initialization..." }; }
                // Bounded retries happen off the game thread. Connect also attaches to an existing engine.
                for (int i = 0; i < 15 && !lifetime.IsCancellationRequested; i++) {
                    try { await RequestAsync(new(), lifetime.Token); StartPolling(); return; }
                    catch (IOException) { }
                    catch (TimeoutException) { }
                    if (process is { HasExited: true } && process.ExitCode != 0) throw new InvalidOperationException("Engine startup failed. Open engine-error.log in the capture profile folder.");
                    await Task.Delay(500, lifetime.Token);
                }
                throw new TimeoutException("Engine did not become ready. Check the capture profile's engine-error.log.");
            } catch (Exception ex) when (ex is not OperationCanceledException) { SetError(ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        });
    }
    public void Connect()
    {
        if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        _ = Task.Run(async () => {
            try { await RequestAsync(new(), lifetime.Token); StartPolling(); lock(sync) error = ""; }
            catch (Exception ex) when (ex is not OperationCanceledException) { SetError(ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        });
    }
    private void StartPolling()
    {
        lock (sync) {
            connection?.Cancel(); connection?.Dispose();
            connection = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            int expected = ++generation; var token = connection.Token;
            _ = Task.Run(async () => {
                int failures = 0;
                try {
                    while (!token.IsCancellationRequested) {
                        await Task.Delay(1000, token);
                        if (Volatile.Read(ref busy) != 0 || Volatile.Read(ref previewPending) != 0) continue;
                        try { await RequestAsync(new(), token, expected); failures = 0; }
                        catch (Exception ex) when (ex is not OperationCanceledException) {
                            if (++failures >= 3) { lock(sync) if (expected == generation) current = current with { Connected = false, Message = "Engine connection lost. Reconnect to check outputs; capture may still be running." }; break; }
                        }
                    }
                } catch (OperationCanceledException) { }
            });
        }
    }
    public void Command(string command, object? args = null)
    {
        if (disposed || !Snapshot.Connected || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        var request = new StudioRequest { Command = command, Args = StudioProtocol.Json(args ?? new { }) };
        int expected = Volatile.Read(ref generation);
        _ = Task.Run(async () => {
            try { await RequestAsync(request, lifetime.Token, expected); lock(sync) error = ""; }
            catch (Exception ex) when (ex is not OperationCanceledException) { SetError(ex.Message); }
            finally { Interlocked.Exchange(ref busy, 0); }
        });
    }
    public void RequestPreview(string sceneId = "")
    {
        if (disposed || !Snapshot.Connected || Volatile.Read(ref busy) != 0 || Interlocked.CompareExchange(ref previewPending, 1, 0) != 0) return;
        int expected = Volatile.Read(ref generation);
        _ = Task.Run(async () => {
            try { await RequestAsync(new() { Command = "Preview", Args = StudioProtocol.Json(new { SceneId = sceneId }) }, lifetime.Token, expected); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetError(ex.Message); }
            finally { Interlocked.Exchange(ref previewPending, 0); }
        });
    }
    private async Task RequestAsync(StudioRequest request, CancellationToken token, int expected = -1)
    {
        await wire.WaitAsync(token);
        try {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var pipe = new NamedPipeClientStream(".", StudioProtocol.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
            await pipe.ConnectAsync(1200, timeout.Token);
            await StudioProtocol.WriteAsync(pipe, request, timeout.Token);
            var response = await StudioProtocol.ReadAsync<StudioResponse>(pipe, timeout.Token);
            if (response.Version != StudioProtocol.Version || response.Id != request.Id) throw new InvalidDataException("Engine response version or request ID mismatch.");
            lock(sync) {
                if (disposed || (expected >= 0 && expected != generation)) return;
                current = response.State;
                if (response.Inspector != null) inspector = response.Inspector;
                if (response.Preview is { } bytes && bytes.Length == StudioProtocol.PreviewWidth * StudioProtocol.PreviewHeight * 4) { preview = bytes; previewSequence = response.PreviewSequence; previewSceneId = response.PreviewSceneId; }
            }
            if (!response.Success) throw new InvalidOperationException(response.Error);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Engine request timed out. Check status before repeating an output command."); }
        finally { wire.Release(); }
    }
    private void SetError(string text) { lock(sync) if (!disposed) error = text; }
    public void Disconnect()
    {
        lock(sync) { ++generation; connection?.Cancel(); current = current with { Connected = false, Message = "Controller disconnected. Engine outputs continue in the background." }; preview = null; inspector = null; }
    }
    public void Dispose()
    {
        lock(sync) { if (disposed) return; disposed = true; ++generation; lifetime.Cancel(); connection?.Cancel(); }
        // No engine shutdown or output stop on plugin unload.
    }
}
