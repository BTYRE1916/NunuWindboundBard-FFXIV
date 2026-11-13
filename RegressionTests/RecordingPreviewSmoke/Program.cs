using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using NunuWindboundBard.Core;
using WindboundCaptureEngine;

int checks = 0;
void Check(bool condition, string description) { if (!condition) throw new Exception(description); checks++; Console.WriteLine("PASS " + description); }
void Reject(Action action, string description) {
    try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException) { Check(true, description); return; }
    throw new Exception(description);
}
var hd = new StudioPreviewSettings();
hd.Validate();
Check(hd.Fit(1920,1080) == (1280,720), "HD preview samples a 1080p canvas at 1280x720");
Check((hd with { Width=1920, Height=1080 }).Fit(3840,2160) == (1920,1080), "Full HD preview samples a 4K canvas at 1920x1080");
Check(hd.Fit(1024,768) == (960,720), "preview keeps a 4:3 canvas aspect ratio");
var wide = hd.Fit(3440,1440);
Check(wide.Width == 1280 && wide.Height < 720 && Math.Abs((double)wide.Width/wide.Height - 3440.0/1440) < .01, "ultrawide preview is not stretched into 16:9");
Check(hd.Fit(640,360) == (640,360), "preview avoids upscaling a smaller source canvas");
Check(StudioPreviewSettings.Legacy.Fit(1920,1080) == (384,216), "old controllers retain their fixed RGBA frame");
Reject(() => (hd with { Width=1921 }).Validate(), "bound preview width");
Reject(() => (hd with { Height=1081 }).Validate(), "bound preview height");
Reject(() => (hd with { Fps=16 }).Validate(), "bound preview refresh rate");
Reject(() => (hd with { Encoding="png" }).Validate(), "reject unnegotiated preview encoding");

byte[] jpeg = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"preview-fixture.jpg"));
var image = new StudioResponse { Success=true, Preview=jpeg, PreviewWidth=1280, PreviewHeight=720, PreviewEncoding="jpeg", PreviewSequence=42, PreviewSceneId="edit" };
StudioProtocol.ValidatePreview(image);
Check(true, "actual HD JPEG dimensions match the response metadata");
using (var wire = new MemoryStream()) {
    await StudioProtocol.WriteAsync(wire,image,default); Check(wire.Length < StudioProtocol.MaxMessageBytes, "encoded HD frame fits the bounded control message");
    wire.Position=0; var restored=await StudioProtocol.ReadAsync<StudioResponse>(wire,default); StudioProtocol.ValidatePreview(restored);
    Check(restored.PreviewSequence==42 && restored.PreviewWidth==1280 && restored.PreviewHeight==720 && restored.Preview!.SequenceEqual(jpeg), "HD frame and dimensions survive the framed roundtrip");
}
Reject(() => StudioProtocol.ValidatePreview(image with { PreviewWidth=1279 }), "reject mismatched JPEG header dimensions");
Reject(() => StudioProtocol.ValidatePreview(image with { PreviewEncoding="rgba" }), "encoded image cannot masquerade as raw pixels");
byte[] oversized=new byte[StudioProtocol.MaxPreviewBytes+1];
jpeg.AsSpan(0,jpeg.Length-2).CopyTo(oversized);oversized[^2]=0xff;oversized[^1]=0xd9;
Reject(() => StudioProtocol.ValidatePreview(image with { Preview=oversized }), "bound encoded preview allocation");
Reject(() => StudioProtocol.ValidatePreview(image with { Preview=jpeg[..^2] }), "reject truncated encoded preview");
var legacy = new StudioResponse { Preview=new byte[StudioProtocol.PreviewWidth*StudioProtocol.PreviewHeight*4] };
StudioProtocol.ValidatePreview(legacy);
Check(legacy.PreviewEncoding=="rgba", "old response metadata remains compatible");
Check(new StudioSnapshot().RecordingFormats.Select(f=>f.Id).SequenceEqual(["mkv"]) && !new StudioSnapshot().SupportsHdPreview, "old engine capabilities never advertise unsupported MP4 or HD");

