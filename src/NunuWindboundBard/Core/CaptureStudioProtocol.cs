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
    public const int MaxPreviewBytes = 2 * 1024 * 1024;
    public const int MaxMessageBytes = 4 * 1024 * 1024;
    public static string PipeName => "WindboundBard.Studio.v2." +
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName + "\\" + Environment.UserName)))[..16];
    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    public static void ValidatePreview(StudioResponse response)
    {
        if (response.Preview is not { } bytes) return;
        if (response.PreviewWidth < 1 || response.PreviewWidth > 1920 || response.PreviewHeight < 1 || response.PreviewHeight > 1080)
            throw new InvalidDataException("Invalid preview dimensions.");
        if (response.PreviewEncoding == "rgba")
        {
            if (response.PreviewWidth != PreviewWidth || response.PreviewHeight != PreviewHeight || bytes.Length != PreviewWidth * PreviewHeight * 4)
                throw new InvalidDataException("Invalid legacy preview frame.");
            return;
        }
        if (response.PreviewEncoding != "jpeg" || bytes.Length < 4 || bytes.Length > MaxPreviewBytes ||
            bytes[0] != 0xff || bytes[1] != 0xd8 || bytes[^2] != 0xff || bytes[^1] != 0xd9)
            throw new InvalidDataException("Invalid encoded preview frame.");
        // Read the JPEG frame header before passing bytes to the image decoder.
        int offset = 2;
        while (offset + 3 < bytes.Length)
        {
            if (bytes[offset++] != 0xff) break;
            while (offset < bytes.Length && bytes[offset] == 0xff) offset++;
            if (offset >= bytes.Length) break;
            int marker = bytes[offset++];
            if (marker is 0xd9 or 0xda) break;
            if (marker is 0x01 or >= 0xd0 and <= 0xd8) continue;
            if (offset + 2 > bytes.Length) break;
            int length = bytes[offset] * 256 + bytes[offset + 1];
            if (length < 2 || offset + length > bytes.Length) break;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                if (length < 8) break;
                int height = bytes[offset + 3] * 256 + bytes[offset + 4];
                int width = bytes[offset + 5] * 256 + bytes[offset + 6];
                if (width == response.PreviewWidth && height == response.PreviewHeight) return;
                break;
            }
            offset += length;
        }
        throw new InvalidDataException("Preview JPEG dimensions do not match its frame header.");
    }
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
    public int PreviewWidth { get; init; } = StudioProtocol.PreviewWidth;
    public int PreviewHeight { get; init; } = StudioProtocol.PreviewHeight;
    public string PreviewEncoding { get; init; } = "rgba";
}
public sealed record StudioPreviewFrame(long Sequence = 0, byte[]? Bytes = null, string SceneId = "", int Width = 0, int Height = 0, string Encoding = "");
public sealed record StudioPreviewSettings
{
    public int Width { get; init; } = 1280;
    public int Height { get; init; } = 720;
    public int Fps { get; init; } = 10;
    public string Encoding { get; init; } = "jpeg";
    public static StudioPreviewSettings Legacy { get; } = new() { Width = StudioProtocol.PreviewWidth, Height = StudioProtocol.PreviewHeight, Fps = 4, Encoding = "rgba" };
    public void Validate()
    {
        if (Width < 160 || Width > 1920 || Height < 90 || Height > 1080 || Fps < 1 || Fps > 15 || Encoding is not ("jpeg" or "rgba") ||
            (Encoding == "rgba" && (Width != StudioProtocol.PreviewWidth || Height != StudioProtocol.PreviewHeight)))
            throw new ArgumentException("Choose a supported preview size, format and refresh rate.");
    }
    public (int Width, int Height) Fit(int canvasWidth, int canvasHeight)
    {
        Validate();
        if (canvasWidth < 1 || canvasHeight < 1) throw new ArgumentException("Invalid preview canvas.");
        if (Encoding == "rgba") return (Width, Height); // Existing v2 controllers expect this fixed frame.
        double scale = Math.Min(1, Math.Min((double)Width / canvasWidth, (double)Height / canvasHeight));
        return (Math.Max(1, (int)Math.Floor(canvasWidth * scale)), Math.Max(1, (int)Math.Floor(canvasHeight * scale)));
    }
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
    public string RecordingFormat { get; init; } = "mkv";
    [System.Text.Json.Serialization.JsonIgnore]
    public string FileExtension => "." + RecordingFormat;
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
            string.IsNullOrWhiteSpace(Folder) || !Path.IsPathFullyQualified(Folder) || RecordingFormat is not ("mkv" or "mp4"))
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
    public StudioType[] RecordingFormats { get; init; } = [new("mkv", "MKV")];
    public bool SupportsHdPreview { get; init; }
    public string PreviewError { get; init; } = "";
}
