---
title: Web and networking overview
description: The two networking stacks in Ion, the embedded web server for browsers and tools and the multiplayer module for game clients, and how to pick between them.
sidebar:
  order: 1
---

Ion ships two independent ways for a running game to talk over a network. They solve different problems, use different
protocols and have different security models, so pick the one that matches who is on the other end of the wire.

| You want to... | Use | Module |
|---|---|---|
| Let a phone, a browser page, a dashboard, a level editor, a webhook or a script read and change the game | The **web server** | `Ion.Extensions.Web` (on the shared core in `Ion.Extensions.Http`) |
| Let several copies of your game play together, with one authoritative simulation | The **multiplayer module** | `Ion.Extensions.Networking` plus a transport (`Ion.Extensions.Networking.LiteNetLib`) |
| Inspect or drive a running game from an agent, the `ion` CLI or an MCP client | The **remote protocol** | `Ion.Extensions.Remote` (see [Remote protocol](/Ion/tooling/remote-protocol/)) |

The remote protocol is a tooling feature documented in the tooling section, but it runs on the same HTTP core as the web
server and can be mounted on the web server's port, so it shows up on these pages too.

## The web stack: HTTP and WebSockets for browsers and tools

`Ion.Extensions.Web` embeds a small HTTP/1.1 and WebSocket server inside the game process. You write endpoints as
ordinary methods on systems and mark them with attributes:

```csharp
[WebJson(typeof(GameJson))]
public sealed class ScoreEndpoints(ScoreBoard board)
{
    [Http("GET", "/score")]
    public ScoreInfo Score() => new(board.Score, board.Lives);

    [WebSocket("/events", Access = WebAccess.Read)]
    public void Events(in WebSocketMessage message)
    {
        if (message.Kind == WebSocketMessageKind.Connected) message.Client.Send("hello");
    }
}
```

A source generator turns these methods into a static route table (no reflection, NativeAOT-safe), and the server calls
them **on the game thread**, at the end of the frame (`StageOrder.Web`, 960), so handlers can touch game state without
locks. It is designed for:

- **Companion apps**: a phone as a controller, a second-screen map, a spectator view. The
  [Companion example](/Ion/examples/companion/) turns every connected phone into a virtual gamepad.
- **Tools**: level editors, tuning dashboards, debug panels served as static files from the game itself.
- **Integrations**: webhooks, stream overlays, chat bots, anything that speaks HTTP or WebSocket.

Its defaults are conservative: it is off unless `Ion:Web:Enabled` is `true`, listens on `127.0.0.1:15780`, refuses
browser origins it does not know, and requires a bearer token for mutating endpoints whenever it is exposed to a LAN.

Read on:

- [HTTP server core](/Ion/networking/http-server/): the listener, threading model, configuration and limits.
- [Web endpoints](/Ion/networking/web-endpoints/): `[Http]` routes, parameter binding, responses, the generator and
  diagnostics `ION401` to `ION407`.
- [WebSockets](/Ion/networking/websockets/): `[WebSocket]` endpoints, messages, push channels and the phone controller.
- [Web security](/Ion/networking/web-security/): loopback defaults, tokens, origins and CORS, rate limits, and what is
  not supported (TLS, HTTP/2).

## The multiplayer stack: replicated game state over UDP

