---
title: Multiplayer overview
description: The architecture of Ion's multiplayer module, server authority, fixed ticks, snapshots and messages, the Network and NetworkSend stages, AddNetworking setup and the Offline, Client, Server and ListenServer modes.
sidebar:
  order: 10
---

`Ion.Extensions.Networking` lets several processes running the same game share one simulation. One process is the
**server**: it owns the truth. Every other process is a **client**: it sends its player's inputs and draws what the
server tells it. The module replicates ECS components automatically, carries typed messages for everything discrete,
and gives clients the tools to hide latency (prediction, interpolation) and servers the tools to judge fairly (lag
compensation).

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Networking;
using Ion.Extensions.Networking.LiteNetLib;

var builder = IonApplication.CreateBuilder(args);
builder.AddIon()
    .AddNetworking()               // binds Ion:Network, registers the session, world, prediction (and the ECS module)
    .AddLiteNetLibTransport()      // UDP
    .AddSystem<GameSystem>();

using var game = builder.Build();
game.UseIon().UseNetworking().UseSystem<GameSystem>();   // adds the network steps (and the ECS systems)
game.Run();
```

```bash
dotnet run -- --Ion:Network:Mode=Server                         # a server with a window
dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server     # a dedicated server
dotnet run -- --Ion:Network:Mode=Client --Ion:Network:Connect=192.168.1.20
```

The game project also references the networking generator as an analyzer, which writes the serializers:

```xml
<ProjectReference Include="..\Ion\Ion.Extensions.Networking.Generators\Ion.Extensions.Networking.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

## Architecture

The design has five parts that build on each other:

1. **Fixed ticks.** The simulation advances in fixed steps (`FixedUpdate`). Every fixed step is a numbered **tick**, and
   every packet carries the tick it belongs to. The server's tick is authoritative.
2. **The snapshot ring.** At the end of every tick, the server copies every replicated component of every networked
   entity into a ring buffer of the last `SnapshotHistory` ticks (128 by default). Clients keep the snapshots they
   receive in the same shape, indexed by server tick. Everything else reads from this ring.
3. **Replication.** Components marked `[Replicated]` are sent from the ring to each client as **delta snapshots**
   against the last tick that client acknowledged, split into MTU-sized unreliable packets. Spawns and despawns are
   part of the snapshot. A client applies only the newest complete snapshot.
4. **Messages.** Discrete things (inputs, requests, chat, "a block broke") are `[NetworkMessage]` structs sent with
   `INetworkMessages` and read with `NetworkReader<T>`, with the delivery guarantee you choose.
5. **Client-side techniques.** [Prediction](/Ion/networking/multiplayer/prediction/) applies a client's own inputs
   immediately and reconciles with the server; [interpolation](/Ion/networking/multiplayer/interpolation/) draws other
   entities smoothly between snapshots; [lag compensation](/Ion/networking/multiplayer/lag-compensation/) lets the
   server rewind to what a client saw.

```text
          client                                             server
 ┌───────────────────────────┐                     ┌─────────────────────────────┐
 │ sample input  ──predict──▶│── PaddleInput ─────▶│ apply input for the tick    │
 │ own entities (predicted)  │   (unreliable,      │ simulate (FixedUpdate)      │
 │ remote ones (interpolated)│    3 per packet)    │ capture snapshot (ring)     │
 │ reconcile on mismatch  ◀──│◀── delta snapshot ──│ encode vs. peer's ack tick  │
 │ ack newest complete tick ─│── ack in header ───▶│                             │
 └───────────────────────────┘                     └─────────────────────────────┘
```

### Server authority

By default only the server writes replicated components (`Authority.Server`). A client that changes one locally keeps
its own value only until the server's value for it changes again.

`[Replicated(Authority = Authority.Owner)]` lets the entity's owning client write a component: the client sends its
values, the server checks that the sender owns the entity, and forwards them to everyone else. A `[Predicted]`
component is owner-authority too, but instead of trusting the client's values the server simulates it from the owner's
inputs, which is the right choice for anything that matters for fairness.

