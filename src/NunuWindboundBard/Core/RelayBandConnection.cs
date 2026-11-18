using System.Buffers.Text;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using Windbound.OrchestraRelay;

namespace NunuWindboundBard.Core;

internal sealed record RelayBandInvite(string Relay, string Room, string Stage, string Secret, string Certificate)
{
    private const string Prefix = "WB1-";

    internal string Encode() => Prefix + Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(this));

    internal static RelayBandInvite Decode(string value)
    {
        value = value.Trim();
        if (!value.StartsWith(Prefix, StringComparison.Ordinal) || value.Length > 2048)
            throw new ArgumentException("Paste a Windbound band join code.");
        try
        {
            var invite = JsonSerializer.Deserialize<RelayBandInvite>(Base64Url.DecodeFromChars(value.AsSpan(Prefix.Length)));
            if (invite is null || !RelayProtocol.IsToken(invite.Room) || invite.Stage is not { Length: >= 1 and <= 50 } ||
                invite.Secret is not { Length: 24 } || !invite.Secret.All(Uri.IsHexDigit) ||
                invite.Certificate is not { Length: 64 } || !invite.Certificate.All(Uri.IsHexDigit))
                throw new FormatException();
            RelayBandConnection.ReadRelayAddress(invite.Relay);
            return invite;
        }
        catch (Exception error) when (error is FormatException or JsonException or ArgumentException)
        {
            throw new ArgumentException("That Windbound band join code is invalid.");
        }
    }
}

internal static class RelayBandConnection
{
    internal static Uri ReadRelayAddress(string? value)
    {
        if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri) ||
            !(uri.Scheme == "wss" || uri.Scheme == "ws" && uri.IsLoopback) ||
            uri.AbsoluteUri.Length > 320 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("Use a wss:// relay address. Local testing can use ws://127.0.0.1.");
        return uri;
    }

    internal static X509Certificate2 CreateHostCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=windbound-orchestra", key, HashAlgorithmName.SHA256);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(5));
    }

    internal static async Task<Stream> OpenAsync(Uri relay, string path, string? bearer, CancellationToken ct, RelayTrace? trace = null)
    {
        var socket = new ClientWebSocket();
        socket.Options.CollectHttpResponseDetails = true;
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        if (bearer is not null) socket.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
        try
        {
            var target = new Uri(relay.AbsoluteUri.TrimEnd('/') + path);
            await socket.ConnectAsync(target, ct).ConfigureAwait(false);
            RecordResponse(socket, trace);
            return new WebSocketStream(socket, trace);
        }
        catch
        {
            RecordResponse(socket, trace);
            socket.Dispose();
            throw;
        }
    }

    private static void RecordResponse(ClientWebSocket socket, RelayTrace? trace)
    {
        if (trace is null || (int)socket.HttpStatusCode == 0) return;
        string reference = "";
        if (socket.HttpResponseHeaders?.TryGetValue(RelayProtocol.TraceHeader, out var values) == true &&
            values.FirstOrDefault() is { Length: 12 } id && id.All(Uri.IsHexDigit))
            reference = $" | relay ref {id}";
        string detail = (int)socket.HttpStatusCode switch
        {
            404 => " (room, pairing request or route not found)",
            409 => " (room has reached its follower limit)",
            429 => " (room creation rate limit)",
            503 => " (service unavailable or at capacity)",
            _ => ""
        };
        trace.Note($"HTTP {(int)socket.HttpStatusCode}{detail}{reference}");
    }

    internal static async Task<string> ReadTokenAsync(Stream control, CancellationToken ct)
    {
        byte[] bytes = new byte[RelayProtocol.TokenLength];
        await control.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        string key = Encoding.ASCII.GetString(bytes);
        return RelayProtocol.IsToken(key) ? key : throw new InvalidDataException("Invalid relay control message.");
    }

    internal static async Task<Stream> AcceptBandPeerAsync(Uri relay, string key, X509Certificate2 certificate,
        CancellationToken ct, RelayTrace? trace = null)
    {
        trace?.Step("Opening conductor attachment /v1/attach");
        Stream socket = await OpenAsync(relay, RelayProtocol.AttachPath, key, ct, trace).ConfigureAwait(false);
        trace?.Step("Conductor TLS handshake");
        var tls = new SslStream(socket);
        try
        {
            await tls.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                AllowRenegotiation = false
            }, ct).ConfigureAwait(false);
            return tls;
        }
        catch
        {
            tls.Dispose();
            throw;
        }
    }

    internal static async Task<Stream> JoinBandAsync(RelayBandInvite invite, CancellationToken ct, RelayTrace? trace = null)
    {
        byte[] pin = Convert.FromHexString(invite.Certificate);
        trace?.Step("Opening follower connection /v1/join");
        Stream socket = await OpenAsync(ReadRelayAddress(invite.Relay), RelayProtocol.JoinPath, invite.Room, ct, trace)
            .ConfigureAwait(false);
        // The outer wss:// connection uses normal certificate validation. This
        // inner connection trusts only the conductor certificate in the invite.
        trace?.Step("Follower TLS handshake");
        var tls = new SslStream(socket, false, (_, certificate, _, _) =>
        {
            bool matches = certificate is not null &&
                CryptographicOperations.FixedTimeEquals(pin, certificate.GetCertHash(HashAlgorithmName.SHA256));
            if (!matches) trace?.Note("Conductor certificate did not match the join code.");
            return matches;
        });
        try
        {
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
            {
                TargetHost = "windbound-orchestra",
                AllowRenegotiation = false
            }, ct).ConfigureAwait(false);
            return tls;
        }
        catch
        {
            tls.Dispose();
            throw;
        }
    }

    private sealed class WebSocketStream(WebSocket socket, RelayTrace? trace) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => true;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Flush() { }
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override void Write(byte[] buffer, int offset, int count) =>
            WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            ReadAsync(buffer.AsMemory(offset, count), ct).AsTask();
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken ct) =>
            WriteAsync(buffer.AsMemory(offset, count), ct).AsTask();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (buffer.IsEmpty) return 0;
            while (true)
            {
                var chunk = await socket.ReceiveAsync(buffer, ct).ConfigureAwait(false);
                if (chunk.MessageType == WebSocketMessageType.Close)
                {
                    string reason = (int?)socket.CloseStatus switch
                    {
                        RelayProtocol.AttachTimeoutCloseCode => "WebSocket close 4001, conductor did not attach within 10 seconds",
                        1000 => "WebSocket close 1000, normal closure",
                        1001 => "WebSocket close 1001, endpoint going away",
                        1011 => "WebSocket close 1011, server error",
                        1012 => "WebSocket close 1012, service restart",
                        int code => $"WebSocket close {code}",
                        null => "WebSocket closed without a status code"
                    };
                    trace?.Note(reason);
                    throw new RelayProtocolException(reason);
                }
                if (chunk.MessageType != WebSocketMessageType.Binary) throw new InvalidDataException("Expected binary data.");
                if (chunk.Count > 0) return chunk.Count;
            }
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default) =>
            socket.SendAsync(buffer, WebSocketMessageType.Binary, true, ct);

        protected override void Dispose(bool disposing)
        {
            if (disposing) socket.Dispose();
            base.Dispose(disposing);
        }
    }
}
