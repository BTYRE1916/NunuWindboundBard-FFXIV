using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NunuWindboundBard.Core;

// Shared wire contract; no capture, Dalamud, or Windows dependency in this file.
public static class StudioProtocol
{
    public const int Version = 2;
    public const int PreviewWidth = 384, PreviewHeight = 216;
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public static string PipeName => "WindboundBard.Studio.v2." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    public static async Task WriteAsync<T>(Stream stream, T value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value);
        if (bytes.Length > MaxMessageBytes) throw new InvalidDataException("Studio message is too large.");
        byte[] header = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, bytes.Length);
        await stream.WriteAsync(header, token);
        await stream.WriteAsync(bytes, token);
        await stream.FlushAsync(token);
    }
    public static async Task<T> ReadAsync<T>(Stream stream, CancellationToken token)
    {
        byte[] header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        int size = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
        if (size < 2 || size > MaxMessageBytes) throw new InvalidDataException("Invalid studio message length.");
        byte[] bytes = new byte[size];
        await stream.ReadExactlyAsync(bytes, token);
        return JsonSerializer.Deserialize<T>(bytes) ?? throw new InvalidDataException("Empty studio message.");
    }
}

public sealed record StudioRequest
{
    public int Version { get; init; } = StudioProtocol.Version;
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Command { get; init; } = "Status";
    public JsonElement Args { get; init; } = StudioProtocol.Json(new { });
}
public sealed record StudioResponse
{
    public int Version { get; init; } = StudioProtocol.Version;
    public string Id { get; init; } = "";
    public bool Success { get; init; }
    public string Error { get; init; } = "";
    public StudioSnapshot State { get; init; } = new();
    public StudioInspector? Inspector { get; init; }
    public byte[]? Preview { get; init; }
    public long PreviewSequence { get; init; }
    public string PreviewSceneId { get; init; } = "";
}
public sealed record StudioType(string Id, string Name);
public sealed record StudioChoice(string Label, JsonElement Value, bool Disabled = false);
public sealed record StudioProperty
{
    public string Name { get; init; } = "";
    public string Label { get; init; } = "";
    public int Type { get; init; }
    public bool Enabled { get; init; } = true;
    public bool Password { get; init; }
    public bool Multiline { get; init; }
    public bool Directory { get; init; }
    public double Min { get; init; }
    public double Max { get; init; }
    public double Step { get; init; }
    public JsonElement Value { get; init; } = StudioProtocol.Json("");
    public StudioChoice[] Choices { get; init; } = [];
}
public sealed record StudioInspector(string SourceId, string FilterId, StudioProperty[] Properties);
public sealed record StudioTransform
{
    public float X { get; init; }
    public float Y { get; init; }
    public float Rotation { get; init; }
    public float ScaleX { get; init; } = 1;
    public float ScaleY { get; init; } = 1;
    public int CropLeft { get; init; }
    public int CropTop { get; init; }
    public int CropRight { get; init; }
    public int CropBottom { get; init; }
    public bool Fit { get; init; }
    public bool Locked { get; init; }
    public void Validate()
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y) || !float.IsFinite(Rotation) ||
            !float.IsFinite(ScaleX) || !float.IsFinite(ScaleY) || Math.Abs(ScaleX) > 100 || Math.Abs(ScaleY) > 100 ||
            CropLeft < 0 || CropTop < 0 || CropRight < 0 || CropBottom < 0)
            throw new ArgumentException("Invalid source transform.");
    }
}
public sealed record StudioFilter
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Filter";
    public string Type { get; init; } = "";
    public bool Enabled { get; init; } = true;
    public string Settings { get; init; } = "{}";
}
public sealed record StudioSource
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Source";
    public string Type { get; init; } = "";
    public string Settings { get; init; } = "{}";
    public bool Visible { get; init; } = true;
    public bool Muted { get; init; }
    public float Volume { get; init; } = 1;
    public long SyncOffsetMs { get; init; }
    public int Monitor { get; init; }
    public uint Tracks { get; init; } = 1;
    public int Width { get; init; }
    public int Height { get; init; }
    public string Status { get; init; } = "";
    public bool Audio { get; init; }
    public float PeakDb { get; init; } = -60;
    public StudioTransform Transform { get; init; } = new();
    public List<StudioFilter> Filters { get; init; } = [];
}
public sealed record StudioScene
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = "Concert";
    // Layer order is bottom to top.
    public List<StudioSource> Sources { get; init; } = [];
}
public sealed record StudioOutputSettings
{
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public int Fps { get; init; } = 30;
    public string Encoder { get; init; } = "libx264";
    public int VideoBitrate { get; init; } = 12000;
    public int AudioBitrate { get; init; } = 192;
    public int ReplaySeconds { get; init; } = 30;
    public int ReplayMegabytes { get; init; } = 512;
    public string Folder { get; init; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "WindboundBard");
    public void Validate()
    {
        if (Width < 320 || Width > 7680 || Height < 240 || Height > 4320 || Width % 2 != 0 || Height % 2 != 0 ||
            Fps < 1 || Fps > 120 || VideoBitrate < 500 || VideoBitrate > 200000 || AudioBitrate < 64 || AudioBitrate > 320 ||
            ReplaySeconds < 5 || ReplaySeconds > 600 || ReplayMegabytes < 64 || ReplayMegabytes > 8192 ||
            string.IsNullOrWhiteSpace(Folder) || !Path.IsPathFullyQualified(Folder))
            throw new ArgumentException("Check resolution, bitrate, replay limits and absolute output folder.");
    }
}
public sealed record StudioCollection
{
    public int Version { get; init; } = StudioProtocol.Version;
    public string ProgramScene { get; set; } = "";
    public StudioOutputSettings Output { get; set; } = new();
    public List<StudioScene> Scenes { get; init; } = [];
}
public sealed record StudioSnapshot
{
    public bool RecordingStarting { get; init; }
    public bool StreamingStarting { get; init; }
    public bool OutputStopping { get; init; }
    public bool Connected { get; init; }
    public bool Busy { get; init; }
    public bool Recording { get; init; }
    public bool Paused { get; init; }
    public bool Streaming { get; init; }
    public bool Replay { get; init; }
    public long RecordingMilliseconds { get; init; }
    public long RecordingBytes { get; init; }
    public int DroppedFrames { get; init; }
    public double? FreeDiskGiB { get; init; }
    public string Message { get; init; } = "Background studio is disconnected.";
    public string LastSavedFile { get; init; } = "";
    public string LastReplayFile { get; init; } = "";
    public string RuntimeVersion { get; init; } = "";
    public string ProgramScene { get; init; } = "";
    public StudioOutputSettings Output { get; init; } = new();
    public StudioScene[] Scenes { get; init; } = [];
    public StudioType[] Inputs { get; init; } = [];
    public StudioType[] Filters { get; init; } = [];
    public StudioType[] Encoders { get; init; } = [];
}
