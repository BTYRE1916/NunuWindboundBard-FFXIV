using System.Diagnostics;
using System.Globalization;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading.Channels;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;

internal sealed record StudioFrame(byte[] Bgra,byte[][] Audio);

// Our output transport and timeline. The codec worker receives only our composed
// pixels and mixed PCM, and owns neither scenes nor capture sources.
internal sealed class OutputPipeline : IDisposable
{
    private sealed class AudioPipe : IDisposable
    {
        private readonly NamedPipeServerStream? named;
        private int descriptor = -1;
        private readonly bool unix;
        public string Path {get;}
        private readonly Task connected;
        [DllImport("libc",SetLastError=true)]private static extern int mkfifo(string path,uint mode);
        [DllImport("libc",SetLastError=true)]private static extern int open(string path,int flags);
        [DllImport("libc",SetLastError=true)]private static extern unsafe nint write(int descriptor,byte* data,nuint size);
        [DllImport("libc")]private static extern int close(int descriptor);
        private static unsafe nint Write(int fd,byte[] bytes,int offset){fixed(byte* data=bytes)return write(fd,data+offset,(nuint)(bytes.Length-offset));}
        public AudioPipe(string folder,int index,CancellationToken token)
        {
            string name="WindboundAudio-"+Guid.NewGuid().ToString("N")+"-"+index;
            if(OperatingSystem.IsWindows()){
                named=new NamedPipeServerStream(name,PipeDirection.Out,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,0,65536);
                Path="\\\\.\\pipe\\"+name;connected=named.WaitForConnectionAsync(token);
            }else{
                Path=System.IO.Path.Combine(folder,name);if(mkfifo(Path,384)!=0)throw new IOException("Could not create test audio FIFO.");
                unix=true;connected=Task.CompletedTask;
            }
        }
        public async Task WriteAsync(byte[] bytes,CancellationToken token)
        {
            if(named!=null){await connected;await named.WriteAsync(bytes,token);return;}
            while(descriptor<0){token.ThrowIfCancellationRequested();descriptor=open(Path,1|2048);if(descriptor>=0)break;int code=Marshal.GetLastPInvokeError();if(code!=6)throw new IOException("FIFO connect error "+code);await Task.Delay(1,token);}
            int offset=0;while(offset<bytes.Length){token.ThrowIfCancellationRequested();long written=Write(descriptor,bytes,offset);if(written>0){offset+=(int)written;continue;}int code=Marshal.GetLastPInvokeError();if(code is not(11 or 4))throw new IOException("FIFO write error "+code);await Task.Delay(1,token);}
        }
        public void Dispose(){named?.Dispose();int fd=Interlocked.Exchange(ref descriptor,-1);if(fd>=0)close(fd);if(unix)try{File.Delete(Path);}catch{}}
    }
    private readonly FfmpegRuntime runtime;
    private readonly StudioOutputSettings settings;
    private readonly Process process;
    private readonly AudioPipe[] audio;
    private readonly CancellationTokenSource lifetime=new();
    private readonly Channel<StudioFrame> queue=Channel.CreateBounded<StudioFrame>(new BoundedChannelOptions(4){SingleReader=true,FullMode=BoundedChannelFullMode.Wait});
    private readonly Task writer,observer;
    private readonly object sync=new();
    private string error="",tail="",secret="";
    private bool stopping,complete,paused,started,disposed;
    private long frames,bytes;
    private int dropped,overflows;
    internal string Diagnostics {get{lock(sync)return tail;}}
    public string Path {get;}
    public string Kind {get;}
    public bool Starting{get{lock(sync)return !started&&!complete;}}
    public bool Active{get{lock(sync)return !complete;}}
    public bool Paused{get{lock(sync)return paused;}}
    public bool Stopping{get{lock(sync)return stopping&&!complete;}}
    public bool Successful{get{lock(sync)return complete&&error.Length==0;}}
    public string Error{get{lock(sync)return error;}}
    public long Milliseconds{get{lock(sync)return frames*1000/settings.Fps;}}
    public long Bytes{get{lock(sync)return bytes;}}
    public int Dropped{get{lock(sync)return dropped;}}
    public OutputPipeline(FfmpegRuntime runtime,StudioOutputSettings settings,string kind,string path,string streamKey="")
    {
        this.runtime=runtime;this.settings=settings;Kind=kind;Path=path;secret=streamKey;
        string temp=System.IO.Path.GetTempPath();audio=Enumerable.Range(0,kind=="stream"?1:6).Select(i=>new AudioPipe(temp,i,lifetime.Token)).ToArray();
        try{process=Process.Start(runtime.StartInfo(Arguments(settings,kind,path,audio.Select(x=>x.Path).ToArray())))??throw new InvalidOperationException("Could not start encoder.");}
        catch{foreach(var pipe in audio)pipe.Dispose();lifetime.Cancel();throw;}
        writer=Task.Run(WriteLoop);observer=Task.Run(Observe);
    }
    internal static string[] Arguments(StudioOutputSettings s,string kind,string path,string[] pipes)
    {
        var a=new List<string>{"-hide_banner","-loglevel","warning","-nostats","-stats_period","0.25","-progress","pipe:1","-n",
            "-thread_queue_size","8","-f","rawvideo","-pixel_format","bgra","-video_size",$"{s.Width}x{s.Height}","-framerate",s.Fps.ToString(CultureInfo.InvariantCulture),"-probesize","32","-analyzeduration","1","-fpsprobesize","0","-i","pipe:0"};
        foreach(string pipe in pipes)a.AddRange(new[]{"-thread_queue_size","32","-f","f32le","-ar","48000","-ac","2","-probesize","32","-analyzeduration","1","-i",pipe});
        a.AddRange(new[]{"-map","0:v:0"});for(int i=0;i<pipes.Length;i++)a.AddRange(new[]{"-map",$"{i+1}:a:0"});
        a.AddRange(new[]{"-c:v",s.Encoder,"-b:v",s.VideoBitrate+"k","-pix_fmt","yuv420p","-g",(s.Fps*2).ToString(),"-force_key_frames","expr:gte(t,n_forced*2)"});
        if(s.Encoder=="libx264")a.AddRange(new[]{"-threads:v","4","-preset","veryfast","-tune","zerolatency","-sc_threshold","0"});
        a.AddRange(new[]{"-c:a","aac","-b:a",s.AudioBitrate+"k","-ar","48000","-shortest"});
        if(kind=="stream")a.AddRange(new[]{"-f","flv",path});
        else if(kind=="replay")a.AddRange(new[]{"-f","segment","-segment_time","2","-reset_timestamps","1","-segment_format","matroska",path});
        else a.AddRange(new[]{"-f","matroska",path});
        return a.ToArray();
    }
    public void Enqueue(StudioFrame frame)
    {
        lock(sync){if(complete||stopping||paused)return;
            if(!queue.Writer.TryWrite(frame)){
                if(started)dropped++;if(started&&++overflows>settings.Fps){error="Encoder cannot keep up. Lower resolution/FPS or select a hardware encoder.";stopping=true;queue.Writer.TryComplete();}
            }else overflows=0;
        }
    }
    public void Pause(bool value){lock(sync){if(Kind!="record"||complete||stopping)throw new InvalidOperationException("Recording cannot pause now.");paused=value;}}
    public void Stop(){lock(sync){if(stopping||complete)return;stopping=true;paused=false;queue.Writer.TryComplete();}}
    private async Task WriteLoop()
    {
        try{
            await foreach(var frame in queue.Reader.ReadAllAsync(lifetime.Token)){
                var writes=new List<Task>{process.StandardInput.BaseStream.WriteAsync(frame.Bgra,lifetime.Token).AsTask()};
                for(int i=0;i<audio.Length;i++)writes.Add(audio[i].WriteAsync(frame.Audio[i],lifetime.Token));
                await Task.WhenAll(writes);
            }
        }catch(OperationCanceledException){}
        catch(Exception ex){lock(sync)if(!stopping)error="Encoder input: "+ex.Message;}
        finally{try{process.StandardInput.Close();}catch{}foreach(var pipe in audio)pipe.Dispose();}
    }
    private async Task Observe()
    {
        var errors=Task.Run(async()=>{string? line;while((line=await process.StandardError.ReadLineAsync())!=null)lock(sync){tail=tail+"\n"+Redact(line);if(tail.Length>8192)tail=tail[^8192..];}});
        try{
            string? line;while((line=await process.StandardOutput.ReadLineAsync())!=null){int split=line.IndexOf('=');if(split<0)continue;string key=line[..split],value=line[(split+1)..];
                lock(sync){if(key=="frame"&&long.TryParse(value,out long f)){frames=f;if(f>0)started=true;}if(key=="total_size"&&long.TryParse(value,out long b))bytes=b;}}
            await process.WaitForExitAsync();await errors;
            lock(sync){if(process.ExitCode!=0&&error.Length==0)error="Encoder failed ("+process.ExitCode+"). "+tail;if(!stopping&&error.Length==0)error="Encoder ended unexpectedly.";complete=true;}
        }catch(Exception ex){lock(sync){error=Redact(ex.Message);complete=true;}}
        finally{lifetime.Cancel();queue.Writer.TryComplete();}
    }
    private string Redact(string text)=>secret.Length==0?text:text.Replace(secret,"[redacted]",StringComparison.Ordinal);
    public async Task FinishAsync(TimeSpan timeout)
    {
        Stop();if(await Task.WhenAny(observer,Task.Delay(timeout))!=observer){lock(sync)error="Encoder did not finalize before the timeout.";lifetime.Cancel();try{process.Kill(true);}catch{}await observer;}
        try{await writer.WaitAsync(TimeSpan.FromSeconds(5));}catch(TimeoutException){lock(sync)if(error.Length==0)error="Encoder input worker did not stop.";}catch(OperationCanceledException){}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        if(!observer.IsCompleted){lifetime.Cancel();queue.Writer.TryComplete();try{process.Kill(true);}catch{}}
        foreach(var pipe in audio)pipe.Dispose();process.Dispose();lifetime.Dispose();
    }
}
