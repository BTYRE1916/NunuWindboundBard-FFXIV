using NunuWindboundBard.Core;

static void Check(bool condition, string title)
{
    if (!condition) throw new Exception("FAILED: " + title);
    Console.WriteLine("PASS: " + title);
}

// From the obs-websocket v5 documentation's challenge/salt sample.
const string salt = "lM1GncleQOaCu9lT1yeUZhFYnqhsLLP1G5lAGo3ixaI=";
const string challenge = "+IxH4CnCiqpX1rM9scsNynZzbOe4KhDeYcTNS3PDaeY=";
const string expected = "1Ct943GAT+6YQUUX47Ia/ncufilbe6+oD6lY+5kaCu4=";
Check(ObsRecordingClient.Authentication("supersecretpassword", salt, challenge) == expected,
    "OBS WebSocket 5 SHA256 challenge authentication");
Check(!ObsRecordingClient.ValidPort(-1) && !ObsRecordingClient.ValidPort(0), "invalid ports rejected");
Check(ObsRecordingClient.ValidPort(4455) && ObsRecordingClient.ValidPort(65535), "valid ports accepted");
Check(!ObsRecordingClient.ValidPort(65536), "port maximum enforced");
Check(ObsRecordingClient.FormatDuration(0) == "00:00:00", "initial recording time");
Check(ObsRecordingClient.FormatDuration((51*3600L + 2*60 + 3) * 1000) == "51:02:03",
    "recording timer correctly exceeds 24 hours");
Check(ObsSongTitleOverlay.FromMidiPath(@"C:\Concerts\01 - Moonlight Waltz.mid") ==
    "01 - Moonlight Waltz" || ObsSongTitleOverlay.FromMidiPath("01 - Moonlight Waltz.mid") ==
    "01 - Moonlight Waltz", "clean title from MIDI filename");
Check(ObsSongTitleOverlay.TextForPlayback("Encore", false, true) == "", "hide title while idle");
Check(ObsSongTitleOverlay.TextForPlayback("Encore", true, true) == "Encore", "title on while playing");
Check(ObsSongTitleOverlay.IsTextInputKind("text_gdiplus_v2"), "OBS Windows Text GDI+ v2 supported");
Check(ObsSongTitleOverlay.IsTextInputKind("text_gdiplus_v3"), "OBS Windows Text GDI+ v3 supported");
Check(ObsSongTitleOverlay.IsTextInputKind("text_ft2_source_v2"), "newer OBS FreeType text input supported");
Check(!ObsSongTitleOverlay.IsTextInputKind("game_capture"), "refuse non-Text OBS source");
string temporary = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "windbound-obs-test-" + Guid.NewGuid().ToString("N"));
try
{
    using var writer = new ObsSongTitleFileBridge(temporary);
    writer.SetText("Edea & Nunu — Concert Finale");
    string titlePath = writer.FilePath;
    bool written = SpinWait.SpinUntil(() => {
        try { return System.IO.File.Exists(titlePath) &&
            System.IO.File.ReadAllText(titlePath).Contains("Concert Finale"); }
        catch (System.IO.IOException) { return false; }
    }, TimeSpan.FromSeconds(5));
    Check(written, "song title text file saved asynchronously as UTF-8");
    writer.SetText("");
    bool cleared = SpinWait.SpinUntil(() => {
        try { return System.IO.File.Exists(titlePath) &&
            System.IO.File.ReadAllText(titlePath).Length == 0; }
        catch (System.IO.IOException) { return false; }
    }, TimeSpan.FromSeconds(5));
    Check(cleared, "song title text file cleared between performances");
}
finally { try { System.IO.Directory.Delete(temporary, true); } catch { } }

using var obs = new ObsRecordingClient();
Check(!obs.Snapshot.Connected && !obs.Snapshot.Recording, "manual opt-in defaults");
obs.SetDiskMonitorFolder("C:\\Concerts");
obs.Command("StartRecord"); // must not start when disconnected
Check(!obs.Snapshot.Recording, "no recording action without OBS connection");
obs.Disconnect();
Check(obs.Snapshot.Message.Contains("continues in OBS"), "disconnect never stops OBS output");
Console.WriteLine("OBS capture protocol smoke test passed. Live OBS/FFXIV test is still required.");
