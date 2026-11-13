using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal sealed class NativeAudioMixer : IDisposable
{
    private sealed class Input : IDisposable
    {
        public required string Signature;
        public WasapiCapture? Capture;
        public MMDevice? Device;
        public readonly object Sync=new();
        public BufferedWaveProvider? Buffer;
        public ISampleProvider? Samples;
        public ProcessAudio? Media;
        public readonly Queue<float[]> Delay=[];
        public int DelaySamples;
        public float Peak=-60;
        public string Error="";
        public void Dispose(){Capture?.StopRecording();Capture?.Dispose();Device?.Dispose();Media?.Dispose();}
    }
    private readonly FfmpegRuntime runtime;
    private readonly Dictionary<string,Input> inputs=[];
    private readonly Dictionary<string,float> peaks=[];
    private readonly BufferedWaveProvider monitorBuffer=new(WaveFormat.CreateIeeeFloatWaveFormat(48000,2)){BufferDuration=TimeSpan.FromSeconds(1),DiscardOnBufferOverflow=true};
    private WasapiOut? monitor;
    public NativeAudioMixer(FfmpegRuntime runtime){this.runtime=runtime;}
    public float Peak(string id)=>peaks.GetValueOrDefault(id,-60);
    public string Status(string id)=>inputs.TryGetValue(id,out var input)?input.Error:"";
    private Input Ensure(StudioSource source)
    {
        string signature=source.Type+source.Settings;
        if(inputs.TryGetValue(source.Id,out var existing)&&existing.Signature==signature)return existing;
        existing?.Dispose();var input=new Input{Signature=signature};inputs[source.Id]=input;
        try{
            var s=Settings.Parse(source.Settings);
            if(source.Type=="media")input.Media=new ProcessAudio(runtime,s);
            else{
                using var enumerator=new MMDeviceEnumerator();string id=Settings.String(s,"device_id","default");
                input.Device=id=="default"?enumerator.GetDefaultAudioEndpoint(source.Type=="desktop_audio"?DataFlow.Render:DataFlow.Capture,Role.Multimedia):enumerator.GetDevice(id);
                input.Capture=source.Type=="desktop_audio"?new WasapiLoopbackCapture(input.Device):new WasapiCapture(input.Device);
                input.Capture.WaveFormat=WaveFormat.CreateIeeeFloatWaveFormat(48000,2);
                input.Buffer=new BufferedWaveProvider(input.Capture.WaveFormat){BufferDuration=TimeSpan.FromSeconds(2),DiscardOnBufferOverflow=true,ReadFully=true};
                input.Samples=input.Buffer.ToSampleProvider();
                input.Capture.DataAvailable+=(_,a)=>{lock(input.Sync)input.Buffer.AddSamples(a.Buffer,0,a.BytesRecorded);};
                input.Capture.RecordingStopped+=(_,a)=>{if(a.Exception!=null)input.Error=a.Exception.Message;};
                input.Capture.StartRecording();
            }
        }catch(Exception ex){input.Error="Audio: "+ex.Message;}
        return input;
    }
    public byte[][] Mix(IEnumerable<StudioSource> sources,int samplesPerChannel)
    {
        int count=samplesPerChannel*2;var buses=Enumerable.Range(0,6).Select(_=>new float[count]).ToArray();var monitored=new float[count];
        var active=sources.Where(s=>s.Visible&&s.Audio).ToArray();var ids=active.Select(s=>s.Id).ToHashSet();
        foreach(string id in inputs.Keys.Where(id=>!ids.Contains(id)).ToArray()){inputs[id].Dispose();inputs.Remove(id);peaks.Remove(id);}
        bool wantsMonitor=false;
        foreach(var source in active){
            var input=Ensure(source);var data=new float[count];
            if(input.Media!=null)input.Media.Read(data);else if(input.Samples!=null)lock(input.Sync)input.Samples.Read(data,0,data.Length);
            // Positive delay is sample-accurate and independent per source. A ring
            // is reset on a delay change rather than retaining stale concert audio.
            int delay=(int)(source.SyncOffsetMs*48*2);
            if(input.DelaySamples!=delay){input.Delay.Clear();if(delay>0)input.Delay.Enqueue(new float[delay]);input.DelaySamples=delay;}
            if(delay>0){input.Delay.Enqueue(data);data=ReadDelay(input.Delay,count);}
            float peak=0;
            foreach(var filter in source.Filters.Where(f=>f.Enabled))ApplyFilter(data,filter);
            for(int n=0;n<count;n++){data[n]*=source.Muted?0:source.Volume;peak=Math.Max(peak,Math.Abs(data[n]));}
            peaks[source.Id]=peak<=0?-60:Math.Clamp(20*MathF.Log10(peak),-60,0);
            if(source.Monitor>0){wantsMonitor=true;for(int n=0;n<count;n++)monitored[n]+=data[n];}
            if(source.Monitor!=1)for(int bus=0;bus<6;bus++)if((source.Tracks&(1u<<bus))!=0)for(int n=0;n<count;n++)buses[bus][n]+=data[n];
        }
        if(wantsMonitor){
            try{monitor??=new WasapiOut(AudioClientShareMode.Shared,100);if(monitor.PlaybackState==PlaybackState.Stopped){monitor.Init(monitorBuffer);monitor.Play();}
                byte[] pcm=new byte[count*4];Buffer.BlockCopy(monitored,0,pcm,0,pcm.Length);monitorBuffer.AddSamples(pcm,0,pcm.Length);
            }catch(Exception ex){foreach(var source in active.Where(s=>s.Monitor>0))inputs[source.Id].Error="Monitoring: "+ex.Message;}
        }else{monitor?.Stop();monitor?.Dispose();monitor=null;monitorBuffer.ClearBuffer();}
        return buses.Select(bus=>{for(int i=0;i<bus.Length;i++)bus[i]=Math.Clamp(bus[i],-1,1);byte[] bytes=new byte[bus.Length*4];Buffer.BlockCopy(bus,0,bytes,0,bytes.Length);return bytes;}).ToArray();
    }
    private static float[] ReadDelay(Queue<float[]> queue,int count)
    {
        var result=new float[count];int offset=0;
        while(offset<count&&queue.Count>0){var first=queue.Dequeue();int take=Math.Min(first.Length,count-offset);Array.Copy(first,0,result,offset,take);offset+=take;if(take<first.Length){var remain=first[take..];var rest=queue.ToArray();queue.Clear();queue.Enqueue(remain);foreach(var a in rest)queue.Enqueue(a);}}
        return result;
    }
    private static void ApplyFilter(float[] data,StudioFilter f)
    {
        var s=Settings.Parse(f.Settings);float gain=(float)Math.Pow(10,Settings.Number(s,"gain_db",0)/20),threshold=(float)Math.Pow(10,Settings.Number(s,"threshold_db",f.Type=="noise_gate"?-45:-18)/20),ratio=(float)Settings.Number(s,"ratio",4),ceiling=(float)Math.Pow(10,Settings.Number(s,"ceiling_db",-1)/20);
        for(int i=0;i<data.Length;i++){float x=data[i],a=Math.Abs(x);
            data[i]=f.Type switch{"gain"=>x*gain,"noise_gate"=>a<threshold?0:x,"compressor"=>a>threshold?Math.Sign(x)*(threshold+(a-threshold)/ratio):x,"limiter"=>Math.Clamp(x,-ceiling,ceiling),_=>x};}
    }
    public void ResetTiming()
    {
        foreach(var input in inputs.Values){lock(input.Sync)input.Buffer?.ClearBuffer();input.Media?.Clear();input.Delay.Clear();input.DelaySamples=-1;}
    }
    public void Reset(string id){if(inputs.Remove(id,out var input))input.Dispose();}
    public void Dispose(){foreach(var input in inputs.Values)input.Dispose();inputs.Clear();monitor?.Dispose();monitor=null;monitorBuffer.ClearBuffer();}
    private sealed class ProcessAudio : IDisposable
    {
        private readonly System.Diagnostics.Process process;
        private readonly BufferedWaveProvider buffer=new(WaveFormat.CreateIeeeFloatWaveFormat(48000,2)){BufferDuration=TimeSpan.FromSeconds(2),ReadFully=true,DiscardOnBufferOverflow=true};
        private readonly CancellationTokenSource lifetime=new();
        private readonly object sync=new();
        public ProcessAudio(FfmpegRuntime runtime,System.Text.Json.JsonElement s){
            var args=new List<string>{"-hide_banner","-loglevel","error","-re"};if(Settings.Bool(s,"loop",true))args.AddRange(new[]{"-stream_loop","-1"});args.AddRange(new[]{"-i",Settings.String(s,"file"),"-vn","-f","f32le","-ar","48000","-ac","2","pipe:1"});
            process=System.Diagnostics.Process.Start(runtime.StartInfo(args))??throw new InvalidOperationException("Media audio decoder failed.");
            _=process.StandardError.ReadToEndAsync();_=Task.Run(async()=>{var bytes=new byte[16384];try{int n;while((n=await process.StandardOutput.BaseStream.ReadAsync(bytes,lifetime.Token))>0)lock(sync)buffer.AddSamples(bytes,0,n);}catch(OperationCanceledException){}catch(IOException){}});
        }
        public void Clear(){lock(sync)buffer.ClearBuffer();}
        public void Read(float[] samples){byte[] bytes=new byte[samples.Length*4];lock(sync)buffer.Read(bytes,0,bytes.Length);Buffer.BlockCopy(bytes,0,samples,0,bytes.Length);}
        public void Dispose(){lifetime.Cancel();try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();}
    }
}
