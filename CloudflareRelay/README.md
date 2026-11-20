# Windbound Bard — Cloudflare Relay (experimental)

This is a Cloudflare Workers + **SQLite-backed Durable Objects** alternative to the Render-hosted .NET relay. It exposes the **same relay protocol as the .NET service**; no FFXIV party, VPN, plugin DLL changes, or Cloudflare account are required for followers.

Two Durable Object classes are used:

- `BandDirectory` allocates up to **32 active rooms** and holds 10-second single-use pair-key → room mappings. It never handles MIDI or game messages.
- `BandRoom` is **one room per conductor**, accepting a control socket, up to **15 followers**, and corresponding conductor attachment sockets. It forwards opaque binary bytes only. The clients still run their own certificate-pinned TLS handshake within the relayed connection.

The room expires after **four hours** and when the conductor disconnects, as with the original .NET relay. Both Durable Object classes use `new_sqlite_classes` so a Workers Free account can deploy them. No secrets or certificates are embedded in the Worker.

Hosted Band defaults to `wss://windbound-orchestra-relay.nunu-orchestra-2026-4728.workers.dev`. To use it, choose **Host Band** and share the resulting join code with followers. The deployment steps below are for operating your own Worker.

## Deploy to Cloudflare

1. Sign up or sign in at https://dash.cloudflare.com/ and open **Workers & Pages**. Select a Workers plan (Free is fine for a trial). Check Free quotas before heavy concerts: https://developers.cloudflare.com/durable-objects/platform/pricing/
2. Install Node.js 20 or newer on Windows.
3. In PowerShell, from the repository root:

   ```powershell
   Set-Location .\CloudflareRelay
   npm install
   npx wrangler login
   npm test
   npx wrangler deploy
   ```

4. Cloudflare prints the actual public URL, for example:
   `https://windbound-orchestra-relay.YOUR-SUBDOMAIN.workers.dev`
   Use **the same hostname but `wss://`**, without any trailing path:
   `wss://windbound-orchestra-relay.YOUR-SUBDOMAIN.workers.dev`
5. In Windbound Bard v0.47.3, the **conductor** opens **BAND → Ensemble**, disables **Use FFXIV native Ensemble**, enables **Multi-Device → Use hosted band with join code**, selects **Conductor**, pastes the `wss://` URL into **Band relay address**, and clicks **Host Band → Copy band join code**. Each follower selects **Follower**, pastes the code, and clicks **Join with Code**. The join code already contains the Cloudflare URL. Edea can host from Lyon while Nunu joins from Glasgow, without an in-game party.
6. Keep `wss://nunuwindboundbard-ffxiv.onrender.com` available in the conductor's relay address field as a fallback. **Do not remove the Render deployment** until Cloudflare passes your duet and octet tests.

Do not enter `https://` in the Windbound relay address setting. Do not append `/v1/host`: the plugin appends the correct WebSocket route automatically.

## Test before publishing

From this folder, run:

```powershell
npm test
npm run smoke -- "wss://windbound-orchestra-relay.YOUR-SUBDOMAIN.workers.dev"
```

The smoke test verifies: room token, invalid room refusal, single-use pairing token, **early follower TLS bytes** buffered before conductor attachment, and bidirectional binary forwarding. It uses synthetic bytes, not actual band credentials or MIDI.

Next, use the full .NET client transport test from the repository root (needs .NET SDK):

```powershell
dotnet run --project Tests/OrchestraRelaySmokeTest.csproj -- "wss://windbound-orchestra-relay.YOUR-SUBDOMAIN.workers.dev" "Samples/Bard-37-Note-Calibration.mid"
```

Finally, test the same MIDI part in FFXIV with both **Edea (conductor)** and **Nunu (follower)** at 0 ms trim. Check whether the approximately one-second audible offset remains, and compare both relay reports. Also try a same-machine follower as a control.

### Important limits and safety

- **Do not expect a new relay to automatically fix clock-offset estimation.** A consistent 1-second remote follower lead can exist even when every START command arrives well before its future scheduled downbeat.
- The Worker logs only sanitized path/stage failures, **not** MIDI, bearer tokens, pairing keys, join codes or player names.
- The relay is public. Private room security relies on the unguessable 48-hex token and the existing inner TLS certificate pin. Avoid sharing join codes publicly.
- The pending-attachment buffer is capped at **10 KiB** to fit hibernation attachment storage; an unusually large pre-attach TLS flight may fail and need protocol improvements.
- Cloudflare Free usage quotas can stop new requests when exhausted. Watch your Workers and Durable Objects usage; consider load/rate-limit protection before opening the relay publicly.
- This is a source implementation for validation, **not a deployed service**. For your own deployment, Cloudflare supplies the Worker hostname.
- On a Worker deployment, changes to the code can briefly affect WebSockets. Do not redeploy during a live concert.

Useful official docs: https://developers.cloudflare.com/durable-objects/best-practices/websockets/ and https://developers.cloudflare.com/durable-objects/platform/limits/