Every inbound message and update is checked against the sender's rights. A client may only send messages whose
`Direction` is `ClientToServer` or `Both`, and only owner-authority, non-predicted components of entities it owns.
Everything else is dropped and counted as a violation (malformed and rate-limited packets count too); a client with
more than `MaxViolations` (50) violations is disconnected with `DisconnectReason.Violations`.

### Network ids and ownership

Every entity with a replicated component gets a `NetworkId` on the server, automatically, when the tick is captured.
The id holds a dense slot (the low 20 bits, up to about one million entities), a generation (12 bits), and the owning
peer. Clients create and destroy their copies from spawn and despawn records and raise `NetworkEntitySpawned` and
`NetworkEntityDespawned` events.

To give an entity to a player, allocate its id for that peer when you create it:

```csharp
world.Create(network.Allocate(player), new Paddle(player.Id), new PaddleControl(x), new Transform2D(position));
```

Tag an entity with `NetworkLocal` to keep it out of replication even when it has replicated components (for example a
wall that every peer creates for itself).

`NetworkPeer` identifies a process: `NetworkPeer.Server` is id 0, clients are 1 to 254, and `NetworkPeer.None` is 255.

## The steps in a frame

`UseNetworking()` adds `NetworkSystem`, whose steps all sit in the engine order bands, so your steps at the default order
0 run between them regardless of registration order. See [Stages](/Ion/concepts/stages/) and the
[stage order reference](/Ion/reference/stage-order/).

| Stage | Order | Step | What it does |
|---|---|---|---|
| `Init` | `StageOrder.Network` (-870) | `Start` | Starts the transport in the configured role; a client begins the handshake. |
| `First` | -870 | `Poll` | Drains the transport, decodes messages onto the event bus, applies the newest complete snapshot (client), updates round trip times. |
| `First` | -860 | `Reconcile` | Client: compares the newest snapshot with its predictions and replays on a mismatch. |
| `FixedUpdate` (Begin scope) | -870 | `BeginTick` | Increments `INetworkWorld.CurrentTick`. |
| `FixedUpdate` | -860 | `Predict` | Client: samples, sends and applies inputs. Server: applies each client's input for the tick. |
| `FixedUpdate` | 0 | your systems | |
| `FixedUpdate` (End scope) | -870 | `EndTick` | Captures the snapshot of the tick. Runs in a `finally`, after every other fixed step including the ECS command playback, so a throwing step never leaves a tick without a snapshot. |
| `Render` | -870 | `Interpolate` | Client: writes interpolated values of remote entities. |
| `Last` | `StageOrder.NetworkSend` (870) | `Send` | Encodes snapshots per peer, packs pending messages, owner updates and pings, flushes the transport. |
| `Destroy` | 870 | `Stop` | Disconnects peers with a reason and stops the transport. |

Inbound messages are decoded in `First`, before any game step, and stay visible to readers for that frame and the next,
including in the frame's fixed steps. Outbound messages are sent in `Last`, after every game step.

## Modes

The role comes from `Ion:Network:Mode` (`NetworkConfig.Mode`):

| Mode | What it does |
|---|---|
| `Offline` (default) | No networking. The steps do nothing and no transport starts. `INetworkMessages` and friends exist, and sending does nothing. |
| `Client` | Connects to `Connect`:`Port`. |
| `Server` | A dedicated server: listens on `Bind`:`Port`. Usually run headless. |
| `ListenServer` | A server that is also a player: listens like `Server`, and its own entities (owned by `NetworkPeer.Server`) take local input. |

Set it on the command line, in `appsettings.json`, or in code. Breakout Net makes the listen server the default when no
mode is configured:

