---
title: Dedicated servers
description: Run the same game as a headless dedicated server with --Ion:Headless=true --Ion:Network:Mode=Server, configure binding, secrets and limits, monitor it, and publish it as a NativeAOT executable.
sidebar:
  order: 16
---

Ion has no separate server project. A dedicated server is **the same game** started headless in server mode:

```bash
dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server
```

`Ion:Headless=true` registers the null graphics and audio backends (no window, no GPU, no audio device), and
`Ion:Network:Mode=Server` starts the transport as a server. Your client-only systems keep running but have nothing to
draw; the usual pattern is to check the session mode and return early, as Breakout Net's presentation system does.

`--headless` is a short form of `--Ion:Headless=true`:

```bash
dotnet run -- --headless --Ion:Network:Mode=Server
```

## Writing the game for both roles

Everything is decided by `INetworkSession.Mode`, so one `Program.cs` serves as client, listen server and dedicated
server. The Breakout Net sample, trimmed:

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Networking;
using Ion.Extensions.Networking.LiteNetLib;
using Ion.Extensions.Physics2D;

var builder = IonApplication.CreateBuilder(args);

// NativeAOT needs every stored component registered with Arch up front.
EcsComponents.Register<Wall>();

var modeConfigured = builder.Configuration[$"{NetworkConfig.Section}:Mode"] is not null;
var headless = builder.Configuration.IsHeadless();

builder.AddIon()
    .AddEcsRendering()
    .AddPhysics2D(physics => physics.GravityY = 0)
    .AddNetworking(network =>
    {
        if (!modeConfigured) network.Mode = NetworkMode.ListenServer;   // default: host and play
        if (string.IsNullOrEmpty(network.GameId)) network.GameId = "ion-breakout-net";
    })
    .AddLiteNetLibTransport()
    .AddSystem<PaddlePredictionSystem>()
    .AddSystem<ServerGameSystem>()
    .AddSystem<PresentationSystem>()
    .AddSystem<PlayerInputSystem>();
builder.Services.AddSingleton<PaddleInputSource>();
if (headless) builder.AddSystem<NetAutopilotSystem>();

using var game = builder.Build();
game.UseIon()
    .UseEcsRendering()
    .UsePhysics2D()
    .UseNetworking()
    .UseSystem<PaddlePredictionSystem>()
    .UseSystem<ServerGameSystem>()
    .UseSystem<PresentationSystem>()
    .UseSystem<PlayerInputSystem>();
if (headless) game.UseSystem<NetAutopilotSystem>();
game.Run();
```

and inside the systems:

```csharp
public sealed class ServerGameSystem(World world, INetworkSession session)
{
    [Init]
    public void Init(GameTime dt)
    {
        if (!session.IsServer) return;          // dedicated or listen server builds the level
        CreateLevel();
    }
}

public sealed class PresentationSystem(INetworkSession session, IAssetManager assets)
{
    private bool Shows => session.Mode is NetworkMode.Client or NetworkMode.ListenServer;

