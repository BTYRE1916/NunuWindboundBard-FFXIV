using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace Windbound.OrchestraRelay;

internal sealed class RelayRooms
{
    private readonly SemaphoreSlim roomLimit = new(32, 32);
    private readonly ConcurrentDictionary<string, Room> rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, PendingLink> links = new(StringComparer.Ordinal);

    private sealed class Room(CancellationToken token)
    {
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

    private sealed class PendingLink
    {
        public TaskCompletionSource<WebSocket> Host { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Finished { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public async Task HostAsync(HttpContext context)
    {
        if (!IsUpgrade(context)) return;
        if (!roomLimit.Wait(0))
        {
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        string roomId = RelayProtocol.NewToken();
        using var roomLife = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        roomLife.CancelAfter(TimeSpan.FromHours(4));
        var room = new Room(roomLife.Token);
        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            if (!rooms.TryAdd(roomId, room)) throw new InvalidOperationException("Room ID collision.");
            await SendTokenAsync(socket, roomId, roomLife.Token);
            Task watch = WaitForCloseAsync(socket, roomLife.Token);
            Task keys = SendPairKeysAsync(socket, room);
            await Task.WhenAny(watch, keys);
            await FinishSocketsAsync(roomLife, watch, keys);
        }
        catch (Exception error) when (ConnectionEnded(error)) { }
        finally
        {
            rooms.TryRemove(roomId, out _);
            await roomLife.CancelAsync();
            roomLimit.Release();
        }
    }

    public async Task JoinAsync(HttpContext context)
    {
        if (!IsUpgrade(context)) return;
        string? roomId = ReadBearer(context);
        if (roomId is null || !rooms.TryGetValue(roomId, out var room) || room.Token.IsCancellationRequested)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        if (!room.Reserve())
        {
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        string pairKey = RelayProtocol.NewToken();
        var pair = new PendingLink();
        try
        {
            using var follower = await context.WebSockets.AcceptWebSocketAsync();
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(room.Token, context.RequestAborted);
            using var pairingTimeout = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
            pairingTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            if (!links.TryAdd(pairKey, pair) || !room.PairKeys.Writer.TryWrite(pairKey)) return;
            var host = await pair.Host.Task.WaitAsync(pairingTimeout.Token);
            Task upstream = CopyAsync(follower, host, lifetime.Token);
            Task downstream = CopyAsync(host, follower, lifetime.Token);
            await Task.WhenAny(upstream, downstream);
            await FinishSocketsAsync(lifetime, upstream, downstream);
        }
        catch (Exception error) when (ConnectionEnded(error)) { }
        finally
        {
            links.TryRemove(pairKey, out _);
            pair.Finished.TrySetResult();
            room.Release();
        }
    }

    public async Task AttachAsync(HttpContext context)
    {
        if (!IsUpgrade(context)) return;
        string? key = ReadBearer(context);
        if (key is null || !links.TryRemove(key, out var pair) || pair.Finished.Task.IsCompleted)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }
        try
        {
            using var host = await context.WebSockets.AcceptWebSocketAsync();
            pair.Host.TrySetResult(host);
            await pair.Finished.Task.WaitAsync(context.RequestAborted);
        }
        catch (Exception error) when (ConnectionEnded(error)) { }
    }

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

    private static async Task SendPairKeysAsync(WebSocket socket, Room room)
    {
        await foreach (string key in room.PairKeys.Reader.ReadAllAsync(room.Token))
            await SendTokenAsync(socket, key, room.Token);
    }

    private static async Task WaitForCloseAsync(WebSocket socket, CancellationToken ct)
    {
        // No application data is expected from the control socket.
        await socket.ReceiveAsync(new byte[1].AsMemory(), ct);
    }

    private static async Task CopyAsync(WebSocket source, WebSocket destination, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        while (!ct.IsCancellationRequested)
        {
            var chunk = await source.ReceiveAsync(buffer.AsMemory(), ct);
            if (chunk.MessageType == WebSocketMessageType.Close) return;
            if (chunk.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Expected binary data.");
            await destination.SendAsync(buffer.AsMemory(0, chunk.Count), WebSocketMessageType.Binary, true, ct);
        }
    }

    private static async Task FinishSocketsAsync(CancellationTokenSource lifetime, Task first, Task second)
    {
        await lifetime.CancelAsync();
        try { await Task.WhenAll(first, second); }
        catch (Exception error) when (ConnectionEnded(error)) { }
    }

    private static bool ConnectionEnded(Exception error) =>
        error is OperationCanceledException or WebSocketException or IOException or ObjectDisposedException;
}
