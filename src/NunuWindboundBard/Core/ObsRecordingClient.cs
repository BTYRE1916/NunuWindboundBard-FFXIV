using System;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuWindboundBard.Core;

/// <summary>
/// Read-only snapshot copied into the ImGui thread. OBS, not Dalamud, owns video,
/// audio, stream credentials and the recording output. No recording duration cap.
/// </summary>
public sealed record ObsCaptureSnapshot
{
    public bool Connected { get; init; }
    public bool Busy { get; init; }
    public bool Recording { get; init; }
    public bool Paused { get; init; }
    public bool Streaming { get; init; }
    public bool ReplayBuffer { get; init; }
    public long RecordingMilliseconds { get; init; }
    public long RecordingBytes { get; init; }
    public string Directory { get; init; } = "";
    public string LastSavedFile { get; init; } = "";
    public int OutputWidth { get; init; }
    public int OutputHeight { get; init; }
    public float OutputFrameRate { get; init; }
    public double? FreeDiskGiB { get; init; }
    public string Message { get; init; } = "OBS not connected. Set up OBS WebSocket and connect manually.";
    public string SongTitleStatus { get; init; } = "Song title overlay is off.";
}

/// <summary>
/// OBS WebSocket 5 JSON client. Intentionally localhost-only; requests and polling
/// run on worker tasks and never block the Dalamud drawing / MIDI framework thread.
/// Only one request is in-flight at a time; every response is correlated by UUID.
/// No password is persisted. The plugin never implicitly starts streaming.
/// </summary>
public sealed class ObsRecordingClient : IDisposable
{
    public const int DefaultPort = 4455;
    private const int MaxFrameBytes = 256 * 1024;
    private readonly object sync = new();
    private readonly SemaphoreSlim requests = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private ClientWebSocket? socket;
    private CancellationTokenSource? connectionLifetime;
    private ObsCaptureSnapshot current = new();
    private int busy;
    private string monitorFolderOverride = "";
    // Desired overlay state is set on the game's framework thread. The worker
    // coalesces changes and applies them to OBS under the existing request gate.
    private string desiredTitleSource = "";
    private string desiredTitleText = "";
    private bool desiredTitleEnabled;
    private string lastAppliedTitleSource = "";
    private long titleVersion;
    private bool titlePending;
    private int generation;
    private bool disposed;
    public ObsCaptureSnapshot Snapshot { get { lock (sync) return current; } }
    public void SetDiskMonitorFolder(string folder) { lock (sync) monitorFolderOverride = folder ?? ""; }

    /// <summary>Queue a song-title change; no socket I/O on the FFXIV thread.</summary>
    public void SetSongTitle(string sourceName, string songTitle, bool enabled)
    {
        sourceName = (sourceName ?? "").Trim();
        if (sourceName.Length > 128) sourceName = sourceName[..128];
        songTitle = ObsSongTitleOverlay.CleanTitle(songTitle);
        enabled = enabled && sourceName.Length != 0;
        lock (sync)
        {
            if (desiredTitleSource == sourceName && desiredTitleText == songTitle &&
                desiredTitleEnabled == enabled) return;
            desiredTitleSource = sourceName;
            desiredTitleText = songTitle;
            desiredTitleEnabled = enabled;
            titleVersion++;
            titlePending = true;
        }
    }

    /// <summary>Force a retry after the user creates/renames an OBS Text source.</summary>
    public void RetrySongTitle()
    {
        lock (sync) { titleVersion++; titlePending = true; }
    }

    private void Update(Func<ObsCaptureSnapshot, ObsCaptureSnapshot> update)
    {
        lock (sync) current = update(current);
    }

    public static bool ValidPort(int port) => port is >= 1 and <= 65535;

    public static string Authentication(string password, string salt, string challenge)
    {
        string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
        return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
    }

