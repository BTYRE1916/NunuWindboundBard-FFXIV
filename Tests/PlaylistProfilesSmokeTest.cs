using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NunuWindboundBard.Core;

namespace NunuWindboundBard
{
    // Minimal test double; no Dalamud or FFXIV dependency.
    public sealed class Configuration
    {
        public int Version { get; set; } = 7;
        public int EnsembleMode { get; set; } = 0;
        public List<string> Playlist { get; set; } = new();
        public List<SavedPlaylist> SavedPlaylists { get; set; } = new();
        public string ActiveSoloPlaylistId { get; set; } = "";
        public string ActiveEnsemblePlaylistId { get; set; } = "";
        public string ActivePlaylistId { get; set; } = "";
    }
}

static class Smoke
{
    private static void Require(bool condition, string reason)
    {
        if (!condition) throw new InvalidOperationException("Playlist test failed: " + reason);
    }
    static void Main()
    {
        string a = Path.GetFullPath("first.mid");
        string b = Path.GetFullPath("second.mid");
        var cfg = new NunuWindboundBard.Configuration { Version = 7, Playlist = new List<string> { a, b } };
        SavedPlaylistLibrary.EnsureMigrated(cfg);
        Require(cfg.Playlist.SequenceEqual(new[] { a,b }), "legacy playlist lost during migration");
        Require(SavedPlaylistLibrary.GetActive(cfg).Purpose == 0, "solo profile missing");
        var ensemble = SavedPlaylistLibrary.ChangeMode(cfg, 1);
        Require(cfg.Playlist.Count == 0, "ensemble should start empty when migrating from Solo");
        cfg.Playlist.Add(b);
        SavedPlaylistLibrary.SnapshotActive(cfg);
        SavedPlaylistLibrary.ChangeMode(cfg, 0);
        Require(cfg.Playlist.SequenceEqual(new[] { a,b }), "solo playlist modified after switching to ensemble");
        SavedPlaylistLibrary.ChangeMode(cfg, 1);
        Require(cfg.Playlist.SequenceEqual(new[] { b }), "ensemble playlist wasn't retained");
        var newProfile = SavedPlaylistLibrary.NewProfile(cfg, "Epic Duets", 1, true);
        SavedPlaylistLibrary.Select(cfg, newProfile.Id);
        Require(cfg.Playlist.SequenceEqual(new[] { b }), "clone playlist failed");
        cfg.Playlist.Add(a);
        SavedPlaylistLibrary.SnapshotActive(cfg);
        SavedPlaylistLibrary.Select(cfg, ensemble.Id);
        Require(cfg.Playlist.SequenceEqual(new[] { b }), "named lists are not independent");
        // Soulweaver can export to an Ensemble library even while a Solo library
        // is selected (and vice versa). Active playlist must stay consistent.
        var oneMore = Path.Combine(Path.GetTempPath(), "soulweaver-playlist-" + Guid.NewGuid().ToString("N") + ".mid");
        File.WriteAllBytes(oneMore, [0x4D, 0x54, 0x68, 0x64]); // this test checks registration, not MIDI parsing
        try
        {
            SavedPlaylistLibrary.ChangeMode(cfg, 0);
            var activeSolo = cfg.Playlist.ToArray();
            var ensembleTarget = SavedPlaylistLibrary.AddMidiToPurpose(cfg, oneMore, 1);
            Require(cfg.Playlist.SequenceEqual(activeSolo), "ensemble export changed active Solo working list");
            Require(ensembleTarget.Files.Contains(oneMore), "ensemble export missing from saved profile");
            SavedPlaylistLibrary.ChangeMode(cfg, 1);
            Require(cfg.Playlist.Contains(oneMore), "ensemble export not visible after switching libraries");
            SavedPlaylistLibrary.AddMidiToPurpose(cfg, oneMore, 1);
            Require(cfg.Playlist.Count(x => x == oneMore) == 1, "export was added twice");
            var soloTarget = SavedPlaylistLibrary.AddMidiToPurpose(cfg, oneMore, 0);
            Require(soloTarget.Files.Contains(oneMore), "solo export missing from saved profile");
            SavedPlaylistLibrary.ChangeMode(cfg, 0);
            Require(cfg.Playlist.Contains(oneMore), "solo export not visible after switching libraries");
            Console.WriteLine("PASS: Soulweaver library registration, inactive library, duplicate protection.");
        }
        finally { File.Delete(oneMore); }
        var path = Path.Combine(Path.GetTempPath(), "windbound-playlist-test-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            SavedPlaylistLibrary.Export(path, newProfile, newProfile.Files);
            var imported = SavedPlaylistLibrary.Import(path);
            Require(imported.Files.SequenceEqual(new[] { b,a }), "JSON file roundtrip failed");
        }
        finally { if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine("PASS: migration, separate Solo/Ensemble lists, named lists, JSON export/import.");
    }
}
