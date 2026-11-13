using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace WindboundCaptureEngine.Core;

public sealed class PipeServer(CaptureController controller) : IDisposable
{
    private readonly CancellationTokenSource lifetime = new();
    private Task? server;
    public void Start() => server ??= Task.Run(RunAsync);

    private async Task RunAsync()
    {
        while (!lifetime.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(EngineProtocol.PipeName,
                    PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(lifetime.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                string? line;
                while (!lifetime.IsCancellationRequested && (line = await reader.ReadLineAsync()) is not null)
                {
                    EngineResponse response;
                    try { response = await controller.HandleAsync(JsonSerializer.Deserialize<EngineRequest>(line, EngineProtocol.JsonOptions)
                        ?? throw new InvalidDataException("Empty request.")); }
                    catch (Exception ex) { response = new(false, "Error", ex.Message); }
                    await writer.WriteLineAsync(JsonSerializer.Serialize(response, EngineProtocol.JsonOptions));
                }
            }
            catch (OperationCanceledException) { break; }
            catch (IOException) when (!lifetime.IsCancellationRequested) { await Task.Delay(200); }
        }
    }

    public void Dispose() { lifetime.Cancel(); try { server?.Wait(1000); } catch { } lifetime.Dispose(); }
}
