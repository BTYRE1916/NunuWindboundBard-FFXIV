from pathlib import Path
base=Path('/mnt/data/wb38-work/src/NunuWindboundBard')
def edit(rel,fn):
 p=base/rel
 s=p.read_text(encoding='utf-8-sig');t=fn(s)
 assert t!=s,rel+' no edit'
 p.write_text(t,encoding='utf-8')
def rep(old,new):
 def change(s):
  n=s.count(old);assert n==1,(old[:80],n)
  return s.replace(old,new,1)
 return change
edit('Configuration.cs',rep('public int Version { get; set; } = 35;', 'public int Version { get; set; } = 36;'))
edit('Configuration.cs',rep('    public bool LiveMidiInputEnabled { get; set; } = false;', '''    // v0.38 Piano Academy: independent local full-range piano practice.
    public int PianoAcademySplitPitch { get; set; } = 60;
    public float PianoAcademySpeed { get; set; } = 1f;
    // v0.38 Showmaster: disabled on followers until they explicitly opt in.
    public bool AllowLeaderShowmaster { get; set; } = false;
    public List<ShowmasterRoutine> ShowmasterRoutines { get; set; } = ShowmasterCodec.Defaults();
    public bool LiveMidiInputEnabled { get; set; } = false;'''))
edit('MainWindow.cs',rep('        this.plugin = plugin;\n        cfg = plugin.Config;', '''        this.plugin = plugin;
        cfg = plugin.Config;
        plugin.LiveMidi.PracticeMidiReceived = OnAcademyMidi;'''))
edit('MainWindow.cs',rep('        plugin.AudioPreview.Stop();\n        DisposeMemberPortraitCapture();', '''        plugin.AudioPreview.Stop();
        plugin.LiveMidi.PracticeMidiReceived = null;
        plugin.LiveMidi.PracticeMonitoring = false;
        DisposeMemberPortraitCapture();'''))
edit('MainWindow.cs',rep('            if (ImGui.BeginTabItem(Loc.T("Band Portraits"))) { DrawBandPortraitsTab(); ImGui.EndTabItem(); }', '''            if (ImGui.BeginTabItem(Loc.T("Band Portraits"))) { DrawBandPortraitsTab(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Piano Academy"))) { DrawPianoAcademy(); ImGui.EndTabItem(); }
            if (ImGui.BeginTabItem(Loc.T("Showmaster"))) { DrawShowmaster(); ImGui.EndTabItem(); }'''))
edit('Core/LiveMidiInputService.cs',rep('    public IReadOnlyList<WinMmMidiInput.Port> Ports => ports;', '''    // Optional raw MIDI monitor for Piano Academy. Runs ONLY on Framework.Update;
    // never delivers callbacks from the winmm driver thread.
    public Action<uint>? PracticeMidiReceived { get; set; }
    public bool PracticeMonitoring { get; set; }
    public IReadOnlyList<WinMmMidiInput.Port> Ports => ports;'''))
edit('Core/LiveMidiInputService.cs',rep('        bool allowed = enabled && solo && armed && performanceReady && input.Connected &&', '''        bool allowed = !PracticeMonitoring && enabled && solo && armed && performanceReady && input.Connected &&'''))
edit('Core/LiveMidiInputService.cs',rep('            input.Drain();\n            return;\n        }\n        outputWasAllowed = true;', '''            if (PracticeMonitoring && input.Connected)
            {
                int sampled = 0;
                while (sampled++ < 512 && input.TryDequeue(out uint raw))
                {
                    try { PracticeMidiReceived?.Invoke(raw); }
                    catch (Exception ex) { Status = "Piano Academy MIDI observer error: " + ex.Message; }
                }
            }
            else input.Drain();
            return;
        }
        outputWasAllowed = true;'''))
edit('Plugin.cs',rep('        Config.StageFormations ??= new();\n        Config.ConcertSnapshots ??= new();', '''        if (Config.Version < 36)
        {
            Config.ShowmasterRoutines ??= ShowmasterCodec.Defaults();
            Config.AllowLeaderShowmaster = false;
            Config.Version = 36;
            Config.Save();
        }
        Config.StageFormations ??= new();
        Config.ConcertSnapshots ??= new();'''))
