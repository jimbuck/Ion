---
title: HTTP server core
description: How Ion's embedded HTTP/1.1 and WebSocket server listens, marshals requests onto the game thread at StageOrder.Web, and which Ion:Web settings control it.
sidebar:
  order: 2
---

Ion does not use Kestrel or ASP.NET Core. Its web server and its remote protocol share a small, purpose-built
HTTP/1.1 and WebSocket core, `Ion.Extensions.Http`, that runs on blocking sockets in its own threads and hands work to
the game thread through a lock-free queue. This page covers how that core behaves inside the web module
(`Ion.Extensions.Web`): enabling it, where it listens, how requests reach your code, and every setting under `Ion:Web`.

To write the endpoints themselves, see [Web endpoints](/Ion/networking/web-endpoints/) and
[WebSockets](/Ion/networking/websockets/).

## Turn it on

The web server is registered with `AddWeb()` on the builder and scheduled with `UseWeb()` on the application. Both are
no-ops unless `Ion:Web:Enabled` is `true` (or the `configure` delegate sets `Enabled`), so you can leave them in every
build and turn the server on with configuration.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Web;

var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddWeb().AddSystem<ScoreEndpoints>();

using var game = builder.Build();
game.UseIon().UseWeb().UseSystem<ScoreEndpoints>();
game.Run();
```

```json title="appsettings.json"
{
  "Ion": {
    "Web": {
      "Enabled": true,
      "Port": 15780,
      "StaticFiles": "wwwroot"
    }
  }
}
```

Or from the command line, without touching `appsettings.json`:

```bash
dotnet run -- --Ion:Web:Enabled=true --Ion:Web:Port=8080
```

You can also set options in code. The delegate runs after configuration binding, so it wins over `appsettings.json`:

```csharp
builder.AddWeb(web =>
{
    web.Enabled = true;
    web.Port = 0;          // any free port; read it back from IWebServer.Port
    web.PrintUrl = false;
});
```

When enabled, `AddWeb` registers `WebServer` (also as `IWebServer`) and `WebSystem` as singletons and binds
`WebOptions` from the `Ion:Web` section. `UseWeb()` adds `WebSystem` to the schedule only when the server was
registered. Endpoint systems must be singletons: `builder.AddSystem<T>()` registers them that way.

:::note[The routing generator]
Endpoints are found by a source generator, so the game project references it as an analyzer:

```xml
<ProjectReference Include="..\Ion\Ion.Extensions.Web.Generators\Ion.Extensions.Web.Generators.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

Without it the server still runs (static files, `/rpc`), but no `[Http]` or `[WebSocket]` method is routed.
:::

## Where it listens

| Setting | Default | Meaning |
|---|---|---|
| `Ion:Web:Bind` | `127.0.0.1` | An IP address, `localhost` (loopback), or `*` (every interface). |
| `Ion:Web:AllowNonLoopback` | `false` | Required for any non-loopback `Bind`; logged as a warning when used. |
| `Ion:Web:Port` | `15780` | The TCP port. `0` picks a free port. |
| `Ion:Web:PrintUrl` | `true` | Print the URL (and a generated token) once to standard error at startup. |

At startup the server prints something like:

```text
Ion web: listening on http://127.0.0.1:15780/
```

On a LAN bind it also prints one line per network interface, with the per-run token in the URL fragment, so you can
open it on a phone:

```text
Ion web: on the network at http://192.168.1.20:15780/#token=3q2-...
Ion web: token 3q2-...
```

Read the actual address at run time from `IWebServer`:

```csharp
public sealed class ShowUrl(IWebServer web)
{
    [Update]
    public void Log(GameTime dt)
    {
        if (web.IsRunning) Console.WriteLine($"{web.BaseUrl} serves {web.Routes.Count} routes on port {web.Port}");
    }
}
```

