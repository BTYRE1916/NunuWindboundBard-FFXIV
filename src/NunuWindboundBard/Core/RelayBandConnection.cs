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

    internal static async Task<Stream> OpenAsync(Uri relay, string path, string? bearer, CancellationToken ct)
    {
        var socket = new ClientWebSocket();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        if (bearer is not null) socket.Options.SetRequestHeader("Authorization", "Bearer " + bearer);
        try
        {
            var target = new Uri(relay.AbsoluteUri.TrimEnd('/') + path);
            await socket.ConnectAsync(target, ct).ConfigureAwait(false);
            return new WebSocketStream(socket);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    internal static async Task<string> ReadTokenAsync(Stream control, CancellationToken ct)
    {
        byte[] bytes = new byte[RelayProtocol.TokenLength];
        await control.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        string key = Encoding.ASCII.GetString(bytes);
        return RelayProtocol.IsToken(key) ? key : throw new InvalidDataException("Invalid relay control message.");
    }

    internal static async Task<Stream> AcceptBandPeerAsync(Uri relay, string key, X509Certificate2 certificate,
        CancellationToken ct)
    {
        Stream socket = await OpenAsync(relay, RelayProtocol.AttachPath, key, ct).ConfigureAwait(false);
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

    internal static async Task<Stream> JoinBandAsync(RelayBandInvite invite, CancellationToken ct)
    {
        byte[] pin = Convert.FromHexString(invite.Certificate);
        Stream socket = await OpenAsync(ReadRelayAddress(invite.Relay), RelayProtocol.JoinPath, invite.Room, ct)
            .ConfigureAwait(false);
        // The outer wss:// connection uses normal certificate validation. This
        // inner connection trusts only the conductor certificate in the invite.
        var tls = new SslStream(socket, false, (_, certificate, _, _) =>
            certificate is not null &&
            CryptographicOperations.FixedTimeEquals(pin, certificate.GetCertHash(HashAlgorithmName.SHA256)));
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

    private sealed class WebSocketStream(WebSocket socket) : Stream
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
                if (chunk.MessageType == WebSocketMessageType.Close) return 0;
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