```csharp
var modeConfigured = builder.Configuration[$"{NetworkConfig.Section}:Mode"] is not null;

builder.AddNetworking(network =>
{
    if (!modeConfigured) network.Mode = NetworkMode.ListenServer;
    if (string.IsNullOrEmpty(network.GameId)) network.GameId = "ion-breakout-net";
});
```

The same game code runs in every mode. Check the role with `INetworkSession`:

```csharp
public sealed class ServerOnlySystem(INetworkSession session, World world)
{
    [FixedUpdate]
    public void Simulate(GameTime dt)
    {
        if (!session.IsServer) return;     // the server simulates; clients receive
        // ... move balls, break blocks, score
    }
}
```

| `INetworkSession` member | Meaning |
|---|---|
| `Mode`, `IsServer`, `IsClient` | The configured role. `IsServer` is true for `Server` and `ListenServer`. |
| `State` | `Offline`, `Connecting`, `Connected` or `Disconnected`. |
| `LocalPeer` | `NetworkPeer.Server` on a server, the assigned id on a connected client, else `NetworkPeer.None`. |
| `DisconnectReason` | Why a client was refused or dropped. |
| `Peers` | Connected peers (a server's clients, or a client's server). |
| `RoundTripTime(peer)` | Last measured round trip. |
| `Stats` | Counters: bytes, packets, messages, rejected, malformed, rate limited, snapshots, corrections, rewinds, and more. |
| `Disconnect(peer, reason)` | Kick a client (server) or leave (client). |

## Connection events

The session raises ordinary events on `IEvents` (see [Events](/Ion/concepts/events/)):

| Event | Raised on | When |
|---|---|---|
| `PeerConnected(Peer)` | Server; a client for itself | A handshake was accepted. |
| `PeerDisconnected(Peer, Reason)` | Server; a client (with `NetworkPeer.Server`) | A peer left or was dropped. |
| `NetworkEntitySpawned(Entity, Id)` | Client | A replicated spawn created a local entity. |
| `NetworkEntityDespawned(Id)` | Client | A replicated despawn destroyed one. |

```csharp
public sealed class Lobby(IEvents events, INetworkSession session, INetworkWorld network, World world)
{
    private EventReader<PeerConnected> _joined = events.Reader<PeerConnected>();

    [First(Order = 10)]
    public void Players(GameTime dt)
    {
        if (!session.IsServer) return;
        foreach (ref readonly var joined in _joined.Read())
            if (joined.Peer.IsClient) world.Create(network.Allocate(joined.Peer), new Paddle(joined.Peer.Id));
    }
}
```

## The handshake

Before a single game packet is parsed, the server sends a random 16-byte challenge and the client answers with:

- the protocol version,
- a hash of the game id (`GameId`, else the game's `Title`),
- the **registry hash** (a hash over every replicated type and message: names, layouts, authority, prediction,
  interpolation, delivery and direction),
- the tick rate,
- HMAC-SHA256 of the challenge keyed with `JoinSecret`, when one is set (the secret never crosses the network).

A mismatch refuses the client with a `DisconnectReason`: `ProtocolMismatch`, `GameMismatch`, `RegistryMismatch`,
`TickRateMismatch`, `WrongSecret` or `ServerFull`. A connection that does not complete in `HandshakeTimeout` (5 s) is
closed. The registry hash is why client and server must be built from the same game code: any change to a replicated
struct changes the hash.

## Configuration

All keys are under `Ion:Network` (`NetworkConfig`):

| Key | Default | Meaning |
|---|---|---|
| `Mode` | `Offline` | `Offline`, `Client`, `Server` or `ListenServer`. |
| `Bind` | `127.0.0.1` | Address a server listens on. A non-loopback bind is logged as a warning. |
| `Port` | `7777` | Server port and the port a client connects to. `0` on a server picks a free one. |
| `Connect` | `127.0.0.1` | Server address a client connects to. |
| `TickRate` | `0` | Ticks per second; `0` takes `GameConfig.FixedUpdateRate`. Must match on both sides. |
| `SnapshotHistory` | `128` | Ticks kept in the ring. |
| `SendRate` | `0` | Snapshots per second per client; `0` sends one per tick. |
| `MaxPeers` | `16` | Clients a server accepts (at most 254). |
| `GameId` | empty | Game identifier checked in the handshake; empty uses the game title. |
| `JoinSecret` | none | When set, clients must prove they know it. |
| `MaxRewindTicks` | `12` | Furthest back lag compensation rewinds. |
| `InterpolationDelay` | `2` | Ticks behind the newest snapshot that remote entities are drawn. |
| `MaxExtrapolationTicks` | `2` | Ticks a remote entity is extrapolated past the newest snapshot before it holds. |
| `InputLeadTicks` | `2` | Ticks a client runs ahead of its estimate of the server tick. |
| `MtuBytes` | `1200` | Largest packet; snapshots are split into parts of this size. |
| `IdleTimeout` | `00:00:10` | A silent peer is disconnected. |
| `HandshakeTimeout` | `00:00:05` | An incomplete handshake is closed. |
| `PingInterval` | `00:00:00.250` | How often round trip time is measured. |
| `MaxMessagesPerSecond` | `600` | Per-client message rate before the server drops them. |
| `MaxBytesPerSecond` | `262144` | Per-client byte rate before the server drops packets. |
| `MaxViolations` | `50` | Violations (unauthorized messages or updates, malformed or rate-limited packets) a client may accumulate before it is disconnected. |
| `Simulate:Latency`, `Simulate:Jitter`, `Simulate:Loss`, `Simulate:Reorder`, `Simulate:Seed` | off, seed `1` | A simulated network (see [Transports](/Ion/networking/multiplayer/transports/)). |

## Metrics and inspection

With the metrics module, the session publishes counters and gauges named `net_bytes_in`, `net_bytes_out`,
`net_packets_in`, `net_packets_out`, `net_messages_in`, `net_messages_out`, `net_rejected`, `net_malformed`,
`net_rate_limited`, `net_handshakes_rejected`, `net_snapshots`, `net_full_snapshots`, `net_snapshots_lost`,
`net_rewinds`, `net_rewinds_rejected`, `net_corrections`, `net_resyncs`, `net_snapshot_bytes`, `net_peers` and
`net_rtt_ms`. See [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).

With the remote protocol, the watchable `network.status` method returns the mode, state, local peer, ticks, peers with
round trip times, and the counters. See [Remote protocol](/Ion/tooling/remote-protocol/).

## Performance

A frame of a server and a client over the loopback transport (200 moving entities, messages both ways, prediction and
interpolation) allocates **0 bytes** in steady state. Over UDP the receive path allocates nothing except when a burst
larger than any before it grows a queue. Snapshot encoding finds changed entities with a vectorized pass per component
column, so unchanged entities cost almost nothing.

## What is not built yet

:::caution[Planned]
- **WebSocket transport and browser clients.** Designed as the second transport (server side on the web module's HTTP
  core), not implemented.
- **Grid interest management.** The `IInterestPolicy` hook exists and works, but no ready-made spatial policy ships;
  write your own.
- **Replication in scene worlds.** The session is bound to the root ECS world; entities inside
  [scene](/Ion/ecs/scenes/) worlds are not replicated.
- Whole-schedule rollback and resimulation (only registered prediction steps are replayed), clock-rate nudging (drift is
  corrected by resynchronizing), NAT punch-through, and bandwidth-aware snapshot prioritization.
:::

## See also

- [Messages and replication](/Ion/networking/multiplayer/messages/)
- [Transports](/Ion/networking/multiplayer/transports/)
- [Dedicated servers](/Ion/networking/multiplayer/dedicated-server/)
- [Breakout Net example](/Ion/examples/breakout-net/)
- [ECS overview](/Ion/ecs/overview/) and [Time and determinism](/Ion/concepts/time-and-determinism/)
- Design notes: [docs/design/ion-networking.md](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-networking.md)
