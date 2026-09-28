---
title: WebSockets
description: Handle WebSocket clients on the game thread with [WebSocket] endpoints, reply to one client or broadcast to all through push channels, and build a phone controller like the Companion example.
sidebar:
  order: 4
---

A WebSocket endpoint is a `void` method on a system with a `[WebSocket(path)]` attribute. The web server calls it on
the game thread, at the end of the frame, once when a client connects, once per message, and once when it disconnects.
The game can push to one client or to every client of the endpoint at any time, from any thread, without blocking.

```csharp
using Ion;
using Ion.Extensions.Web;

public sealed class EchoEndpoints
{
    [WebSocket("/echo", Access = WebAccess.Read)]
    public void Echo(in WebSocketMessage message)
    {
        if (!message.IsMessage) return;
        message.Client.Send(message.Data);   // reply with the same bytes
    }
}
```

Register it like any endpoint system (see [Web endpoints](/Ion/networking/web-endpoints/)) and connect from a browser:

```js
const socket = new WebSocket("ws://127.0.0.1:15780/echo", ["ion"]);
socket.onmessage = (e) => console.log("game says", e.data);
socket.onopen = () => socket.send("hello");
```

## The handler

The method must have exactly this shape, or the generator reports `ION403`:

```csharp
public void AnyName(in WebSocketMessage message)
```

`[WebSocket]` may appear once per method. The path is literal segments only (no `{parameters}`, `ION401`), and two
endpoints with the same path are `ION402`.

| `[WebSocket]` member | Meaning |
|---|---|
| `Route` (constructor) | The path, for example `/events`. |
| `Access` | `WebAccess.Auto` (default), `Read` or `Mutate`. For WebSockets, `Auto` means **Mutate**: connecting needs the token when one is configured. Set `Access = WebAccess.Read` for read-only feeds. |

## Messages

`WebSocketMessage` is a `ref struct` that describes one event:

| Member | Meaning |
|---|---|
| `Kind` | `Connected`, `Text`, `Binary` or `Disconnected` (`WebSocketMessageKind`). |
| `Client` | The `WebSocketClient` it concerns. |
| `Channel` | The endpoint's `WebSocketChannel` (same as `Client.Channel`). |
| `Data` | The payload of a `Text` or `Binary` message, as `ReadOnlySpan<byte>`. **Valid during the call only**: the buffer is reused. |
| `IsMessage` | True for `Text` and `Binary`. |
| `Text` | The payload decoded as a UTF-8 `string` (allocates). |
| `TryReadJson<T>(JsonTypeInfo<T>, out T?)` | Deserializes the payload with source-generated metadata; false when it is not valid JSON of that shape. |

The lifecycle of one client:

1. **`Connected`**: the WebSocket handshake succeeded. The client has already joined the endpoint's channel, so a
   broadcast in this call reaches it.
2. **`Text`** / **`Binary`**: zero or more messages, in the order they arrived. Fragmented messages are reassembled
   before you see them.
3. **`Disconnected`**: always the last event for the client. It leaves the channel after the call.

Messages share the web server's queue with HTTP requests, so everything is handled in arrival order across clients and
protocols, at most `MaxRequestsPerFrame` per frame.

:::caution[Do not keep Data]
`message.Data` points into a buffer the server recycles as soon as your handler returns. Copy what you need
(`message.Data.ToArray()`, parse it, or store `message.Text`) before returning.
:::

## Clients

`WebSocketClient` represents one connection:

| Member | Meaning |
|---|---|
| `Id` | A number unique among the server's clients, from 1. |
| `RemoteAddress` | The client's IP address. |
| `IsAuthenticated` | Whether it presented the token (always true when none is configured). |
| `IsClosed` | Whether the connection is closed. |
| `State` | An `object?` slot for the game, for example the player index this client controls. |
| `SentCount` | Messages sent to the client. |
| `Send(ReadOnlySpan<byte>)`, `Send(string)` | Send a text message (UTF-8). Returns false when the client is gone. |
| `SendBinary(ReadOnlySpan<byte>)` | Send a binary message. |
| `SendJson<T>(value, JsonTypeInfo<T>)` | Serialize and send as a text message. |
| `Close(ushort code = 1000)` | Close the connection with a status code. |

Use `State` to associate a client with game data instead of a dictionary:

```csharp
[WebSocket("/join")]
public void Join(in WebSocketMessage message)
{
    switch (message.Kind)
    {
        case WebSocketMessageKind.Connected:
            message.Client.State = _lobby.AddPlayer();
            break;
        case WebSocketMessageKind.Text when message.Client.State is int player:
            _lobby.Chat(player, message.Text);
            break;
        case WebSocketMessageKind.Disconnected when message.Client.State is int player:
            _lobby.RemovePlayer(player);
            break;
    }
}
```

