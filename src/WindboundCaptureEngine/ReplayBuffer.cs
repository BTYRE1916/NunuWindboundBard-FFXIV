namespace WindboundCaptureEngine;
// Bounded RAM ring of finalized two-second encoded blocks. Disk is only a staging
// area for the codec worker, never the long-term replay store.
internal sealed class ReplayBuffer : IDisposable
{
    private readonly Queue<byte[]> blocks=[];
    private readonly string folder;
    private readonly long maximumBytes;
    private readonly int maximumBlocks;
    private long bytes;
    public string Pattern=>Path.Combine(folder,"block-%09d.mkv");
    public ReplayBuffer(int seconds,int mebibytes){folder=Path.Combine(Path.GetTempPath(),"WindboundReplay-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);maximumBytes=mebibytes*1048576L;maximumBlocks=Math.Max(1,(seconds+1)/2);}
    public void Collect(bool stopped=false)
    {
        var paths=Directory.GetFiles(folder,"block-*.mkv").Order().ToArray();
        foreach(string path in paths.Take(Math.Max(0,paths.Length-(stopped?0:1)))){
            byte[] data;try{data=File.ReadAllBytes(path);File.Delete(path);}catch(IOException){continue;}
            if(data.Length==0)continue;
            blocks.Enqueue(data);bytes+=data.Length;
            while(blocks.Count>maximumBlocks||bytes>maximumBytes){bytes-=blocks.Dequeue().Length;}
        }
    }
    public byte[][] Snapshot(){Collect();return blocks.ToArray();}
    public static async Task SaveAsync(FfmpegRuntime runtime,byte[][] blocks,string destination)
    {
        if(blocks.Length==0)throw new InvalidOperationException("Replay is warming up. Allow at least four seconds before saving.");
        string temp=Path.Combine(Path.GetTempPath(),"WindboundReplaySave-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try{
            var list=new List<string>();for(int i=0;i<blocks.Length;i++){string file="clip-"+i.ToString("D5")+".mkv";await File.WriteAllBytesAsync(Path.Combine(temp,file),blocks[i]);list.Add("file '"+file+"'");}
            string concat=Path.Combine(temp,"clips.txt");await File.WriteAllLinesAsync(concat,list);
            var result=await Task.Run(()=>runtime.Run(new[]{"-hide_banner","-loglevel","error","-n","-f","concat","-safe","1","-i",concat,"-map","0","-c","copy",destination},120));
            if(result.ExitCode!=0)throw new InvalidOperationException("Replay could not be saved: "+result.Output);
        }finally{try{Directory.Delete(temp,true);}catch{}}
    }
    public void Dispose(){blocks.Clear();try{Directory.Delete(folder,true);}catch{}}
}
