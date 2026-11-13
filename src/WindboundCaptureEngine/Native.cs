using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal static class Native
{
    [DllImport("combase")] internal static extern int RoInitialize(uint type);
    [DllImport("combase")] internal static extern void RoUninitialize();
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsWindow(nint window);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool IsIconic(nint window);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32", CharSet=CharSet.Unicode)] private static extern int GetWindowText(nint window,StringBuilder text,int count);
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window,out uint process);
    private delegate bool WindowProc(nint window,nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumWindows(WindowProc callback,nint param);
    private delegate bool MonitorProc(nint monitor,nint dc,nint rect,nint param);
    [DllImport("user32")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplayMonitors(nint dc,nint clip,MonitorProc callback,nint param);
    internal static StudioChoice[] Windows()
    {
        var list=new List<StudioChoice>();
        EnumWindows((window,_)=>{
            if(!IsWindowVisible(window))return true;
            var title=new StringBuilder(1024);GetWindowText(window,title,title.Capacity);
            if(title.Length==0)return true;
            GetWindowThreadProcessId(window,out uint pid);
            string exe="";try{using var process=Process.GetProcessById((int)pid);exe=process.ProcessName;}catch{}
            list.Add(new(title+" ["+exe+" / "+pid+"]",StudioProtocol.Json(window.ToInt64().ToString())));return true;
        },0);
        return list.OrderBy(x=>x.Label).ToArray();
    }
    internal static StudioChoice[] Displays()
    {
        var list=new List<StudioChoice>();
        EnumDisplayMonitors(0,0,(monitor,_,_,_)=>{list.Add(new("Display "+(list.Count+1),StudioProtocol.Json(monitor.ToInt64().ToString())));return true;},0);
        return list.ToArray();
    }
    internal static nint WindowForProcess(int pid)
    {
        using var process=Process.GetProcessById(pid);
        process.Refresh();
        return process.MainWindowHandle!=0?process.MainWindowHandle:throw new InvalidOperationException("The selected process has no capture window.");
    }
    internal static Dictionary<string,object> WindowIdentity(nint window)
    {
        if(!IsWindow(window))throw new ArgumentException("The selected window is no longer available.");
        var title=new StringBuilder(1024);GetWindowText(window,title,title.Capacity);GetWindowThreadProcessId(window,out uint pid);
        using var process=Process.GetProcessById((int)pid);
        return new(){["window"]=window.ToInt64().ToString(),["window_title"]=title.ToString(),["process_name"]=process.ProcessName};
    }
    internal static nint ResolveWindow(System.Text.Json.JsonElement settings)
    {
        string title=Settings.String(settings,"window_title"),exe=Settings.String(settings,"process_name");
        if(long.TryParse(Settings.String(settings,"window"),out long handle)&&IsWindow((nint)handle)){
            var identity=WindowIdentity((nint)handle);
            if((title.Length==0||(string)identity["window_title"]==title)&&(exe.Length==0||(string)identity["process_name"]==exe))return(nint)handle;
        }
        var matches=new List<nint>();
        foreach(var choice in Windows()){
            if(!long.TryParse(choice.Value.GetString(),out handle))continue;
            var identity=WindowIdentity((nint)handle);
            if((string)identity["window_title"]==title&&(string)identity["process_name"]==exe)matches.Add((nint)handle);
        }
        if(matches.Count==1)return matches[0];
        throw new InvalidOperationException(matches.Count>1?"Several windows match this source. Select the intended window in source properties.":"Select an available window in source properties.");
    }
}