edit('Plugin.cs',rep('        try { main.PollGamePartyChat(); }', '''        // Showmaster party cues share the existing verified leader-only receiver.
        try { main.PollGamePartyChat(); }'''))
edit('Plugin.cs',rep('        // Flush live controller note-offs before the MIDI-file scheduler so', '''        // Both new modules run independently of live FFXIV MIDI note playback.
        // Never let rehearsal or a broken emote prevent a game note-off tick.
        try { main.PollPianoAcademy(); }
        catch (Exception error) { Log.Error("Piano Academy update failed: " + error); }
        try { main.PollShowmaster(); }
        catch (Exception error) { Log.Error("Showmaster update failed: " + error); }
        // Flush live controller note-offs before the MIDI-file scheduler so'''))
edit('Core/GamePartyCueProtocol.cs',rep('    FormationFailed, FormationStop }', '    FormationFailed, FormationStop, ShowmasterStart, ShowmasterStop }'))
edit('Core/GamePartyCueProtocol.cs',rep('    uint FormationTerritory = 0, Vector3 FormationPosition = default, float FormationHeading = 0);', '''    uint FormationTerritory = 0, Vector3 FormationPosition = default, float FormationHeading = 0,
    string ShowmasterSteps = "");'''))
edit('Core/GamePartyCueProtocol.cs',rep('    // v0.30: positioning is a SEPARATE, opt-in stage utility, never a music START.', '''    /// <summary>Conductor-triggered, opt-in emote choreography. Payload only contains
    /// a strict fixed emote codebook, never arbitrary scripts or text commands.</summary>
    public static string ShowmasterStart(string nonce, string rosterTag, long startUnixMs, string steps)
    {
        if (!IsHex(nonce, 16) || !IsHex(rosterTag, 8) ||
            startUnixMs is < 1577836800000L or > 4102444800000L ||
            !ShowmasterCodec.TryDecode(steps, out _))
            throw new ArgumentException("Invalid intermission choreography cue.");
        string cmd = $"{Prefix} show {nonce.ToUpperInvariant()} {rosterTag.ToUpperInvariant()} {startUnixMs} {steps}";
        if (cmd.Length > MaxChatCharacters) throw new ArgumentException("Show cue too long.");
        return cmd;
    }
    public static string ShowmasterStop(string nonce) =>
        IsHex(nonce,16) ? $"{Prefix} showstop {nonce.ToUpperInvariant()}" :
        throw new ArgumentException("Invalid show-stop nonce.");

    // v0.30: positioning is a SEPARATE, opt-in stage utility, never a music START.'''))
edit('Core/GamePartyCueProtocol.cs',rep('        if (p.Length < 2 || !p[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;', '''        if (p.Length < 2 || !p[0].Equals(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (p.Length == 6 && p[1].Equals("show", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsHex(p[2], 16) || !IsHex(p[3], 8) ||
                !long.TryParse(p[4], NumberStyles.None, CultureInfo.InvariantCulture, out long start) ||
                start is < 1577836800000L or > 4102444800000L ||
                !ShowmasterCodec.TryDecode(p[5], out _)) return false;
            cue = new GamePartyCue(GamePartyCueKind.ShowmasterStart, -1, "",
                StartUnixMs: start, Nonce: p[2].ToUpperInvariant(),
                RosterTag: p[3].ToUpperInvariant(), ShowmasterSteps: p[5]);
            return true;
        }
        if (p.Length == 3 && p[1].Equals("showstop", StringComparison.OrdinalIgnoreCase) && IsHex(p[2],16))
        {
            cue = new GamePartyCue(GamePartyCueKind.ShowmasterStop,-1,"",Nonce:p[2].ToUpperInvariant());
            return true;
        }'''))
edit('MainWindow.GameEnsemble.cs',rep('            if (cue.Kind == GamePartyCueKind.FormationCapture)', '''            if (cue.Kind == GamePartyCueKind.ShowmasterStart)
            { ReceiveShowmasterStart(cue); continue; }
            if (cue.Kind == GamePartyCueKind.ShowmasterStop)
            { ReceiveShowmasterStop(cue); continue; }
            if (cue.Kind == GamePartyCueKind.FormationCapture)'''))
# GitHub/public release urls intentionally left unchanged. This is a developer source archive, not a published release.
print('Integrated Piano Academy and Showmaster into 6 current source files.')