| `IWebServer` member | What it is |
|---|---|
| `IsRunning` | Whether the server is listening. |
| `Port` | The bound port, or 0 when not running (useful with `Port = 0`). |
| `BaseUrl` | `http://127.0.0.1:<port>/` (or the bound address), or null when not running. |
| `Routes` | The HTTP routes served, most specific first. |
| `WebSockets` | The WebSocket endpoints served. |
| `RequestCount` | Requests answered so far (by a route, a static file, `/rpc` or an error). |
| `Channel(path)` | The push channel of a `[WebSocket]` endpoint (see [WebSockets](/Ion/networking/websockets/)). |

`IWebServer` exists whenever `AddWeb` registered the module, running or not. The server starts in the `Init` stage at
`StageOrder.Web`, after your own `Init` steps, so your systems are ready before the first request arrives. It stops in
`Destroy`.

:::caution[A non-loopback bind fails at Init]
`--Ion:Web:Bind=0.0.0.0` on its own throws `WebSecurityException` when the server starts. Add
`--Ion:Web:AllowNonLoopback=true` to opt in. See [Web security](/Ion/networking/web-security/).
:::

## Threading: handlers run on the game thread

No async code runs inside the frame and nothing in the frame waits on the network. The core uses:

- **one accept thread** (`TcpListener`, backlog 64, `NoDelay`),
- **one thread per connection**, which parses the request into buffers the connection reuses, applies the security
  checks, matches the route and reads the body,
- **one writer thread per WebSocket**, which drains that client's send queue.

