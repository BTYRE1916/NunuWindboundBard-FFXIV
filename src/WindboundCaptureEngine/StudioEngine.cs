using System.Diagnostics;
using System.Drawing;
using System.Text.Json;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;

// Windbound owns the scene graph, capture sessions, composition, audio routing,
// recording timeline, replay ring and controller state. No OBS runtime is loaded.
internal sealed class StudioEngine : IDisposable
{
    private readonly object gate=new();
    private readonly string profile;
    private readonly FfmpegRuntime runtime;
    private readonly SourceCatalog catalog;
    private readonly NativeSceneRenderer renderer;
    private readonly NativeAudioMixer mixer;
    private readonly Thread renderThread;
    private StudioCollection collection;
    private OutputPipeline? record,stream,replay;
    private ReplayBuffer? replayRing;
    private readonly HashSet<OutputPipeline> reported=[];
    private Task? replaySave;
    private string message="Native studio ready. Outputs start only from the plugin.",lastSaved="",lastReplay="",renderError="";
    private string previousScene="",previewScene="";
    private long transitionStart,previewRequested,previewSequence,previewGenerated,replayCollected;
    private int transitionDuration;
    private byte[]? preview;
    private StudioPreviewSettings previewSettings=StudioPreviewSettings.Legacy;
    private Task? previewEncoderTask;
    private int previewGeneration,previewWidth=StudioProtocol.PreviewWidth,previewHeight=StudioProtocol.PreviewHeight;
    private string previewEncoding="rgba",previewError="";
    private volatile bool stop;
    private bool disposed;
    public bool ShutdownRequested {get;private set;}
    public StudioEngine(string ffmpegPath,string profileDirectory)
    {
        profile=profileDirectory;Directory.CreateDirectory(profile);
        runtime=new FfmpegRuntime(ffmpegPath);catalog=new SourceCatalog(runtime);
        collection=StudioCollectionStore.Load(Path.Combine(profile,"studio.json"));collection.Output.Validate();
        if(!runtime.Encoders.Any(e=>e.Id==collection.Output.Encoder))collection.Output=collection.Output with{Encoder=runtime.Encoders[0].Id};
        if(collection.Scenes.Count==0)collection.Scenes.Add(new StudioScene());
        if(!collection.Scenes.Any(s=>s.Id==collection.ProgramScene))collection.ProgramScene=collection.Scenes[0].Id;
        foreach(var scene in collection.Scenes)foreach(var source in scene.Sources){
            if(!SourceCatalog.Inputs.Any(t=>t.Id==source.Type))throw new InvalidDataException("This collection contains an unsupported source type: "+source.Type);
            source.Transform.Validate();
        }
        renderer=new NativeSceneRenderer(runtime);mixer=new NativeAudioMixer(runtime);
        // Camera discovery runs before any output exists, never in the render loop.
        try{catalog.Source("camera","{}");}catch{}
        Save();
        renderThread=new Thread(RenderLoop){IsBackground=true,Name="Windbound native compositor"};renderThread.SetApartmentState(ApartmentState.MTA);renderThread.Start();
    }
    private bool AnyOutput=>record?.Active==true||stream?.Active==true||replay?.Active==true;
    private bool SavingReplay=>replaySave is{IsCompleted:false};
    private void Save()=>StudioCollectionStore.Save(Path.Combine(profile,"studio.json"),collection);
    private StudioScene Scene(string id)=>collection.Scenes.FirstOrDefault(s=>s.Id==id)??throw new ArgumentException("Select an existing scene.");
    private (StudioScene Scene,StudioSource Source,int Index) Source(string id)
    {
        foreach(var scene in collection.Scenes){int i=scene.Sources.FindIndex(s=>s.Id==id);if(i>=0)return(scene,scene.Sources[i],i);}
        throw new ArgumentException("Select an existing source.");
    }
    private static string Name(string name)=>!string.IsNullOrWhiteSpace(name)&&name.Length<=128?name.Trim():throw new ArgumentException("Use a name between 1 and 128 characters.");
    private void ResetSource(string id){renderer.Reset(id);mixer.Reset(id);}
    public StudioResponse Execute(StudioRequest request)
    {
        lock(gate){
            StudioInspector? inspection=null;byte[]? pixels=null;long sequence=0;string previewId="";
            try{
                if(request.Version!=StudioProtocol.Version)throw new InvalidOperationException("Controller/engine protocol mismatch. Update both together.");
                var a=request.Args;string Id(string key)=>a.GetProperty(key).GetString()??"";
                switch(request.Command){
                    case "Status":break;
                    case "Preview":{
                        string selected=a.TryGetProperty("SceneId",out var scene)?scene.GetString()??"":"";if(selected.Length>0)Scene(selected);
                        var options=a.TryGetProperty("Options",out var requested)&&requested.ValueKind==JsonValueKind.Object
                            ? requested.Deserialize<StudioPreviewSettings>()??throw new ArgumentException("Invalid preview options.")
                            : StudioPreviewSettings.Legacy;
                        options.Validate();
                        if(previewScene!=selected||previewSettings!=options){previewScene=selected;previewSettings=options;preview=null;previewGenerated=0;previewGeneration++;}
                        previewRequested=Stopwatch.GetTimestamp();pixels=preview;sequence=previewSequence;previewId=selected;break;
                    }
                    case "AddScene":collection.Scenes.Add(new StudioScene{Name=Name(Id("Name"))});break;
                    case "RenameScene":{var s=Scene(Id("SceneId"));int i=collection.Scenes.IndexOf(s);collection.Scenes[i]=s with{Name=Name(Id("Name"))};break;}
                    case "RemoveScene":{
                        var s=Scene(Id("SceneId"));if(collection.Scenes.Count<2||s.Id==collection.ProgramScene)throw new InvalidOperationException("Keep one scene and take another before removing Program.");
                        if(previousScene==s.Id&&transitionDuration>0)throw new InvalidOperationException("Wait for the transition to finish.");
                        foreach(var source in s.Sources)ResetSource(source.Id);collection.Scenes.Remove(s);break;
                    }
                    case "TakeScene":{
                        var next=Scene(Id("SceneId"));previousScene=collection.ProgramScene;collection.ProgramScene=next.Id;
                        transitionDuration=previousScene==next.Id?0:Math.Clamp(a.GetProperty("Duration").GetInt32(),0,5000);transitionStart=Stopwatch.GetTimestamp();break;
                    }
                    case "AddSource":{
                        var scene=Scene(Id("SceneId"));string type=Id("Type");if(!SourceCatalog.Inputs.Any(t=>t.Id==type))throw new ArgumentException("Unavailable native source type.");
                        string settings=a.TryGetProperty("Settings",out var data)?data.GetRawText():"{}";
                        if(a.TryGetProperty("GameProcessId",out var pid))settings=JsonSerializer.Serialize(Native.WindowIdentity(Native.WindowForProcess(pid.GetInt32())));
                        var source=new StudioSource{Name=Name(Id("Name")),Type=type,Settings=settings,Audio=SourceCatalog.Audio(type,settings),Transform=new StudioTransform{Fit=type is "window_capture" or "display_capture" or "camera"}};
                        scene.Sources.Add(source);inspection=Inspect(source,"");break;
                    }
                    case "RemoveSource":{var(s,source,index)=Source(Id("SourceId"));s.Sources.RemoveAt(index);ResetSource(source.Id);break;}
                    case "RenameSource":{var(s,source,index)=Source(Id("SourceId"));s.Sources[index]=source with{Name=Name(Id("Name"))};break;}
                    case "Layer":{var(s,source,index)=Source(Id("SourceId"));int next=Math.Clamp(index+a.GetProperty("Delta").GetInt32(),0,s.Sources.Count-1);s.Sources.RemoveAt(index);s.Sources.Insert(next,source);break;}
                    case "Visibility":{var(s,source,index)=Source(Id("SourceId"));s.Sources[index]=source with{Visible=a.GetProperty("Visible").GetBoolean()};break;}
                    case "Transform":{
                        var(s,source,index)=Source(Id("SourceId"));var t=a.GetProperty("Transform").Deserialize<StudioTransform>()!;t.Validate();
                        if(source.Transform.Locked&&t.Locked)throw new InvalidOperationException("Unlock the layer before changing its transform.");s.Sources[index]=source with{Transform=t};break;
                    }
                    case "Audio":{
                        var(s,source,index)=Source(Id("SourceId"));float volume=a.GetProperty("Volume").GetSingle();long offset=a.GetProperty("SyncOffsetMs").GetInt64();int monitor=a.GetProperty("Monitor").GetInt32();
                        if(!float.IsFinite(volume)||volume<0||volume>4||offset<0||offset>20000||monitor<0||monitor>2)throw new ArgumentException("Use gain 0–4, delay 0–20000 ms and a valid monitoring mode.");
                        s.Sources[index]=source with{Volume=volume,Muted=a.GetProperty("Muted").GetBoolean(),SyncOffsetMs=offset,Monitor=monitor,Tracks=a.GetProperty("Tracks").GetUInt32()&63};break;
                    }
                    case "Inspect":{var(_,source,_)=Source(Id("SourceId"));inspection=Inspect(source,Id("FilterId"));break;}
                    case "UpdateProperties":{
                        var(s,source,index)=Source(Id("SourceId"));string fid=Id("FilterId");
                        if(fid.Length==0){string settings=catalog.Update(source.Type,source.Settings,a.GetProperty("Settings"));source=source with{Settings=settings,Audio=SourceCatalog.Audio(source.Type,settings)};s.Sources[index]=source;ResetSource(source.Id);}
                        else{int f=source.Filters.FindIndex(x=>x.Id==fid);if(f<0)throw new ArgumentException("Filter no longer exists.");var old=source.Filters[f];source.Filters[f]=old with{Settings=catalog.Update(old.Type,old.Settings,a.GetProperty("Settings"),true)};}
                        inspection=Inspect(source,fid);break;
                    }
                    case "AddFilter":{
                        var(_,source,_)=Source(Id("SourceId"));string type=Id("Type");if(!SourceCatalog.Filters.Any(f=>f.Id==type))throw new ArgumentException("Unavailable filter.");
                        var f=new StudioFilter{Name=Name(Id("Name")),Type=type};source.Filters.Add(f);inspection=Inspect(source,f.Id);break;
                    }
                    case "RemoveFilter":{var(_,source,_)=Source(Id("SourceId"));source.Filters.RemoveAll(f=>f.Id==Id("FilterId"));break;}
                    case "FilterEnabled":{var(_,source,_)=Source(Id("SourceId"));int i=source.Filters.FindIndex(f=>f.Id==Id("FilterId"));source.Filters[i]=source.Filters[i] with{Enabled=a.GetProperty("Enabled").GetBoolean()};break;}
                    case "FilterOrder":{var(_,source,_)=Source(Id("SourceId"));int i=source.Filters.FindIndex(f=>f.Id==Id("FilterId"));var filter=source.Filters[i];int next=Math.Clamp(i+a.GetProperty("Delta").GetInt32(),0,source.Filters.Count-1);source.Filters.RemoveAt(i);source.Filters.Insert(next,filter);break;}
                    case "OutputSettings":{
                        if(AnyOutput||SavingReplay)throw new InvalidOperationException("Stop all outputs before changing video/encoding settings.");
                        var settings=a.Deserialize<StudioOutputSettings>()!;settings.Validate();if(!runtime.Encoders.Any(e=>e.Id==settings.Encoder))throw new ArgumentException("Select an available H.264 encoder.");collection.Output=settings;break;
                    }
                    case "StartRecord":if(record?.Active!=true){record?.Dispose();record=Start("record",null);}break;
                    case "PauseRecord":(record??throw new InvalidOperationException("Start recording first.")).Pause(true);break;
                    case "ResumeRecord":(record??throw new InvalidOperationException("Start recording first.")).Pause(false);break;
                    case "StopRecord":record?.Stop();break;
                    case "StartStream":{
                        if(!a.GetProperty("Confirmed").GetBoolean())throw new InvalidOperationException("Confirm broadcasting in the plugin first.");
                        if(stream?.Active!=true){stream?.Dispose();stream=Start("stream",a);}break;
                    }
                    case "StopStream":stream?.Stop();break;
                    case "StartReplay":if(replay?.Active!=true){replay?.Dispose();replayRing?.Dispose();replayRing=new ReplayBuffer(collection.Output.ReplaySeconds,collection.Output.ReplayMegabytes);replay=Start("replay",null);}break;
                    case "StopReplay":replay?.Stop();break;
                    case "SaveReplay":{
                        if(replay?.Active!=true||replayRing==null)throw new InvalidOperationException("Start replay buffer first.");if(SavingReplay)throw new InvalidOperationException("A replay save is already running.");
                        var blocks=replayRing.Snapshot();string path=NewFile("Replay");message="Saving replay...";
                        replaySave=Task.Run(async()=>{try{await ReplayBuffer.SaveAsync(runtime,blocks,path);lock(gate){lastReplay=path;message="Replay saved.";}}catch(Exception ex){lock(gate)message="Replay save failed: "+ex.Message;}});break;
                    }
                    case "Shutdown":if(AnyOutput||SavingReplay)throw new InvalidOperationException("Stop all outputs before shutting down.");ShutdownRequested=true;break;
                    default:throw new ArgumentException("Unknown native studio command.");
                }
                if(request.Command is not("Status" or "Preview" or "Inspect" or "StartRecord" or "PauseRecord" or "ResumeRecord" or "StopRecord" or "StartStream" or "StopStream" or "StartReplay" or "StopReplay" or "SaveReplay" or "Shutdown"))Save();
                return new StudioResponse{Id=request.Id,Success=true,State=SnapshotLocked(),Inspector=inspection,Preview=pixels,PreviewSequence=sequence,PreviewSceneId=previewId,
                    PreviewWidth=previewWidth,PreviewHeight=previewHeight,PreviewEncoding=previewEncoding};
            }catch(Exception ex){return new StudioResponse{Id=request.Id,Error=ex.Message,State=SnapshotLocked()};}
        }
    }
    private StudioInspector Inspect(StudioSource source,string filterId)
    {
        if(filterId.Length==0)return new(source.Id,"",catalog.Source(source.Type,source.Settings));
        var f=source.Filters.Single(x=>x.Id==filterId);return new(source.Id,filterId,SourceCatalog.Filter(f.Type,f.Settings));
    }
    private string NewFile(string prefix){Directory.CreateDirectory(collection.Output.Folder);return Path.Combine(collection.Output.Folder,prefix+"-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+collection.Output.FileExtension);}
    private OutputPipeline Start(string kind,JsonElement? args)
    {
        collection.Output.Validate();if(!AnyOutput)mixer.ResetTiming();string path,secret="";
        if(kind=="stream"){
            var a=args!.Value;string server=a.GetProperty("Server").GetString()??"";secret=a.GetProperty("Key").GetString()??"";
            if(!Uri.TryCreate(server,UriKind.Absolute,out var uri)||uri.Scheme is not("rtmp" or "rtmps")||secret.Length==0||secret.Any(char.IsControl))throw new ArgumentException("Enter an RTMP/RTMPS server and stream key.");
            path=server.TrimEnd('/')+"/"+secret;
        }else path=kind=="replay"?replayRing!.Pattern:NewFile("Concert");
        renderError="";var output=new OutputPipeline(runtime,collection.Output,kind,path,secret);message=kind+" output is starting.";return output;
    }
    private void UpdateOutputs()
    {
        foreach(var output in new[]{record,stream,replay}){
            if(output==null||output.Active||!reported.Add(output))continue;
            if(output.Successful){message=output.Kind+" output finalized.";if(output==record&&File.Exists(output.Path)&&new FileInfo(output.Path).Length>0)lastSaved=output.Path;}
            else message=output.Error;
            if(renderError.Length>0)message=renderError+" "+message;
            if(output==replay){replayRing?.Dispose();replayRing=null;}
        }
    }
    public StudioSnapshot Snapshot(){lock(gate)return SnapshotLocked();}
    private StudioSnapshot SnapshotLocked()
    {
        UpdateOutputs();double? free=null;try{free=new DriveInfo(Path.GetPathRoot(collection.Output.Folder)!).AvailableFreeSpace/1073741824.0;}catch{}
        return new StudioSnapshot{Connected=true,Busy=SavingReplay,Recording=record?.Active==true,RecordingStarting=record?.Starting==true,Paused=record?.Paused==true,
            Streaming=stream?.Active==true&&stream?.Starting!=true,StreamingStarting=stream?.Starting==true,Replay=replay?.Active==true,OutputStopping=new[]{record,stream,replay}.Any(o=>o?.Stopping==true),
            RecordingMilliseconds=record?.Milliseconds??0,RecordingBytes=record?.Bytes??0,DroppedFrames=(stream?.Dropped??0)+(record?.Dropped??0),FreeDiskGiB=free,
            Message=message,RuntimeVersion="Windbound native 0.42.4 · Windows Graphics Capture / D3D11 / WASAPI",LastSavedFile=lastSaved,LastReplayFile=lastReplay,ProgramScene=collection.ProgramScene,Output=collection.Output,
            RecordingFormats=[new("mkv","MKV"),new("mp4","MP4")],SupportsHdPreview=true,PreviewError=previewError,
            Scenes=collection.Scenes.Select(s=>s with{Sources=s.Sources.Select(source=>{var info=renderer.Inspect(source.Id);string audio=mixer.Status(source.Id);return source with{Width=info.Size.Width,Height=info.Size.Height,PeakDb=mixer.Peak(source.Id),Status=string.Join(" ",new[]{info.Status,audio}.Where(x=>x.Length>0))};}).ToList()}).ToArray(),Inputs=SourceCatalog.Inputs,Filters=SourceCatalog.Filters,Encoders=runtime.Encoders};
    }
    private void RenderLoop()
    {
        int winrt=Native.RoInitialize(1);var clock=Stopwatch.StartNew();long index=0;int activeFps=0;bool idle=false;
        try{
            while(!stop){
                bool work;int fps;
                lock(gate){UpdateOutputs();work=AnyOutput||Stopwatch.GetTimestamp()-previewRequested<Stopwatch.Frequency*5;fps=AnyOutput?collection.Output.Fps:previewSettings.Fps;}
                if(!work){if(!idle){lock(gate){renderer.Dispose();mixer.Dispose();}idle=true;}Thread.Sleep(50);activeFps=0;continue;}
                idle=false;if(fps!=activeFps){clock.Restart();index=0;activeFps=fps;}
                long target=clock.ElapsedTicks*fps/Stopwatch.Frequency;
                if(index>target){Thread.Sleep(1);continue;}
                lock(gate){
                    try{
                        renderer.TargetFps=fps;
                        var current=Scene(collection.ProgramScene);var previous=collection.Scenes.FirstOrDefault(s=>s.Id==previousScene);
                        float fraction=transitionDuration<=0?1:Math.Clamp((Stopwatch.GetTimestamp()-transitionStart)*1000f/Stopwatch.Frequency/transitionDuration,0,1);
                        using var image=renderer.Compose(current,collection.Output);Bitmap? faded=null;
                        try{
                            if(fraction<1&&previous!=null){faded=renderer.Compose(previous,collection.Output);NativeSceneRenderer.Fade(faded,image,fraction);}
                            else{previousScene="";transitionDuration=0;}
                            Bitmap program=faded??image;byte[]? bgra=AnyOutput?NativeSceneRenderer.Bgra(program):null;
                            int catchup=(int)Math.Min(target-index+1,fps*2);
                            for(int n=0;n<catchup;n++,index++){
                                int count=(int)(((index+1)*48000/fps)-(index*48000/fps));
                                var audioSources=fraction<1&&previous!=null?previous.Sources.Select(s=>s with{Volume=s.Volume*(1-fraction)}).Concat(current.Sources.Select(s=>s with{Volume=s.Volume*fraction})):current.Sources;
                                byte[][] audio=mixer.Mix(audioSources,count);
                                if(bgra!=null){var frame=new StudioFrame(bgra,audio);record?.Enqueue(frame);stream?.Enqueue(frame);replay?.Enqueue(frame);}
                            }
                            long now=Stopwatch.GetTimestamp();
                            if(now-previewGenerated>=Stopwatch.Frequency/previewSettings.Fps&&now-previewRequested<Stopwatch.Frequency*5&&
                                (previewEncoderTask==null||previewEncoderTask.IsCompleted)){
                                try{
                                    Bitmap scaled;
                                    if(previewScene.Length==0||previewScene==collection.ProgramScene)scaled=NativeSceneRenderer.ScalePreview(program,previewSettings);
                                    else{using var edit=renderer.Compose(Scene(previewScene),collection.Output);scaled=NativeSceneRenderer.ScalePreview(edit,previewSettings);}
                                    var options=previewSettings;int expected=previewGeneration;
                                    previewGenerated=now;
                                    previewEncoderTask=Task.Run(()=>{
                                        using(scaled)try{
                                            byte[] data=NativeSceneRenderer.EncodePreview(scaled,options);
                                            lock(gate)if(!stop&&expected==previewGeneration){preview=data;previewWidth=scaled.Width;previewHeight=scaled.Height;previewEncoding=options.Encoding;previewSequence++;previewError="";}
                                        }catch(Exception ex){lock(gate)if(!stop&&expected==previewGeneration)previewError="Preview: "+ex.Message;}
                                    });
                                }catch(Exception ex){previewError="Preview: "+ex.Message;}
                            }
                            if(now-replayCollected>Stopwatch.Frequency/2){replayRing?.Collect();replayCollected=now;renderer.Prune();}
                        }finally{faded?.Dispose();}
                    }catch(Exception ex){renderError="Native render error: "+ex.Message;message=renderError;record?.Stop();stream?.Stop();replay?.Stop();index=target+1;Thread.Sleep(25);}
                }
            }
        }finally{if(winrt>=0)Native.RoUninitialize();}
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;stop=true;if(renderThread.IsAlive)renderThread.Join(5000);
        try{previewEncoderTask?.Wait(3000);}catch{}
        foreach(var output in new[]{record,stream,replay}){if(output==null)continue;try{output.FinishAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();}catch{}output.Dispose();}
        if(replaySave!=null)try{replaySave.GetAwaiter().GetResult();}catch{}
        replayRing?.Dispose();renderer.Dispose();mixer.Dispose();
    }
}
