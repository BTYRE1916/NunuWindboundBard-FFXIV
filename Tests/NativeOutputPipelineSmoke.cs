using System.Text.RegularExpressions;
using NunuWindboundBard.Core;
using WindboundCaptureEngine;
if(args.Length!=1)throw new ArgumentException("Pass the path to ffmpeg (Windows or Linux).");
var runtime=new FfmpegRuntime(args[0]);string folder=Path.Combine(Path.GetTempPath(),"Windbound-native-output-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
var settings=new StudioOutputSettings{Width=640,Height=360,Fps=10,VideoBitrate=1000,Folder=folder};
byte[] video=new byte[640*360*4];for(int i=0;i<video.Length;i+=4){video[i]=40;video[i+1]=120;video[i+2]=200;video[i+3]=255;}
byte[][] Audio(int frame)=>Enumerable.Range(0,6).Select(bus=>{
    float[] samples=new float[4800*2];for(int n=0;n<4800;n++){float value=.1f*MathF.Sin(2*MathF.PI*(220+bus*110)*(frame*4800+n)/48000);samples[n*2]=samples[n*2+1]=value;}
    byte[] bytes=new byte[samples.Length*4];Buffer.BlockCopy(samples,0,bytes,0,bytes.Length);return bytes;
}).ToArray();
string file=Path.Combine(folder,"recording.mkv");
using(var output=new OutputPipeline(runtime,settings,"record",file)){
    for(int i=0;i<30;i++){output.Enqueue(new(video,Audio(i)));if(i==10){output.Pause(true);await Task.Delay(700);output.Pause(false);}await Task.Delay(100);}
    await output.FinishAsync(TimeSpan.FromSeconds(15));
    if(!output.Successful||output.Dropped!=0||output.Milliseconds!=3000)throw new Exception("Recording/finalization failed: "+output.Error);
    Console.WriteLine("PASS record, pause/resume and finalize 30 frames without dropping any");
}
var inspect=runtime.Run(new[]{"-hide_banner","-i",file},10).Output;
if(Regex.Matches(inspect,@"Stream #0:\d+.*Audio:").Count!=6)throw new Exception("Six audio tracks were not saved.");
var duration=Regex.Match(inspect,@"Duration: 00:00:(\d+\.\d+)");
if(!duration.Success||Math.Abs(double.Parse(duration.Groups[1].Value,System.Globalization.CultureInfo.InvariantCulture)-3)>.2)throw new Exception("Pause added wall-clock time to the recording.");
Console.WriteLine("PASS MKV contains six AAC tracks and a three-second timeline excluding pause");
using(var p=System.Diagnostics.Process.Start(runtime.StartInfo(new[]{"-hide_banner","-loglevel","error","-i",file,"-map","0:v:0","-frames:v","1","-pix_fmt","bgra","-f","rawvideo","pipe:1"}))!){
    var errors=p.StandardError.ReadToEndAsync();using var pixels=new MemoryStream();await p.StandardOutput.BaseStream.CopyToAsync(pixels);await p.WaitForExitAsync();byte[] decoded=pixels.ToArray();
    if(p.ExitCode!=0||decoded.Length!=video.Length||Math.Abs(decoded[0]-40)>6||Math.Abs(decoded[1]-120)>6||Math.Abs(decoded[2]-200)>6)throw new Exception("Decoded video differs from supplied scene pixels: "+await errors);
}
Console.WriteLine("PASS saved recording decodes to the supplied scene pixels");
using(var ring=new ReplayBuffer(4,64)){
    using(var output=new OutputPipeline(runtime,settings,"replay",ring.Pattern)){
        for(int i=0;i<70;i++){output.Enqueue(new(video,Audio(i)));ring.Collect();await Task.Delay(100);}
        await output.FinishAsync(TimeSpan.FromSeconds(15));if(!output.Successful)throw new Exception(output.Error);
    }
    ring.Collect(true);string replay=Path.Combine(folder,"replay.mkv");await ReplayBuffer.SaveAsync(runtime,ring.Snapshot(),replay);
    if(!File.Exists(replay)||new FileInfo(replay).Length<1000)throw new Exception("Replay save failed.");
    var info=runtime.Run(new[]{"-hide_banner","-i",replay},10).Output;
    if(Regex.Matches(info,@"Stream #0:\d+.*Audio:").Count!=6)throw new Exception("Replay lost audio tracks.");
    if(Directory.GetFiles(Path.GetDirectoryName(ring.Pattern)!,"block-*.mkv").Length!=0)throw new Exception("Replay retained disk staging blocks.");
}
Console.WriteLine("PASS replay RAM ring, concatenated MKV save and staging cleanup");
Console.WriteLine("NATIVE OUTPUT SMOKE PASSED. Files: "+folder);