    public void Connect(int port, string password)
    {
        if (!ValidPort(port)) { Update(s => s with { Message = "Choose an OBS port from 1 to 65535." }); return; }
        if (disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        if (Snapshot.Connected) { Interlocked.Exchange(ref busy, 0); return; }
        int revision = Interlocked.Increment(ref generation);
        var tokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        connectionLifetime = tokenSource;
        Update(s => s with { Busy = true, Message = "Connecting to local OBS..." });
        // Password exists only in a temporary parameter captured by this task;
        // the window clears its input as soon as Connect() is called.
        _ = Task.Run(async () =>
        {
            ClientWebSocket? ws = null;
            try
            {
                ws = new ClientWebSocket();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(tokenSource.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(8));
                await ws.ConnectAsync(new Uri($"ws://127.0.0.1:{port}"), timeout.Token).ConfigureAwait(false);
                var hello = await ReceiveOpAsync(ws, 0, timeout.Token).ConfigureAwait(false);
                int rpc = hello.GetProperty("rpcVersion").GetInt32();
                if (rpc < 1) throw new InvalidOperationException("OBS WebSocket 5 (RPC 1+) required.");
                var identification = new System.Collections.Generic.Dictionary<string, object> { ["rpcVersion"] = 1 };
                if (hello.TryGetProperty("authentication", out var auth))
                {
                    var salt = auth.GetProperty("salt").GetString() ?? "";
                    var challenge = auth.GetProperty("challenge").GetString() ?? "";
                    if (password.Length == 0) throw new InvalidOperationException("OBS requires its WebSocket password.");
                    identification["authentication"] = Authentication(password, salt, challenge);
                }
                await SendAsync(ws, new { op = 1, d = identification }, timeout.Token).ConfigureAwait(false);
                await ReceiveOpAsync(ws, 2, timeout.Token).ConfigureAwait(false);
                if (tokenSource.IsCancellationRequested || revision != Volatile.Read(ref generation)) return;
                socket = ws;
                ws = null;
                Update(s => s with { Connected = true, Message = "Connected to OBS. Recording is controlled manually." });
                RetrySongTitle(); // Restores the requested title after reconnection.
                _ = Task.Run(() => PollLoopAsync(revision, tokenSource.Token));
            }
            catch (OperationCanceledException) when (tokenSource.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (revision == Volatile.Read(ref generation))
                    Update(s => s with { Connected = false, Message = "OBS connection failed: " + SafeError(ex) });
            }
            finally
            {
                ws?.Dispose();
                if (revision == Volatile.Read(ref generation))
                {
                    Interlocked.Exchange(ref busy, 0);
                    Update(s => s with { Busy = false });
                }
            }
        });
    }

    public void Disconnect()
    {
        Interlocked.Increment(ref generation);
        try { connectionLifetime?.Cancel(); } catch (ObjectDisposedException) { }
        var previous = Interlocked.Exchange(ref socket, null);
        try { previous?.Abort(); previous?.Dispose(); } catch (WebSocketException) { }
        Interlocked.Exchange(ref busy, 0);
        Update(_ => new ObsCaptureSnapshot {
            Message = "Disconnected from OBS. Any active OBS recording/stream continues until stopped in OBS."
        });
    }

    /// <summary>Named requests only. No arbitrary URL or remote command execution.</summary>
    public void Command(string requestType)
    {
        if (requestType is not ("StartRecord" or "StopRecord" or "PauseRecord" or "ResumeRecord" or
            "SplitRecordFile" or "StartStream" or "StopStream" or "StartReplayBuffer" or
            "StopReplayBuffer" or "SaveReplayBuffer")) return;
        if (!Snapshot.Connected || disposed || Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
        int revision = Volatile.Read(ref generation);
        Update(s => s with { Busy = true, Message = "OBS: " + requestType + "..." });
        _ = Task.Run(async () =>
        {
            bool hasGate = false;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, connectionLifetime?.Token ?? lifetime.Token);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await requests.WaitAsync(timeout.Token).ConfigureAwait(false);
                hasGate = true;
                var ws = socket ?? throw new InvalidOperationException("OBS is disconnected.");
                JsonElement data = await RequestAsync(ws, requestType, timeout.Token).ConfigureAwait(false);
                if (revision != Volatile.Read(ref generation)) return;
                if (requestType == "StopRecord" && data.ValueKind == JsonValueKind.Object &&
                    data.TryGetProperty("outputPath", out var path))
                    Update(s => s with { LastSavedFile = path.GetString() ?? "" });
                Update(s => s with { Message = "OBS accepted " + requestType + "." });
            }
            catch (OperationCanceledException) when (revision != Volatile.Read(ref generation) || disposed) { }
            catch (Exception ex) { if (revision == Volatile.Read(ref generation)) Update(s => s with { Message = "OBS: " + SafeError(ex) }); }
            finally
            {
                if (hasGate) requests.Release();
                Interlocked.Exchange(ref busy, 0);
                if (revision == Volatile.Read(ref generation)) Update(s => s with { Busy = false });
            }
        });
    }

