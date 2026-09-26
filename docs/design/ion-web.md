# Ion web server module

`Ion.Extensions.Web` embeds a small HTTP/1.1 and WebSocket server in a running game, so companion apps (a phone as a
controller, a level editor, a dashboard) and integrations (webhooks, stream overlays, chat bots) can talk to it. Game
endpoints are ordinary methods on systems marked `[Http("GET", "/score")]` or `[WebSocket("/events")]`; a source
generator turns them into a static route table (no reflection, NativeAOT-clean), and the server calls them on the game
thread at the end of a frame. It is the web half of Stage 6b in `docs/plans/2026-09-engine-review-and-roadmap.md`
(section 4.12), built on the HTTP core it shares with the remote protocol (`docs/design/ion-remote.md`).

Projects:

| Project | What it holds |
|---|---|
| `Ion.Extensions.Web.Abstractions` | `[Http]`, `[WebSocket]`, `[FromBody]`, `[WebJson]`, `WebRequest`, `WebResponse`, `WebSocketMessage`, `WebSocketClient`, `WebSocketChannel`, `IWebServer`, and the route table types the generator emits against (`WebRoute`, `WebSocketRoute`, `WebRouteTable`, `WebRoutes`, `WebBinding`) |
| `Ion.Extensions.Web.Generators` | The routing generator (netstandard2.0, Roslyn 4.4) and diagnostics `ION401` to `ION407` |
| `Ion.Extensions.Web` | `WebServer`, `WebSystem`, `WebOptions`, `AddWeb`/`UseWeb`/`AddWebRoutes`, static files |
| `Ion.Extensions.Http` | The server core shared with `Ion.Extensions.Remote`: blocking listener, allocation-free request parser and response writer, RFC 6455 framing, the Host/Origin/token policy, rate limits, the lock-free queue |

## 1. Using it

```csharp
builder.Services.AddWeb(builder.Configuration);          // registers nothing unless Ion:Web:Enabled
builder.Services.AddSingleton<ScoreSystem>();            // endpoint systems are singletons
app.UseIon().UseWeb().UseSystem<ScoreSystem>();

[WebJson(typeof(GameJson))]                               // System.Text.Json source generation for bodies and results
public sealed class ScoreSystem(PlayerStore players, IWebServer web)
{
    [Http("GET", "/score")]
    public ScoreInfo Score() => new(players.Score, players.Lives);

    [Http("POST", "/players/{id}/name")]                  // mutating: needs the token when one is configured
    public void Rename(int id, [FromBody] string name) => players[id].Name = name;

    [Http("GET", "/players")]
    public PlayerList List(int page = 0, string? filter = null) => players.Page(page, filter);

    [WebSocket("/events", Access = WebAccess.Read)]      // clients connect, send, disconnect; the game pushes
    public void Events(in WebSocketMessage message)
    {
        if (message.Kind == WebSocketMessageKind.Text) message.Client.Send("ack"u8);
    }

    [Update]
    public void Push(GameTime dt) { if (players.Changed) web.Channel("/events").BroadcastJson(Score(), GameJson.Default.ScoreInfo); }
}

[JsonSerializable(typeof(ScoreInfo))]
[JsonSerializable(typeof(PlayerList))]
internal sealed partial class GameJson : JsonSerializerContext;
```

The game project references the generator as an analyzer:

```xml
<ProjectReference Include="...\Ion.Extensions.Web.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Configuration (`Ion:Web`, class `WebOptions`):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Run the server. Nothing listens otherwise. |
| `Bind` | `127.0.0.1` | An IP address, `localhost`, or `*` (every interface). Non-loopback needs `AllowNonLoopback`. |
| `AllowNonLoopback` | `false` | The explicit, logged opt-in for a LAN bind. |
| `Port` | `15780` | `0` picks a free port (`IWebServer.Port`, `BaseUrl`). |
| `Token` | none | The bearer token of mutating endpoints. On a LAN bind without one, a token is generated per run. |
| `GenerateToken` | `false` | Generate a per-run token on loopback too. |
| `AllowAnonymousMutations` | `false` | On a LAN bind, serve mutating endpoints without a token (logged). |
| `AllowedOrigins` | none | Browser origins allowed besides the server's own (exact `Origin` values); they get CORS headers. |
| `StaticFiles` | none | A folder (relative to the application's base directory) served for `GET` and `HEAD`. |
| `DefaultFile` | `index.html` | The file served for a folder. |
| `MaxStaticFileBytes` | 16 MiB | The largest static file. |
| `HostEndpoints` | `true` | Mount the `IHttpEndpoint`s other modules register (the remote protocol at `/rpc`). |
| `MaxConnections` | `32` | Concurrent HTTP and WebSocket connections; one more gets 503. |
| `MaxRequestBytes` | 1 MiB | The largest request body (Content-Length or chunked); larger is 413. |
| `MaxWebSocketMessageBytes` | 64 KiB | The largest client message; larger closes with 1009. |
| `MaxPendingMessagesPerClient` | `256` | Messages of one client waiting for the game thread; more closes it with 1008. |
| `QueueCapacity` | `1024` | The queue into the game thread; a full queue answers 503 (and drops WebSocket messages). |
| `MaxRequestsPerFrame` | `256` | Requests and messages handled per frame; the rest wait for the next frame. |
| `RequestTimeoutMs` | `10000` | How long a request waits for the game thread before 504. |
| `RateLimit` / `RateLimitBurst` | `100` / `200` | Requests and WebSocket messages per second per client address (token bucket); `0` disables. |
| `UseGeneratedRoutes` | `true` | Serve every assembly's generated table; `false` serves only `AddWebRoutes` tables. |
| `PrintUrl` | `true` | Print the URL (and a generated token) once to standard error. |

## 2. Threading: handlers run on the game thread at a stage boundary

No async runs in the frame, and nothing in the frame waits on the network. The server is the shared core's blocking
listener: one accept thread and one thread per connection, plus a writer thread per WebSocket. A connection thread
parses the request (into buffers the connection reuses), applies the security checks, matches the route, reads the body
and puts the connection's reusable exchange on a bounded lock-free queue (Vyukov's multi-producer queue), then waits.
Static files and mounted endpoints are answered on the connection thread without the game.

The game thread drains the queue in `WebSystem.Process`, a Last step at **`StageOrder.Web` = 960**:

- after every gameplay, render and ECS step of the frame, including the ECS command playback at `StageOrder.Ecs` (950),
  so a handler sees the finished frame and never runs inside a query;
- before the remote protocol (`StageOrder.Remote`, 970), so a remote read in the same frame sees what a handler changed;
- before the event stepping (`StageOrder.Events`, 1000): events a handler emits are read by the next frame's steps, and
  input it injects through `ScriptedInput` is applied at the next frame's start.

Requests and WebSocket connection changes and messages share the queue, so they are handled in arrival order; at most
`MaxRequestsPerFrame` per frame. A request's response goes back to its connection thread, which writes it. A request the
game does not answer within `RequestTimeoutMs` gets 504 and its connection is closed; if the game reaches it later it is
skipped. While the remote protocol pauses the game (`game.pause`), web requests wait and time out.

**Allocation.** In steady state a small request allocates nothing on either thread: the parser, route match, exchange,
response buffer, JSON number and text writers, and the event the connection waits on are all reused; WebSocket messages
come from a per-client pool and frame buffers are recycled by the writer. `AllocationTests` checks 0 bytes over 500
requests (`GET` returning a number, a `POST` with a query parameter, a `GET` returning a source-generated JSON struct)
and 500 WebSocket round trips, on the connection thread and in the web step. Handler code that builds strings allocates,
as anywhere.

## 3. Routing

`[Http(method, route)]` may appear several times on a method; `[WebSocket(path)]` once. The generator emits, per
assembly, an internal class `Ion.Generated.IonWebRoutes_<Assembly>` with a `WebRouteTable` and one invoker per route,
registered with `WebRoutes.Register` from a module initializer; the server reads `WebRoutes.Tables` (plus the tables
added with `AddWebRoutes`) when it starts. The handler's target is resolved from the root services by type
(`sp.GetService(typeof(T))`) on the game thread on first use; an unregistered system makes its endpoints answer 503.

**Templates.** Literal segments, `{name}` segments and a last `{*name}` catch-all. Literals match ASCII
case-insensitively and one trailing slash is ignored. When several routes match a path, the most specific wins: literal
segments before parameters, parameters before a catch-all. Two routes with the same method and shape (`/a/{x}` and
`/A/{y}/`) are `ION402` at compile time, and an `InvalidOperationException` at startup when they come from different
tables. A path that matches only other methods gets 405 with `Allow`; `HEAD` falls back to the `GET` route without a
body. `OPTIONS` answers CORS preflights.

**Parameters.**

| Parameter | Bound from |
|---|---|
| name of a `{name}` segment | the route value: `string`, `bool`, a number type or `Guid` (a catch-all is a `string`) |
| `WebRequest` (by value or `in`) | the request: method, path, query, headers, body, route values, remote address, whether it presented the token |
| `WebResponse` (by value or `ref`) | the response to write: status, headers, text, bytes, JSON |
| `[FromBody] string`, `ReadOnlySpan<byte>`, `byte[]` | the body |
| `[FromBody] T` | the body as JSON, with the context's `JsonTypeInfo<T>` (400 with the reason when it does not parse) |
| anything else | the query parameter of the same name: `string`, `bool`, a number type or `Guid`; optional when nullable or with a default value, otherwise 400 when missing |

Values are percent-decoded and parsed with the invariant culture through `IUtf8SpanParsable<T>`, without allocating;
`bool` accepts `true/false`, `1/0`, `on/off`, `yes/no`, and a bare key (`?flag`) as true.

**Results.** `void` answers 204 unless the method wrote the response; `string` is `text/plain`; `bool` and number types
are JSON literals written directly; nullable ones write `null`; any other type is serialized with the JSON context named
by `[Http(..., Json = typeof(Ctx))]`, else `[WebJson(typeof(Ctx))]` on the class (or an outer class), else
`[WebJson]` on the assembly. The generated code looks the type up once with `Ctx.Default.GetTypeInfo(typeof(T))`, which
is a switch in the source-generated context (NativeAOT-safe). A handler that throws gets 500 (with the message on a
loopback bind) and a logged error; the game goes on.

**Diagnostics** (category `Ion.Web`):

| Id | Severity | When |
|---|---|---|
| `ION401` | Error | Invalid route: an unsupported HTTP method, a template that does not start with `/`, an empty segment, a malformed or repeated parameter, a catch-all that is not last, a query in the template, a WebSocket path with parameters, or a `{name}` without a parameter of that name. |
| `ION402` | Error | Duplicate route: same method and shape, or two WebSocket endpoints with the same path (case-insensitive). |
| `ION403` | Error | Unsupported method: static, generic, not public or internal, in a generic or private type, returning by reference, or a WebSocket handler that is not `void M(in WebSocketMessage)`. |
| `ION404` | Error | Unsupported parameter: a type that cannot bind from the route or query, `ref`/`out`/`in` on anything but `WebRequest`/`WebResponse`, a second `[FromBody]`. |
| `ION405` | Error | A body or result type needs a JSON context and has none, the named context is not a `JsonSerializerContext`, or the type cannot be written (`object`, interfaces, ref structs). |
| `ION406` | Warning | The JSON context has no `[JsonSerializable(typeof(T))]` for a type the endpoint needs (it fails at run time until added). |
| `ION407` | Error | An async endpoint (`async`, `Task`, `ValueTask`, `IAsyncEnumerable`): handlers run synchronously on the game thread. |

## 4. WebSockets and push

A `[WebSocket]` handler is called on the game thread with a `WebSocketMessage`: `Connected` (the client joins the
endpoint's channel before the call), `Text` or `Binary` (the payload, valid during the call), and `Disconnected` (its
last message; it leaves the channel after the call). `WebSocketClient` has an `Id`, the remote address, whether it
presented the token, a `State` slot for the game, and `Send`, `SendBinary`, `SendJson` and `Close`.
`IWebServer.Channel(path)` returns the endpoint's `WebSocketChannel`: `Broadcast`, `BroadcastBinary` and
`BroadcastJson` queue a copy for every client. Sending is safe from any thread and never blocks: messages go into a
per-client ring drained by the client's writer thread, and a client that falls 1,024 messages behind is disconnected.
Pings are answered, fragmented messages are reassembled, client frames must be masked; no extensions.

If the client offers the `ion` subprotocol it is selected (browsers require the server to pick one of the offered
subprotocols when they offer any).

## 5. Static files

With `StaticFiles` set, `GET` and `HEAD` requests that match no route are served from that folder on the connection
thread: `index.html` for a folder (a folder path without its trailing slash is redirected with 301), content types by
extension, `ETag` with `If-None-Match` (304), `Cache-Control: no-cache`. A path with a `..` or `.` segment, a segment
starting with `.` (hidden files), a backslash or NUL (after percent-decoding) is 404, and the resolved path must stay
inside the folder. Files are read on each request (edits show at once). Routes take precedence over files.

## 6. Security

The policy is the remote protocol's (`docs/design/ion-remote.md` section 3), with an origin allow-list for browsers:

- **Off by default.** Nothing listens unless `Ion:Web:Enabled`.
- **Loopback by default.** A non-loopback `Bind` (`0.0.0.0`, `*`, a LAN address) throws `WebSecurityException` at Init
  unless `AllowNonLoopback` is set, which is logged as a warning. The URL on each interface is printed, with the token
  in the fragment (`http://192.168.1.20:15780/#token=...`) so a phone can scan or type it; browsers never send the
  fragment to the server.