    [Init]
    public void Init(GameTime dt)
    {
        if (!Shows) return;                     // a dedicated server loads no textures or sounds
        // assets.Load<ITexture2D>(...)
    }
}
```

The same executable then runs in three ways:

```bash
dotnet run                                                   # listen server: hosts and plays
dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server   # dedicated server
dotnet run -- --Ion:Network:Mode=Client                      # client of Ion:Network:Connect
```

:::tip[With the ion CLI]
`ion run` takes `--headless` as a flag and passes everything after `--` to the game:

```bash
ion run Ion.Examples/Ion.Examples.Breakout.Net --headless -- --Ion:Network:Mode=Server
```

A dedicated `--server` shortcut for the CLI is planned but not built.
:::

## Server configuration

Put server settings in `appsettings.json`, an environment-specific file, environment variables, or the command line.
Everything binds through standard .NET configuration (see
[Services and configuration](/Ion/concepts/services-and-configuration/)).

```json title="appsettings.json"
{
  "Ion": {
    "Title": "My Game",
    "MaxFPS": 120,
    "Network": {
      "Bind": "0.0.0.0",
      "Port": 7777,
      "MaxPeers": 16,
      "GameId": "my-game",
      "SendRate": 30,
      "MaxRewindTicks": 12
    }
  }
}
```

```bash
# Secrets from the environment, not from source control
export Ion__Network__JoinSecret="$(openssl rand -hex 24)"
./MyGame --Ion:Headless=true --Ion:Network:Mode=Server
```

The settings that matter most for a server:

| Key | Default | For a dedicated server |
|---|---|---|
| `Ion:Headless` | `false` | `true`: no window, GPU or audio device. |
| `Ion:Network:Mode` | `Offline` | `Server`. |
| `Ion:Network:Bind` | `127.0.0.1` | An interface address or `0.0.0.0` so other machines can connect. A non-loopback bind is logged as a warning. |
| `Ion:Network:Port` | `7777` | The UDP port to open in the firewall. |
| `Ion:Network:MaxPeers` | `16` | Clients accepted (at most 254). Extra clients are refused with `ServerFull`. |
| `Ion:Network:JoinSecret` | none | Clients must prove they know it (HMAC over a random challenge; the secret never crosses the network). |
| `Ion:Network:GameId` | the title | Keeps other games' clients out (`GameMismatch`). |
| `Ion:Network:TickRate` | `Ion:FixedUpdateRate` (60) | Must match the clients (`TickRateMismatch`). |
| `Ion:Network:SendRate` | every tick | Lower to save bandwidth; raise `InterpolationDelay` on clients to match. |
| `Ion:Network:MaxMessagesPerSecond`, `MaxBytesPerSecond` | `600`, `262144` | Per-client inbound budgets; excess is dropped and counted. |
| `Ion:Network:MaxViolations` | `50` | Violations before a client is disconnected (`Violations`). |
| `Ion:Network:IdleTimeout`, `HandshakeTimeout` | 10 s, 5 s | Silent peers and stalled handshakes are dropped. |
| `Ion:MaxFPS` | `300` | Caps frames per second. Keep it at or above the tick rate; a server has nothing to draw, so a lower cap saves CPU. |

The full list is in the [multiplayer overview](/Ion/networking/multiplayer/overview/) and the
[configuration reference](/Ion/reference/configuration/).

:::danger[A server is a security boundary]
A server bound to a public address accepts packets from the whole internet. Ion refuses clients with the wrong
protocol, game, registry or tick rate before parsing any game packet, checks every message against the sender's
authority, rate-limits every client, and survives malformed packets (a fuzz test sends thousands of random packets both
ways without an exception). It does not encrypt traffic. Set a `JoinSecret` for private servers, keep game logic
server-authoritative, and never trust owner-authority components for anything that decides the outcome.
:::

## What the server logs

The `Ion.Networking` log category reports the lifecycle. With the console logger, a session looks like this (the hash
and connection numbers vary):

```text
warn: Ion.Networking Network server bound to the public address 0.0.0.0:7777: it accepts connections from other machines.
info: Ion.Networking Network server started on 0.0.0.0:7777 (litenetlib, 60 ticks per second, registry 3F2A...).
info: Ion.Networking Client peer 1 joined (connection 0).
warn: Ion.Networking Refused a client on connection 1: WrongSecret.
info: Ion.Networking Client peer 1 left: Timeout.
```

The registry hash in the start line is the one clients must match; a `RegistryMismatch` means the client was built from
different game code.

## Monitoring

- **Metrics.** With the metrics module, `net_peers`, `net_rtt_ms`, `net_bytes_in/out`, `net_rejected`,
  `net_rate_limited`, `net_handshakes_rejected`, `net_snapshot_bytes` and the other `net_*` instruments are published.
  See [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).
- **Remote protocol.** Start the server with `--remote` and read or watch `network.status` (mode, state, ticks, peers
  with round trip times, counters). The remote protocol binds loopback, so run it on the server machine or through an
  SSH tunnel. See [Remote protocol](/Ion/tooling/remote-protocol/).
- **A status page.** The web module can run next to the multiplayer server on its own port, for example to expose
  `GET /status` for a load balancer or a dashboard. See [Web endpoints](/Ion/networking/web-endpoints/).

```csharp
public sealed class StatusEndpoints(INetworkSession session)
{
    [Http("GET", "/players")]
    public int Players() => session.Peers.Count;
}
```

## Shutdown

When the game exits, the `Destroy` step at `StageOrder.NetworkSend` disconnects every client with
`DisconnectReason.Shutdown` and stops the transport, so clients see a clean `PeerDisconnected` rather than a timeout.

## Publishing

A dedicated server publishes like any Ion game, as a NativeAOT executable with no .NET runtime to install. The Breakout
Net sample is in the NativeAOT CI lane and publishes without LiteNetLib or `Ion.*` warnings.

```bash
dotnet publish Ion.Examples/Ion.Examples.Breakout.Net -p:IonTarget=linux-x64
# or
ion publish Ion.Examples/Ion.Examples.Breakout.Net --target linux-x64
```

Then run the published binary on the server machine:

```bash
./Ion.Examples.Breakout.Net --Ion:Headless=true --Ion:Network:Mode=Server --Ion:Network:Bind=0.0.0.0
```

Notes:

- Publish with `appsettings.json` copied to the output (`CopyToOutputDirectory`): it is read from the executable's
  folder, so a service manager can start the server from any working directory. Override per machine with
  environment variables or arguments.
- Register every ECS component you store with `EcsComponents.Register<T>()` before building; the networking generator
  registers replicated ones and physics modules register theirs.
- A game that links Box2D statically (`<Box2DStaticLink>true</Box2DStaticLink>`, as Breakout Net does) ships no
  `libbox2d` next to the executable.
- Clients and servers must come from the same build of the replicated types and messages, or the handshake refuses
  them with `RegistryMismatch`.

See [Publishing](/Ion/platforms/publishing/) and [Native AOT](/Ion/platforms/native-aot/).

## Testing a dedicated server

`IonTestHost.UseEntryPoint<Program>(args)` runs your real `Program.cs`, so a test can start a dedicated server and a
client exactly as they run in production, headless, in one process:

```csharp
var server = new IonTestHost()
    .UseEntryPoint<Program>(["--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0"])
    .Start();
Assert.Equal(NetworkMode.Server, server.Get<INetworkSession>().Mode);
```

See [Transports](/Ion/networking/multiplayer/transports/) for swapping UDP for the deterministic loopback transport, and
[Testing](/Ion/tooling/testing/).

## Not built yet

:::caution[Planned]
- A `--server` shortcut in the `ion` CLI.
- The WebSocket transport, so browser clients can join.
- A ready-made grid interest policy for large worlds (write an `IInterestPolicy` for now).
- Replication inside scene worlds; replicated entities must live in the root ECS world.
- NAT punch-through, and bandwidth-aware prioritization of snapshot contents.
:::

## See also

- [Multiplayer overview](/Ion/networking/multiplayer/overview/)
- [Transports](/Ion/networking/multiplayer/transports/)
- [Breakout Net example](/Ion/examples/breakout-net/)
- [Desktop platforms](/Ion/platforms/desktop/)
- [ion CLI](/Ion/tooling/ion-cli/)
