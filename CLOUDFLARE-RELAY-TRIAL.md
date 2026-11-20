# Windbound Cloudflare Relay Trial for v0.47.3

Windbound Bard **v0.47.3** includes the optional Cloudflare Workers relay source. Hosted Band uses `wss://windbound-orchestra-relay.nunu-orchestra-2026-4728.workers.dev` by default. A conductor can enter a custom URL for a different deployment. See [the Windows build guide](BUILD-v0.47.3.md) for the plugin installer.

**Deploy guide:** `CloudflareRelay/README.md`

**Windows PowerShell commands:**

```powershell
Set-Location ".\CloudflareRelay"
npm install
npx wrangler login
npm test
npx wrangler deploy
```

After deployment: `npm run smoke -- "wss://windbound-orchestra-relay.YOUR-SUBDOMAIN.workers.dev"`, replacing `YOUR-SUBDOMAIN` with the exact hostname Cloudflare gives you. The conductor sets the same address in Ensemble → Hosted Band, hosts, and shares the usual join code. The follower does not need a Cloudflare account or VPN.

The Render service at `wss://nunuwindboundbard-ffxiv.onrender.com` is deliberately unchanged and available as a fallback. Keep it until Cloudflare live duet tests are successful.

**Validation:** Offline Node protocol and mock integration tests passed. No real Cloudflare deployment, full .NET transport test, FFXIV live duet or octet test has been performed here.
