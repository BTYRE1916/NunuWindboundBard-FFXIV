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
// v0.38.1: full Emote-sheet IDs instead of just 12 fixed emote keywords.
var fullRoutine = Enumerable.Range(0, 12).Select(i => new ShowmasterStep {
    EmoteId = (uint)(101 + i), Emote = "", DelayAfterMs = 1250 + (i % 3) * 250
}).ToList();
var allEmoteCodes = ShowmasterCodec.EncodeV2(fullRoutine);
Check(allEmoteCodes.Length == 72, "up to 12 arbitrary native Emote-sheet IDs");
Check(ShowmasterCodec.TryDecodeV2(allEmoteCodes, out var decodedV2) && decodedV2.Count == 12 &&
    decodedV2[0].EmoteId == 101 && decodedV2[11].EmoteId == 112,
    "native ID routine roundtrip");
Check(!ShowmasterCodec.TryDecodeV2(allEmoteCodes + "000001", out _), "more than 12 emotes rejected");
Check(!ShowmasterCodec.TryDecodeV2("000000", out _), "zero Emote-sheet ID rejected");
Check(!ShowmasterCodec.TryDecodeV2("002A15", out _), "invalid delay ticks rejected");
string nativeCue = GamePartyCueProtocol.ShowmasterStartV2(nonce, tag, 1794320000000L, allEmoteCodes);
Check(nativeCue.Length <= GamePartyCueProtocol.MaxChatCharacters,
    "12-step native emote cue fits the 130-character party-chat maximum");
Check(GamePartyCueProtocol.TryParse(nativeCue, out var parsedV2) &&
    parsedV2.Kind == GamePartyCueKind.ShowmasterStartV2 && parsedV2.ShowmasterSteps == allEmoteCodes,
    "native emote cue parsed");
string availabilityQuery = GamePartyCueProtocol.ShowmasterCheck(nonce, tag, allEmoteCodes);
Check(GamePartyCueProtocol.TryParse(availabilityQuery, out var parsedQuery) &&
    parsedQuery.Kind == GamePartyCueKind.ShowmasterCheck, "band availability query parsed");
string availabilityReply = GamePartyCueProtocol.ShowmasterAvailability(nonce, 0xAAA);
Check(GamePartyCueProtocol.TryParse(availabilityReply, out var parsedReply) &&
    parsedReply.Kind == GamePartyCueKind.ShowmasterAvailabilityReply && parsedReply.ShowmasterMask == 0xAAA,
    "band member per-step capability mask parsed");
var next = new ShowmasterPlayer();
next.Start(decodedV2, 1000);
next.TickWithSteps(1000, true, _ => ShowmasterExecutionResult.SkippedUnavailable);
Check(next.Active && next.SkippedSteps == 1 && next.CompletedSteps == 1,
    "locked member emote skips without aborting choreography");
next.TickWithSteps(1100, false, _ => ShowmasterExecutionResult.Performed);
Check(!next.Active, "gameplay safety aborts native ID choreography");
Console.WriteLine("PASS: full-range piano, wait chords, score, emote allowlist, party cues and stop safety");
