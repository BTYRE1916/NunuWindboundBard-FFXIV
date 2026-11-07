using System;
using System.Collections.Generic;
using System.IO;
using NunuWindboundBard.Core;

static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception("FAIL: " + message); }
    public static void Main()
    {
        var songs = new List<string>{"C:/a.mid", "C:/b.mid", "C:/c.mid"};
        var state = new SmartPlaylistState { Repeat = PlaylistRepeatMode.Off };
        Check(ConcertSetlist.PickNext(state, songs, 0, _=>true)==songs[1], "regular next song");
        SmartPlaylistManager.AddPlayNext(state,songs,songs[2],true);
        Check(ConcertSetlist.PickNext(state,songs,0,_=>true)==songs[2],"queue overrides list");
        Check(ConcertSetlist.PickNext(state,songs,0,p=>p==songs[1])==songs[1],"missing file skipped");
        state.PlayNext.Clear();
        SmartPlaylistManager.AddPlayNext(state,songs,songs[0],true);
        Check(ConcertSetlist.PickNext(state,songs,0,_=>true)==songs[0],"explicit queued encore may repeat current song");
        state.PlayNext.Clear();
        Check(ConcertSetlist.PickNext(state,songs,2,_=>true)==null,"repeat off ends setlist");
        state.Repeat=PlaylistRepeatMode.All;
        Check(ConcertSetlist.PickNext(state,songs,2,_=>true)==songs[0],"repeat all wraps");
        var cue=new ConcertSongCue{Announcement="Intermission",BreakAfterSeconds=15,HoldAfter=true,Encore=true};
        Check(cue.HoldAfter && cue.BreakAfterSeconds==15 && cue.Encore,"annotations");
        const string nonce="0123456789ABCDEF", hash="1234567890ABCDEF", tag="AABBCCDD";
        string tracks=GamePartyCueProtocol.NativeSetTracks(hash,nonce,tag,"AA",3);
        Check(GamePartyCueProtocol.TryParse(tracks,out var parsed) && parsed.Kind==GamePartyCueKind.NativeSetTracks && parsed.TargetSlot==3,"targeted track map");
        string inst=GamePartyCueProtocol.NativeSetInstruments(nonce,tag,"AgM",2);
        Check(GamePartyCueProtocol.TryParse(inst,out parsed) && parsed.Kind==GamePartyCueKind.NativeSetInstruments && parsed.TargetSlot==2,"targeted instrument map");
        string filename=Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".mid");
        File.WriteAllBytes(filename,new byte[]{77,84,104,100,0,0,0,6,0,0,0,1,0,96,77,84,114,107,0,0,0,4,0,255,47,0});
        try
        {
            string file=GamePartyCueProtocol.NativeFile(0,filename,nonce,tag,"AA",8);
            Check(GamePartyCueProtocol.TryParse(file,out parsed) && parsed.Kind==GamePartyCueKind.NativeFile && parsed.TargetSlot==8,"targeted MIDI file");
            Check(!GamePartyCueProtocol.TryParse(file+" t1",out _),"reject double targeting");
            Check(!GamePartyCueProtocol.TryParse(file.Replace(" t8"," t99"),out _),"invalid roster slot");
        }
        finally{File.Delete(filename);}
        Console.WriteLine("PASS: Concert Setlist selection, queue precedence, missing MIDI, repeat mode and targeted party commands.");
    }
}