    private async Task PollLoopAsync(int revision, CancellationToken cancel)
    {
        try
        {
            while (!cancel.IsCancellationRequested && revision == Volatile.Read(ref generation))
            {
                try
                {
                    await requests.WaitAsync(cancel).ConfigureAwait(false);
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                        timeout.CancelAfter(TimeSpan.FromSeconds(10));
                        await RefreshAsync(timeout.Token).ConfigureAwait(false);
                        await UpdateSongTitleAsync(timeout.Token).ConfigureAwait(false);
                    }
                    finally { requests.Release(); }
                }
                catch (ObsRequestException ex)
                {
                    Update(s => s with { Message = "OBS status request: " + SafeError(ex) });
                }
                await Task.Delay(TimeSpan.FromSeconds(2), cancel).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested) { }
        catch (OperationCanceledException)
        {
            if (revision == Volatile.Read(ref generation))
            {
                Disconnect();
                Update(s => s with { Message = "OBS status timed out. Check OBS and reconnect." });
            }
        }
        catch (Exception ex)
        {
            if (revision == Volatile.Read(ref generation))
            {
                Disconnect();
                Update(s => s with { Message = "OBS connection lost: " + SafeError(ex) });
            }
        }
    }

    private async Task RefreshAsync(CancellationToken cancel)
    {
        var ws = socket ?? throw new WebSocketException("OBS is disconnected.");
        var rec = await RequestAsync(ws, "GetRecordStatus", cancel).ConfigureAwait(false);
        var live = await RequestAsync(ws, "GetStreamStatus", cancel).ConfigureAwait(false);
        var replay = await RequestAsync(ws, "GetReplayBufferStatus", cancel).ConfigureAwait(false);
        string directory = Snapshot.Directory;
        int w = Snapshot.OutputWidth, h = Snapshot.OutputHeight;
        float fps = Snapshot.OutputFrameRate;
        if (w == 0)
        {
            try
            {
                var video = await RequestAsync(ws, "GetVideoSettings", cancel).ConfigureAwait(false);
                w = GetInt(video, "outputWidth"); h = GetInt(video, "outputHeight");
                int numerator = GetInt(video, "fpsNumerator"), denominator = GetInt(video, "fpsDenominator");
                fps = denominator > 0 ? (float)numerator / denominator : 0f;
            }
            catch (ObsRequestException) { /* OBS older version: resolution display is optional. */ }
        }
        if (directory.Length == 0)
        {
            try
            {
                var result = await RequestAsync(ws, "GetRecordDirectory", cancel).ConfigureAwait(false);
                directory = GetString(result, "recordDirectory");
            }
            catch (ObsRequestException) { /* Optional: users can set their local disk-monitor path in UI. */ }
        }
        string folderOverride;
        lock (sync) folderOverride = monitorFolderOverride;
        double? available = GetFreeDiskGiB(folderOverride, directory);
        Update(s => s with {
            Connected = true,
            FreeDiskGiB = available,
            Recording = GetBool(rec, "outputActive"),
            Paused = GetBool(rec, "outputPaused"),
            Streaming = GetBool(live, "outputActive"),
            ReplayBuffer = GetBool(replay, "outputActive"),
            RecordingMilliseconds = GetLong(rec, "outputDuration"),
            RecordingBytes = GetLong(rec, "outputBytes"),
            OutputWidth = w, OutputHeight = h, OutputFrameRate = fps, Directory = directory
        });
    }

