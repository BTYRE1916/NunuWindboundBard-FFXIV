# Nunu's Windbound Bard v0.37.3 — Portrait Crop Drag Fix

## Fixed

- The screenshot preview in the **Band Portraits** capture window was drawn with `ImGui.Image`, which did not own the mouse drag. Depending on ImGui window behaviour, dragging to select a portrait could move the entire window instead of selecting a crop.
- Replaced the non-interactive preview image with an `ImGui.InvisibleButton` covering exactly the image area. The actual screenshot is drawn on that item using the ImGui draw list, while the active button captures drag input.
- Crop selection now starts only when the image surface owns the left mouse press. It remains active while the mouse is held; movement outside the preview is clamped to its edges. The window can still be moved by its title bar.
- Closing or replacing a capture also resets any pending drag state.

## Preserved

- User-captured in-game Adventurer Plate screenshots, crop-to-square logic, 128 x 128 local thumbnail saving, and cached portraits keyed by Content ID.
- Dedicated Band Portraits tab in Solo, Conductor and Follower modes, plus conductor/follower track and instrument thumbnails.
- v0.37.1 instrument-switching fix, v0.36.1 instrument-icon alignment, native ensembles, stage formations, automatic facing, and MIDI/playlist functionality.

## Testing

- `python Tests/Check-v0.37.3-Portrait-Crop-Drag.py` runs deterministic source-level regression checks.
- Actual GUI input and game screenshot functionality can only be verified in FFXIV with a compatible Dalamud runtime. No Windows/Dalamud compile was available here.