## Pushing from the game: channels

Every `[WebSocket]` endpoint has a `WebSocketChannel`: the set of its connected clients. Get it from
`IWebServer.Channel(path)` (it throws `KeyNotFoundException` for a path with no endpoint) or from `message.Channel`
inside a handler.

| `WebSocketChannel` member | Meaning |
|---|---|
| `Path` | The endpoint's path. |
| `Count` | Connected clients. |
| `Broadcast(ReadOnlySpan<byte>)`, `Broadcast(string)` | Queue a text message for every client. Returns how many it was queued for. |
| `BroadcastBinary(ReadOnlySpan<byte>)` | Queue a binary message for every client. |
| `BroadcastJson<T>(value, JsonTypeInfo<T>)` | Serialize once and queue for every client. |
| `BroadcastCount` | Messages queued by broadcasts (one per client reached). |
| `Clients()` | A copy of the connected clients (allocates). |

A typical pattern pushes state when it changes, from an ordinary `[Update]` step:

```csharp
[WebJson(typeof(GameJson))]
public sealed class ScoreFeed(ScoreBoard board, IWebServer web)
{
    private int _pushed = -1;

    [WebSocket("/score", Access = WebAccess.Read)]
    public void Score(in WebSocketMessage message)
    {
        // Greet each new client with the current score.
        if (message.Kind == WebSocketMessageKind.Connected)
            message.Client.SendJson(new ScoreInfo(board.Score, board.Lives), GameJson.Default.ScoreInfo);
    }

    [Update]
    public void Push(GameTime dt)
    {
        if (board.Score == _pushed || !web.IsRunning) return;
        _pushed = board.Score;
        web.Channel("/score").BroadcastJson(new ScoreInfo(board.Score, board.Lives), GameJson.Default.ScoreInfo);
    }
}
```

:::tip[Injecting IWebServer when the server may be off]
`IWebServer` is only registered when `Ion:Web:Enabled` is true. If the same system must run with the server off, take
`IServiceProvider` and resolve `IWebServer` lazily with `GetService`, as the Companion example does, and check
`IsRunning` before pushing.
:::

### Sending never blocks

`Send` and `Broadcast` are safe from any thread and never wait on the network. Each client has a send ring (1,024
messages) drained by its own writer thread; a message is copied into the ring and the call returns. A client that falls
1,024 messages behind (it stopped reading) is disconnected rather than growing memory. In steady state, broadcasting
allocates nothing. The benchmark measured about 2.2 microseconds per broadcast to one client and 8.3 to four.

## Protocol details

- **Subprotocol.** If the client offers the `ion` subprotocol, the server selects it. Browsers require the server to
  pick one of the offered subprotocols when they offer any, so always offer `ion` from a browser.
- **Token.** Browsers cannot set an `Authorization` header on a WebSocket, so the token may be offered as a second
  subprotocol, `bearer.<token>`, next to `ion`. A non-browser client can send `Authorization: Bearer <token>` instead.
  See [Web security](/Ion/networking/web-security/).
- **Frames.** Pings are answered with pongs, fragmented messages are reassembled, client frames must be masked, and no
  extensions (compression) are negotiated.

### Limits and close codes

| Limit | Default | What happens |
|---|---|---|
| `MaxWebSocketMessageBytes` | 64 KiB | A larger client message closes the connection with **1009** (message too big). |
| `MaxPendingMessagesPerClient` | 256 | A client whose messages pile up waiting for the game thread is closed with **1008** (policy violation). |
| `QueueCapacity` | 1024 | When the queue into the game is full, messages are dropped. |
| `RateLimit` / `RateLimitBurst` | 100 / 200 | Messages above the per-address rate are dropped. |
| Send ring | 1,024 messages | A client that stops reading is disconnected. |
| Server shutdown | | Every client is closed with **1001** (going away). |

Your own code can close with any code, for example **1013** (try again later) when the game is full.

## Example: a phone as a gamepad

The [Companion example](/Ion/examples/companion/) is a paddle game whose paddle a phone drives. The game serves a
controller page from `wwwroot` (plain HTML and JavaScript, no build step) and a `/paddle` WebSocket. Each phone becomes
a **virtual gamepad** through Input's `ScriptedInput`, so the paddle code reads ordinary gamepads and knows nothing
about the web.

