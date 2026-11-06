using System;
using System.Collections.Generic;
using System.Linq;
using NunuWindboundBard.Core;

static void Check(bool ok, string message)
{
    if (!ok) throw new InvalidOperationException("SmartPlaylist test failed: " + message);
}

var states = new Dictionary<string, SmartPlaylistState>();
var solo = SmartPlaylistManager.Get(states, "solo");
var ensemble = SmartPlaylistManager.Get(states, "ensemble");
var tracks = new List<string> { "C:/Song A.mid", "C:/Song B.mid", "C:/Song C.mid" };

Check(!ReferenceEquals(solo, ensemble), "profile isolation");
SmartPlaylistManager.ToggleFavorite(solo, tracks[1]);
Check(SmartPlaylistManager.IsFavorite(solo, tracks[1]), "favorite on");
Check(!SmartPlaylistManager.IsFavorite(ensemble, tracks[1]), "favorite isolation");
SmartPlaylistManager.SetTags(solo, tracks[1], "duet, piano, duet, harmony");
Check(SmartPlaylistManager.GetTags(solo, tracks[1]).Count == 3, "tag deduplication");
Check(SmartPlaylistManager.Matches(solo, tracks[1], "harmony", "duet", true), "search by tag");
Check(!SmartPlaylistManager.Matches(solo, tracks[0], "", "", true), "favorites-only filter");
SmartPlaylistManager.Move(tracks, 2, 0);
Check(tracks.SequenceEqual(new[] { "C:/Song C.mid", "C:/Song A.mid", "C:/Song B.mid" }), "reorder without changing membership");

SmartPlaylistManager.AddPlayNext(solo, tracks, tracks[2]);
SmartPlaylistManager.AddPlayNext(solo, tracks, tracks[0], true);
var candidates = SmartPlaylistManager.Candidates(solo, tracks, 1, false);
Check(candidates.Count >= 2 && candidates[0].Path == tracks[0] && candidates[1].Path == tracks[2], "queue priority");
Check(candidates[0].Origin == PlaylistCandidateOrigin.PlayNext, "queue origin");
Check(tracks.SequenceEqual(new[] { "C:/Song C.mid", "C:/Song A.mid", "C:/Song B.mid" }), "queue does not reorder saved playlist");
SmartPlaylistManager.ConsumeQueueItem(solo, tracks[0]);
Check(solo.PlayNext.Count == 1, "consume exactly one queue entry");

solo.PlayNext.Clear();
solo.Repeat = PlaylistRepeatMode.One;
candidates = SmartPlaylistManager.Candidates(solo, tracks, 1, false);
Check(candidates.Count == 1 && candidates[0].Path == tracks[1], "repeat one after song completion");
candidates = SmartPlaylistManager.Candidates(solo, tracks, 1, true);
Check(candidates[0].Path == tracks[2], "manual next bypasses repeat one");

solo.Repeat = PlaylistRepeatMode.Off;
Check(SmartPlaylistManager.Candidates(solo, tracks, 2, false).Count == 0, "repeat off stops at end");
solo.Repeat = PlaylistRepeatMode.All;
Check(SmartPlaylistManager.Candidates(solo, tracks, 2, false)[0].Path == tracks[0], "repeat all wraps to beginning");

solo.Shuffle = true;
solo.Repeat = PlaylistRepeatMode.Off;
SmartPlaylistManager.ResetShuffle(solo);
SmartPlaylistManager.RecordStarted(solo, tracks[1]);
var randomOrder = SmartPlaylistManager.Candidates(solo, tracks, 1, false);
Check(randomOrder.Count == 2 && !randomOrder.Any(c => c.Path == tracks[1]) &&
      randomOrder.Select(c => c.Path).Distinct().Count() == 2, "shuffle doesn't replay current song in cycle");
foreach (var item in randomOrder) SmartPlaylistManager.RecordStarted(solo, item.Path);
Check(SmartPlaylistManager.Candidates(solo, tracks, 1, false).Count == 0, "shuffle repeat off ends once all songs have played");
Check(solo.Recent.Count == 3, "recent history");
Check(solo.Recent[0].Path == randomOrder[^1].Path, "latest recently played on top");
Check(ensemble.Recent.Count == 0, "history isolated by profile");
solo.Repeat = PlaylistRepeatMode.All;
Check(SmartPlaylistManager.Candidates(solo, tracks, 1, false).Count == 2, "shuffle repeat all refills without immediate repeat");

SmartPlaylistManager.AddPlayNext(solo, tracks, tracks[2]);
tracks.Remove(tracks[2]);
SmartPlaylistManager.PruneQueue(solo, tracks);
Check(solo.PlayNext.Count == 0, "removed songs pruned from play next");

Console.WriteLine("Windbound Smart Playlist tests PASS: isolation, favourites, tags, queue, reorder, history, shuffle, repeat modes, prune.");