A connection thread then puts its reusable exchange on a bounded multi-producer queue (Vyukov's algorithm) and waits.
The game thread drains that queue in `WebSystem.Process`, a `Last` step at **`StageOrder.Web` (960)**, runs your
handler, and hands the response back to the connection thread, which writes it.

```text
connection thread                        game thread (Last stage)
-----------------                        ------------------------
parse request                            ... gameplay, render, ECS playback (950)
Host / Origin / rate limit / token       WebSystem.Process (960)
match route, read body          ---->      dequeue up to MaxRequestsPerFrame
wait                                       call your [Http] / [WebSocket] method
write response                  <----      hand back the response
                                         Remote (970), Events (1000)
```

Why 960:

- It runs **after** every gameplay, render and ECS step of the frame, including the ECS command playback at
  `StageOrder.Ecs` (950). A handler sees the finished frame and never runs inside a query.
- It runs **before** the remote protocol (`StageOrder.Remote`, 970), so a remote read in the same frame sees what a
  handler changed.
- It runs **before** event stepping (`StageOrder.Events`, 1000). Events a handler emits are read by the next frame's
  steps, and input it injects through `ScriptedInput` is applied at the start of the next frame.

HTTP requests, WebSocket connects, messages and disconnects share the one queue, so they are handled in arrival order,
across clients and across protocols. At most `MaxRequestsPerFrame` items are handled per frame; the rest wait for the
next frame.

Two kinds of work never touch the game thread: **static files** and **mounted endpoints** (the remote protocol at
`/rpc`) are answered on the connection thread.

:::tip[Latency]
A request waits for the end of the frame it arrives in, so at 60 fps expect up to about 16.7 ms on top of the network.
The benchmark on a 4-core VM measured a keep-alive `GET` round trip of about 34 microseconds with the web step running
continuously in place of a game loop.
:::

### Timeouts and a paused game

If the game does not reach a request within `RequestTimeoutMs` (10 seconds by default), the connection thread answers
**504** and closes the connection; if the game reaches it later, it is skipped. While the remote protocol pauses the
game (`game.pause`), web requests wait and eventually time out the same way.

### Handler errors

A handler that throws answers **500** and logs an error (`Ion web: <route> failed.`). On a loopback bind the exception
message is included in the body. The game keeps running.

### Unregistered systems

The handler's target is resolved from the root services by type on the game thread the first time it is needed. If the
system is not registered (or is scoped), its endpoints answer **503** and the server logs a warning once.

## Allocation

In steady state a small request allocates nothing on either thread: the parser, route match, exchange, response buffer,
JSON number writers and the wait handle are all reused, and WebSocket messages come from a per-client pool. The web
module's `AllocationTests` checks 0 bytes over 500 requests and 500 WebSocket round trips. Your handler's own code (for
example building a `string`) allocates as usual.

## Static files

Set `StaticFiles` to a folder (relative to the application's base directory, or absolute) and the server answers `GET`
and `HEAD` requests that match no route from it, on the connection thread:

```json
{ "Ion": { "Web": { "Enabled": true, "StaticFiles": "wwwroot", "DefaultFile": "index.html" } } }
```

Copy the folder to the output in the `.csproj`:

```xml
<ItemGroup>
  <Content Include="wwwroot\**" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

Behavior:

- A folder serves `DefaultFile` (`index.html`); a folder path without its trailing slash is redirected with **301**.
- Content types come from the file extension.
- Responses carry an `ETag`; `If-None-Match` answers **304**. `Cache-Control: no-cache` is always sent, and files are
  read on every request, so edits show up at once.
- A path with a `.` or `..` segment, a segment starting with `.` (hidden files), a backslash or a NUL (after
  percent-decoding) is **404**, and the resolved path must stay inside the folder.
- Files larger than `MaxStaticFileBytes` (16 MiB) are not served.
- Routes take precedence over files.

If the folder does not exist, the server logs a warning and serves no files.

## Mounted endpoints and the remote protocol

Other modules can register an `IHttpEndpoint` (a path plus a `Serve(HttpConnection)` method) and the web server mounts
it on its own port when `HostEndpoints` is `true` (the default). The remote protocol does this: with both `AddRemote()`
and `AddWeb()` running, `POST /rpc` and a WebSocket upgrade on `/rpc` reach the remote server on the web module's port,
after the web module's Host, Origin and rate-limit checks. The remote protocol keeps its own tokens and scopes and its
own port (`15702`). See [Remote protocol](/Ion/tooling/remote-protocol/).

## Hand-written route tables

`WebOptions.UseGeneratedRoutes` (default `true`) serves every assembly's generated table. You can add tables built by
hand (or by another generator) with `AddWebRoutes`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Ion.Extensions.Web;

var health = new WebRouteTable("health",
    [
        new WebRoute("GET", "/health", typeof(HealthEndpoints),
            static sp => sp.GetService(typeof(HealthEndpoints)),
            static (object target, in WebRequest request, WebResponse response) => response.Text("ok")),
    ],
    []);

builder.Services.AddWebRoutes(health);
builder.Services.AddSingleton<HealthEndpoints>();

public sealed class HealthEndpoints;
```

The `resolve` delegate supplies the handler's target; if it returns null the route answers 503, just like a generated
route whose system is not registered.

Set `UseGeneratedRoutes` to `false` to serve only the tables added this way. Two routes with the same method and shape
from different tables throw `InvalidOperationException` when the server starts.

## Configuration reference

All keys live under `Ion:Web` and bind to `WebOptions`.

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Run the server. Nothing listens otherwise. |
| `Bind` | `127.0.0.1` | IP address, `localhost`, or `*`. Non-loopback needs `AllowNonLoopback`. |
| `AllowNonLoopback` | `false` | The explicit, logged opt-in for a LAN bind. |
| `Port` | `15780` | `0` picks a free port. |
| `Token` | none | Bearer token for mutating endpoints. On a LAN bind without one, a token is generated per run. |
| `GenerateToken` | `false` | Generate a per-run token on loopback too. |
| `AllowAnonymousMutations` | `false` | On a LAN bind, serve mutating endpoints without a token (logged). |
| `AllowedOrigins` | empty | Browser origins allowed besides the server's own (exact `Origin` values); they get CORS headers. |
| `StaticFiles` | none | Folder served for `GET` and `HEAD`. |
| `DefaultFile` | `index.html` | File served for a folder. |
| `MaxStaticFileBytes` | 16 MiB | Largest static file. |
| `HostEndpoints` | `true` | Mount `IHttpEndpoint`s other modules register (`/rpc`). |
| `MaxConnections` | `32` | Concurrent HTTP and WebSocket connections; one more gets 503. |
| `MaxRequestBytes` | 1 MiB | Largest request body (Content-Length or chunked); larger is 413. |
| `MaxWebSocketMessageBytes` | 64 KiB | Largest client WebSocket message; larger closes with 1009. |
| `MaxPendingMessagesPerClient` | `256` | Messages of one client waiting for the game thread; more closes it with 1008. |
| `QueueCapacity` | `1024` | Capacity of the queue into the game thread; a full queue answers 503 and drops WebSocket messages. |
| `MaxRequestsPerFrame` | `256` | Requests and messages handled per frame. |
| `RequestTimeoutMs` | `10000` | How long a request waits for the game thread before 504. |
| `RateLimit` | `100` | Requests and WebSocket messages per second per client address; `0` or less disables. |
| `RateLimitBurst` | `200` | Burst allowed above `RateLimit`. |
| `UseGeneratedRoutes` | `true` | Serve every assembly's generated table; `false` serves only `AddWebRoutes` tables. |
| `PrintUrl` | `true` | Print the URL (and a generated token) once to standard error. |

Fixed limits of the core (not configurable through `Ion:Web`): request heads up to 16 KiB and 64 headers (**431**
otherwise), idle connections time out after 120 seconds, only origin-form request targets are accepted, and
`Transfer-Encoding` other than a lone `chunked` on HTTP/1.1 is **411**. See
[Web security](/Ion/networking/web-security/) for why.

## Status codes the server produces

| Status | When |
|---|---|
| 204 | A `void` handler that wrote nothing; a CORS preflight (`OPTIONS`). |
| 301 | A static folder requested without its trailing slash. |
| 304 | A static file whose `ETag` matches `If-None-Match`. |
| 400 | Malformed request, bad chunked body, a missing required query parameter, an unparsable value or JSON body. |
| 401 | A mutating endpoint without the token (when one is configured). |
| 403 | A foreign `Host` on a loopback bind, or a refused `Origin`. |
| 404 | No route, no static file, or no WebSocket endpoint at the path. |
| 405 | The path matches only other methods (with an `Allow` header). |
| 411, 413, 431 | Unsupported framing, body too large, head too large. |
| 429 | Rate limit exceeded (`Retry-After: 1`). |
| 500 | The handler threw. |
| 503 | Too many connections, the queue is full, the server is stopping, or the endpoint's system is not registered. |
| 504 | The game thread did not answer within `RequestTimeoutMs`. |

## Testing

`IonTestHost` runs the real `Program.cs` headless. Give the server a free port and step the game on a background
thread so the connection threads have a game thread to hand work to, as the Companion tests do:

```csharp
var host = new IonTestHost()
    .WithConfiguration(new Dictionary<string, string?> { ["Ion:Web:Port"] = "0", ["Ion:Web:PrintUrl"] = "false" })
    .UseEntryPoint<Program>()
    .Start();

var server = host.Get<IWebServer>();
var loop = new Thread(() => { while (true) { host.Step(); Thread.Sleep(1); } }) { IsBackground = true };
loop.Start();

using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl!) };
var score = await http.GetStringAsync("/score");
```

See [Testing](/Ion/tooling/testing/) for the test host.

## See also

- [Web endpoints](/Ion/networking/web-endpoints/)
- [WebSockets](/Ion/networking/websockets/)
- [Web security](/Ion/networking/web-security/)
- [Stage order reference](/Ion/reference/stage-order/)
- Source: [Ion.Extensions.Http](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Http/),
  [WebOptions.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Web/WebOptions.cs)
