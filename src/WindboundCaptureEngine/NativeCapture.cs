using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;
namespace WindboundCaptureEngine;

// Our capture driver: Windows Graphics Capture -> D3D11 texture -> compositor pixels.
// It has no OBS code, capture hooks, OBS modules, or frontend dependency.
internal sealed class NativeCapture : IDisposable
{
    private readonly object pixels=new(),gpu=new();
    private readonly ID3D11Device device;
    private readonly ID3D11DeviceContext context;
    private readonly IDirect3DDevice projectedDevice;
    private readonly GraphicsCaptureItem item;
    private readonly Direct3D11CaptureFramePool pool;
    private readonly GraphicsCaptureSession session;
    private ID3D11Texture2D? staging;
    private Bitmap? latest;
    private bool disposed;
    private long lastFrameAt;
    public int MaximumFps {get;set;} = 30;
    private Windows.Graphics.SizeInt32 poolSize;
    private string error="Waiting for captured frames.";
    public string Status {get{lock(pixels)return error;}}
    public Size Size {get{lock(pixels)return latest?.Size??Size.Empty;}}
    public NativeCapture(nint handle,bool display,bool cursor)
    {
        if(!GraphicsCaptureSession.IsSupported())throw new PlatformNotSupportedException("Windows Graphics Capture is unavailable on this system.");
        try {
        Vortice.Direct3D11.D3D11.D3D11CreateDevice(null,DriverType.Hardware,DeviceCreationFlags.BgraSupport,
            new[]{FeatureLevel.Level_11_0},out device,out context).CheckError();
        using(var multithread=context.QueryInterface<ID3D11Multithread>())multithread.SetMultithreadProtected(true);
        using var dxgi=device.QueryInterface<IDXGIDevice>();
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgi.NativePointer,out nint inspectable));
        try{projectedDevice=MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);}finally{Marshal.Release(inspectable);}
        item=CaptureItem(handle,display);poolSize=item.Size;
        pool=Direct3D11CaptureFramePool.CreateFreeThreaded(projectedDevice,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,item.Size);
        session=pool.CreateCaptureSession(item);session.IsCursorCaptureEnabled=cursor;
        item.Closed+=OnClosed;pool.FrameArrived+=OnFrame;
        session.StartCapture();
        } catch {
            session?.Dispose();pool?.Dispose();projectedDevice?.Dispose();context?.Dispose();device?.Dispose();throw;
        }
    }
    private void OnClosed(GraphicsCaptureItem _,object __){lock(pixels)error="Capture target closed. Select another window/display.";}
    private unsafe void OnFrame(Direct3D11CaptureFramePool sender,object _)
    {
        lock(gpu){
            if(disposed)return;
            try{
                Windows.Graphics.SizeInt32 size;
                using(var frame=sender.TryGetNextFrame()){
                    if(frame==null)return;
                    size=frame.ContentSize;if(size.Width<1||size.Height<1)return;
                    long now=System.Diagnostics.Stopwatch.GetTimestamp();if(now-lastFrameAt<System.Diagnostics.Stopwatch.Frequency/Math.Max(1,MaximumFps))return;lastFrameAt=now;
                    nint surface=MarshalInterface<IDirect3DSurface>.FromManaged(frame.Surface);
                    nint access=0,texture=0;
                    try{
                        Guid accessId=new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");Marshal.ThrowExceptionForHR(Marshal.QueryInterface(surface,in accessId,out access));
                        Guid textureId=typeof(ID3D11Texture2D).GUID;
                        var get=(delegate* unmanaged[Stdcall]<nint,Guid*,nint*,int>)(*(nint**)access)[3];
                        Marshal.ThrowExceptionForHR(get(access,&textureId,&texture));
                        using var source=new ID3D11Texture2D(texture);texture=0;
                        var description=source.Description;
                        if(staging==null||staging.Description.Width!=description.Width||staging.Description.Height!=description.Height){
                            staging?.Dispose();description.Usage=ResourceUsage.Staging;description.BindFlags=BindFlags.None;
                            description.CPUAccessFlags=CpuAccessFlags.Read;description.MiscFlags=ResourceOptionFlags.None;
                            staging=device.CreateTexture2D(description);
                        }
                        context.CopyResource(staging,source);
                        var mapped=context.Map(staging,0,MapMode.Read,Vortice.Direct3D11.MapFlags.None);
                        Bitmap? image=null;
                        try{
                            int width=Math.Min(size.Width,(int)description.Width),height=Math.Min(size.Height,(int)description.Height);
                            image=new Bitmap(width,height,PixelFormat.Format32bppArgb);
                            var data=image.LockBits(new Rectangle(0,0,width,height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
                            try{for(int y=0;y<height;y++)Buffer.MemoryCopy((void*)(mapped.DataPointer+y*(int)mapped.RowPitch),(void*)(data.Scan0+y*data.Stride),width*4,width*4);}
                            finally{image.UnlockBits(data);}
                            lock(pixels){var old=latest;latest=image;image=null;old?.Dispose();error="";}
                        }finally{image?.Dispose();context.Unmap(staging,0);}
                    }finally{if(texture!=0)Marshal.Release(texture);if(access!=0)Marshal.Release(access);MarshalInterface<IDirect3DSurface>.DisposeAbi(surface);}
                }
                if(poolSize.Width!=size.Width||poolSize.Height!=size.Height){poolSize=size;pool.Recreate(projectedDevice,DirectXPixelFormat.B8G8R8A8UIntNormalized,2,size);}
            }catch(Exception ex){lock(pixels)error="Windows capture: "+ex.Message;}
        }
    }
    internal void WithImage(Action<Bitmap> draw){lock(pixels)if(latest!=null)draw(latest);}
    public void Dispose()
    {
        lock(gpu){if(disposed)return;disposed=true;}
        pool.FrameArrived-=OnFrame;item.Closed-=OnClosed;session.Dispose();pool.Dispose();
        lock(gpu){staging?.Dispose();projectedDevice.Dispose();context.Dispose();device.Dispose();}
        lock(pixels){latest?.Dispose();latest=null;}
    }
    [DllImport("d3d11.dll")]private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint device,out nint projected);
    [DllImport("combase.dll",CharSet=CharSet.Unicode)]private static extern int WindowsCreateString(string text,uint length,out nint value);
    [DllImport("combase.dll")]private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")]private static extern int RoGetActivationFactory(nint name,in Guid iid,out nint factory);
    private static unsafe GraphicsCaptureItem CaptureItem(nint handle,bool display)
    {
        const string name="Windows.Graphics.Capture.GraphicsCaptureItem";
        Marshal.ThrowExceptionForHR(WindowsCreateString(name,(uint)name.Length,out nint text));nint factory=0,item=0;
        try{
            Guid factoryId=new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");Marshal.ThrowExceptionForHR(RoGetActivationFactory(text,in factoryId,out factory));
            Guid itemId=new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
            var create=(delegate* unmanaged[Stdcall]<nint,nint,Guid*,nint*,int>)(*(nint**)factory)[display?4:3];
            Marshal.ThrowExceptionForHR(create(factory,handle,&itemId,&item));
            return MarshalInspectable<GraphicsCaptureItem>.FromAbi(item);
        }finally{if(item!=0)Marshal.Release(item);if(factory!=0)Marshal.Release(factory);WindowsDeleteString(text);}
    }
}
