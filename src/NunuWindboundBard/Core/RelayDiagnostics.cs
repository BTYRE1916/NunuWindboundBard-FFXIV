using System.ComponentModel;
using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;

namespace NunuWindboundBard.Core;

internal sealed class RelayDiagnostics
{
    private const int Capacity = 120;
    private readonly Queue<string> events = new();
    private int nextId;
    private string? report;

    public RelayTrace Begin(string role) => new(this, $"{role} #{Interlocked.Increment(ref nextId)}");

    public string Report
    {
        get
        {
            lock (events)
                return report ??= $"Windbound {typeof(MultiDeviceEnsemble).Assembly.GetName().Version} | Hosted Band | UTC\n" +
                    (events.Count == 0 ? "No relay activity in this plugin session." : string.Join('\n', events));
        }
    }

    public void Add(string line)
    {
        lock (events)
        {
            if (events.Count == Capacity) events.Dequeue();
            events.Enqueue($"{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss.fff}Z  {line}");
            report = null;
        }
    }
}

internal sealed class RelayTrace(RelayDiagnostics history, string label)
{
    private readonly long start = Stopwatch.GetTimestamp();
    private string stage = "starting";

    public void Step(string value)
    {
        stage = value;
        Note(value);
    }

    public void Note(string text) =>
        history.Add($"{label} (connected +{Stopwatch.GetElapsedTime(start).TotalSeconds:0.0}s)  {text}");

    public string Failed(Exception error, CancellationToken deadline = default)
    {
        string timedOut = deadline.IsCancellationRequested ? "connection deadline elapsed / " : "";
        string detail = $"{stage} — {timedOut}{Describe(error)}";
        Note("Failed at " + detail);
        return detail;
    }

    // Only controlled labels and machine codes enter the report. A remote close description,
    // exception message, request URL or handshake payload can contain user data.
    private static string Describe(Exception error)
    {
        var details = new List<string>();
        for (Exception? e = error; e is not null && details.Count < 5; e = e.InnerException)
        {
            details.Add(e switch
            {
                RelayProtocolException => e.Message,
                OperationCanceledException => "operation cancelled",
                WebSocketException { WebSocketErrorCode: WebSocketError.ConnectionClosedPrematurely } =>
                    "WebSocket ended without a close frame",
                EndOfStreamException => "peer stream ended",
                WebSocketException ws => $"WebSocket {ws.WebSocketErrorCode} (0x{ws.HResult:X8})",
                SocketException socket => $"socket {socket.SocketErrorCode} ({socket.NativeErrorCode})",
                Win32Exception native => $"native error 0x{native.NativeErrorCode:X8}",
                AuthenticationException => "TLS authentication failed",
                UnauthorizedAccessException => "band credential, stage or identity rejected",
                InvalidDataException => "invalid band protocol data",
                _ => $"{e.GetType().Name} (0x{e.HResult:X8})"
            });
        }
        return string.Join(" / ", details);
    }
}

// Construct only with locally defined descriptions and numeric protocol codes.
internal sealed class RelayProtocolException(string message) : IOException(message);
