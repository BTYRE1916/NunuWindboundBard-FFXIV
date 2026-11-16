# Windbound Bard v0.46.0 hosted bands

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

## Run the transport checks from source

In the source folder, start the relay:

```text
dotnet run --project src/WindboundOrchestraRelay --no-launch-profile -- --urls http://127.0.0.1:48523
```

In another terminal, run the conductor and eight-follower smoke test:

```text
dotnet run --project Tests/OrchestraRelaySmokeTest.csproj -- ws://127.0.0.1:48523 Samples/Bard-37-Note-Calibration.mid
```
