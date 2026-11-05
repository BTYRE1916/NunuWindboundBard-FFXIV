using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Diagnostics;

namespace NunuWindboundBard.Core;

public enum NetworkNoticeKind { Connected, Disconnected, Session, Cue, Stop, Error }

public sealed record RemoteArrangementSettings(bool RepairOctaves, int ChordStrategy,
    int GlobalTranspose, int MinimumGapMs, int ArpeggioSpacingMs,
    Dictionary<string, int> Octaves);

public sealed record NetworkNotice(NetworkNoticeKind Kind, string Detail = "",
    string Name = "", string Hash = "", byte[]? Midi = null,
    RemoteArrangementSettings? Arrangement = null,
    Dictionary<string, ulong>? Assignments = null,
    long Revision = 0, double HostStartMs = 0, int SpeedThousand = 1000);

public sealed record NetworkPerformer(string Character, ulong ContentId, bool Ready, string Detail,
    double AgeSeconds, long Revision);

/// <summary>
/// Authenticated, private-network, cross-device ensemble transport. The wire protocol is
/// framed JSON over TCP. Session code is authenticated by challenge/response HMAC (never
/// sent in the clear). MIDI and other messages are NOT encrypted: use a trusted LAN or
/// an encrypted private network such as Tailscale; NEVER forward this TCP port publicly.
/// Does not use game chat, SendInput, or any MIDI playback API.
/// </summary>
public sealed class MultiDeviceEnsemble : IDisposable
{
    public const int DefaultPort = 47542;
    public const int MaxMidiBytes = 4 * 1024 * 1024;
    private const int MaxFrameBytes = 7 * 1024 * 1024;
    private const int ProtocolVersion = 1;
    private readonly object gate = new();
    private readonly ConcurrentQueue<NetworkNotice> notices = new();
    private readonly ConcurrentDictionary<int, Peer> peers = new();
    private CancellationTokenSource? cancel;
    private TcpListener? listener;
    private Peer? hostPeer;
    private int nextPeerId;
    private string sessionCode = "";
    private string group = "";
    private string status = "Multi-device disconnected.";
    private bool hosting;
    private bool joining;
    private Wire? currentSession;
    private string sessionFingerprint = "";
    private long revision;
    private double? hostClockOffsetMs;
    private double roundTripMs;
    private readonly List<(double When, double Offset, double Rtt)> clockSamples = new();

    public string Status { get { lock (gate) return status; } }
    public bool IsHosting { get { lock (gate) return hosting; } }
    public bool IsConnected { get { lock (gate) return hostPeer is not null; } }
    public bool IsJoining { get { lock (gate) return joining; } }
    public bool HasClockSync { get { lock (gate) return hostClockOffsetMs.HasValue && clockSamples.Count >= 3; } }
    public double RoundTripMs { get { lock (gate) return roundTripMs; } }
    public long SessionRevision { get { lock (gate) return currentSession?.Revision ?? 0; } }
    public string SharedSongHash { get { lock (gate) return currentSession?.Hash ?? ""; } }
    public IReadOnlyList<NetworkPerformer> Performers => peers.Values
        .Select(p => new NetworkPerformer(p.Name, p.ContentId, p.Ready,
            p.ReadyDetail, Math.Max(0, NowMs() - p.LastSeenMs) / 1000.0, p.ReadyRevision))
        .OrderBy(p => p.Character, StringComparer.OrdinalIgnoreCase).ToArray();

    public static double NowMs() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public static string NewSessionCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    public bool TryDequeue(out NetworkNotice item) => notices.TryDequeue(out item!);

    private sealed class Wire
    {
        public Wire() { }
        public int Version { get; set; } = ProtocolVersion;
        public string Type { get; set; } = "";
        public string Stage { get; set; } = "";
        public string Nonce { get; set; } = "";
        public string ClientNonce { get; set; } = "";
        public string Proof { get; set; } = "";
        public string Character { get; set; } = "";
        public ulong ContentId { get; set; }
        public string Hash { get; set; } = "";
        public string Name { get; set; } = "";
        public string Data { get; set; } = "";
        public RemoteArrangementSettings? Arrangement { get; set; }
        public Dictionary<string, ulong>? Assignments { get; set; }
        public long Revision { get; set; }
        public bool Ready { get; set; }
        public string Detail { get; set; } = "";
        public double ClientTimeMs { get; set; }
        public double HostReceiveMs { get; set; }
        public double HostSendMs { get; set; }
        public double HostStartMs { get; set; }
        public int SpeedThousand { get; set; } = 1000;
    }