    /// <summary>
    /// A text source is created/styled by the OBS user. We change its text
    /// only, preserving every other input setting via overlay=true. Missing or
    /// wrong-kind sources are reported once rather than retried every frame.
    /// </summary>
    private async Task UpdateSongTitleAsync(CancellationToken cancel)
    {
        string source, text, previous;
        bool enabled;
        long currentVersion;
        lock (sync)
        {
            if (!titlePending) return;
            currentVersion = titleVersion;
            source = desiredTitleSource;
            text = desiredTitleText;
            enabled = desiredTitleEnabled;
            previous = lastAppliedTitleSource;
        }
        var ws = socket;
        if (ws == null) return;
        try
        {
            // If a source was renamed or the overlay was disabled, don't leave
            // the old song caption stuck on screen. Ignore a missing old source.
            if (previous.Length != 0 && (!enabled || previous != source))
            {
                try { await WriteSongTitleAsync(ws, previous, "", cancel).ConfigureAwait(false); }
                catch (Exception ex) when (ex is ObsRequestException or ObsWrongTitleSourceException)
                { /* Old source may have been removed or changed in OBS. */ }
                previous = "";
            }
            if (enabled)
            {
                await WriteSongTitleAsync(ws, source, text, cancel).ConfigureAwait(false);
                previous = source;
            }
            lock (sync)
            {
                lastAppliedTitleSource = previous;
                if (titleVersion == currentVersion) titlePending = false;
            }
            Update(s => s with { SongTitleStatus = enabled
                ? (text.Length == 0 ? "Song title hidden between performances." : "OBS song title updated: " + text)
                : "Song title overlay disabled." });
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // Don't spam OBS requests for a missing or non-text source. The
            // Sync button, connection restart, or source-name edit retries it.
            lock (sync) { if (titleVersion == currentVersion) titlePending = false; }
            string reason = ex is ObsWrongTitleSourceException ? "OBS source is not a Text source."
                : ex is ObsRequestException ? "OBS source not found or rejected."
                : SafeError(ex);
            Update(s => s with { SongTitleStatus = "Title overlay: " + reason +
                " Create a Text (GDI+) source with the exact configured name, then Sync Title." });
        }
    }

    private sealed class ObsWrongTitleSourceException : Exception { }

    private static async Task WriteSongTitleAsync(ClientWebSocket ws, string source, string text, CancellationToken cancel)
    {
        var info = await RequestAsync(ws, "GetInputSettings", new { inputName = source }, cancel).ConfigureAwait(false);
        if (!ObsSongTitleOverlay.IsTextInputKind(GetString(info, "inputKind")))
            throw new ObsWrongTitleSourceException();
        await RequestAsync(ws, "SetInputSettings", new {
            inputName = source,
            inputSettings = new { text },
            overlay = true
        }, cancel).ConfigureAwait(false);
    }

    /// <summary>Use for an advisory ONLY; OBS decides where it records.</summary>
    private static double? GetFreeDiskGiB(string optionalFolder, string obsDirectory)
    {
        string path = string.IsNullOrWhiteSpace(optionalFolder) ? obsDirectory : optionalFolder.Trim();
        if (path.Length == 0) return null;
        try
        {
            string root = Path.GetPathRoot(Path.GetFullPath(path)) ?? "";
            if (root.Length == 0) return null;
            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace / (1024.0 * 1024.0 * 1024.0) : null;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or System.Security.SecurityException) { return null; }
    }

    public static string FormatDuration(long milliseconds)
    {
        var totalSeconds = Math.Max(0L, milliseconds / 1000L);
        return $"{totalSeconds / 3600:00}:{totalSeconds / 60 % 60:00}:{totalSeconds % 60:00}";
    }

