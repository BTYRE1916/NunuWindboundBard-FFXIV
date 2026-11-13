using System.Diagnostics;
using System.Text.RegularExpressions;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal sealed class FfmpegRuntime
{
    public string Executable {get;}
    public string Version {get;}
    public StudioType[] Encoders {get;}
    public FfmpegRuntime(string executable)
    {
        Executable=Path.GetFullPath(executable);
        if(!File.Exists(Executable))throw new FileNotFoundException("The bundled codec worker is missing. Choose ffmpeg.exe in the plugin's engine setup.",Executable);
        var version=Run(new[]{"-version"},10);if(version.ExitCode!=0)throw new InvalidOperationException("Codec worker could not start.");
        Version=version.Output.Split('\n')[0].Trim();
        var list=Run(new[]{"-hide_banner","-encoders"},10);
        var names=new Dictionary<string,string>{{"libx264","Software H.264 (x264)"},{"h264_nvenc","NVIDIA H.264"},{"h264_qsv","Intel Quick Sync H.264"},{"h264_amf","AMD H.264"}};
        Encoders=names.Where(n=>Regex.IsMatch(list.Output,@"\b"+n.Key+@"\b")).Select(n=>new StudioType(n.Key,n.Value)).ToArray();
        if(Encoders.Length==0)throw new InvalidOperationException("The codec worker has no supported H.264 encoder.");
    }
    public (int ExitCode,string Output) Run(IEnumerable<string> arguments,int seconds=30)
    {
        var start=StartInfo(arguments);start.RedirectStandardInput=false;
        using var p=Process.Start(start)??throw new InvalidOperationException("Codec worker did not start.");
        var stdout=p.StandardOutput.ReadToEndAsync();var stderr=p.StandardError.ReadToEndAsync();
        if(!p.WaitForExit(seconds*1000)){p.Kill(true);throw new TimeoutException("Codec worker timed out.");}
        return(p.ExitCode,stdout.GetAwaiter().GetResult()+stderr.GetAwaiter().GetResult());
    }
    public ProcessStartInfo StartInfo(IEnumerable<string> arguments)
    {
        var start=new ProcessStartInfo(Executable){UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(string a in arguments)start.ArgumentList.Add(a);return start;
    }
    public StudioChoice[] Cameras()
    {
        if(!OperatingSystem.IsWindows())return[];
        var result=Run(new[]{"-hide_banner","-list_devices","true","-f","dshow","-i","dummy"},10);
        return Regex.Matches(result.Output,"\"([^\"]+)\" \\(video\\)").Select(m=>new StudioChoice(m.Groups[1].Value,StudioProtocol.Json(m.Groups[1].Value))).ToArray();
    }
}
