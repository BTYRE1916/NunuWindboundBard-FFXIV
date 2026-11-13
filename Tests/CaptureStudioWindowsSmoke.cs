using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using NunuWindboundBard.Core;
using WindboundCaptureEngine;

if (!OperatingSystem.IsWindows() || args.Length != 1) throw new ArgumentException("Run on Windows with the bundled ffmpeg.exe path as the sole argument.");
string root = Path.GetFullPath(args[0]);
string profile = Path.Combine(Path.GetTempPath(),"WindboundStudioSmoke-"+Guid.NewGuid().ToString("N"));
int com = Native.RoInitialize(1);
string sceneId, textId;
try {
    using (var engine = new StudioEngine(root,profile)) {
        StudioResponse Send(string command, object? a=null) {
            var response = engine.Execute(new() { Command=command, Args=StudioProtocol.Json(a ?? new {}) });
            if (!response.Success) throw new Exception(command+": "+response.Error);
            return response;
        }
        void Wait(Func<bool> predicate) { var timer=Stopwatch.StartNew(); while (!predicate()) { if(timer.ElapsedMilliseconds>20000) throw new TimeoutException("Native output/preview did not reach expected state."); Thread.Sleep(100); } }
        var state=Send("Status").State;
        if (state.Recording || state.Streaming || state.Replay) throw new Exception("Startup started an output.");
        Send("OutputSettings", state.Output with { Width=640, Height=360, Fps=30, VideoBitrate=1500, Encoder="libx264", Folder=Path.Combine(profile,"recordings"), ReplaySeconds=5, ReplayMegabytes=64 });
        state=Send("AddScene",new { Name="Synthetic smoke scene" }).State;
        sceneId=state.Scenes.Last().Id;
        string color="color";
        state=Send("AddSource",new { SceneId=sceneId, Name="Color fixture", Type=color, Settings=new { width=640,height=360,color=0xff123456u } }).State;
        string text="text";
        state=Send("AddSource",new { SceneId=sceneId,Name="Title fixture",Type=text,Settings=new { text="Windbound smoke test",font=new { face="Arial",size=24,flags=0 } } }).State;
        textId=state.Scenes.Last().Sources.Last().Id;
        Send("Transform",new { SourceId=textId,Transform=new StudioTransform { X=12,Y=24,CropRight=1 } });
        Send("UpdateProperties",new { SourceId=textId,FilterId="",Settings=new { text="Overlay edited from controller" } });
        Send("Visibility",new { SourceId=textId,Visible=false }); Send("Visibility",new { SourceId=textId,Visible=true });
        Send("TakeScene",new { SceneId=sceneId,Duration=100 });
        Wait(()=>Send("Preview",new { SceneId=sceneId }).Preview is { Length: StudioProtocol.PreviewWidth*StudioProtocol.PreviewHeight*4 });
        Console.WriteLine("PASS native scene composition, overlay edits and auxiliary edit preview");
        Send("StartRecord"); Wait(()=> { var s=Send("Status").State; return s.Recording && !s.RecordingStarting; });
        Thread.Sleep(1500); Send("PauseRecord"); if(!Send("Status").State.Paused) throw new Exception("Pause not acknowledged.");
        Thread.Sleep(500); Send("ResumeRecord"); Thread.Sleep(1000); Send("StopRecord"); Wait(()=>!Send("Status").State.Recording);
        string saved=Send("Status").State.LastSavedFile;
        if(!File.Exists(saved)||new FileInfo(saved).Length<1000) throw new Exception("Recording was not saved.");
        Console.WriteLine("PASS native record/pause/resume/stop: "+saved);
        Send("StartReplay"); Thread.Sleep(6500); Send("SaveReplay"); Wait(()=>File.Exists(Send("Status").State.LastReplayFile));
        Send("StopReplay"); Wait(()=>!Send("Status").State.Replay);
        var rejected=engine.Execute(new() { Command="StartStream",Args=StudioProtocol.Json(new { Confirmed=false,Server="rtmp://invalid.test/live",Key="test" }) });
        if(rejected.Success) throw new Exception("Unconfirmed broadcast accepted.");
        Console.WriteLine("PASS replay save and explicit stream confirmation guard");
    }
    using var restored = new StudioEngine(root,profile);
    var loaded=restored.Snapshot();
    if(loaded.ProgramScene!=sceneId || !loaded.Scenes.Any(s=>s.Sources.Any(x=>x.Id==textId)) || loaded.Recording || loaded.Streaming || loaded.Replay) throw new Exception("Collection restore mismatch or output auto-started.");
    Console.WriteLine("PASS native collection restore without auto-starting outputs");
    Console.WriteLine("SMOKE PASSED. Fixtures retained for playback inspection: "+profile);
} finally { if(com>=0) Native.RoUninitialize(); }