string folder=Path.Combine(Path.GetTempPath(),"Windbound-format-preview-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
var output=new StudioOutputSettings { Width=320, Height=240, Fps=10, VideoBitrate=1000, Folder=folder };
Check(output.RecordingFormat=="mkv" && output.FileExtension==".mkv", "existing output settings retain MKV by default");
var mp4=output with { RecordingFormat="mp4" }; mp4.Validate();
Check(mp4.FileExtension==".mp4", "MP4 selection provides the matching file extension");
Reject(() => (output with { RecordingFormat="avi" }).Validate(), "unsupported recording formats are rejected");
var prior=JsonSerializer.Deserialize<StudioOutputSettings>("{\"Width\":320,\"Height\":240,\"Fps\":10}")!;
Check(prior.RecordingFormat=="mkv", "older JSON without the new option remains MKV");
string collectionFile=Path.Combine(folder,"collection.json");
StudioCollectionStore.Save(collectionFile,new StudioCollection { Output=mp4 });
Check(StudioCollectionStore.Load(collectionFile).Output.RecordingFormat=="mp4", "recording format persists in the studio collection");
var streamArgs=OutputPipeline.Arguments(mp4,"stream","rtmp://example.invalid/live/test",["audio"]);
Check(streamArgs[^2]=="flv" && !streamArgs.Contains("+faststart"), "MP4 recording preference does not change RTMP streaming");
var replayArgs=OutputPipeline.Arguments(mp4,"replay","block-%09d.mkv",["audio"]);
Check(replayArgs.Contains("matroska") && replayArgs.Contains("-segment_time"), "replay staging remains bounded MKV segments");
if (args.Length==0) { Console.WriteLine($"ALL {checks} FORMAT/PREVIEW CHECKS PASSED. Pass ffmpeg path for real container/replay checks."); return; }

var runtime=new FfmpegRuntime(args[0]);
const int frames=20, samplesPerFrame=4800;
byte[] video=new byte[320*240*4]; for(int i=0;i<video.Length;i+=4){video[i]=40;video[i+1]=120;video[i+2]=200;video[i+3]=255;}
string[] audioFiles=Enumerable.Range(0,6).Select(i=>Path.Combine(folder,"audio-"+i+".f32")).ToArray();
for(int bus=0;bus<6;bus++) {
    float[] samples=new float[frames*samplesPerFrame*2];
    for(int n=0;n<samples.Length/2;n++) samples[n*2]=samples[n*2+1]=.1f*MathF.Sin(2*MathF.PI*(220+bus*110)*n/48000);
    byte[] data=new byte[samples.Length*4]; Buffer.BlockCopy(samples,0,data,0,data.Length); await File.WriteAllBytesAsync(audioFiles[bus],data);
}
async Task Encode(StudioOutputSettings settings,string file) {
    var arguments=OutputPipeline.Arguments(settings,"record",file,audioFiles);
    using var process=Process.Start(runtime.StartInfo(arguments))!;
    var stdout=process.StandardOutput.ReadToEndAsync(); var stderr=process.StandardError.ReadToEndAsync();
    try {
        using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));
        for(int n=0;n<frames;n++) await process.StandardInput.BaseStream.WriteAsync(video,deadline.Token);
        process.StandardInput.Close(); await process.WaitForExitAsync(deadline.Token);
        string error=await stderr; await stdout;
        if(process.ExitCode!=0)throw new Exception("Codec: "+error);
    } catch { try { process.Kill(true); } catch { } throw; }
}
void Inspect(string file,int expectedFrames) {
    var info=runtime.Run(["-hide_banner","-i",file],10).Output;
    Check(info.Contains("Video: h264") && Regex.Matches(info,@"Stream #0:\d+.*Audio: aac").Count==6, Path.GetExtension(file)+" contains H.264 video and all six AAC tracks");
    var decode=runtime.Run(["-hide_banner","-loglevel","error","-nostats","-progress","pipe:1","-i",file,"-map","0:v:0","-an","-f","null","-"],20);
    var counts=Regex.Matches(decode.Output,@"(?m)^frame=(\d+)");
    Check(decode.ExitCode==0 && counts.Count>0 && int.Parse(counts[^1].Groups[1].Value)==expectedFrames, Path.GetExtension(file)+" decodes all supplied frames");
}
string mkvFile=Path.Combine(folder,"recording.mkv"), mp4File=Path.Combine(folder,"recording.mp4");
await Encode(output,mkvFile); Inspect(mkvFile,frames);
await Encode(mp4,mp4File); Inspect(mp4File,frames);
void CheckFastStart(string file) {
    byte[] data=File.ReadAllBytes(file);long moov=-1,mdat=-1;
    for(long offset=0;offset+8<=data.Length;) {
        long size=BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan((int)offset,4));
        string type=System.Text.Encoding.ASCII.GetString(data,(int)offset+4,4);
        if(size==1) size=(long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan((int)offset+8,8));
        if(size==0)size=data.Length-offset;
        if(size<8||offset+size>data.Length)throw new Exception("Invalid MP4 atom.");
        if(type=="moov")moov=offset;if(type=="mdat")mdat=offset;offset+=size;
    }
    Check(moov>=0 && mdat>=0 && moov<mdat, "MP4 faststart metadata precedes video data");
}
CheckFastStart(mp4File);
string replayFile=Path.Combine(folder,"replay.mp4");
await ReplayBuffer.SaveAsync(runtime,[await File.ReadAllBytesAsync(mkvFile),await File.ReadAllBytesAsync(mkvFile)],replayFile);
Inspect(replayFile,frames*2); CheckFastStart(replayFile);
Check(true, "MKV replay blocks remux to MP4 without re-encoding");
foreach(string file in audioFiles) File.Delete(file);
Console.WriteLine($"ALL {checks} FORMAT/PREVIEW AND REAL CODEC CHECKS PASSED. Fixtures: {folder}");
Console.WriteLine("The codec checks use finite audio files with the production muxer arguments; Windows live capture/pipe and JPEG rendering tests are separate.");
