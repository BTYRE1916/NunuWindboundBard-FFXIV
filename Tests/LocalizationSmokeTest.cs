using System;
using NunuWindboundBard;

static void Check(bool condition, string label)
{
    if (!condition) throw new Exception("Localization regression failed: " + label);
    Console.WriteLine("PASS: " + label);
}

Loc.SetLanguage("en");
Check(Loc.T("Songs & Playback") == "Songs & Playback", "English default unchanged");
Check(Loc.T("Hide HUD##hud") == "Hide HUD##hud", "English ImGui ID preserved");
Loc.SetLanguage("fr");
Check(Loc.T("Songs & Playback") == "Morceaux et lecture", "French tab label");
Check(Loc.T("Guitar Tones") == "Sons de guitare", "Guitar Tones tab");
Check(Loc.T("Live MIDI Piano") == "Piano MIDI en direct", "Live controller tab");
Check(Loc.T("SET PARTY TRACKS — assign every Bard").Contains("AFFECTER LES PISTES"), "Leader track controls");
Check(Loc.T("Hide HUD##hud") == "Masquer le HUD##hud", "French ImGui ID preserved");
Check(Loc.T("##windboundLocale") == "##windboundLocale", "Hidden ImGui ID unchanged");
Check(Loc.T("Repeat##smart") == "Répétition##smart", "Smart Playlist widgets");
Check(Loc.T("Off") == "Désactivé" && Loc.T("One") == "Un seul", "Repeat mode display");
Check(Loc.T("Lute") == "Luth" && Loc.T("Violin") == "Violon", "Instrument names");
Check(Loc.T("Could not load MIDI: The file is missing").StartsWith("Impossible de charger le MIDI :"), "Runtime error prefix");
Check(Loc.T("SMART PLAYLIST MANAGER — 29 songs").Contains("GESTIONNAIRE INTELLIGENT"), "Live song counts");
Check(Loc.T("Playlist: My Favourites").StartsWith("Liste de lecture"), "Dynamic HUD playlist label");
Check(Loc.T("SAVED Solo PLAYLISTS").Contains("LISTES DE LECTURE"), "Dynamic playlist heading");
Check(Loc.T(@"C:\Concerts\Piano Sonata.mid") == @"C:\Concerts\Piano Sonata.mid", "MIDI filenames not translated");
Check(Loc.T("My original track name") == "My original track name", "Unknown user track names left intact");
Loc.SetLanguage("en");
Check(Loc.T("SET PARTY INSTRUMENTS — auto-switch everyone") == "SET PARTY INSTRUMENTS — auto-switch everyone", "Live switch back to English");
Console.WriteLine("Windbound English/French localisation test passed.");
