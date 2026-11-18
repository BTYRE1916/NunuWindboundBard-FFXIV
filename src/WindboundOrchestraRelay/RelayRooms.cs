using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Windbound.OrchestraRelay;

internal sealed class RelayRooms(ILogger log, CancellationToken stopping)
{
    private readonly SemaphoreSlim roomLimit = new(32, 32);
    private readonly ConcurrentDictionary<string, Room> rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingLink> links = new(StringComparer.Ordinal);

    private sealed class Room(CancellationToken token, string reference)
    {
        public string Reference { get; } = reference;
        private int followers;
        public CancellationToken Token { get; } = token;
        public Channel<string> PairKeys { get; } = Channel.CreateBounded<string>(RelayProtocol.MaxFollowers);
        public bool Reserve()
        {
            if (Interlocked.Increment(ref followers) <= RelayProtocol.MaxFollowers) return true;
            Release();
            return false;
        }
        public void Release() => Interlocked.Decrement(ref followers);
    }

    private sealed class PendingLink(string reference)
    {
        public string Reference { get; } = reference;
        public TaskCompletionSource<WebSocket> Host { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task HostAsync(HttpContext context)
    {
        string reference = SetReference(context);
        if (!IsUpgrade(context)) return;
        if (!roomLimit.Wait(0))
        {
            Event(reference, "Host refused", "room capacity reached (HTTP 503)");
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        string roomId = RelayProtocol.NewToken();
        using var expiry = new CancellationTokenSource(TimeSpan.FromHours(4));
        using var roomLife = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, expiry.Token, stopping);
        var room = new Room(roomLife.Token, reference);
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            if (!rooms.TryAdd(roomId, room)) throw new InvalidOperationException("Room ID collision.");
            await SendTokenAsync(socket, roomId, roomLife.Token);
            Event(reference, "Host", "room open");
            Task<string> watch = WaitForCloseAsync(socket, roomLife.Token);
            Task<string> keys = SendPairKeysAsync(socket, room);
            await ObserveFirstAsync(reference, roomLife, watch, "control receive", keys, "control send");
        }
        catch (Exception error) when (ConnectionEnded(error)) { Event(reference, "Host", ErrorCode(error)); }
        finally
        {
            Event(reference, "Room ended", stopping.IsCancellationRequested ? "relay shutting down" :
                expiry.IsCancellationRequested ? "four-hour lifetime expired" :
                context.RequestAborted.IsCancellationRequested ? "conductor request aborted" : "control channel ended");
            rooms.TryRemove(roomId, out _);
            await roomLife.CancelAsync();
            roomLimit.Release();
        }
    }

