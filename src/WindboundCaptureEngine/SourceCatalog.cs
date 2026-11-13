using System.Text.Json;
using NAudio.CoreAudioApi;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal static class Settings
{
    public static JsonElement Parse(string json)=>JsonDocument.Parse(json).RootElement.Clone();
    public static string String(JsonElement s,string key,string fallback="")=>s.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
    public static double Number(JsonElement s,string key,double fallback=0)=>s.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetDouble(out double n)&&double.IsFinite(n)?n:fallback;
    public static bool Bool(JsonElement s,string key,bool fallback=false)=>s.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.True or JsonValueKind.False?v.GetBoolean():fallback;
    public static JsonElement Object(JsonElement s,string key)=>s.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Object?v:StudioProtocol.Json(new{});
    public static string Merge(string original,JsonElement patch)
    {
        var values=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(original)??[];
        foreach(var p in patch.EnumerateObject())values[p.Name]=p.Value.Clone();
        return JsonSerializer.Serialize(values);
    }
}
internal sealed class SourceCatalog(FfmpegRuntime runtime)
{
    public static readonly StudioType[] Inputs=[new("window_capture","Window capture (Windows Graphics Capture)"),new("display_capture","Display capture"),new("image","Image overlay"),new("text","Text overlay"),new("color","Color panel"),new("media","Video/media overlay"),new("camera","Camera"),new("desktop_audio","Desktop audio"),new("microphone","Microphone")];
    public static readonly StudioType[] Filters=[new("color_correction","Color correction / opacity"),new("chroma_key","Chroma key"),new("gain","Audio gain"),new("noise_gate","Audio noise gate"),new("compressor","Audio compressor"),new("limiter","Audio limiter")];
    private StudioChoice[]? cameras;
    private static StudioProperty P(string name,string label,int type,object value,double min=0,double max=0,bool multiline=false,StudioChoice[]? choices=null)=>new(){Name=name,Label=label,Type=type,Value=StudioProtocol.Json(value),Min=min,Max=max,Multiline=multiline,Choices=choices??[]};
    public StudioProperty[] Source(string type,string json)
    {
        var s=Settings.Parse(json);
        string Str(string key,string fallback="")=>Settings.String(s,key,fallback);
        double Num(string key,double fallback=0)=>Settings.Number(s,key,fallback);
        bool Bool(string key,bool fallback=false)=>Settings.Bool(s,key,fallback);
        return type switch{
            "window_capture"=>[P("window","Window",6,Str("window"),choices:Native.Windows()),P("cursor","Capture cursor",1,Bool("cursor"))],
            "display_capture"=>[P("monitor","Display",6,Str("monitor"),choices:Native.Displays()),P("cursor","Capture cursor",1,Bool("cursor"))],
            "image"=>[P("file","Image file",5,Str("file"))],
            "text"=>[P("read_from_file","Read from text file",1,Bool("read_from_file")),P("text_file","Text file",5,Str("text_file")),P("text","Text",4,Str("text","Windbound Bard"),multiline:true),P("font","Font",9,s.TryGetProperty("font",out var f)?f:StudioProtocol.Json(new{face="Segoe UI",size=72,flags=0})),P("color","Text color",13,(long)Num("color",4294967295)),P("background","Background color",13,(long)Num("background",0)),P("padding","Padding (pixels)",2,(int)Num("padding",8),0,512)],
            "color"=>[P("color","Panel color",13,(long)Num("color",4278190080)),P("width","Width",2,(int)Num("width",640),1,7680),P("height","Height",2,(int)Num("height",360),1,4320)],
            "media"=>[P("file","Media file",5,Str("file")),P("loop","Loop media",1,Bool("loop",true)),P("width","Decode width",2,(int)Num("width",1280),64,3840),P("height","Decode height",2,(int)Num("height",720),64,2160),P("audio","Include media audio",1,Bool("audio",true))],
            "camera"=>[P("device","Camera",6,Str("device"),choices:cameras??=runtime.Cameras()),P("width","Capture width",2,(int)Num("width",1280),64,3840),P("height","Capture height",2,(int)Num("height",720),64,2160)],
            "desktop_audio"=>[P("device_id","Output device",6,Str("device_id","default"),choices:Devices(DataFlow.Render))],
            "microphone"=>[P("device_id","Input device",6,Str("device_id","default"),choices:Devices(DataFlow.Capture))],
            _=>throw new ArgumentException("Unknown native source type.")
        };
    }
    public static bool Audio(string type,string json)=>type is "desktop_audio" or "microphone"||type=="media"&&Settings.Bool(Settings.Parse(json),"audio",true);
    private static StudioChoice[] Devices(DataFlow flow)
    {
        var result=new List<StudioChoice>{new("Windows default",StudioProtocol.Json("default"))};
        using var enumerator=new MMDeviceEnumerator();var devices=enumerator.EnumerateAudioEndPoints(flow,DeviceState.Active);
        foreach(var device in devices){using(device)result.Add(new(device.FriendlyName,StudioProtocol.Json(device.ID)));}
        return result.ToArray();
    }
    public static StudioProperty[] Filter(string type,string json)
    {
        var s=Settings.Parse(json);
        double N(string key,double value)=>Settings.Number(s,key,value);
        return type switch{
            "color_correction"=>[P("brightness","Brightness",3,N("brightness",0),-1,1),P("contrast","Contrast",3,N("contrast",1),0,3),P("saturation","Saturation",3,N("saturation",1),0,3),P("gamma","Gamma",3,N("gamma",1),.1,4),P("opacity","Opacity",3,N("opacity",1),0,1)],
            "chroma_key"=>[P("key_color","Key color",13,(long)N("key_color",4278255360)),P("similarity","Similarity",3,N("similarity",.15),0,1),P("smoothness","Edge softness",3,N("smoothness",.08),.001,.5)],
            "gain"=>[P("gain_db","Gain (dB)",3,N("gain_db",0),-60,12)],
            "noise_gate"=>[P("threshold_db","Gate threshold (dB)",3,N("threshold_db",-45),-80,0)],
            "compressor"=>[P("threshold_db","Threshold (dB)",3,N("threshold_db",-18),-60,0),P("ratio","Ratio",3,N("ratio",4),1,20)],
            "limiter"=>[P("ceiling_db","Ceiling (dB)",3,N("ceiling_db",-1),-20,0)],
            _=>throw new ArgumentException("Unknown native filter type.")
        };
    }
    public string Update(string type,string original,JsonElement patch,bool filter=false)
    {
        string merged=Settings.Merge(original,patch);var properties=filter?Filter(type,merged):Source(type,merged);
        foreach(var p in properties){
            if(p.Type is 2 or 3){double n=p.Value.GetDouble();if(!double.IsFinite(n)||n<p.Min||n>p.Max)throw new ArgumentException(p.Label+" is outside its supported range.");}
            if(p.Type is 4 or 5&&p.Value.GetString() is {Length:>32768})throw new ArgumentException("Source property is too long.");
        }
        if(type=="text"){
            var font=Settings.Object(Settings.Parse(merged),"font");double size=Settings.Number(font,"size",72);
            if(size<6||size>512)throw new ArgumentException("Font size must be between 6 and 512.");
        }
        if(type=="window_capture"&&patch.TryGetProperty("window",out var value)&&long.TryParse(value.GetString(),out long hwnd))
            merged=Settings.Merge(merged,StudioProtocol.Json(Native.WindowIdentity((nint)hwnd)));
        return merged;
    }
}