`Ion.Extensions.Networking` is a server-authoritative, fixed-tick replication system for games that run as several
processes: one server (dedicated or a player's own game as a listen server) and any number of clients running the same
game code.

```csharp
[Replicated]
public record struct Health(int Current, int Max);

[NetworkMessage(Direction = MessageDirection.ClientToServer, Delivery = Delivery.Unreliable)]
public record struct MoveInput(Vector2 Move);
```

You mark ECS components as `[Replicated]` and the server streams delta-encoded snapshots of them to every client; you
declare discrete events (inputs, chat, commands) as `[NetworkMessage]` structs and send them with `INetworkMessages`.
On top of that it provides client-side prediction with reconciliation, snapshot interpolation, lag compensation, a
handshake with an optional join secret, per-message authority checks and rate limits. Serializers are generated at
compile time.

Read on:

- [Multiplayer overview](/Ion/networking/multiplayer/overview/): architecture, ticks, stages, setup and modes.
- [Messages and replication](/Ion/networking/multiplayer/messages/): `[Replicated]`, `[NetworkMessage]`, generated
  serializers and diagnostics `ION201` to `ION210`.
- [Transports](/Ion/networking/multiplayer/transports/): LiteNetLib (UDP), the loopback transport for tests, and the
  planned WebSocket transport.
- [Prediction](/Ion/networking/multiplayer/prediction/), [Interpolation](/Ion/networking/multiplayer/interpolation/)
  and [Lag compensation](/Ion/networking/multiplayer/lag-compensation/).
- [Dedicated servers](/Ion/networking/multiplayer/dedicated-server/): running headless, configuration and publishing.

## Which one should I use?

| Question | Web server | Multiplayer module |
|---|---|---|
| Who is the peer? | A browser, a phone, `curl`, a script, another service | Another copy of your game |
| Protocol | HTTP/1.1 and WebSocket (RFC 6455), JSON or text or bytes | Binary packets over UDP (LiteNetLib) or in-process loopback |
| Data model | Request/response and push messages you design | Replicated ECS components and typed messages |
| Timing | Handled once per frame; a request waits for the end of its frame | Fixed ticks; every packet carries its tick |
| Who is authoritative? | The game; the web is a remote control | The server; clients predict and are corrected |
| Default exposure | Off; loopback `127.0.0.1:15780` when enabled | Offline; a server binds loopback `127.0.0.1:7777` |
| Access control | Bearer token for mutating endpoints, origin allow-list, Host check | Handshake with protocol, game and registry hashes, optional HMAC join secret, per-message authority |
| Allocation | Zero per small request in steady state | Zero per frame in steady state |

Rules of thumb:

- If the other side is **not** your game (it is a browser, a tool, or a service), use the web server.
- If the other side **is** your game and players see each other's actions, use the multiplayer module.
- A game can use both at once. They listen on different ports and do not share state: for example a dedicated
  multiplayer server can also enable the web server to expose a `GET /status` page or an admin WebSocket.

:::note[Browser multiplayer clients]
A browser build of the game joining a multiplayer session needs the WebSocket transport, which is designed but not
built yet. Today, browsers talk to the game through the web server; multiplayer clients use LiteNetLib over UDP.
:::

## Packages

| Package | Holds |
|---|---|
| `Ion.Extensions.Http` | The shared server core: blocking listener, request parser, response writer, WebSocket framing, security policy, rate limiter, lock-free queue. Not a module on its own. |
| `Ion.Extensions.Web.Abstractions` | `[Http]`, `[WebSocket]`, `[FromBody]`, `[WebJson]`, `WebAccess`, `WebRequest`, `WebResponse`, `WebSocketMessage`, `WebSocketClient`, `WebSocketChannel`, `IWebServer`, route table types |
| `Ion.Extensions.Web.Generators` | The routing generator and diagnostics `ION401` to `ION407` (reference as an analyzer) |
| `Ion.Extensions.Web` | `WebServer`, `WebSystem`, `WebOptions`, `AddWeb`/`UseWeb`/`AddWebRoutes`, static files |
| `Ion.Extensions.Networking.Abstractions` | Attributes, `NetworkConfig`, `INetworkMessages`, `NetworkReader<T>`, `INetworkSession`, `INetworkWorld`, `INetworkPrediction`, `INetworkTransport`, `NetworkId`, `FixedString32/64/128` |
| `Ion.Extensions.Networking.Generators` | Serializers, registry and diagnostics `ION201` to `ION210` (reference as an analyzer) |
| `Ion.Extensions.Networking` | `AddNetworking`/`UseNetworking`, the session, snapshot ring, prediction, interpolation, lag compensation, `LoopbackTransport` |
| `Ion.Extensions.Networking.LiteNetLib` | `AddLiteNetLibTransport()`: UDP on LiteNetLib 2.1.4 |

## See also

- [Stages](/Ion/concepts/stages/) and [Stage order reference](/Ion/reference/stage-order/) for where `Web`, `Network`
  and `NetworkSend` run in a frame.
- [Configuration reference](/Ion/reference/configuration/) for every `Ion:Web` and `Ion:Network` key.
- [Diagnostics reference](/Ion/reference/diagnostics/) for `ION2xx` and `ION4xx`.
- [Breakout Net example](/Ion/examples/breakout-net/) and [Companion example](/Ion/examples/companion/).