```csharp title="Program.cs (excerpt)"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddWeb().AddScriptedInput()
    .AddSystem<PaddleSystem>().AddSystem<BallSystem>().AddSystem<DrawSystem>().AddSystem<CompanionEndpoints>();
builder.Services.AddSingleton<PaddleGame>();

using var game = builder.Build();
game.UseIon().UseWeb().UseSystem<PaddleSystem>().UseSystem<BallSystem>().UseSystem<DrawSystem>().UseSystem<CompanionEndpoints>();
game.Run();
```

The endpoint maps connections to gamepad indexes 1 to 3 (0 stays free for a real pad), closes a fourth phone with
1013, and turns `{"x": -1..1}` messages into left-stick input:

```csharp title="CompanionEndpoints (excerpt)"
[WebJson(typeof(CompanionJson))]
public sealed class CompanionEndpoints(PaddleGame game, ScriptedInput script, IServiceProvider services)
{
    public const int FirstPad = 1, LastPad = 3;
    private readonly bool[] _padInUse = new bool[LastPad + 1];

    [Http("GET", "/score")]
    public ScoreInfo GetScore() => new(game.Score, game.Misses, game.PaddleX, game.Controllers);

    [WebSocket("/paddle")]
    public void Paddle(in WebSocketMessage message)
    {
        switch (message.Kind)
        {
            case WebSocketMessageKind.Connected:
                var pad = FreePad();
                if (pad < 0)
                {
                    message.Client.Close(1013); // every virtual gamepad is taken
                    return;
                }

                _padInUse[pad] = true;
                message.Client.State = pad;
                game.Controllers++;
                script.Enqueue(InputEvent.ForGamepadConnection(pad, true));
                message.Client.SendJson(GetScore(), CompanionJson.Default.ScoreInfo);
                break;
            case WebSocketMessageKind.Text when message.Client.State is int index:
                if (TryReadStick(message.Data, out var x)) script.Enqueue(InputEvent.ForGamepadAxis(index, GamepadAxis.LeftX, x));
                break;
            case WebSocketMessageKind.Disconnected when message.Client.State is int index:
                _padInUse[index] = false;
                game.Controllers--;
                script.Enqueue(InputEvent.ForGamepadAxis(index, GamepadAxis.LeftX, 0));
                script.Enqueue(InputEvent.ForGamepadConnection(index, false));
                break;
        }
    }

    // FreePad() and TryReadStick() (an allocation-free Utf8JsonReader parse) are in the sample.
}
```

Because the handler runs at `StageOrder.Web` in `Last`, the scripted input it enqueues is applied at the start of the
next frame, exactly like a real device event.

The controller page reads the token from the URL fragment and offers it as a subprotocol:

```js title="wwwroot/controller.js (excerpt)"
const token = new URLSearchParams(location.hash.slice(1)).get("token");
const url = (location.protocol === "https:" ? "wss://" : "ws://") + location.host + "/paddle";
// Browsers cannot set headers on a WebSocket: the token travels as an offered subprotocol next to "ion".
socket = new WebSocket(url, token ? ["ion", "bearer." + token] : ["ion"]);
```

Run it on one machine with `dotnet run` and open `http://127.0.0.1:15780/`. To use a real phone on the same network:

```bash
dotnet run -- --Ion:Web:Bind=0.0.0.0 --Ion:Web:AllowNonLoopback=true
```

and open the printed `on the network at http://...#token=...` URL on the phone. The token is generated per run because
the bind is not loopback.

## Testing a WebSocket endpoint

The Companion tests run the real `Program.cs` headless, step the game on its own thread, and connect with
`ClientWebSocket`:

```csharp
using var socket = new ClientWebSocket();
socket.Options.AddSubProtocol("ion");
await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{server.Port}/paddle"), CancellationToken.None);
Assert.Equal("ion", socket.SubProtocol);

await socket.SendAsync("{\"x\":1}"u8.ToArray(), WebSocketMessageType.Text, true, CancellationToken.None);
```

See [HTTP server core](/Ion/networking/http-server/) for the host setup and [Testing](/Ion/tooling/testing/) for
`IonTestHost`.

## See also

- [Web endpoints](/Ion/networking/web-endpoints/)
- [Web security](/Ion/networking/web-security/)
- [Input overview](/Ion/interaction/input/overview/) for gamepads and scripted input
- [Companion example](/Ion/examples/companion/)
- Source: [WebSockets.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Web.Abstractions/WebSockets.cs),
  [Companion Program.cs](https://github.com/jimbuck/Ion/blob/main/Ion.Examples/Ion.Examples.Companion/Program.cs)
