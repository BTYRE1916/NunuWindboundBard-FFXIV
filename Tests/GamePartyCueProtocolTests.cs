using System;
using System.IO;
using NunuWindboundBard.Core;

static void Check(bool ok, string message)
{
    if (!ok) throw new Exception("Game Ensemble protocol test failed: " + message);
}

var midi = Path.Combine(Path.GetTempPath(), "windbound-game-ensemble-" + Guid.NewGuid().ToString("N") + ".mid");
try
{
    // Deliberately minimal valid SMF, 1 track, end-of-track.
    File.WriteAllBytes(midi, new byte[] {
        0x4D,0x54,0x68,0x64,0,0,0,6,0,0,0,1,0,0x60,
        0x4D,0x54,0x72,0x6B,0,0,0,4,0,0xFF,0x2F,0});
    var command = GamePartyCueProtocol.Song(123, midi);
    Check(command.Contains("song 124"), "one-based playlist index");
    Check(GamePartyCueProtocol.TryParse(command, out var parsed), "round trip song parse");
    Check(parsed.Kind == GamePartyCueKind.Song && parsed.PlaylistIndex == 123, "song index");
    Check(parsed.Hash == GamePartyCueProtocol.GetHash(midi), "byte fingerprint");
    Check(GamePartyCueProtocol.TryParse(GamePartyCueProtocol.Stop, out var stop) && stop.Kind == GamePartyCueKind.Stop,
        "STOP parse");
    foreach (string bad in new [] {
        "", "!windbound song -1 AAAAAAAAAAAAAAAA", "!windbound song 1 ZZZZZZZZZZZZZZZZ",
        "!windbound song 1 DEADBEEF", "!windbound song 0 AAAAAAAAAAAAAAAA",
        "!windbound song 10001 AAAAAAAAAAAAAAAA", "!windbound stop now", "!windbound shell cmd",
        "switchto 2", "!windbound song 3 AAAAAAAAAAAAAAAA injected"
    })
        Check(!GamePartyCueProtocol.TryParse(bad, out _), $"reject '{bad}'");
    const long sentAt = 1794828000123;
    const string nonce = "1234567890abcdef";
    const string roster = "aabbccdd";
    foreach (int slot in new[] { 0, 7, 99, 100, 107, 199 })
    {
        string macro = GamePartyCueProtocol.PartyMacro(nonce, roster, sentAt, slot);
        Check(macro.Length <= GamePartyCueProtocol.MaxChatCharacters &&
            GamePartyCueProtocol.TryParse(macro, out var cue) && cue.Kind == GamePartyCueKind.PartyMacro &&
            cue.MacroSlot == slot && cue.StartUnixMs == sentAt && cue.Nonce == "1234567890ABCDEF" &&
            cue.RosterTag == "AABBCCDD", "party macro cue round trip");
    }
    foreach (string suffix in new[] { "", "-1", "+7", "200", "7 more", "7\u0000", "/echo" })
        Check(!GamePartyCueProtocol.TryParse($"!windbound macro {nonce} {roster} {sentAt} {suffix}", out _),
            "reject invalid macro slot or payload");
    Check(!GamePartyCueProtocol.TryParse($"!windbound macro bad {roster} {sentAt} 7", out _) &&
        !GamePartyCueProtocol.TryParse($"!windbound macro {nonce} bad {sentAt} 7", out _) &&
        !GamePartyCueProtocol.TryParse($"!windbound macro {nonce} {roster} 0 7", out _),
        "party macro cues require a nonce, roster tag and plausible timestamp");
    Console.WriteLine("PASS: song, stop and party macro protocol, invalid commands and bounds.");
}
finally { try { File.Delete(midi); } catch { } }
