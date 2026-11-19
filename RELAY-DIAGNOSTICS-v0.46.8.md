# Windbound Bard v0.46.8 — relay timing visibility

This is a source update for testing; the Windows plugin and capture engine need to be built.

- When a follower is cued but no FFXIV MIDI callbacks are observed three seconds after the scheduled downbeat, the follower sends an explicit **AWAITING_NOTES** timing status; the conductor report explains that this is **not** a latency measurement.
- If the follower has no assigned part, **SILENT_PART** appears immediately instead.
- Conductor without note callbacks logs the same no-notes-yet caveat, and a silent conductor part logs its expected silence.
- Each follower status includes RTT and clock spread as measured locally on that follower. RTT and clock spread are network estimates, not audible synchronization delay.
- If the first notes arrive after a rest, the follower sends the real first/mean/worst callback timing and the conductor replaces the interim status.
- Follower readiness shows **CUED / PLAYING** instead of claiming an armed player is unready before the countdown.
- Normal START delivery, ACK/retry, whole-band cancellation, MIDI sharing, timing trim and Render relay routing remain unchanged.

## Testing

1. Build and install v0.46.8 on **both** conductor and follower; no Render redeployment required.
2. Start a duet where both have assigned non-silent MIDI parts. Wait at least three seconds **after** the downbeat.
3. In Conductor → Relay diagnostics → Copy relay report, look for either `follower ... dispatched ... first ... mean ... worst` or `follower ... timing status: no note callbacks yet at +3s`.
4. For the follower, open its own Relay diagnostics to see the local clock RTT and spread and any note-dispatch statistics.
5. These values measure client scheduling and network clock quality, **not** final heard audio latency. A microphone/two-PC audio recording is necessary to measure audible differences.

If the report still only contains START acknowledgements, confirm both plugins actually run 0.46.8.0, that the follower received a new cue after updating, and that the FFXIV Performance output was armed.
