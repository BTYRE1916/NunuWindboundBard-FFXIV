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

public enum NetworkNoticeKind { Connected, Disconnected, Session, Cue, Stop, Error, BandSetup, CueAborted }

public enum BandSetupAction { None, SetInstruments, Arm, Disarm, Prepare, Unequip }

public sealed record RemoteArrangementSettings(bool RepairOctaves, int ChordStrategy,
    int GlobalTranspose, int MinimumGapMs, int ArpeggioSpacingMs,
    Dictionary<string, int> Octaves);

public sealed record NetworkNotice(NetworkNoticeKind Kind, string Detail = "",
    string Name = "", string Hash = "", byte[]? Midi = null,
    RemoteArrangementSettings? Arrangement = null,
    Dictionary<string, ulong>? Assignments = null,
    long Revision = 0, double HostStartMs = 0, int SpeedThousand = 1000, BandSetupAction Setup = BandSetupAction.None,
    int Instrument = 0, long SetupSequence = 0, int ConnectionId = 0, long StartSequence = 0);

public sealed record NetworkPerformer(string Character, ulong ContentId, bool Ready, string Detail,
    double AgeSeconds, long Revision, bool SupportsBandSetup = false,
    long StartSequence = 0, string StartOutcome = "No START cue sent.",
    string TimingOutcome = "No actual playback timing reported yet.");

