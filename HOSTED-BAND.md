# Windbound Bard v0.46.2 hosted bands

## Join a band

1. Update Windbound Bard on each performer through the Dalamud repository. Open **BAND → Ensemble**, turn off **Use FFXIV native Ensemble**, then turn on **Enable Multi-Device** and **Use hosted band with join code**.
2. Set the host to **Conductor**. Enter the band relay's `wss://` address once, choose a stage name, click **Host Band**, then click **Copy band join code**. Share the code privately with the band.
3. Set other performers to **Follower**. Paste the code in **Band join code** and click **Join with Code**. The code includes the relay address. Performers can stay outside the FFXIV party.
4. Load a MIDI on the conductor and assign parts under **Band Track Assignments**. On each performing client, equip the instrument, open the expanded Performance keyboard, and ARM note output. Followers select **Wait for conductor cue**. When all participants show **READY**, the conductor clicks **Cue ensemble start**.

The relay supports one conductor and up to fifteen followers per room. The transport test covered nine clients. For a first in-game test above eight, give each part a distinctive entrance and listen from a nearby audience character.

## Run the included relay

The installer ZIP includes `Relay/WindboundOrchestraRelay.dll` and its runtime files. Extract the **Relay** folder to its own location on the host machine. Install the **.NET 10 ASP.NET Core Runtime** on that machine. Band members use the Windbound plugin and a join code.

For a local test, open **Start-Relay.cmd** from the Relay folder on Windows. On other platforms, run this from the Relay folder:

```text
dotnet WindboundOrchestraRelay.dll --urls http://127.0.0.1:48523
```

Use `ws://127.0.0.1:48523` in Windbound on that computer. The local launcher binds to the loopback interface.

### Host for performers on different networks

Run one relay instance behind a public HTTPS endpoint with a valid certificate. The reverse proxy must pass through WebSocket upgrades and the Authorization header. Enter the corresponding `wss://` address in the conductor's plugin. For example, a Caddy proxy on the same host can forward to the local relay:

```caddy
relay.example.com {
    reverse_proxy 127.0.0.1:48523
}
```

Replace the example domain with your actual DNS name. The relay's health check is `/healthz`.

The full source includes `Dockerfile.relay` for container hosts. Build it from the repository root with `docker build -f Dockerfile.relay -t windbound-relay .`. The container listens on port 10000. Direct your host's HTTPS route to that port, with one instance for the shared room directory.

Rooms are held in memory and close when the conductor disconnects, after four hours, or when the relay restarts. The relay sees connection metadata and traffic timing. MIDI, assignments, and cues travel through a separate encrypted connection between the conductor and each follower.

## Diagnose a relay disconnect

Open **BAND → Ensemble → Relay diagnostics** on the affected follower and conductor. Click **Copy relay report** on each before unloading the plugin. The history survives leaving or retrying a connection. UTC timestamps, elapsed time and the attempt label let you follow one connection through the report.

| Report entry | What it tells you |
| --- | --- |
| HTTP 404 during /v1/join | The requested room or route was unavailable. Check that the conductor still has the room open and is sharing its current code. |
| HTTP 101 | That WebSocket upgrade succeeded. Read the following attachment, TLS and band credential entries. |
| Close 4001 | The relay waited ten seconds for the conductor to attach. Inspect the conductor report and relay log at the same UTC time. |
| Conductor TLS handshake with native error 0x8009030E on v0.46.1 | Update the conductor to v0.46.2, host a fresh room and use its new code. The existing relay can serve this update. |
| TLS handshake failure | Check both clients for a certificate mismatch or native error code. |
| Connected band traffic | Pairing completed. The first send or receive failure, last-frame age and transport details describe what the client observed. |
| Connection stopped on this client | This client closed, cancelled or reset its connection. |

To match both clients to the service logs, redeploy the relay from this release. A successful pair shares a 12-character relay reference. Search for that reference in the relay console or hosting logs. The pairing entry also gives the room reference for looking up a conductor disconnect, service shutdown or room expiry. An older relay still accepts the connection and the plugin records local observations. An upstream proxy can return a status without a relay reference.

Each client retains the last 120 events until the plugin unloads. The report omits the join code, authentication data, relay address, player names, MIDI and server-supplied close text.

## Run the transport checks from source

In the source folder, start the relay:

```text
dotnet run --project src/WindboundOrchestraRelay --no-launch-profile -- --urls http://127.0.0.1:48523
```

In another terminal, run the conductor and eight-follower smoke test:

```text
dotnet run --project Tests/OrchestraRelaySmokeTest.csproj -- ws://127.0.0.1:48523 Samples/Bard-37-Note-Calibration.mid
```
