# Windbound Bard v0.37 — In-game band member portraits (experimental)

- Capture thumbnails from the **real FFXIV Adventurer Plate shown on your game screen**.
- In the Ensemble tab, expand **In-game member portraits**; with a member's Adventurer Plate open, choose **CAPTURE** and drag a square around their character portrait in the preview.
- Save the selected 128 × 128 crop; full screen captures are held only in memory and **never written to disk**.
- Portraits appear next to band members in the conductor's track-assignment dropdown and instrument roster; followers see their own portrait by their name.
- Per-client local cache: `BandPortraits` within the plugin configuration directory, keyed by party member ContentId. No network sync of images.
- Game's Adventurer Plate visibility and privacy settings remain unchanged. No bypass, auto-request, scraping or external images.
- Normal selection works when a portrait is unavailable; existing v0.36.1 instrument icons, stage controls and MIDI playback are untouched.

**Validation:** Source integration/static tests only. Requires Dalamud API 15 and an in-game test. This captures *displayed* Adventurer Plate images, rather than invisibly fetching portraits for unviewed players.
