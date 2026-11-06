# v0.17 Native Ensemble Reliability

- Replaced unreliable timed party-chat start with the same **native FFXIV Ready Check → auto-confirm → game ensemble-start notification** flow used by MidiBard 2.
- Native UI callbacks now run only on the Dalamud framework thread.
- One-click conductor Ready Check; followers auto-confirm when prepared and ARMed.
- Same-PC and different-PC FFXIV party ensembles share the same native path. No Tailscale or cross-PC clock/UTC countdown.
- Native listener availability, auto-confirm attempts, conductor confirmation attempts, and START events are visible in diagnostics.
- Saved playlists, Soulweaver, Smart Playlists, Compact HUD, instruments and note engine retained from v0.16.
- Legacy party-chat START explicitly disabled on upgrade. **Remote per-performer instrument preparation and automatic track-assignment transmission from that retired pathway are not provided by native START**; performers prepare their parts before the Ready Check.

**Experimental source build:** must be compiled and validated inside the current FFXIV/Dalamud release.
