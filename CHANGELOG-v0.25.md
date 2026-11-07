# v0.25 — Concert Setlist Automation & Single-Bard Recovery

Built on v0.24 Concert Director, retaining native FFXIV Ensemble START. The conductor can opt into **Automatically prepare next Ensemble song**. When the current native ensemble completes successfully (or its known MIDI duration elapses for a silent conductor), Windbound chooses the next existing MIDI from the saved setlist / Play Next queue, loads it, and prepares the party's MIDI/track/instrument state in the background. It deliberately NEVER presses native Ready Check automatically. You decide when to start the next song.

You can add per-song introduction/announcement notes, mark encores, choose a break of 0–120 seconds, or HOLD until the conductor manually presses PREPARE NEXT SONG. Audience announcements are displayed as suggestions, never automatically sent to public or party chat.

**Recovery:** the conductor can Retry an individual missing Bard. Commands are scoped to a ContentId-sorted roster slot and authenticated with a roster fingerprint; the other Bards ignore the targeted command. The follower is re-synchronised in order: MIDI file, assigned tracks, instrument, then another stage status request. No music starts during retry. Targeted commands require all performers running v0.25 or newer.

Default setting: Auto-Prepare OFF on upgrade; opt-in in the Concert Director's Setlist section. A manual STOP cancels pending automatic setlist advance.

Live FFXIV testing still required: this archive was statically checked and its format validated but the Dalamud SDK/runtime isn't available in the packaging environment.
