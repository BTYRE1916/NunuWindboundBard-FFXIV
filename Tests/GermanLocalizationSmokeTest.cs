using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NunuWindboundBard;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception("German localization regression: " + message);
    Console.WriteLine("PASS: " + message);
}

Loc.SetLanguage("en");
Check(Loc.Language == "en" && Loc.T("Songs & Playback") == "Songs & Playback", "English default unchanged");
Check(Loc.T("Stage Formations") == "Stage Formations", "Existing English stage tab unchanged");

Loc.SetLanguage("fr");
Check(Loc.IsFrench && !Loc.IsGerman, "French mode selected");
Check(Loc.T("Songs & Playback") == "Morceaux et lecture", "Existing French tab preserved");
Check(Loc.T("Stage Formations") == "Formations de scène", "Existing French stage planner preserved");
Check(Loc.T("Hide HUD##hud") == "Masquer le HUD##hud", "French ImGui widget ID remains stable");

Loc.SetLanguage("de");
Check(Loc.Language == "de" && Loc.IsGerman && !Loc.IsFrench, "German locale can be selected");
Check(Loc.T("Songs & Playback") == "Songs & Wiedergabe", "German playback tab");
Check(Loc.T("Concert Snapshots") == "Konzert-Schnappschüsse", "German snapshots tab");
Check(Loc.T("Stage Formations") == "Bühnenformationen", "German stage tab");
Check(Loc.T("VISUAL STAGE DESIGNER — DRAG YOUR BARDS").StartsWith("VISUELLER BÜHNENPLANER"), "German stage planner");
Check(Loc.T("CONCERT DIRECTOR — ORCHESTRA STAGE MANAGER").Contains("KONZERTDIREKTOR"), "German Concert Director");
Check(Loc.T("LIVE MIDI PIANO — SOLO PERFORMANCE").Contains("SOLO-PERFORMANCE"), "German Live MIDI Piano");
Check(Loc.T("LOCAL MIDI AUDIO PREVIEW — SOUNDFONT").Contains("AUDIOVORSCHAU"), "German SoundFont preview");
Check(Loc.T("ARM ALL PARTY BARDS##conductorArm") == "ALLE GRUPPENBARDEN SCHARFSCHALTEN##conductorArm", "German party ARM with stable ID");
Check(Loc.T("Stop##test-internal-993") == "Stop##test-internal-993", "Unknown widget text is unchanged");
Check(Loc.T("##windboundLocale") == "##windboundLocale", "Hidden-only ImGui ID unchanged");
Check(Loc.T("Keep current##0-12345") == "Aktuelles beibehalten##0-12345", "Dynamic widget suffix unchanged");
Check(Loc.T("Repeat##smart") == "Wiederholen##smart", "German smart-playlist widget ID");
Check(Loc.T("SAVED Solo PLAYLISTS") == "GESPEICHERTE SOLO-PLAYLISTS", "German dynamic playlist heading");
Check(Loc.T("MIDI could not be loaded: C:\\Concerts\\Nunu.mid").Contains("C:\\Concerts\\Nunu.mid"), "Technical paths survive untranslated");
Check(Loc.T(@"C:\Concerts\Bard Song.mid") == @"C:\Concerts\Bard Song.mid", "MIDI filename not translated");
Check(Loc.T("Veits Nunu Bard") == "Veits Nunu Bard", "Character names remain unchanged");
Check(Loc.T("PLAY ENSEMBLE") == "ENSEMBLE ABSPIELEN", "German ensemble command label");
Check(Loc.T("Lute") == "Laute" && Loc.T("Violin") == "Violine", "Instrument names translated");
Check(Loc.T("No song loaded") == "Kein Song geladen", "German runtime statuses");
Check(Loc.T("Output not ARMed") == "Ausgabe nicht scharfgeschaltet", "German ready-state diagnostics");
Check(Loc.GermanTranslationCount >= Loc.FrenchTranslationCount && Loc.GermanTranslationCount == 951, "Complete French catalogue coverage plus extra German labels");

var field = typeof(Loc).GetField("German", BindingFlags.NonPublic | BindingFlags.Static);
var german = field?.GetValue(null) as Dictionary<string, string>;
Check(german is not null && german.Count == 951, "German dictionary available");
foreach (var (english, translation) in german!)
{
    if (string.IsNullOrWhiteSpace(translation))
        throw new Exception("Empty German translation for: " + english);
    // Every catalog label must resolve in German. A widget's hidden ID is not translated.
    string expectedVisible = translation.Split("##", 2, StringSplitOptions.None)[0];
    string actualVisible = Loc.T(english).Split("##", 2, StringSplitOptions.None)[0];
    if (actualVisible != expectedVisible)
        throw new Exception($"Catalog mismatch '{english}': expected '{expectedVisible}', got '{actualVisible}'.");
}
Console.WriteLine("PASS: all " + german.Count + " German catalogue labels resolve.");
Loc.SetLanguage("DE");
Check(Loc.IsGerman, "Uppercase German code accepted");
Loc.SetLanguage("xx");
Check(Loc.Language == "en" && Loc.T("Concert Snapshots") == "Concert Snapshots", "Unsupported locale falls back to English");
Loc.SetLanguage("fr");
Check(Loc.T("Songs & Playback") == "Morceaux et lecture", "Switching back to French clears translation cache");
Loc.SetLanguage("en");
Console.WriteLine("PASS: English/French/German localization regression suite.");
