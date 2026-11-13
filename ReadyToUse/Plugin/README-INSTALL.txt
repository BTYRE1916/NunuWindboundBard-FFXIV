Nunu Windbound Bard 0.42.4 - MP4 / HD preview
Windows x64 / Dalamud API 15

To activate the update, stop all capture outputs and choose SHUT DOWN IDLE
ENGINE in Concert Capture before updating. Launch the updated engine after
installing; its status should identify 0.42.4. Plugin updates intentionally
do not interrupt a running engine or recording.

Repository maintainer setup:
1. Upload this ZIP as FFXIVMIDIPLAYER-BY-NUNU-v0.42.4.zip on the latest
   published release of BTYRE1916/NunuWindboundBard-FFXIV.
2. Commit and push the companion Plugin.json at the repository root.
3. Refresh the repository in Dalamud and install/update to 0.42.4.0.
The ZIP's plugin DLL and JSON belong at its root. Keep every file and the
CaptureEngine subfolder together. The engine is self-contained and FFmpeg
is included. For a dev load, select the root NunuWindboundBard.dll.

MP4: SHOW > Concert Capture > Video, encoding and storage > Recording format
> MP4 > APPLY OUTPUT SETTINGS (outputs stopped). Stop & Save finalizes it.
Saved replays use the selected format too. Existing settings default to MKV.

Preview: choose 540p/720p/1080p and 5/10/15 FPS. OPEN LARGE PREVIEW opens a
separate resizable monitor that remains visible with the main window closed.
Preview settings do not change recording resolution or frame rate.

Validation: both projects compile; 35 format/preview/codec checks, 14 offline
studio checks and 19 memory-loaded startup checks passed. Actual recording,
pause/resume, finalization and replay tests passed at 30 FPS for MKV and MP4
on the Linux host transport. The Windows native/JPEG smoke test compiled;
Windows in-game preview, capture devices and hardware encoders were not
executed here. Requested preview FPS is an upper bound.

Third-party notices and FFmpeg license/build information are included.