    private static bool GetBool(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.True;
    private static long GetLong(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.TryGetInt64(out long v) ? v : 0L;
    private static int GetInt(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) && value.TryGetInt32(out int v) ? v : 0;
    private static string GetString(JsonElement obj, string property) =>
        obj.ValueKind == JsonValueKind.Object && obj.TryGetProperty(property, out var value) ? (value.GetString() ?? "") : "";

    private static string SafeError(Exception ex)
    {
        // Never include WebSocket URLs, raw JSON, credentials or local filesystem paths
        // in the status line. Error types and bounded static diagnostics only.
        return ex is ObsRequestException known ? known.Message : ex switch {
            WebSocketException => "WebSocket connection failed or OBS closed.",
            OperationCanceledException => "Request timed out.",
            InvalidOperationException => "OBS WebSocket authentication or protocol check failed.",
            JsonException => "OBS sent an unexpected protocol message.",
            _ => "OBS request failed (" + ex.GetType().Name + ")."
        };
    }

    private sealed class ObsRequestException : Exception
    {
        public ObsRequestException(string method, int code) : base($"{method} rejected (code {code}). Check OBS settings and output state.") { }
    }

    private static async Task SendAsync(ClientWebSocket ws, object payload, CancellationToken cancel)
    {
        byte[] data = JsonSerializer.SerializeToUtf8Bytes(payload);
        await ws.SendAsync(data.AsMemory(), WebSocketMessageType.Text, true, cancel).ConfigureAwait(false);
    }

    private static async Task<JsonElement> ReceiveAsync(ClientWebSocket ws, CancellationToken cancel)
    {
        using var stream = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var segment = await ws.ReceiveAsync(buffer.AsMemory(), cancel).ConfigureAwait(false);
            if (segment.MessageType == WebSocketMessageType.Close) throw new WebSocketException("OBS closed the connection.");
            if (segment.MessageType != WebSocketMessageType.Text) throw new JsonException("Unsupported OBS frame type.");
            stream.Write(buffer, 0, segment.Count);
            if (stream.Length > MaxFrameBytes) throw new JsonException("OBS WebSocket message exceeds safe size.");
            if (segment.EndOfMessage) break;
        }
        using var packet = JsonDocument.Parse(stream.ToArray());
        return packet.RootElement.Clone();
    }

    private static async Task<JsonElement> ReceiveOpAsync(ClientWebSocket ws, int opcode, CancellationToken cancel)
    {
        while (true)
        {
            var packet = await ReceiveAsync(ws, cancel).ConfigureAwait(false);
            int type = packet.GetProperty("op").GetInt32();
            if (type == opcode) return packet.GetProperty("d").Clone();
            if (type == 5) continue; // unsolicited OBS event, harmless for polling client
            throw new JsonException("Unexpected OBS handshake frame.");
        }
    }

    private static Task<JsonElement> RequestAsync(ClientWebSocket ws, string type, CancellationToken cancel) =>
        RequestAsync(ws, type, null, cancel);

    private static async Task<JsonElement> RequestAsync(ClientWebSocket ws, string type, object? requestData, CancellationToken cancel)
    {
        string id = Guid.NewGuid().ToString("N");
        if (requestData == null)
            await SendAsync(ws, new { op = 6, d = new { requestType = type, requestId = id } }, cancel).ConfigureAwait(false);
        else
            await SendAsync(ws, new { op = 6, d = new { requestType = type, requestId = id, requestData } }, cancel).ConfigureAwait(false);
        while (true)
        {
            var packet = await ReceiveAsync(ws, cancel).ConfigureAwait(false);
            int op = packet.GetProperty("op").GetInt32();
            if (op == 5) continue;
            if (op != 7) throw new JsonException("Unexpected OBS response.");
            var data = packet.GetProperty("d");
            if (!string.Equals(data.GetProperty("requestId").GetString(), id, StringComparison.Ordinal))
                throw new JsonException("OBS response ID did not match its request.");
            var status = data.GetProperty("requestStatus");
            if (!status.GetProperty("result").GetBoolean())
                throw new ObsRequestException(type, status.GetProperty("code").GetInt32());
            return data.TryGetProperty("responseData", out var body) ? body.Clone() : default;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        lifetime.Cancel();
        Disconnect();
        // Deliberately leave active recordings/streams running in OBS.
        lifetime.Dispose();
        // The request semaphore is allowed to be GC'd after outstanding workers finish.
    }
}
