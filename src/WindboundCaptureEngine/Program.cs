using System.IO.Pipes;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal static class Program
{
    [MTAThread]
    private static int Main(string[] args)
    {
        string profile=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"WindboundBard","CaptureStudioNative");
        string ffmpeg=Path.Combine(AppContext.BaseDirectory,"ffmpeg.exe");
        for(int i=0;i<args.Length;i++){
            if(args[i]=="--profile"&&i+1<args.Length)profile=Path.GetFullPath(args[++i]);
            else if(args[i]=="--ffmpeg"&&i+1<args.Length)ffmpeg=Path.GetFullPath(args[++i]);
        }
        Directory.CreateDirectory(profile);using var mutex=new Mutex(true,"Local\\"+StudioProtocol.PipeName,out bool owner);if(!owner)return 0;
        int winrt=Native.RoInitialize(1);
        try{
            if(!OperatingSystem.IsWindowsVersionAtLeast(10,0,19041))throw new PlatformNotSupportedException("Windows 10 version 2004 or newer is required.");
            using var studio=new StudioEngine(ffmpeg,profile);
            while(!studio.ShutdownRequested){
                using var pipe=new NamedPipeServerStream(StudioProtocol.PipeName,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                pipe.WaitForConnection();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try{var request=StudioProtocol.ReadAsync<StudioRequest>(pipe,timeout.Token).GetAwaiter().GetResult();var response=studio.Execute(request);StudioProtocol.WriteAsync(pipe,response,timeout.Token).GetAwaiter().GetResult();}
                catch(Exception ex)when(ex is IOException or OperationCanceledException or System.Text.Json.JsonException){}
            }
            return 0;
        }catch(Exception ex){File.AppendAllText(Path.Combine(profile,"engine-error.log"),DateTimeOffset.Now+" "+ex+Environment.NewLine);return 1;}
        finally{if(winrt>=0)Native.RoUninitialize();}
    }
}
