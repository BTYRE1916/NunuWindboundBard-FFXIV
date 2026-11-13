using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using NunuWindboundBard.Core;
namespace WindboundCaptureEngine;
internal sealed class NativeSceneRenderer : IDisposable
{
    private sealed class Source : IDisposable
    {
        public required string Signature;
        public NativeCapture? Capture;
        public MediaVideo? Media;
        public Bitmap? Static;
        public string Error="";
        public long LastUsed;
        public nint Window;
        public Size Size=>Capture?.Size??Media?.Size??Static?.Size??Size.Empty;
        public void Dispose(){Capture?.Dispose();Media?.Dispose();Static?.Dispose();}
    }
    public int TargetFps {get;set;}=30;
    private readonly FfmpegRuntime runtime;
    private readonly Dictionary<string,Source> sources=[];
    public NativeSceneRenderer(FfmpegRuntime runtime){this.runtime=runtime;}
    private Source Ensure(StudioSource model)
    {
        string signature=model.Type+model.Settings;
        if(model.Type=="text"){
            var settings=Settings.Parse(model.Settings);if(Settings.Bool(settings,"read_from_file")){
                string path=Settings.String(settings,"text_file");try{signature+="|"+File.GetLastWriteTimeUtc(path).Ticks+"|"+new FileInfo(path).Length;}catch{}
            }
        }
        if(sources.TryGetValue(model.Id,out var source)&&source.Signature==signature){source.LastUsed=Stopwatch.GetTimestamp();if(source.Capture!=null)source.Capture.MaximumFps=TargetFps;return source;}
        source?.Dispose();source=new Source{Signature=signature,LastUsed=Stopwatch.GetTimestamp()};sources[model.Id]=source;
        if(model.Type is "desktop_audio" or "microphone")return source;
        try{
            var s=Settings.Parse(model.Settings);
            if(model.Type=="window_capture"){source.Window=Native.ResolveWindow(s);source.Capture=new NativeCapture(source.Window,false,Settings.Bool(s,"cursor")){MaximumFps=TargetFps};}
            else if(model.Type=="display_capture"){
                string monitor=Settings.String(s,"monitor");if(monitor.Length==0)monitor=Native.Displays().FirstOrDefault()?.Value.GetString()??"";
                if(!long.TryParse(monitor,out long handle))throw new InvalidOperationException("Select a display.");
                source.Capture=new NativeCapture((nint)handle,true,Settings.Bool(s,"cursor")){MaximumFps=TargetFps};
            }
            else if(model.Type=="image"){
                string file=Settings.String(s,"file");if(!File.Exists(file))throw new FileNotFoundException("Choose an image file.");
                using var image=Image.FromFile(file);source.Static=new Bitmap(image); // release the file lock immediately
            }
            else if(model.Type=="text")source.Static=TextImage(s);
            else if(model.Type=="color"){
                int width=(int)Settings.Number(s,"width",640),height=(int)Settings.Number(s,"height",360);
                source.Static=new Bitmap(width,height,PixelFormat.Format32bppArgb);using var g=Graphics.FromImage(source.Static);g.Clear(Color((uint)Settings.Number(s,"color",4278190080)));
            }
            else if(model.Type is "media" or "camera")source.Media=new MediaVideo(runtime,model.Type,s);
            else throw new ArgumentException("Unknown video source.");
        }catch(Exception ex){source.Error=ex.Message;}
        return source;
    }
    internal static Color Color(uint packed)=>System.Drawing.Color.FromArgb((int)(packed>>24),(int)(packed&255),(int)((packed>>8)&255),(int)((packed>>16)&255));
    private static Bitmap TextImage(JsonElement s)
    {
        var f=Settings.Object(s,"font");string face=Settings.String(f,"face","Segoe UI");int flags=(int)Settings.Number(f,"flags",0),padding=(int)Settings.Number(s,"padding",8);
        using var font=new Font(face,(float)Settings.Number(f,"size",72),(FontStyle)(flags&15),GraphicsUnit.Pixel);
        string text=Settings.Bool(s,"read_from_file")?File.ReadAllText(Settings.String(s,"text_file")):Settings.String(s,"text","Windbound Bard");
        if(text.Length>32768)text=text[..32768];
        using var measure=new Bitmap(1,1);using var mg=Graphics.FromImage(measure);var size=mg.MeasureString(text.Length==0?" ":text,font,7680);
        var image=new Bitmap(Math.Clamp((int)Math.Ceiling(size.Width)+padding*2,1,7680),Math.Clamp((int)Math.Ceiling(size.Height)+padding*2,1,4320),PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(image);g.Clear(Color((uint)Settings.Number(s,"background",0)));g.TextRenderingHint=TextRenderingHint.AntiAliasGridFit;
        using var brush=new SolidBrush(Color((uint)Settings.Number(s,"color",4294967295)));g.DrawString(text,font,brush,new RectangleF(padding,padding,image.Width-padding*2,image.Height-padding*2));
        return image;
    }
    public Bitmap Compose(StudioScene scene,StudioOutputSettings output)
    {
        var canvas=new Bitmap(output.Width,output.Height,PixelFormat.Format32bppArgb);
        using var g=Graphics.FromImage(canvas);g.Clear(System.Drawing.Color.Black);g.CompositingMode=CompositingMode.SourceOver;g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.PixelOffsetMode=PixelOffsetMode.HighQuality;
        foreach(var model in scene.Sources.Where(s=>s.Visible&&s.Type is not ("desktop_audio" or "microphone"))){
            var source=Ensure(model);
            try{if(source.Capture!=null)source.Capture.WithImage(image=>Draw(g,image,model,output));else if(source.Media!=null)source.Media.WithImage(image=>Draw(g,image,model,output));else if(source.Static!=null)Draw(g,source.Static,model,output);}
            catch(Exception ex){source.Error="Composition: "+ex.Message;}
        }
        return canvas;
    }
    public static void Fade(Bitmap previous,Bitmap current,float fraction)
    {
        using var g=Graphics.FromImage(previous);using var attributes=new ImageAttributes();var matrix=new ColorMatrix{Matrix33=fraction};attributes.SetColorMatrix(matrix);
        g.DrawImage(current,new Rectangle(0,0,current.Width,current.Height),0,0,current.Width,current.Height,GraphicsUnit.Pixel,attributes);
    }
    private static void Draw(Graphics g,Bitmap original,StudioSource model,StudioOutputSettings output)
    {
        Bitmap? keyed=null;Bitmap image=original;
        try{
            foreach(var key in model.Filters.Where(f=>f.Enabled&&f.Type=="chroma_key")){keyed??=new Bitmap(original);Chroma(keyed,Settings.Parse(key.Settings));image=keyed;}
            var t=model.Transform;float width=image.Width-t.CropLeft-t.CropRight,height=image.Height-t.CropTop-t.CropBottom;if(width<=0||height<=0)return;
            float sx=t.ScaleX,sy=t.ScaleY;if(t.Fit){float scale=Math.Min(output.Width/width,output.Height/height);sx=scale*Math.Sign(sx);sy=scale*Math.Sign(sy);}
            using var attributes=new ImageAttributes();var matrix=new ColorMatrix();float gamma=1;
            foreach(var filter in model.Filters.Where(f=>f.Enabled&&f.Type=="color_correction")){
                var s=Settings.Parse(filter.Settings);float b=(float)Settings.Number(s,"brightness",0),c=(float)Settings.Number(s,"contrast",1),sat=(float)Settings.Number(s,"saturation",1),opacity=(float)Settings.Number(s,"opacity",1);
                float r=.2126f*(1-sat),green=.7152f*(1-sat),blue=.0722f*(1-sat);
                matrix=Combine(matrix,new ColorMatrix(new[]{new[]{(r+sat)*c,r*c,r*c,0,0},new[]{green*c,(green+sat)*c,green*c,0,0},new[]{blue*c,blue*c,(blue+sat)*c,0,0},new[]{0f,0,0,opacity,0},new[]{b+.5f*(1-c),b+.5f*(1-c),b+.5f*(1-c),0,1}}));
                gamma*=(float)Settings.Number(s,"gamma",1);
            }
            attributes.SetColorMatrix(matrix);attributes.SetGamma(gamma);
            var state=g.Save();try{g.TranslateTransform(t.X,t.Y);g.RotateTransform(t.Rotation);g.ScaleTransform(sx,sy);
                g.DrawImage(image,new Rectangle(0,0,(int)width,(int)height),t.CropLeft,t.CropTop,width,height,GraphicsUnit.Pixel,attributes);
            }finally{g.Restore(state);}
        }finally{keyed?.Dispose();}
    }
    private static ColorMatrix Combine(ColorMatrix a,ColorMatrix b)
    {
        var result=new ColorMatrix();for(int row=0;row<5;row++)for(int column=0;column<5;column++){float sum=0;for(int k=0;k<5;k++)sum+=a[row,k]*b[k,column];result[row,column]=sum;}return result;
    }
    private static unsafe void Chroma(Bitmap image,JsonElement s)
    {
        uint color=(uint)Settings.Number(s,"key_color",4278255360);float r=color&255,green=(color>>8)&255,b=(color>>16)&255;
        float similarity=(float)Settings.Number(s,"similarity",.15)*441.673f,soft=(float)Settings.Number(s,"smoothness",.08)*441.673f;
        var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<image.Height;y++){byte* row=(byte*)data.Scan0+y*data.Stride;for(int x=0;x<image.Width;x++){byte* p=row+x*4;float dr=p[2]-r,dg=p[1]-green,db=p[0]-b,distance=MathF.Sqrt(dr*dr+dg*dg+db*db);p[3]=(byte)(p[3]*Math.Clamp((distance-similarity)/Math.Max(1,soft),0,1));}}}
        finally{image.UnlockBits(data);}
    }
    public static byte[] Bgra(Bitmap image)
    {
        byte[] bytes=new byte[image.Width*image.Height*4];var data=image.LockBits(new Rectangle(0,0,image.Width,image.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<image.Height;y++)Marshal.Copy(data.Scan0+y*data.Stride,bytes,y*image.Width*4,image.Width*4);}finally{image.UnlockBits(data);}return bytes;
    }
    public static byte[] Preview(Bitmap image)
    {
        using var scaled=new Bitmap(StudioProtocol.PreviewWidth,StudioProtocol.PreviewHeight,PixelFormat.Format32bppArgb);using(var g=Graphics.FromImage(scaled))g.DrawImage(image,0,0,scaled.Width,scaled.Height);
        var bytes=Bgra(scaled);for(int i=0;i<bytes.Length;i+=4)(bytes[i],bytes[i+2])=(bytes[i+2],bytes[i]);return bytes;
    }
    public (Size Size,string Status) Inspect(string id)
    {
        if(!sources.TryGetValue(id,out var s))return(Size.Empty,"");
        string status=s.Error.Length>0?s.Error:s.Capture?.Status??s.Media?.Status??"";
        if(s.Window!=0&&Native.IsIconic(s.Window))status="Window is minimized; Windows may stop providing new frames.";
        return(s.Size,status);
    }
    public void Reset(string id){if(sources.Remove(id,out var source))source.Dispose();}
    public void Prune(){long cutoff=Stopwatch.GetTimestamp()-Stopwatch.Frequency*5;foreach(string id in sources.Where(p=>p.Value.LastUsed<cutoff).Select(p=>p.Key).ToArray())Reset(id);}
    public void Dispose(){foreach(var source in sources.Values)source.Dispose();sources.Clear();}
    private sealed class MediaVideo : IDisposable
    {
        private readonly object sync=new();
        private readonly Process process;
        private readonly CancellationTokenSource lifetime=new();
        private Bitmap? image;
        private string error="Waiting for decoded frames.";
        public string Status{get{lock(sync)return error;}}
        public Size Size{get{lock(sync)return image?.Size??Size.Empty;}}
        public MediaVideo(FfmpegRuntime runtime,string type,JsonElement s)
        {
            int width=(int)Settings.Number(s,"width",1280),height=(int)Settings.Number(s,"height",720);
            var args=new List<string>{"-hide_banner","-loglevel","error"};
            if(type=="camera")args.AddRange(new[]{"-f","dshow","-i","video="+Settings.String(s,"device")});
            else{args.Add("-re");if(Settings.Bool(s,"loop",true))args.AddRange(new[]{"-stream_loop","-1"});args.AddRange(new[]{"-i",Settings.String(s,"file")});}
            args.AddRange(new[]{"-an","-vf",$"scale={width}:{height}:force_original_aspect_ratio=decrease,pad={width}:{height}:(ow-iw)/2:(oh-ih)/2,fps=30","-pix_fmt","bgra","-f","rawvideo","pipe:1"});
            process=Process.Start(runtime.StartInfo(args))??throw new InvalidOperationException("Video decoder did not start.");
            _=Task.Run(async()=>{string text=await process.StandardError.ReadToEndAsync();if(text.Length>0)lock(sync)error=text[..Math.Min(2048,text.Length)];});
            _=Task.Run(async()=>{byte[] bytes=new byte[width*height*4];try{while(!lifetime.IsCancellationRequested){await process.StandardOutput.BaseStream.ReadExactlyAsync(bytes,lifetime.Token);var next=new Bitmap(width,height,PixelFormat.Format32bppArgb);var data=next.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{for(int y=0;y<height;y++)Marshal.Copy(bytes,y*width*4,data.Scan0+y*data.Stride,width*4);}finally{next.UnlockBits(data);}lock(sync){var old=image;image=next;old?.Dispose();error="";}}}catch(EndOfStreamException){}catch(OperationCanceledException){}catch(Exception ex){lock(sync)error=ex.Message;}});
        }
        public void WithImage(Action<Bitmap> draw){lock(sync)if(image!=null)draw(image);}
        public void Dispose(){lifetime.Cancel();try{if(!process.HasExited)process.Kill(true);}catch{}process.Dispose();lock(sync){image?.Dispose();image=null;}}
    }
}
