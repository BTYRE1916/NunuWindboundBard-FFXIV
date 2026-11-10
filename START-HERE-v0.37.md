# START HERE — v0.37 In-Game Member Portraits

1. Open `NunuWindboundBard.sln` in Visual Studio, choose **x64 / Release**, and rebuild with Dalamud.NET.Sdk 15.
2. Load the plugin in Dalamud. In **Ensemble**, choose Conductor or Follower.
3. Expand **In-game member portraits (Adventurer Plates)**.
4. In the FFXIV party list, open a band member's **View Adventurer Plate**. The plate must actually be visible on your screen and not blocked by other game UI. Privacy settings can prevent this.
5. In WindboundBard, click **CAPTURE** next to the corresponding member. Drag a square around the portrait in the game's screenshot preview. Click **SAVE SELECTED GAME PORTRAIT**.
6. The saved member thumbnail now appears in track assignment and party instrument lists on THIS client. Do it once per character per client.
7. To update a portrait, repeat capture; to erase it, choose **Remove**.

**Notes:** This is a real game-screen screenshot crop, not an API for automatically retrieving unseen players' portraits. No screenshots leave your computer. Only the selected square thumbnail is saved. If a capture doesn't show the plate, cancel, reopen the plate and try again. Keep backups of v0.36.1. This is an experimental build pending Visual Studio and FFXIV validation.