    private sealed class Peer(TcpClient client, string name, ulong contentId, int id)
    {
        public TcpClient Client { get; } = client;
        public NetworkStream Stream { get; } = client.GetStream();
        public string Name { get; } = name;
        public ulong ContentId { get; } = contentId;
        public int Id { get; } = id;
        public Channel<Wire> Outgoing { get; } = Channel.CreateUnbounded<Wire>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        public volatile bool Ready;
        public volatile string ReadyDetail = "Not ready";
        public long ReadyRevision;
        public double LastSeenMs = NowMs();
        public void Send(Wire message) => Outgoing.Writer.TryWrite(message);
        public void Close()
        {
            Outgoing.Writer.TryComplete();
            try { Client.Close(); } catch { /* closing */ }
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private static async Task<Wire> ReadAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, ct).ConfigureAwait(false);
        int size = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 2 || size > MaxFrameBytes) throw new InvalidDataException("Invalid network frame length.");
        byte[] bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
        var message = JsonSerializer.Deserialize<Wire>(bytes, JsonOptions)
            ?? throw new InvalidDataException("Empty network message.");
        if (message.Version != ProtocolVersion) throw new InvalidDataException("Windbound network versions differ.");
        return message;
    }

    private static async Task WriteAsync(Stream stream, Wire message, CancellationToken ct)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        if (bytes.Length > MaxFrameBytes) throw new InvalidDataException("The network message is too large.");
        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(bytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    private static string ComputeProof(string code, string role, string serverNonce,
        string clientNonce, string stage, ulong playerId)
    {
        byte[] key = Encoding.UTF8.GetBytes(code);
        byte[] body = Encoding.UTF8.GetBytes($"Windbound-v1|{role}|{serverNonce}|{clientNonce}|{stage}|{playerId}");
        return Convert.ToHexString(HMACSHA256.HashData(key, body));
    }

    private static bool ProofMatches(string actual, string expected)
    {
        try
        {
            byte[] a = Convert.FromHexString(actual);
            byte[] b = Convert.FromHexString(expected);
            return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
        }
        catch (FormatException) { return false; }
    }

    private void StatusChanged(string value)
    {
        lock (gate) status = value;
    }

    public void Host(int port, string stage, string code)
    {
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        ValidateStageAndCode(stage, code);
        Stop();
        var cts = new CancellationTokenSource();
        var l = new TcpListener(IPAddress.Any, port);
        try { l.Start(8); }
        catch { cts.Dispose(); throw; }
        lock (gate)
        {
            cancel = cts; listener = l; hosting = true; joining = false;
            group = stage; sessionCode = code; revision = 0;
            status = $"Hosting private stage '{stage}' on TCP {port}. Waiting for a follower...";
        }
        _ = AcceptLoopAsync(l, cts.Token);
    }

    public void Join(string address, int port, string stage, string code, string character, ulong contentId)
    {
        if (string.IsNullOrWhiteSpace(address) || address.Length > 255)
            throw new ArgumentException("Enter conductor's LAN/Tailscale IP or hostname.");
        if (port is < 1024 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        ValidateStageAndCode(stage, code);
        if (contentId == 0) throw new InvalidOperationException("Can't determine your FFXIV character ID. Join the party first.");
        if (string.IsNullOrWhiteSpace(character)) throw new InvalidOperationException("Your character isn't loaded yet.");
        Stop();
        var cts = new CancellationTokenSource();
        lock (gate)
        {
            cancel = cts; hosting = false; joining = true; group = stage; sessionCode = code;
            status = $"Connecting to {address}:{port}...";
        }
        _ = JoinLoopAsync(address.Trim(), port, stage, code, character, contentId, cts.Token);
    }

    private static void ValidateStageAndCode(string stage, string code)
    {
        if (string.IsNullOrWhiteSpace(stage) || stage.Length > 50)
            throw new ArgumentException("Stage name must be 1–50 characters.");
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 8 or > 128)
            throw new ArgumentException("A session code of at least 8 characters is required.");
    }

    private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _ = HostPeerAsync(client, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Error, "Host listen failed: " + ex.Message)); }
    }

    private async Task HostPeerAsync(TcpClient client, CancellationToken ct)
    {
        Peer? peer = null;
        try
        {
            NetworkStream stream = client.GetStream();
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await WriteAsync(stream, new Wire { Type = "Challenge", Nonce = nonce }, handshakeTimeout.Token).ConfigureAwait(false);
            var hello = await ReadAsync(stream, handshakeTimeout.Token).ConfigureAwait(false);
            if (hello.Type != "Hello" || hello.Stage != group || hello.ContentId == 0 ||
                hello.Character.Length is < 1 or > 100 || hello.ClientNonce.Length != 48 ||
                !ProofMatches(hello.Proof, ComputeProof(sessionCode, "client", nonce, hello.ClientNonce,
                    group, hello.ContentId)))
                throw new UnauthorizedAccessException("Invalid session code/stage or player identity.");
            string proof = ComputeProof(sessionCode, "server", nonce, hello.ClientNonce, group, hello.ContentId);
            await WriteAsync(stream, new Wire { Type = "Welcome", Proof = proof }, handshakeTimeout.Token).ConfigureAwait(false);
            peer = new Peer(client, hello.Character, hello.ContentId, Interlocked.Increment(ref nextPeerId));
            peers[peer.Id] = peer;
            notices.Enqueue(new(NetworkNoticeKind.Connected, $"{peer.Name} joined the stage."));
            Wire? session;
            lock (gate) session = currentSession;
            if (session is not null) peer.Send(session);
            await RunPeerAsync(peer, ct, isHostSide: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (IOException) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Error,
                "Follower connection rejected/disconnected: " + ex.Message));
        }
        finally
        {
            if (peer is not null)
            {
                peers.TryRemove(peer.Id, out _);
                notices.Enqueue(new(NetworkNoticeKind.Disconnected, $"{peer.Name} disconnected."));
                peer.Close();
            }
            else client.Dispose();
        }
    }

    private async Task JoinLoopAsync(string address, int port, string stage, string code,
        string character, ulong contentId, CancellationToken ct)
    {
        TcpClient? client = null;
        Peer? peer = null;
        try
        {
            client = new TcpClient { NoDelay = true };
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(9));
            await client.ConnectAsync(address, port, connectTimeout.Token).ConfigureAwait(false);
            var stream = client.GetStream();
            var challenge = await ReadAsync(stream, connectTimeout.Token).ConfigureAwait(false);
            if (challenge.Type != "Challenge" || challenge.Nonce.Length != 48)
                throw new InvalidDataException("Conductor did not send a valid challenge.");
            string clientNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await WriteAsync(stream, new Wire
            {
                Type = "Hello", Stage = stage, Character = character, ContentId = contentId,
                ClientNonce = clientNonce,
                Proof = ComputeProof(code, "client", challenge.Nonce, clientNonce, stage, contentId)
            }, connectTimeout.Token).ConfigureAwait(false);
            var welcome = await ReadAsync(stream, connectTimeout.Token).ConfigureAwait(false);
            if (welcome.Type != "Welcome" || !ProofMatches(welcome.Proof,
                    ComputeProof(code, "server", challenge.Nonce, clientNonce, stage, contentId)))
                throw new UnauthorizedAccessException("The conductor's session code does not match.");
            peer = new Peer(client, "Conductor", 0, 0);
            lock (gate) { hostPeer = peer; status = "Connected! Synchronising clocks and receiving MIDI..."; }
            notices.Enqueue(new(NetworkNoticeKind.Connected, "Connected to conductor."));
            await RunPeerAsync(peer, ct, isHostSide: false).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException)
        {
            if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Error,
                "The conductor closed pairing. Check the stage name, private code, and plugin version."));
        }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Error,
                "Connection failed: " + ex.Message));
        }
        finally
        {
            peer?.Close();
            client?.Dispose();
            lock (gate)
            {
                if (ReferenceEquals(hostPeer, peer)) hostPeer = null;
                hostClockOffsetMs = null; clockSamples.Clear(); joining = false;
                if (!ct.IsCancellationRequested) status = "Disconnected from conductor.";
            }
            if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Disconnected,
                "Connection to conductor lost. Ensemble stopped."));
        }
    }

    private async Task RunPeerAsync(Peer peer, CancellationToken ct, bool isHostSide)
    {
        using var socketLife = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = socketLife.Token;
        Task sendTask = SendLoopAsync(peer, token);
        Task pingTask = isHostSide ? Task.CompletedTask : PingLoopAsync(peer, token);
        try
        {
            while (!token.IsCancellationRequested)
            {
                Wire message = await ReadAsync(peer.Stream, token).ConfigureAwait(false);
                peer.LastSeenMs = NowMs();
                if (isHostSide)
                {
                    switch (message.Type)
                    {
                        case "Ping":
                            double received = NowMs();
                            peer.Send(new Wire { Type = "Pong", ClientTimeMs = message.ClientTimeMs,
                                HostReceiveMs = received, HostSendMs = NowMs() });
                            break;
                        case "Ready":
                            Wire? active;
                            lock (gate) active = currentSession;
                            peer.Ready = message.Ready && active is not null &&
                                message.Revision == active.Revision &&
                                string.Equals(message.Hash, active.Hash, StringComparison.OrdinalIgnoreCase);
                            peer.ReadyRevision = message.Revision;
                            peer.ReadyDetail = message.Detail.Length > 200 ? message.Detail[..200] : message.Detail;
                            break;
                    }
                }
                else
                {
                    switch (message.Type)
                    {
                        case "Pong": UpdateClock(message); break;
                        case "Session":
                            if (message.Data.Length > MaxMidiBytes * 4 / 3 + 128 ||
                                !IsHexHash(message.Hash) || message.Revision <= 0 || message.Arrangement is null)
                                throw new InvalidDataException("Conductor sent invalid MIDI metadata.");
                            byte[] midi = Convert.FromBase64String(message.Data);
                            if (midi.Length is < 14 or > MaxMidiBytes || !midi.AsSpan(0, 4).SequenceEqual("MThd"u8) ||
                                !string.Equals(Sha256(midi), message.Hash, StringComparison.OrdinalIgnoreCase))
                                throw new InvalidDataException("MIDI validation failed. Song ignored.");
                            notices.Enqueue(new(NetworkNoticeKind.Session, "Conductor shared an ensemble MIDI.",
                                message.Name, message.Hash, midi, message.Arrangement,
                                message.Assignments, message.Revision));
                            break;
                        case "Start":
                            notices.Enqueue(new(NetworkNoticeKind.Cue, "Conductor sent START.", Hash: message.Hash,
                                Revision: message.Revision, HostStartMs: message.HostStartMs,
                                SpeedThousand: message.SpeedThousand));
                            break;
                        case "Stop": notices.Enqueue(new(NetworkNoticeKind.Stop, "Conductor sent STOP.")); break;
                    }
                }
            }
        }
        finally
        {
            socketLife.Cancel();
            peer.Close();
            try { await sendTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
            try { await pingTask.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    private static async Task SendLoopAsync(Peer peer, CancellationToken ct)
    {
        while (await peer.Outgoing.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            while (peer.Outgoing.Reader.TryRead(out Wire? message))
                await WriteAsync(peer.Stream, message, ct).ConfigureAwait(false);
    }

    private static async Task PingLoopAsync(Peer peer, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            peer.Send(new Wire { Type = "Ping", ClientTimeMs = NowMs() });
            await Task.Delay(750, ct).ConfigureAwait(false);
        }
    }

    private void UpdateClock(Wire msg)
    {
        double clientReceive = NowMs();
        double rtt = clientReceive - msg.ClientTimeMs - (msg.HostSendMs - msg.HostReceiveMs);
        double offset = ((msg.HostReceiveMs - msg.ClientTimeMs) +
                         (msg.HostSendMs - clientReceive)) / 2.0;
        if (rtt is < 0 or > 2500 || double.IsNaN(offset) || double.IsInfinity(offset)) return;
        lock (gate)
        {
            clockSamples.Add((clientReceive, offset, rtt));
            clockSamples.RemoveAll(x => x.When < clientReceive - 30000);
            if (clockSamples.Count > 40) clockSamples.RemoveRange(0, clockSamples.Count - 40);
            var best = clockSamples.MinBy(x => x.Rtt);
            hostClockOffsetMs = best.Offset;
            roundTripMs = best.Rtt;
            if (clockSamples.Count >= 3)
                status = $"Connected to conductor; clock sync {roundTripMs:0} ms RTT.";
        }
    }

    public bool TryConvertHostTimeToLocalQpc(double hostStartMs, out long localStartQpc)
    {
        lock (gate)
        {
            if (!hostClockOffsetMs.HasValue || clockSamples.Count < 3)
            {
                localStartQpc = 0; return false;
            }
            double localMs = hostStartMs - hostClockOffsetMs.Value;
            localStartQpc = checked((long)Math.Round(localMs * Stopwatch.Frequency / 1000.0));
            return true;
        }
    }

    private static bool IsHexHash(string s) => s.Length == 64 && s.All(Uri.IsHexDigit);

    /// <summary>
    /// Publish the same MIDI, arrangement and party assignments as an atomic session revision.
    /// Returns true when a NEW revision was published (followers must become ready again).
    /// </summary>
    public bool PublishSession(string midiPath, RemoteArrangementSettings arrangement,
        IReadOnlyDictionary<string, ulong> assignments)
    {
        if (!IsHosting) throw new InvalidOperationException("Host Multi-Device first.");
        byte[] data = File.ReadAllBytes(midiPath);
        if (data.Length is < 14 or > MaxMidiBytes)
            throw new InvalidOperationException("Network ensembles support MIDI files up to 4 MB.");
        string hash = Sha256(data);
        string name = Path.GetFileName(midiPath);
        var ordered = assignments.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
        var octaves = arrangement.Octaves.OrderBy(p => p.Key, StringComparer.Ordinal).ToDictionary(p => p.Key, p => p.Value);
        var normalized = arrangement with { Octaves = octaves };
        string identity = Sha256(Encoding.UTF8.GetBytes(hash + "|" + JsonSerializer.Serialize(normalized) + "|" +
            JsonSerializer.Serialize(ordered)));
        Wire newSession;
        lock (gate)
        {
            if (identity == sessionFingerprint) return false;
            sessionFingerprint = identity;
            long newRev = ++revision;
            newSession = new Wire { Type = "Session", Name = name, Hash = hash,
                Data = Convert.ToBase64String(data), Arrangement = normalized,
                Assignments = ordered, Revision = newRev };
            currentSession = newSession;
            foreach (var peer in peers.Values) { peer.Ready = false; peer.ReadyRevision = 0; peer.ReadyDetail = "Applying updated song/parts..."; }
        }
        foreach (var peer in peers.Values) peer.Send(newSession);
        return true;
    }

    public bool CanCue(out string detail)
    {
        if (!IsHosting) { detail = "Start hosting first."; return false; }
        Wire? snapshot;
        lock (gate) snapshot = currentSession;
        if (snapshot is null) { detail = "Load and share a MIDI first."; return false; }
        var connected = peers.Values.ToArray();
        if (connected.Length == 0) { detail = "No follower connected yet."; return false; }
        foreach (var peer in connected)
        {
            if (!peer.Ready || peer.ReadyRevision != snapshot.Revision)
            {
                detail = $"{peer.Name} is not ready: {peer.ReadyDetail}";
                return false;
            }
            if (NowMs() - peer.LastSeenMs > 4000)
            {
                detail = $"{peer.Name} is not responding.";
                return false;
            }
        }
        detail = $"{connected.Length} performer(s) ready for synchronized start.";
        return true;
    }

    public void BroadcastStart(double hostStartMs, int speedThousand)
    {
        if (!CanCue(out string detail)) throw new InvalidOperationException(detail);
        Wire session;
        lock (gate) session = currentSession!;
        var msg = new Wire { Type = "Start", Hash = session.Hash,
            Revision = session.Revision, HostStartMs = hostStartMs,
            SpeedThousand = speedThousand };
        foreach (var peer in peers.Values) peer.Send(msg);
    }

    public void BroadcastStop()
    {
        if (!IsHosting) return;
        foreach (var peer in peers.Values) peer.Send(new Wire { Type = "Stop" });
    }

    public void SendReady(long revision, string hash, bool ready, string detail)
    {
        Peer? peer;
        lock (gate) peer = hostPeer;
        if (peer is null) return;
        peer.Send(new Wire { Type = "Ready", Revision = revision, Hash = hash,
            Ready = ready, Detail = detail.Length > 200 ? detail[..200] : detail });
    }

    public void Stop()
    {
        CancellationTokenSource? old;
        TcpListener? oldListener;
        Peer? oldHost;
        lock (gate)
        {
            old = cancel; cancel = null; oldListener = listener; listener = null;
            oldHost = hostPeer; hostPeer = null;
            hosting = false; joining = false; status = "Multi-device disconnected.";
            sessionCode = ""; group = ""; currentSession = null; sessionFingerprint = "";
            hostClockOffsetMs = null; clockSamples.Clear(); roundTripMs = 0;
        }
        try { old?.Cancel(); } catch { /* ignore */ }
        try { oldListener?.Stop(); } catch { /* ignore */ }
        oldHost?.Close();
        foreach (var peer in peers.Values) peer.Close();
        peers.Clear();
        // Cancel token source may still be held by asynchronous network tasks; it is
        // disposed when the process unloads, not while a connection loop is active.
    }

    public void Dispose() => Stop();
}