- **Host check.** On a loopback bind the `Host` header must name a loopback host, which defeats DNS rebinding.
- **Origins.** A request with an `Origin` header is refused (403) unless it is the server's own origin
  (`http://` + the `Host` header: pages the server serves) or listed in `AllowedOrigins`; listed origins get
  `Access-Control-Allow-Origin` and preflight answers. The same check applies to WebSocket handshakes.
- **Token for mutating endpoints.** With a token (configured, or generated on a LAN bind), endpoints that need
  `WebAccess.Mutate` answer 401 without `Authorization: Bearer <token>`. `WebAccess.Auto` means read for `GET` and
  `HEAD`, mutate for other methods and for WebSocket endpoints; `Access = WebAccess.Read` or `Mutate` overrides it.
  Browsers cannot set headers on a WebSocket, so the token may also be offered as a subprotocol `bearer.<token>` next
  to `ion`. Tokens are compared in constant time. The token is not a user system: it separates "can look" from "can
  change the game" on a trusted network.
- **Rate limits.** A token bucket per client address for requests and WebSocket messages (429 with `Retry-After`,
  dropped messages).
- **Bounds.** Request heads 16 KiB and 64 headers (431), bodies `MaxRequestBytes` (413), messages
  `MaxWebSocketMessageBytes` (1009), `MaxConnections` (503), per-client and global queues. Only origin-form targets;
  `Transfer-Encoding` other than a lone `chunked` on HTTP/1.1 is 411, both framings together 400 (request smuggling).