    public async Task JoinAsync(HttpContext context)
    {
        string reference = SetReference(context);
        if (!IsUpgrade(context)) return;
        string? roomId = ReadBearer(context);
        if (roomId is null || !rooms.TryGetValue(roomId, out var room) || room.Token.IsCancellationRequested)
        {
            Event(reference, "Join refused", "room unavailable (HTTP 404)");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!room.Reserve())
        {
            Event(reference, "Join refused", "room is full (HTTP 409)");
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        string pairKey = RelayProtocol.NewToken();
        var pair = new PendingLink(reference);
        bool waitingForAttach = false;
        try
        {
            using var follower = await context.WebSockets.AcceptWebSocketAsync();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(room.Token, context.RequestAborted);
            using var pairingTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            pairingTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            if (!links.TryAdd(pairKey, pair) || !room.PairKeys.Writer.TryWrite(pairKey))
            {
                Event(reference, "Pairing refused", "pairing queue unavailable");
                return;
            }
            waitingForAttach = true;
            Event(reference, "Waiting for conductor attachment", $"room ref {room.Reference}");
            WebSocket host;
            try { host = await pair.Host.Task.WaitAsync(pairingTimeout.Token); }
            catch (OperationCanceledException) when (!lifetime.IsCancellationRequested)
            {
                using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                try
                {
                    await follower.CloseOutputAsync((WebSocketCloseStatus)RelayProtocol.AttachTimeoutCloseCode,
                        "Conductor attachment timed out", closeTimeout.Token);
                }
                catch (Exception closeError) when (ConnectionEnded(closeError)) { }
                throw;
            }
            waitingForAttach = false;
            Event(reference, "Pair attached", $"room ref {room.Reference}");
            Task<string> upstream = CopyAsync(follower, host, lifetime.Token);
            Task<string> downstream = CopyAsync(host, follower, lifetime.Token);
            await ObserveFirstAsync(reference, lifetime, upstream, "follower to conductor", downstream, "conductor to follower");
        }
        catch (Exception error) when (ConnectionEnded(error))
        {
            string reason = room.Token.IsCancellationRequested ? "room ended" :
                context.RequestAborted.IsCancellationRequested ? "follower request aborted" :
                waitingForAttach && error is OperationCanceledException ? "10-second conductor attachment deadline elapsed" :
                ErrorCode(error);
            Event(reference, "Join ended", reason);
        }
        finally
        {
            links.TryRemove(pairKey, out _);
            pair.Finished.TrySetResult();
            room.Release();
        }
    }

    public async Task AttachAsync(HttpContext context)
    {
        string reference = SetReference(context);
        if (!IsUpgrade(context)) return;
        string? key = ReadBearer(context);
        if (key is null || !links.TryRemove(key, out var pair) || pair.Finished.Task.IsCompleted)
        {
            Event(reference, "Attachment refused", "pairing request unavailable (HTTP 404)");
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        reference = SetReference(context, pair.Reference);
        try
        {
            using var host = await context.WebSockets.AcceptWebSocketAsync();
            pair.Host.TrySetResult(host);
            await pair.Finished.Task.WaitAsync(context.RequestAborted);
        }
        catch (Exception error) when (ConnectionEnded(error)) { Event(reference, "Attachment ended", ErrorCode(error)); }
    }

    private static string SetReference(HttpContext context, string? reference = null)
    {
        // Independent of all credentials. Both ends of an accepted pair receive the same reference.
        reference ??= Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(6));
        context.Response.Headers[RelayProtocol.TraceHeader] = reference;
        return reference;
    }

    private void Event(string reference, string stage, string reason) =>
        log.LogInformation("Windbound relay {Reference} | {Stage} | {Reason}", reference, stage, reason);

    private static string ErrorCode(Exception error) => error switch
    {
        WebSocketException ws => $"WebSocket {ws.WebSocketErrorCode} (0x{ws.HResult:X8})",
        OperationCanceledException => "operation cancelled",
        _ => $"{error.GetType().Name} (0x{error.HResult:X8})"
    };

    private static bool IsUpgrade(HttpContext context)
    {
        if (context.WebSockets.IsWebSocketRequest) return true;
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return false;
    }

    private static string? ReadBearer(HttpContext context)
    {
        string header = context.Request.Headers.Authorization.ToString();
        const string prefix = "Bearer ";
        if (header.Length != prefix.Length + RelayProtocol.TokenLength ||
            !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        string token = header[prefix.Length..];
        return RelayProtocol.IsToken(token) ? token : null;
    }

    private static Task SendTokenAsync(WebSocket socket, string token, CancellationToken ct) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.ASCII.GetBytes(token)), WebSocketMessageType.Binary, true, ct);

    private static async Task<string> SendPairKeysAsync(WebSocket socket, Room room)
    {
        await foreach (string key in room.PairKeys.Reader.ReadAllAsync(room.Token))
            await SendTokenAsync(socket, key, room.Token);
        return "pair queue closed";
    }

    private static async Task<string> WaitForCloseAsync(WebSocket socket, CancellationToken ct)
    {
        // No application data is expected from the control socket.
        var chunk = await socket.ReceiveAsync(new byte[1].AsMemory(), ct);
        return chunk.MessageType == WebSocketMessageType.Close ? CloseCode(socket) : "unexpected control data";
    }

    private static async Task<string> CopyAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested)
        {
            var chunk = await source.ReceiveAsync(buffer.AsMemory(), ct);
            if (chunk.MessageType == WebSocketMessageType.Close) return CloseCode(source);
            if (chunk.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Expected binary data.");
            await destination.SendAsync(buffer.AsMemory(0, chunk.Count), WebSocketMessageType.Binary, true, ct);
        }
        return "connection cancelled";
    }

    private static string CloseCode(WebSocket socket) => socket.CloseStatus is { } close
        ? $"WebSocket close {(int)close}" : "WebSocket closed without a status code";

    private async Task ObserveFirstAsync(string reference, CancellationTokenSource lifetime,
        Task<string> first, string firstDirection, Task<string> second, string secondDirection)
    {
        var ended = await Task.WhenAny(first, second);
        string direction = ended == first ? firstDirection : secondDirection;
        try { Event(reference, direction, await ended); }
        catch (Exception error) when (ConnectionEnded(error)) { Event(reference, direction, ErrorCode(error)); }
        finally
        {
            await lifetime.CancelAsync();
            try { await Task.WhenAll(first, second); }
            catch (Exception error) when (ConnectionEnded(error)) { }
        }
    }

    private static bool ConnectionEnded(Exception error) =>
        error is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException;
}
