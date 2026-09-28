---
title: Transports
description: Choose how multiplayer packets travel, LiteNetLib over UDP for real games, the deterministic in-process loopback transport for tests with simulated latency and loss, and the planned WebSocket transport.
sidebar:
  order: 12
---

A transport moves packets between processes. The networking module sits on top of it and does everything else
(handshake, security checks, serialization, snapshots), so every transport gets the same behavior and the same
protections. You register exactly one:

| Transport | Package | Register with | For |
|---|---|---|---|
| **LiteNetLib** (UDP) | `Ion.Extensions.Networking.LiteNetLib` | `builder.AddLiteNetLibTransport()` | Real games on a LAN or the internet |
| **Loopback** (in process) | `Ion.Extensions.Networking` | `builder.AddLoopbackTransport(network)` | Tests: a server and clients in one process, deterministic |
| **WebSocket** | planned | | Browser clients and relays |

Both `AddLiteNetLibTransport()` and `AddLoopbackTransport()` also call `AddNetworking()` for you, and the transport is
only started when `Ion:Network:Mode` is not `Offline`. Without a transport, a non-offline mode has nothing to start.

## LiteNetLib (UDP)

`LiteNetLibTransport` runs on [LiteNetLib](https://github.com/RevenantX/LiteNetLib) 2.1.4, a pure C# UDP library with
reliability channels. Only its transport layer is used: its reflection-based `NetPacketProcessor` and `NetSerializer`
are not, which keeps NativeAOT publishes free of warnings.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Networking;
using Ion.Extensions.Networking.LiteNetLib;

var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddNetworking().AddLiteNetLibTransport().AddSystem<GameSystem>();

using var game = builder.Build();
game.UseIon().UseNetworking().UseSystem<GameSystem>();
game.Run();
```

```xml
<ProjectReference Include="..\Ion\Ion.Extensions.Networking.LiteNetLib\Ion.Extensions.Networking.LiteNetLib.csproj" />
```

How it works:

- LiteNetLib receives and resends on its own threads. Its events are delivered on the **game thread** by `Poll` in
  `First` (no unsynchronized events), and each packet is copied into a pooled buffer, so the receive path does not
  allocate in steady state.
- `Flush` in `Last` asks LiteNetLib's logic thread to send immediately instead of at its next update (every 5 ms).
- MTU discovery is off and the MTU is fixed at **1,200 bytes**, so the largest unreliable or sequenced packet is known
  up front: **1,150 bytes** after LiteNetLib's headers. Reliable packets can be up to 32 KiB (LiteNetLib fragments them).
- An unreliable packet that does not fit is sent reliably instead (counted in `LiteNetLibTransport.Oversized`).
- LiteNetLib's own disconnect timeout is 10 seconds; Ion's `IdleTimeout` and `HandshakeTimeout` apply on top.
- A fixed connection key keeps stray LiteNetLib clients of other applications out before Ion's handshake runs. It is
  not a secret: use `JoinSecret` to restrict who may join.

| `Delivery` | LiteNetLib method |
|---|---|
| `Unreliable` | Unreliable |
| `Sequenced` | Sequenced |
| `ReliableUnordered` | ReliableUnordered |
| `ReliableOrdered` | ReliableOrdered |

### Addresses and ports

| Key | Default | Meaning |
|---|---|---|
| `Ion:Network:Bind` | `127.0.0.1` | Address the server listens on. IPv4 or IPv6. |
| `Ion:Network:Port` | `7777` | UDP port. `0` on a server picks a free one (`INetworkTransport.LocalPort`). |
| `Ion:Network:Connect` | `127.0.0.1` | Address a client connects to. |

A server bound to loopback accepts only clients on the same machine. For a LAN or internet server, bind to an interface
address or `0.0.0.0`; the server logs a warning that it accepts connections from other machines:

```bash
dotnet run -- --Ion:Headless=true --Ion:Network:Mode=Server --Ion:Network:Bind=0.0.0.0 --Ion:Network:JoinSecret=hunter2
dotnet run -- --Ion:Network:Mode=Client --Ion:Network:Connect=203.0.113.7 --Ion:Network:JoinSecret=hunter2
```

:::caution[Firewalls and NAT]
The server's UDP port must be reachable: open it in the host firewall and forward it on the router for internet play.
NAT punch-through is not implemented.
:::

## Loopback (tests)

`LoopbackTransport` connects processes that live in the same .NET process through a `LoopbackNetwork`. Nothing touches
a socket. Servers "listen" on a port number of that network (0 picks the next free one, from 40000) and clients connect
to it.

It is **deterministic**: packets are stamped with the sender's `IClock` and delivered when the receiver's clock reaches
the stamp plus the simulated latency and jitter. With a fixed-step test clock, the same test produces the same packets
in the same order every time.

```csharp
var network = new LoopbackNetwork();          // one per test; hosts on it see each other
builder.AddLoopbackTransport(network);        // or services.AddLoopbackTransport(network)
```

Without an argument, `AddLoopbackTransport()` uses `LoopbackNetwork.Shared`. Give every host of one test the same
network, and different tests different networks, so parallel tests do not see each other.

### Simulating a bad network

`Ion:Network:Simulate` adds latency, jitter, loss and reordering to what a loopback transport sends:

| Key | Default | Meaning |
|---|---|---|
| `Simulate:Latency` | `00:00:00` | One-way delay added to every packet. |
| `Simulate:Jitter` | `00:00:00` | A random extra delay between 0 and this, per packet. |
| `Simulate:Loss` | `0` | Probability (0 to 1) that an unreliable or sequenced packet is dropped. |
| `Simulate:Reorder` | `0` | Probability (0 to 1) that an unreliable packet is held back by one more latency, so later packets overtake it. |
| `Simulate:Seed` | `1` | Seed of the random decisions, so a run is repeatable. |

The loopback transport honors the delivery guarantees: unreliable and sequenced packets can be lost, unreliable ones
reordered, and reliable-ordered packets never overtake each other. `LoopbackTransport.Dropped` and `Reordered` count
what the simulation did.

:::note
The simulation settings are applied by the loopback transport. LiteNetLib does not read them.
:::

### Testing the real game over loopback

The Breakout Net tests run the game's own `Program.cs` twice in one process, a headless dedicated server and a headless
client, and swap the UDP transport for loopback:

```csharp
using System.Globalization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ion.Extensions.Networking;
using Ion.Testing;

static IonTestHost Host(LoopbackNetwork network, params string[] args)
{
    var host = new IonTestHost().UseEntryPoint<Program>(args);
    host.Configure(services => services.RemoveAll<INetworkTransport>().AddLoopbackTransport(network));
    return host;
}

var network = new LoopbackNetwork();
var server = Host(network, "--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0").Start();
var port = server.Get<NetworkSession>().Transport!.LocalPort.ToString(CultureInfo.InvariantCulture);
var client = Host(network, "--Ion:Headless=true", "--Ion:Network:Mode=Client", "--Ion:Network:Port=" + port,
    "--Ion:Network:Simulate:Latency=00:00:00.040", "--Ion:Network:Simulate:Loss=0.05").Start();

for (var frame = 0; frame < 900; frame++)
{
    server.Step();
    client.Step();
}

Assert.Equal(NetworkState.Connected, client.Get<INetworkSession>().State);
```

Stepping both hosts in lockstep on one thread makes the test deterministic. The Breakout Net convergence tests check at
every frame that the client holds exactly the server's replicated state at its newest snapshot, within 12 ticks of the
server, under clean loopback, 40 ms latency with jitter, 5 % loss and reordering, and over real UDP on `127.0.0.1` with
a join secret. See [Testing](/Ion/tooling/testing/).

:::tip[Real sockets in tests]
Over LiteNetLib, give its threads a moment each frame (`Thread.Sleep(1)` between steps), as the UDP convergence test
does, and bind to `127.0.0.1` with `Port=0`.
:::

## WebSocket transport (planned)

:::caution[Not built yet]
A WebSocket transport is designed as the second transport and the only one available to a browser build: server side on
the web module's HTTP core (see [HTTP server core](/Ion/networking/http-server/)), client side on `ClientWebSocket`. It
is not implemented. Browsers can talk to a game today only through the web server's own
[WebSocket endpoints](/Ion/networking/websockets/), which are not part of the multiplayer module.
:::

## Writing a transport

`INetworkTransport` is small, and the module's handshake and checks run on top of it, so a new transport (Steam, a
relay service) only moves bytes:

| Member | Called | Contract |
|---|---|---|
| `Name` | logs | A short name (`loopback`, `litenetlib`). |
| `IsRunning`, `LocalPort` | any time | State, and the actual port once started. |
| `StartServer(bind, port, maxConnections)` | `Init` | Listen. |
| `StartClient(address, port)` | `Init` | Connect; the server's connection id is 0. |
| `MaxPacketSize(delivery)` | any time | Largest packet `Send` accepts for that delivery. |
| `Send(connection, packet, delivery)` | `Last` | Queue a copy of the packet. |
| `Poll()` | `First` | Pump the transport once per frame. |
| `TryReceive(buffer, out TransportEvent)` | `First` | Next `Connected`, `Data` or `Disconnected` event; copy data into `buffer`. |
| `Flush()` | `Last` | Send everything queued. |
| `Disconnect(connection)`, `Stop()`, `Dispose()` | any time | Close. |

Rules: every method is called from the game thread only; socket work may run on a thread of your own but must hand
finished packets over through a queue and never block the game thread; the receive path must not allocate in steady
state. Register it as the `INetworkTransport` singleton:

```csharp
builder.AddNetworking();
builder.Services.AddSingleton<INetworkTransport, MyRelayTransport>();
```

## See also

- [Multiplayer overview](/Ion/networking/multiplayer/overview/)
- [Dedicated servers](/Ion/networking/multiplayer/dedicated-server/)
- [Testing](/Ion/tooling/testing/)
- [Breakout Net example](/Ion/examples/breakout-net/)
- Source: [INetworkTransport.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking.Abstractions/INetworkTransport.cs),
  [LiteNetLibTransport.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking.LiteNetLib/LiteNetLibTransport.cs),
  [LoopbackTransport.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Networking/LoopbackTransport.cs)