## 7. The remote protocol at /rpc

`AddRemote` registers the remote protocol as an `IHttpEndpoint` (`/rpc`). When both modules run, the web server mounts
it: JSON-RPC `POST /rpc` and the WebSocket upgrade on `/rpc` reach the remote server on the web module's port, after the
web module's Host, Origin and rate-limit checks. The remote protocol keeps its own bearer tokens and scopes, and on a
LAN-bound web server it still refuses clients that reach it on a non-loopback interface unless
`Ion:Remote:AllowNonLoopback` is set, so hosting it never widens the remote policy. A page the web module serves (same
origin) can therefore call the protocol with a remote token, which is how a browser dashboard reaches `world.query` or
`ui.tree`.

## 8. The companion sample

`Ion.Examples.Companion` is a paddle game with a phone controller page in `wwwroot` (plain HTML, CSS and JavaScript, no
build step). The page opens `/paddle` and sends `{"x": -1..1}` while a finger is on the pad; the endpoint turns each phone
into a virtual gamepad (indexes 1 to 3, up to three phones; a fourth is closed with 1013) through Input v2's
`ScriptedInput`, so the game's paddle code reads `IInputState` gamepads and knows nothing about the web. The game pushes
the score to the phones when it changes, and `GET /score` returns it. Run it with `dotnet run` and open
`http://127.0.0.1:15780/`, or from a phone with `--Ion:Web:Bind=0.0.0.0 --Ion:Web:AllowNonLoopback=true` and the
printed network URL. Its headless tests open the WebSocket from the test while the game runs on its own thread, move the
paddle both ways and read `/score`. It publishes with NativeAOT without Ion warnings (CI lane).

## 9. Performance

`WebBenchmarks` (loopback, the web step run continuously in place of a game thread, 4-core VM, `--job short`): a
keep-alive `GET` round trip about 34 us with 0 B allocated; 1,000 broadcasts to 1 client about 2.2 us each, to 4 clients
about 8.3 us each (the allocation column there is the benchmark's own `ClientWebSocket` receivers). In a game a request
also waits for the end of the frame it arrives in (up to 16.7 ms at 60 fps).

## 10. Files and tests

- `Ion/Ion.Extensions.Web.Tests` (104 tests): the generator (`RoutingGeneratorTests`: a golden route table, every
  diagnostic, JSON context lookup), routes end to end (`EndpointTests`), HTTP edge cases on raw sockets
  (`HttpParsingTests`: framing, smuggling, obsolete folding, pipelining, byte-by-byte arrival, keep-alive, chunked
  bodies, limits), `StaticFileTests` (types, ETag, redirects, traversal, hidden files), `WebSocketTests` (messages on the
  game thread, replies, channels, limits, subprotocol), `SecurityTests` (LAN bind refused by default, the LAN opt-in and
  its generated token, the configured token on routes and sockets, origins and CORS, Host, rate limit, connection cap),
  `MarshallingTests` (game thread, inside the web step, arrival order across HTTP and WebSocket and across concurrent
  clients, per-frame budget, timeouts), `AllocationTests`, and `RemoteHostingTests` (`/rpc`).
- `Ion.Examples/Ion.Examples.Companion.Tests`: the phone-controller flow headless.

## 11. Not done

HTTPS (put a reverse proxy in front for anything beyond a trusted LAN), HTTP/2, request streaming and response
streaming (bodies are buffered; server-sent events), compression, range requests and caching of static files in memory,
per-route rate limits, route constraints (`{id:int}`) and optional route segments, cookies or user sessions, `+watch`
streams of the remote protocol over server-sent events, and the WebSocket transport of the networking module (it will
reuse the same core).
