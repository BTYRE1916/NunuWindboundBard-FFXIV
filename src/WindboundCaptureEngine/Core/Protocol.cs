namespace WindboundCaptureEngine.Core;

public sealed record EngineRequest(string Command, string? OutputPath = null,
    int Width = 3840, int Height = 2160, int Fps = 60,
    string Encoder = "h264_nvenc", int BitrateMbps = 35);

public sealed record EngineResponse(bool Success, string State, string Message,
    string? OutputPath = null, long ElapsedSeconds = 0);

public static class EngineProtocol
{
    public const string PipeName = "WindboundCaptureEngine";
    public static readonly System.Text.Json.JsonSerializerOptions JsonOptions =
        new() { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase };
}