/// <summary>
/// Clock and MIDI protocol used by direct private-network connections and hosted bands.
/// Direct TCP requires a trusted network. Hosted bands use a TLS stream over the relay.
/// Does not use game chat, SendInput, or any MIDI playback API.
/// </summary>
public sealed partial class MultiDeviceEnsemble : IDisposable
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
    private string status = "Multi-device disconnected.";
    private bool hosting;
    private bool joining;
    private Wire? currentSession;
    private string sessionFingerprint = "";
    private long revision;
    private long startSequence;
    private Wire? currentStart;
    private double? hostClockOffsetMs;
    private double roundTripMs;
    private double clockSpreadMs;
    private readonly List<(double When, double Offset, double Rtt)> clockSamples = new();

    public int RelayFollowerConnectionId { get { lock (gate) return activeRelay is null ? 0 : hostPeer?.Id ?? 0; } }
    public string Status { get { lock (gate) return status; } }
    public bool IsHosting { get { lock (gate) return hosting; } }
    public bool IsConnected { get { lock (gate) return hostPeer is not null; } }
    public bool IsJoining { get { lock (gate) return joining; } }
    public bool HasClockSync { get { lock (gate) return hostClockOffsetMs.HasValue && clockSamples.Count >= 3; } }
    public double RoundTripMs { get { lock (gate) return roundTripMs; } }
    /// <summary>Spread in milliseconds between low-RTT clock-offset samples; low spread is more stable.</summary>
    public double ClockSpreadMs { get { lock (gate) return clockSpreadMs; } }
    public long SessionRevision { get { lock (gate) return currentSession?.Revision ?? 0; } }
    public string SharedSongHash { get { lock (gate) return currentSession?.Hash ?? ""; } }
    public IReadOnlyList<NetworkPerformer> Performers
    {
        get
        {
            lock (gate)
                return peers.Values.Select(p => new NetworkPerformer(p.Name, p.ContentId, p.Ready,
                    p.ReadyDetail, Math.Max(0, NowMs() - p.LastSeenMs) / 1000.0, p.ReadyRevision,
                    p.SupportsBandSetup, p.StartSequence, p.StartOutcome, p.TimingOutcome))
                    .OrderBy(p => p.Character, StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    public static double NowMs() => Stopwatch.GetTimestamp() * 1000d / Stopwatch.Frequency;
    public static string Sha256(byte[] data) => Convert.ToHexString(SHA256.HashData(data));
    public static string NewSessionCode() => Convert.ToHexString(RandomNumberGenerator.GetBytes(12));

    public bool TryDequeue(out NetworkNotice item) => notices.TryDequeue(out item!);

    private sealed record Wire
    {
        public Wire() { }
        public int Version { get; set; } = ProtocolVersion;
        public bool SupportsBandSetup { get; set; }
        public bool SupportsStartAck { get; set; }
        public long StartSequence { get; set; }
        public BandSetupAction Setup { get; set; }
        public int Instrument { get; set; }
        public long SetupSequence { get; set; }
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
        public int TimedNoteCount { get; set; }
        public double FirstNoteDelayMs { get; set; }
        public int? AppliedTrimMs { get; set; }
        public double FirstScheduledNoteMs { get; set; }
        public double MeanNoteDelayMs { get; set; }
        public double WorstNoteDelayMs { get; set; }
        public double ClockRttMs { get; set; }
        public double ClockSpreadMs { get; set; }
    }

    private sealed class Peer(Stream stream, string name, ulong contentId, int id)
    {
        public Stream Stream { get; } = stream;
        public string Name { get; } = name;
        public ulong ContentId { get; } = contentId;
        public int Id { get; } = id;
        public bool SupportsBandSetup { get; init; }
        public bool SupportsStartAck { get; init; }
        public long SetupSequence;
        public long StartSequence;
        public bool StartAcknowledged;
        public string StartOutcome = "No START cue sent.";
        public string TimingOutcome = "No actual playback timing reported yet.";
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
            try { Stream.Dispose(); } catch { /* closing */ }
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
        // A peer may have a MIDI transfer ahead of the clock probe in its send queue.
        // Timestamp at the writer so that local queue time does not bias clock offset.
        if (message.Type == "Ping") message.ClientTimeMs = NowMs();
        else if (message.Type == "Pong") message.HostSendMs = NowMs();
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
            revision = 0;
            status = $"Hosting private stage '{stage}' on TCP {port}. Waiting for a follower...";
        }
        _ = AcceptLoopAsync(l, stage, code, cts.Token);
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
            cancel = cts; hosting = false; joining = true;
            status = $"Connecting to {address}:{port}...";
        }
        _ = JoinLoopAsync(ct => ConnectDirectAsync(address.Trim(), port, ct),
            stage, code, character, contentId, cts.Token);
    }

    private static void ValidateStageAndCode(string stage, string code)
    {
        if (string.IsNullOrWhiteSpace(stage) || stage.Length > 50)
            throw new ArgumentException("Stage name must be 1–50 characters.");
        if (string.IsNullOrWhiteSpace(code) || code.Length is < 8 or > 128)
            throw new ArgumentException("A session code of at least 8 characters is required.");
    }

    private static async Task<Stream> ConnectDirectAsync(string address, int port, CancellationToken ct)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(address, port, ct).ConfigureAwait(false);
            return client.GetStream();
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private async Task AcceptLoopAsync(TcpListener l, string stage, string code, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client = await l.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                client.NoDelay = true;
                _ = HostPeerAsync(client.GetStream(), stage, code, ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (ObjectDisposedException) { }
        catch (Exception ex) { if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Error, "Host listen failed: " + ex.Message)); }
    }

    private async Task HostPeerAsync(Stream stream, string stage, string code, CancellationToken ct, RelayTrace? trace = null)
    {
        Peer? peer = null;
        CancellationToken deadline = default;
        string? failure = null;
        try
        {
            trace?.Step("Checking follower band credentials");
            using var handshakeTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline = handshakeTimeout.Token;
            handshakeTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await WriteAsync(stream, new Wire { Type = "Challenge", Nonce = nonce }, handshakeTimeout.Token).ConfigureAwait(false);
            var hello = await ReadAsync(stream, handshakeTimeout.Token).ConfigureAwait(false);
            if (hello.Type != "Hello" || hello.Stage != stage || hello.ContentId == 0 ||
                hello.Character.Length is < 1 or > 100 || hello.ClientNonce.Length != 48 ||
                !ProofMatches(hello.Proof, ComputeProof(code, "client", nonce, hello.ClientNonce,
                    stage, hello.ContentId)))
                throw new UnauthorizedAccessException("Invalid session code/stage or player identity.");
            string proof = ComputeProof(code, "server", nonce, hello.ClientNonce, stage, hello.ContentId);
            await WriteAsync(stream, new Wire { Type = "Welcome", Proof = proof }, handshakeTimeout.Token).ConfigureAwait(false);
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                if (cancel is null || cancel.Token != ct) return;
                if (peers.Values.Any(existing => existing.ContentId == hello.ContentId))
                {
                    trace?.Note("Duplicate character already connected.");
                    throw new InvalidOperationException("That character already joined the stage.");
                }
                peer = new Peer(stream, hello.Character, hello.ContentId, Interlocked.Increment(ref nextPeerId))
                { SupportsBandSetup = trace is not null && hello.SupportsBandSetup,
                    SupportsStartAck = hello.SupportsStartAck };
                peers[peer.Id] = peer;
                if (currentSession is { } session) peer.Send(session);
                notices.Enqueue(new(NetworkNoticeKind.Connected, $"{peer.Name} joined the stage."));
            }
            handshakeTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            trace?.Step("Connected band traffic");
            await RunPeerAsync(peer, ct, isHostSide: true, trace).ConfigureAwait(false);
        }
        catch (Exception error) when (trace is not null)
        {
            if (!ct.IsCancellationRequested)
            {
                failure = trace.Failed(error, deadline);
                if (peer is null) notices.Enqueue(new(NetworkNoticeKind.Error, failure));
            }
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
                if (!ct.IsCancellationRequested)
                    notices.Enqueue(new(NetworkNoticeKind.Disconnected, $"{peer.Name} disconnected. {failure}".TrimEnd()));
                peer.Close();
            }
            else stream.Dispose();
        }
    }

    private async Task JoinLoopAsync(Func<CancellationToken, Task<Stream>> connect, string stage, string code,
        string character, ulong contentId, CancellationToken ct, RelayTrace? trace = null)
    {
        CancellationToken deadline = default;
        string? failure = null;
        Stream? stream = null;
        Peer? peer = null;
        try
        {
            using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline = connectTimeout.Token;
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(12));
            stream = await connect(connectTimeout.Token).ConfigureAwait(false);
            trace?.Step("Checking conductor band credentials");
            var challenge = await ReadAsync(stream, connectTimeout.Token).ConfigureAwait(false);
            if (challenge.Type != "Challenge" || challenge.Nonce.Length != 48)
                throw new InvalidDataException("Conductor did not send a valid challenge.");
            string clientNonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            await WriteAsync(stream, new Wire
            {
                Type = "Hello", SupportsBandSetup = trace is not null, SupportsStartAck = true,
                Stage = stage, Character = character, ContentId = contentId,
                ClientNonce = clientNonce,
                Proof = ComputeProof(code, "client", challenge.Nonce, clientNonce, stage, contentId)
            }, connectTimeout.Token).ConfigureAwait(false);
            var welcome = await ReadAsync(stream, connectTimeout.Token).ConfigureAwait(false);
            if (welcome.Type != "Welcome" || !ProofMatches(welcome.Proof,
                    ComputeProof(code, "server", challenge.Nonce, clientNonce, stage, contentId)))
                throw new UnauthorizedAccessException("The conductor's session code does not match.");
            lock (gate)
            {
                ct.ThrowIfCancellationRequested();
                if (cancel is null || cancel.Token != ct) return;
                peer = new Peer(stream, "Conductor", 0, Interlocked.Increment(ref nextPeerId))
                { SupportsBandSetup = trace is not null };
                hostPeer = peer;
                joining = false;
                status = "Connected! Synchronising clocks and receiving MIDI...";
            }
            notices.Enqueue(new(NetworkNoticeKind.Connected, "Connected to conductor."));
            connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            trace?.Step("Connected band traffic");
            await RunPeerAsync(peer, ct, isHostSide: false, trace).ConfigureAwait(false);
        }
        catch (Exception error) when (trace is not null)
        {
            if (!ct.IsCancellationRequested) failure = trace.Failed(error, deadline);
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
            if (peer is null) stream?.Dispose();
            lock (gate)
            {
                if (cancel is not null && cancel.Token == ct)
                {
                    hostPeer = null;
                    hostClockOffsetMs = null; clockSamples.Clear(); joining = false;
                    activeRelay = null;
                    status = failure ?? "Disconnected from conductor.";
                }
            }
            if (!ct.IsCancellationRequested) notices.Enqueue(new(NetworkNoticeKind.Disconnected,
                failure ?? "Connection to conductor lost. Ensemble stopped."));
        }
    }

    private async Task RunPeerAsync(Peer peer, CancellationToken ct, bool isHostSide, RelayTrace? trace)
    {
        using var socketLife = CancellationTokenSource.CreateLinkedTokenSource(ct);
        CancellationToken token = socketLife.Token;
        Task sendTask = SendLoopAsync(peer, token);
        Task pingTask = isHostSide ? Task.CompletedTask : PingLoopAsync(peer, token);
        Task receiveTask = ReceivePeerAsync(peer, token, isHostSide);
        try
        {
            Task first = await Task.WhenAny(receiveTask, sendTask).ConfigureAwait(false);
            if (first.IsFaulted && !ct.IsCancellationRequested && trace is not null)
            {
                string direction = first == receiveTask ? "receive" : "send";
                double quietSeconds = Math.Max(0, NowMs() - Volatile.Read(ref peer.LastSeenMs)) / 1000;
                trace.Note($"First band {direction} failure, {quietSeconds:0.0}s since the last received frame or handshake.");
            }
            await first.ConfigureAwait(false);
        }
        finally
        {
            socketLife.Cancel();
            peer.Close();
            // The other direction can fail during teardown. Keep the original failure.
            try { await Task.WhenAll(receiveTask, sendTask, pingTask).ConfigureAwait(false); }
            catch (Exception) when (token.IsCancellationRequested) { }
        }
    }

    private async Task ReceivePeerAsync(Peer peer, CancellationToken token, bool isHostSide)
    {
        while (!token.IsCancellationRequested)
        {
            Wire message = await ReadAsync(peer.Stream, token).ConfigureAwait(false);
            double receivedMs = NowMs();
            Volatile.Write(ref peer.LastSeenMs, receivedMs);
            if (isHostSide)
            {
                switch (message.Type)
                {
                    case "Ping":
                        peer.Send(new Wire { Type = "Pong", ClientTimeMs = message.ClientTimeMs,
                            HostReceiveMs = receivedMs });
                        break;
                    case "Ready":
                        lock (gate)
                        {
                            if (currentSession is not { } active || message.Revision != active.Revision ||
                                message.SetupSequence != peer.SetupSequence ||
                                !string.Equals(message.Hash, active.Hash, StringComparison.OrdinalIgnoreCase)) break;
                            peer.Ready = message.Ready;
                            peer.ReadyRevision = message.Revision;
                            peer.ReadyDetail = message.Detail.Length > 200 ? message.Detail[..200] : message.Detail;
                        }
                        break;
                    case "StartTimingStatus":
                        lock (gate)
                        {
                            // Telemetry only. Never treat a timing-status packet as an
                            // acknowledgement, a new cue, or permission to start playback.
                            if (!peer.SupportsStartAck || currentStart is not { } statusStart ||
                                peer.StartSequence != message.StartSequence ||
                                !peer.StartAcknowledged ||
                                !peer.StartOutcome.StartsWith("ARMED:", StringComparison.Ordinal) ||
                                statusStart.StartSequence != message.StartSequence ||
                                statusStart.Revision != message.Revision ||
                                !string.Equals(statusStart.Hash, message.Hash, StringComparison.OrdinalIgnoreCase) ||
                                message.Detail is not ("SILENT_PART" or "AWAITING_NOTES") ||
                                !ValidQualityMs(message.ClockRttMs) ||
                                !ValidQualityMs(message.ClockSpreadMs)) break;
                            string explanation = message.Detail == "SILENT_PART"
                                ? "no local notes assigned (silent part)"
                                : "no note callbacks yet at +3s (possible opening rest or stopped/unsafe output)";
                            peer.TimingOutcome = $"{explanation}; RTT {message.ClockRttMs:0.0}ms, " +
                                $"clock spread {message.ClockSpreadMs:0.0}ms";
                            activeRelay?.Note($"START #{message.StartSequence} follower {peer.Id} timing status: " +
                                peer.TimingOutcome);
                        }
                        break;
                    case "StartTiming":
                        lock (gate)
                        {
                            if (!peer.SupportsStartAck || currentStart is not { } timedStart ||
                                peer.StartSequence != message.StartSequence ||
                                !peer.StartAcknowledged ||
                                !peer.StartOutcome.StartsWith("ARMED:", StringComparison.Ordinal) ||
                                timedStart.StartSequence != message.StartSequence ||
                                timedStart.Revision != message.Revision ||
                                !string.Equals(timedStart.Hash, message.Hash, StringComparison.OrdinalIgnoreCase) ||
                                message.TimedNoteCount < 1 || message.TimedNoteCount > 1000000 ||
                                message.AppliedTrimMs is < -1500 or > 1500 ||
                                !ValidTimingMs(message.FirstNoteDelayMs) ||
                                !ValidFirstScheduledMs(message.FirstScheduledNoteMs) ||
                                !ValidTimingMs(message.MeanNoteDelayMs) ||
                                !ValidTimingMs(message.WorstNoteDelayMs) ||
                                !ValidQualityMs(message.ClockRttMs) ||
                                !ValidQualityMs(message.ClockSpreadMs)) break;
                            peer.TimingOutcome = MultiDeviceEnsembleTiming.DescribeTrim(message.AppliedTrimMs, message.FirstNoteDelayMs) +
                                $"part first note at +{message.FirstScheduledNoteMs:0.0}ms; " +
                                $"callback first {message.FirstNoteDelayMs:+0.0;-0.0;0.0}ms, " +
                                $"mean {message.MeanNoteDelayMs:+0.0;-0.0;0.0}ms, " +
                                $"worst {message.WorstNoteDelayMs:+0.0;-0.0;0.0}ms; " +
                                $"RTT {message.ClockRttMs:0.0}ms, clock spread {message.ClockSpreadMs:0.0}ms";
                            activeRelay?.Note($"START #{message.StartSequence} follower {peer.Id} dispatched " +
                                $"{message.TimedNoteCount} notes; {peer.TimingOutcome}");
                        }
                        break;
                    case "StartAck":
                        lock (gate)
                        {
                            if (!peer.SupportsStartAck || currentStart is not { } start ||
                                start.StartSequence != message.StartSequence || message.StartSequence <= 0 ||
                                peer.StartSequence != message.StartSequence ||
                                start.Revision != message.Revision ||
                                !string.Equals(start.Hash, message.Hash, StringComparison.OrdinalIgnoreCase)) break;
                            peer.StartAcknowledged = true;
                            peer.StartOutcome = (message.Ready ? "ARMED: " : "FAILED: ") +
                                (message.Detail.Length > 180 ? message.Detail[..180] : message.Detail);
                            activeRelay?.Note($"START #{message.StartSequence}: follower connection {peer.Id} " +
                                (message.Ready ? "armed" : "rejected"));
                            // A hosted band must not play with only a random subset of Bards.
                            // The local conductor is also cancelled via the CueAborted notice.
                            if (activeRelay is not null && !message.Ready)
                                AbortRelayStartLocked(start, $"{peer.Name} rejected the cue: {message.Detail}");
                            else if (activeRelay is not null && peers.Values.All(p =>
                                p.StartSequence == start.StartSequence && p.StartAcknowledged &&
                                p.StartOutcome.StartsWith("ARMED:", StringComparison.Ordinal)))
                                activeRelay.Note($"START #{start.StartSequence}: all {peers.Count} Bards armed.");
                        }
                        break;
                }
            }
            else
            {
                switch (message.Type)
                {
                    case "Pong": UpdateClock(message, receivedMs); break;
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
                            message.Assignments, message.Revision, SetupSequence: message.SetupSequence, ConnectionId: peer.Id));
                        break;
                    case "BandSetup":
                        if (peer.SupportsBandSetup && message.Setup is >= BandSetupAction.SetInstruments and <= BandSetupAction.Unequip &&
                            message.Revision > 0 && IsHexHash(message.Hash) && message.SetupSequence > 0 &&
                            message.Instrument is >= 0 and <= 255)
                            notices.Enqueue(new(NetworkNoticeKind.BandSetup, Hash: message.Hash, Revision: message.Revision,
                                Setup: message.Setup, Instrument: message.Instrument, SetupSequence: message.SetupSequence,
                                ConnectionId: peer.Id));
                        break;
                    case "Start":
                        activeRelay?.Note($"START #{message.StartSequence} received from conductor.");
                        notices.Enqueue(new(NetworkNoticeKind.Cue, "Conductor sent START.", Hash: message.Hash,
                            Revision: message.Revision, HostStartMs: message.HostStartMs,
                            SpeedThousand: message.SpeedThousand, ConnectionId: peer.Id,
                            StartSequence: message.StartSequence));
                        break;
                    case "Stop": notices.Enqueue(new(NetworkNoticeKind.Stop, "Conductor sent STOP.", ConnectionId: peer.Id)); break;
                }
            }
        }
    }

    private static bool ValidTimingMs(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= -5000 && value <= 5000;

    private static bool ValidFirstScheduledMs(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 86400000;

    private static bool ValidQualityMs(double value) =>
        !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0 && value <= 2500;

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
            peer.Send(new Wire { Type = "Ping" });
            await Task.Delay(750, ct).ConfigureAwait(false);
        }
    }

    private void UpdateClock(Wire msg, double clientReceive)
    {
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
            roundTripMs = best.Rtt;
            // The single smallest RTT can carry a noisy asymmetric delay sample.
            // Use the median of recent low-latency samples when at least three
            // exist, otherwise retain the original minimum-RTT estimator.
            var lowLatency = clockSamples.Where(s => s.Rtt <= best.Rtt + 25).ToArray();
            var sortedOffsets = lowLatency.Select(s => s.Offset).OrderBy(v => v).ToArray();
            hostClockOffsetMs = sortedOffsets.Length >= 3
                ? (sortedOffsets[(sortedOffsets.Length - 1) / 2] + sortedOffsets[sortedOffsets.Length / 2]) / 2
                : best.Offset;
            clockSpreadMs = sortedOffsets.Length <= 1 ? 0 :
                Math.Min(2500, sortedOffsets[^1] - sortedOffsets[0]);
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
        IReadOnlyDictionary<string, ulong> assignments, bool resend = false)
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
            if (identity == sessionFingerprint && !resend) return false;
            sessionFingerprint = identity;
            currentStart = null;
            long newRev = ++revision;
            newSession = new Wire { Type = "Session", Name = name, Hash = hash,
                Data = Convert.ToBase64String(data), Arrangement = normalized,
                Assignments = ordered, Revision = newRev };
            currentSession = newSession;
            foreach (var peer in peers.Values)
            {
                peer.Ready = false;
                peer.ReadyRevision = 0;
                peer.SetupSequence = 0;
                peer.ReadyDetail = "Applying updated song/parts...";
                peer.StartSequence = 0;
                peer.StartAcknowledged = false;
                peer.StartOutcome = "No START cue sent.";
                peer.Send(newSession);
            }
        }
        return true;
    }

    // Setup requests are scoped to the current song and currently connected performers.
    // Rejoining receives the song snapshot, never a previous ARM or instrument command.
    public int SendBandSetup(BandSetupAction action, IReadOnlyDictionary<ulong, int> instruments)
    {
        if (action is < BandSetupAction.SetInstruments or > BandSetupAction.Unequip)
            throw new ArgumentOutOfRangeException(nameof(action));
        lock (gate)
        {
            if (activeRelay is null || !hosting || currentSession is not { } session)
                throw new InvalidOperationException("Host a band and share a MIDI first.");
            int sent = 0;
            foreach (var peer in peers.Values)
            {
                if (!peer.SupportsBandSetup) continue;
                peer.Ready = false;
                peer.ReadyDetail = "Waiting for follower to process band setup.";
                peer.Send(new Wire { Type = "BandSetup", Setup = action, Instrument = instruments.GetValueOrDefault(peer.ContentId),
                    SetupSequence = ++peer.SetupSequence, Hash = session.Hash, Revision = session.Revision });
                sent++;
            }
            return sent;
        }
    }

    public bool RetryBandMember(ulong contentId)
    {
        lock (gate)
        {
            if (!hosting || activeRelay is null || currentSession is not { } session) return false;
            var peer = peers.Values.FirstOrDefault(p => p.ContentId == contentId);
            if (peer is null || !peer.SupportsBandSetup) return false;
            peer.Ready = false;
            peer.ReadyDetail = "Retrying MIDI and track assignments.";
            peer.Send(session with { SetupSequence = ++peer.SetupSequence });
            return true;
        }
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
            if (activeRelay is not null && !peer.SupportsStartAck)
            {
                detail = $"{peer.Name} uses an old plugin without START confirmations. Upgrade every Bard before cueing.";
                return false;
            }
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
        if (double.IsNaN(hostStartMs) || double.IsInfinity(hostStartMs) ||
            hostStartMs < NowMs() + 1500 || speedThousand is < 500 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(hostStartMs), "START needs a future time and valid speed.");
        // Snapshot and queue the identical START to every follower while holding the
        // roster lock. A peer can join/leave during a show, but never receive a
        // different downbeat due to two independent roster enumerations.
        Wire msg;
        Peer[] recipients;
        CancellationToken lifetime;
        bool hosted;
        lock (gate)
        {
            if (!hosting || currentSession is not { } session || cancel is null)
                throw new InvalidOperationException("Host and share a MIDI before START.");
            recipients = peers.Values.ToArray();
            if (recipients.Length == 0) throw new InvalidOperationException("No follower connected yet.");
            if (activeRelay is not null && recipients.Any(p => !p.SupportsStartAck))
                throw new InvalidOperationException("Every hosted Bard must support START confirmations. Update the older followers.");
            foreach (var peer in recipients)
            {
                if (!peer.Ready || peer.ReadyRevision != session.Revision || NowMs() - peer.LastSeenMs > 4000)
                    throw new InvalidOperationException($"{peer.Name} is not ready: {peer.ReadyDetail}");
            }
            msg = new Wire { Type = "Start", Hash = session.Hash, Revision = session.Revision,
                HostStartMs = hostStartMs, SpeedThousand = speedThousand,
                StartSequence = ++startSequence };
            currentStart = msg;
            foreach (var peer in recipients)
            {
                peer.StartSequence = msg.StartSequence;
                peer.StartAcknowledged = false;
                peer.TimingOutcome = "Awaiting first-note timing...";
                peer.StartOutcome = peer.SupportsStartAck
                    ? "START queued; waiting for Bard result."
                    : "Legacy Bard: START sent, but playback confirmation is unavailable.";
                peer.Send(msg);
            }
            activeRelay?.Note($"START #{msg.StartSequence} queued for {recipients.Length} follower(s).");
            lifetime = cancel.Token;
            hosted = activeRelay is not null;
        }
        // Re-send only to clients that understand acknowledgements; older versions
        // would incorrectly treat retransmission as a second playback command.
        _ = RetryUnacknowledgedStartsAsync(msg, recipients, lifetime);
        if (hosted)
            _ = VerifyRelayStartBeforeDownbeatAsync(msg, lifetime);
    }

    // All participants must confirm playback is queued BEFORE the downbeat.
    // Otherwise send STOP to everyone, including the local conductor through the notice queue.
    private async Task VerifyRelayStartBeforeDownbeatAsync(Wire start, CancellationToken ct)
    {
        try
        {
            double remaining = start.HostStartMs - NowMs() - 1250;
            if (remaining > 0)
                await Task.Delay(TimeSpan.FromMilliseconds(remaining), ct).ConfigureAwait(false);
            lock (gate)
            {
                if (!hosting || activeRelay is null || currentStart?.StartSequence != start.StartSequence)
                    return;
                var missing = peers.Values.Where(p =>
                    p.StartSequence == start.StartSequence && !p.StartAcknowledged).ToArray();
                if (missing.Length > 0)
                    AbortRelayStartLocked(start, $"No START confirmation from {string.Join(", ", missing.Select(p => p.Name))} before the downbeat.");
            }
        }
        catch (OperationCanceledException) { /* band closed or song changed */ }
    }

    // Call only while holding gate. The STOP is queued on every follower's own connection.
    private void AbortRelayStartLocked(Wire start, string reason)
    {
        if (currentStart?.StartSequence != start.StartSequence) return;
        currentStart = null;
        foreach (var peer in peers.Values)
        {
            if (peer.StartSequence != start.StartSequence) continue;
            if (!peer.StartOutcome.StartsWith("FAILED:", StringComparison.Ordinal))
                peer.StartOutcome = "CANCELLED: " + reason;
            peer.Send(new Wire { Type = "Stop" });
        }
        activeRelay?.Note($"START #{start.StartSequence} aborted for whole band: {reason}");
        notices.Enqueue(new(NetworkNoticeKind.CueAborted,
            $"Hosted START #{start.StartSequence} cancelled for all Bards: {reason}"));
    }

    private async Task RetryUnacknowledgedStartsAsync(Wire start, Peer[] recipients, CancellationToken ct)
    {
        try
        {
            // The common start time never changes, including on retransmission.
            // A follower deduplicates using session revision + connection + sequence.
            for (int attempt = 0; attempt < 3; attempt++)
            {
                await Task.Delay(650, ct).ConfigureAwait(false);
                lock (gate)
                {
                    if (!hosting || currentStart?.StartSequence != start.StartSequence ||
                        NowMs() >= start.HostStartMs - 750) return;
                    int retried = 0;
                    foreach (var peer in recipients)
                    {
                        if (!peer.SupportsStartAck || peer.StartAcknowledged ||
                            peer.StartSequence != start.StartSequence || !peers.ContainsKey(peer.Id)) continue;
                        peer.Send(start);
                        retried++;
                    }
                    if (retried > 0)
                        activeRelay?.Note($"START #{start.StartSequence} retransmitted to {retried} unconfirmed follower(s).");
                    else return;
                }
            }
        }
        catch (OperationCanceledException) { /* band closed */ }
    }

    public void SendStartAck(long startSequence, long revision, string hash, bool armed, string detail)
    {
        if (startSequence <= 0) return; // Legacy conductor, no acknowledgement support.
        Peer? peer;
        lock (gate)
        {
            peer = hostPeer;
            activeRelay?.Note($"START #{startSequence} result: " + (armed ? "armed" : "rejected"));
        }
        peer?.Send(new Wire { Type = "StartAck", StartSequence = startSequence,
            Revision = revision, Hash = hash, Ready = armed,
            Detail = detail.Length > 180 ? detail[..180] : detail });
    }

    /// <summary>
    /// Report a follower's actual MIDI callback dispatch timing, NOT the WebSocket ACK delay.
    /// This is measured against the scheduled MIDI note time; audible game latency
    /// is outside our scheduler and must be measured separately.
    /// </summary>
    public void SendStartTiming(long startSeq, long rev, string hash, int count,
        double firstMs, double meanMs, double worstMs, double firstScheduledMs, int? appliedTrimMs = null)
    {
        if (startSeq <= 0 || count <= 0 || appliedTrimMs is < -1500 or > 1500 || !ValidTimingMs(firstMs) ||
            !ValidTimingMs(meanMs) || !ValidTimingMs(worstMs) ||
            !ValidFirstScheduledMs(firstScheduledMs)) return;
        Peer? peer;
        double rtt, spread;
        lock (gate)
        {
            peer = hostPeer;
            rtt = roundTripMs;
            spread = clockSpreadMs;
            activeRelay?.Note($"START #{startSeq} local callbacks: " +
                MultiDeviceEnsembleTiming.DescribeTrim(appliedTrimMs, firstMs) +
                $"part first note at +{firstScheduledMs:0.0}ms; " +
                $"first {firstMs:+0.0;-0.0;0.0}ms, " +
                $"mean {meanMs:+0.0;-0.0;0.0}ms; RTT {rtt:0.0}ms, clock spread {spread:0.0}ms.");
        }
        peer?.Send(new Wire { Type = "StartTiming", StartSequence = startSeq,
            Revision = rev, Hash = hash, TimedNoteCount = count,
            AppliedTrimMs = appliedTrimMs, FirstNoteDelayMs = firstMs, FirstScheduledNoteMs = firstScheduledMs, MeanNoteDelayMs = meanMs,
            WorstNoteDelayMs = worstMs, ClockRttMs = rtt, ClockSpreadMs = spread });
    }

    /// <summary>
    /// Say explicitly when there are no note callbacks to measure. This is not
    /// a latency measurement; later real timing data replaces the status.
    /// </summary>
    public void SendStartTimingStatus(long startSeq, long rev, string hash, string code)
    {
        if (startSeq <= 0 || code is not ("SILENT_PART" or "AWAITING_NOTES")) return;
        Peer? peer;
        double rtt, spread;
        lock (gate)
        {
            peer = hostPeer;
            rtt = roundTripMs;
            spread = clockSpreadMs;
            activeRelay?.Note($"START #{startSeq} timing status: {code}; " +
                $"RTT {rtt:0.0}ms, clock spread {spread:0.0}ms.");
        }
        peer?.Send(new Wire { Type = "StartTimingStatus", StartSequence = startSeq,
            Revision = rev, Hash = hash, Detail = code,
            ClockRttMs = rtt, ClockSpreadMs = spread });
    }

    public void NoteHostTimingStatus(string reason)
    {
        lock (gate)
            activeRelay?.Note($"START #{currentStart?.StartSequence ?? 0} conductor timing status: {reason}");
    }

    public void NoteHostStartTiming(double firstMs, double meanMs, double worstMs, int count,
        double firstScheduledMs)
    {
        lock (gate)
            if (hosting && currentStart is { } start && count > 0 && ValidFirstScheduledMs(firstScheduledMs))
                activeRelay?.Note($"START #{start.StartSequence} conductor callbacks: " +
                    $"{count} notes; part first note at +{firstScheduledMs:0.0}ms; " +
                    $"first callback vs shared note {firstMs:+0.0;-0.0;0.0}ms, " +
                    $"mean {meanMs:+0.0;-0.0;0.0}ms, worst {worstMs:+0.0;-0.0;0.0}ms.");
    }

    public void BroadcastStop()
    {
        if (!IsHosting) return;
        lock (gate) currentStart = null;
        foreach (var peer in peers.Values) peer.Send(new Wire { Type = "Stop" });
    }

    public void SendReady(long revision, string hash, bool ready, string detail, long setupSequence = 0)
    {
        Peer? peer;
        lock (gate) peer = hostPeer;
        if (peer is null) return;
        peer.Send(new Wire { Type = "Ready", Revision = revision, Hash = hash,
            Ready = ready, SetupSequence = setupSequence, Detail = detail.Length > 200 ? detail[..200] : detail });
    }

    public void Stop()
    {
        CancellationTokenSource? old;
        TcpListener? oldListener;
        Peer? oldHost;
        lock (gate)
        {
            activeRelay?.Note("Connection stopped on this client.");
            activeRelay = null;
            old = cancel; cancel = null; oldListener = listener; listener = null;
            oldHost = hostPeer; hostPeer = null;
            hosting = false; joining = false; status = "Multi-device disconnected.";
            relayJoinCode = ""; currentSession = null; currentStart = null; sessionFingerprint = "";
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
