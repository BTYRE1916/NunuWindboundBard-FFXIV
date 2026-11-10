using System;
using System.Collections.Generic;
using System.Linq;
using NunuWindboundBard.Core;

static void Check(bool cond, string text)
{
    if (!cond) throw new Exception("v0.38 smoke test failed: " + text);
}

var song = new MidiSong("lesson.mid", new MidiNote[] {
    new(0, 1000, 60, 100, 0, 0, 300),
    new(0, 1000, 64, 100, 0, 0, 300),
    new(20, 1700, 67, 80, 0, 0, 150),
    new(25, 1900, 45, 80, 1, 0, 200),
    new(0, 1100, 38, 90, 2, 9, 100),
    new(0, 1200, 115, 90, 0, 0, 150),
}, new MidiTrack[] { new(0,"Piano",4,false),new(1,"Bass",1,false),new(2,"Drums",1,true) }, 3000, 100);
var engine = new PianoAcademyEngine();
engine.Load(song, 0);
Check(engine.Notes.Count == 3, "exclude percussion and notes outside 88-key piano");
Check(engine.SkippedOutOfRange == 1, "out-of-range indicator");
engine.Play(); engine.Advance(1000);
Check(Math.Abs(engine.PositionMs - 250) < .001, "framework tick capped against lost frames");
for (int i = 0; i < 4; i++) engine.Advance(250);
Check(Math.Abs(engine.PositionMs - 1000) < .01, "wait mode reaches first chord");
engine.NoteOn(60,100);
Check(engine.Hits == 0, "wait mode requires the complete chord");
engine.NoteOn(64,100);
Check(engine.Hits == 2, "both chord pitches accepted");
for (int i = 0; i < 4; i++) engine.Advance(250);
Check(Math.Abs(engine.PositionMs - 1700) < .01, "wait mode stops at next chord");
engine.NoteOn(67,100);
Check(engine.Finished && engine.Hits == 3, "wait completion");
engine.Load(song,0,PianoPracticeHand.UpperNotes,splitPitch:64);
Check(engine.Notes.Count == 2, "higher notes only");
engine.Load(song,0);
engine.Mode = PianoPracticeMode.PlayAlong;
engine.Play();
for (int i = 0; i < 4; i++) engine.Advance(250);
engine.NoteOn(60,100);
Check(engine.Hits == 1, "play along matched correct pitch at exact time");
engine.NoteOn(12,100);
Check(engine.WrongKeys == 1, "unexpected pitch recorded");
for (int i = 0; i < 4; i++) engine.Advance(250);
Check(engine.Misses >= 1, "unplayed notes scored as misses");

var defaults = ShowmasterCodec.Defaults();
Check(defaults.Count >= 3, "default routines");
var packed = ShowmasterCodec.Encode(defaults[0].Steps);
Check(ShowmasterCodec.TryDecode(packed, out var restored) && restored.Count == defaults[0].Steps.Count,
    "emote codebook roundtrip");
Check(!ShowmasterCodec.TryDecode("/p hello",out _), "reject arbitrary chat command");
Check(!ShowmasterCodec.TryDecode("Z04",out _), "reject unsupported emote");
string nonce="0123456789ABCDEF",tag="0123ABCD";
string cue=GamePartyCueProtocol.ShowmasterStart(nonce,tag,1794320000000L,packed);
Check(cue.Length < GamePartyCueProtocol.MaxChatCharacters,"compact party cue");
Check(GamePartyCueProtocol.TryParse(cue,out var parsed) && parsed.Kind==GamePartyCueKind.ShowmasterStart &&
    parsed.RosterTag==tag && parsed.StartUnixMs==1794320000000L && parsed.ShowmasterSteps==packed,
    "group stage cue parsed");
Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.ShowmasterStop(nonce),out var stop) &&
    stop.Kind==GamePartyCueKind.ShowmasterStop,"stop cue parsed");
Check(!GamePartyCueProtocol.TryParse(cue.Replace(packed,"BZZ"),out _),"reject malformed show cue");
var player=new ShowmasterPlayer();
var performed=new List<string>();
player.Start(restored,1000);
player.Tick(900,true,e=>{performed.Add(e);return true;});
Check(performed.Count==0, "scheduled start not premature");
player.Tick(1000,true,e=>{performed.Add(e);return true;});
Check(performed.Count==1, "first emote executed once");
player.Tick(1100,false,e=>{performed.Add(e);return true;});
Check(!player.Active && performed.Count==1, "emotes stopped when gameplay resumes");
Console.WriteLine("PASS: full-range piano, wait chords, score, emote allowlist, party cues and stop safety");
