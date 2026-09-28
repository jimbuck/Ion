---
title: Remote protocol
description: Inspect and drive a running Ion game over JSON-RPC 2.0 on HTTP, WebSocket or stdio, with per-run tokens, read and mutate scopes, and every built-in method.
sidebar:
  order: 2
---

`Ion.Extensions.Remote` lets tools and coding agents look inside a running game and drive it: query and change ECS
entities, read and write named resources, read the schedule, metrics, events and logs, take screenshots, inject input,
pause and step frames. It is JSON-RPC 2.0, modelled on the Bevy Remote Protocol, carried over HTTP/1.1, WebSocket and
stdio. The [`ion remote`](/Ion/tooling/ion-cli/#ion-remote) command and the [MCP server](/Ion/tooling/mcp-server/) are
clients of it.

```bash
dotnet run -- --remote                     # read-only: queries, schema, metrics, screenshots, watches
dotnet run -- --remote-allow-mutations     # also writes: components, spawn/despawn, resources, input, pause/step
dotnet run -- --remote-stdio               # JSON-RPC on stdin/stdout instead of HTTP
ion remote world.query '{"name":"Ball*"}'  # from another shell, using the token file
```

## Enabling it

Every game built with `AddIon`/`UseIon` already contains the module; it registers nothing and opens no socket unless
`Ion:Remote:Enabled` is true. The module-style builder call `builder.AddRemote()` is the same as `builder.AddIon()`: the
protocol is part of the engine core.

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon()
	.AddEcs()
	.AddSystem<BallSystem>();

using var game = builder.Build();
game.UseIon()      // UseIon adds RemoteSystem when the server was registered
	.UseEcs()
	.UseSystem<BallSystem>();
game.Run();
```

Without the `Ion` meta package, register it yourself with `services.AddRemote(configuration)` and add the system with
`app.UseRemote()`.

The short switches are rewritten by `IonApplication.CreateBuilder(args)`:

| Switch | Configuration |
|---|---|
| `--remote` | `Ion:Remote:Enabled=true` |
| `--remote-allow-mutations` | `Ion:Remote:Enabled=true`, `Ion:Remote:AllowMutations=true` |
| `--remote-stdio` | `Ion:Remote:Enabled=true`, `Ion:Remote:Transport=Stdio` |

`ion run --remote`, `--remote-allow-mutations` and `--pause-at N` also pass `Ion:Remote:Port=0` (a free port) and the run
directory.

### Options

`RemoteOptions`, bound from `Ion:Remote`:

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Run the server. |
| `Transport` | `Http` | `Http` (HTTP and WebSocket), `Stdio`, or `Both`. |
| `Bind` | `127.0.0.1` | HTTP bind address (an IP address or `localhost`). A non-loopback address needs `AllowNonLoopback`. |
| `AllowNonLoopback` | `false` | The explicit opt-in for a non-loopback bind; logged as a warning. |
| `Port` | `15702` | HTTP port (the Bevy Remote Protocol's). `0` picks a free port, written to the token file. |
| `AllowMutations` | `false` | Create the mutate scope. Without it every session is read-only. |
| `RunDirectory` | `.ion/run` | Where the token file is written, relative to the current directory. |
| `TokenFile` | `remote.json` | The token file name. |
| `PrintToken` | `true` | Print the endpoint and tokens once to standard error at startup. |
| `AllowedOrigins` | empty | Browser origins (exact `Origin` values) allowed to call the HTTP transport. |
| `MaxConnections` | `8` | Concurrent HTTP and WebSocket connections. |
| `MaxRequestBytes` | 4 MiB | Largest request body or WebSocket message. |
| `MaxRequestsPerFrame` | `64` | Requests applied per frame while the game runs; the rest wait for the next frame. |
| `RequestTimeoutMs` | `30000` | How long an HTTP request waits for the game thread. |
| `IdempotencyCacheSize` | `1024` | Completed mutation responses kept for replays. |
| `StartPaused` | `false` | Pause at the end of the first frame. |
| `PauseAtFrame` | `-1` | Pause after this many frames (`-1`: never). |

```json title="appsettings.Development.json"
{
  "Ion": {
    "Remote": {
      "Enabled": true,
      "AllowMutations": true,
      "Port": 0
    }
  }
}
```

### Compiled out of Release builds

The `Ion.Remote.IsSupported` feature switch comes from the `IonRemote` MSBuild property, which defaults to `false` for
`-c Release` and `true` otherwise. With `false`, a trimmed or NativeAOT publish removes the server, the transports and
every method provider, resource and event registered through the remote helpers; an untrimmed Release build keeps the
code but `AddRemote` registers nothing, and says so on standard error if `--remote` is passed.

```xml title="MyGame.csproj"
<PropertyGroup>
  <!-- Keep the remote protocol in Release builds (for end-to-end tests of the published game). -->
  <IonRemote>true</IonRemote>
</PropertyGroup>
```

Or at publish time: `dotnet publish -c Release -p:IonRemote=true`.

## Security model

The protocol is built to be safe to leave compiled into development builds:

- **Off by default.** Nothing listens unless `Ion:Remote:Enabled` is true.
- **Loopback only.** `Bind` defaults to `127.0.0.1`. A non-loopback address makes the server throw
  `RemoteSecurityException` at Init unless `AllowNonLoopback` is also set, which logs a warning.
- **Per-run bearer tokens.** Each run creates a read token and, only with `AllowMutations`, a mutate token (random,
  base64url). HTTP and WebSocket clients send `Authorization: Bearer <token>`. Tokens are compared in constant time and
  never accepted in URLs. A missing or unknown token gets HTTP 401 and error `-32001`.
- **Owner-only token file.** The tokens and endpoints are written to `<RunDirectory>/remote.json` with mode 600 on Unix
  (set at creation, so there is no window where the file is readable) and deleted at shutdown.
- **Read and mutate scopes.** A read session calling a mutate method gets HTTP 403 and error `-32002`, checked on the
  transport thread and again on the game thread.
- **Stdio needs no token** (the parent process is trusted) but still gets the mutate scope only with `AllowMutations`.
- **Browsers are refused.** A request with an `Origin` header is refused unless the origin is in `AllowedOrigins`, and
  when bound to loopback the `Host` header must name a loopback host (`localhost`, `127.0.0.1`, `[::1]`), which defeats
  DNS rebinding.
- **Idempotent mutations.** A mutation's response is cached under (request id, method, params). Repeating the same
  request replays the stored response without applying it again, so a retried request never double-applies. Use unique
  ids; the `ion` tools use a GUID per request. Notifications (no id) get no such protection.
- **Never during a scene change.** A mutation that arrives while a scene is loading or unloading gets `-32003` and is not
  cached: retry it with the same id on a later frame.

The token file looks like this:

```json title=".ion/run/remote.json"
{
  "version": 1,
  "pid": 41213,
  "title": "Arena",
  "url": "http://127.0.0.1:40125/",
  "webSocketUrl": "ws://127.0.0.1:40125/ws",
  "readToken": "q7Q...",
  "mutateToken": "Zp1...",
  "allowMutations": true,
  "startedAt": "2026-09-28T10:15:02.1234567+00:00"
}
```

## Threading: when requests run

Transports run on their own threads with blocking sockets and streams. A transport thread parses the message,
authenticates it, answers what needs no game state (parse errors, unknown methods, a missing scope, a watch over plain
HTTP) and queues the rest.

The game thread applies the queue in `RemoteSystem.Process`, a `Last` step at `StageOrder.Remote` (970):

- after every gameplay, render and ECS step of the frame, including ECS command playback (`StageOrder.Ecs`, 950), so a
  read sees the finished frame and a screenshot the rendered image;
- before event stepping (`StageOrder.Events`, 1000), so `events.tail` still sees the frame's events;
- a mutation is therefore visible from the next frame's `First` stage.

Nothing in the frame waits on the network. At most `MaxRequestsPerFrame` requests run per frame.

**Pausing.** `game.pause`, `PauseAtFrame` or `StartPaused` makes the remote step block the game thread at the end of the
frame, serving requests as they arrive, until `game.resume`, `game.step` or `game.exit`. While paused the window is not
pumped. `game.step {"frames": n}` answers after the n frames have run, paused again.

## Transports and messages

One JSON-RPC 2.0 request per:

- HTTP `POST /` (or `/rpc`) body. When the [web module](/Ion/networking/http-server/) runs too, it also mounts the
  protocol at `/rpc` on its own port, still loopback-only unless `AllowNonLoopback` is set.
- WebSocket text message, on `GET /ws` with the upgrade.
- stdio line (newline-delimited JSON). With the stdio transport, standard output carries the protocol, so `Console.Out`
  is redirected to standard error and console logs go there too.

Batches are not supported. `params` must be an object or absent.

```bash
TOKEN=$(jq -r .mutateToken .ion/run/remote.json)
URL=$(jq -r .url .ion/run/remote.json)
curl -s "$URL" -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' \
  -d '{"jsonrpc":"2.0","id":"q1","method":"world.query","params":{"with":["Ball"],"components":["Transform2D"]}}'
```

```json
{"jsonrpc":"2.0","id":"q1","result":{"entities":[{"entity":0,"name":"Ball0","components":{"Transform2D":{"Position":[282.2,175.7],"Rotation":0,"Scale":[1,1]}}}],"total":1,"truncated":false}}
```

### Watches

A read method registered as watchable has a `+watch` variant (for example `world.get_components+watch`) over WebSocket
and stdio. The server answers at once, then re-evaluates the handler at the end of every frame and sends a new response
with the same id whenever the result differs from the last one sent. `rpc.unwatch {"id": <watch id>}` stops one watch of
the session (all of them without an id); closing the connection stops them all. Plain HTTP gets `-32005`.

```json
{"jsonrpc":"2.0","id":"w1","method":"game.info+watch"}
{"jsonrpc":"2.0","id":"w2","method":"rpc.unwatch","params":{"id":"w1"}}
```

### Error codes

| Code | HTTP | Meaning |
|---|---|---|
| `-32700` | 200 | Parse error. |
| `-32600` | 200 | Invalid request (not an object, no `"jsonrpc": "2.0"`, a batch, an unwatchable `+watch`). |
| `-32601` | 200 | Method not found. |
| `-32602` | 200 | Invalid params. |
| `-32603` | 200 | The handler threw; the message is the exception's. |
| `-32001` | 401 | Missing or unknown bearer token. |
| `-32002` | 403 | A read session called a mutate method, or a refused origin or Host. |
| `-32003` | 200 | Mutation while a scene is loading; retry with the same id. |
| `-32004` | 200 | Entity, component, resource or path not found. |
| `-32005` | 200 | Unsupported here (no ECS, no screenshot source, read-only resource, watch over HTTP). |
| `-32006` | 504 | The game thread did not answer within `RequestTimeoutMs`. |

The constants are in `RemoteErrorCodes`.

## Methods

`rpc.discover` lists every method the running game has, with its access, description, parameter schema and whether it
can be watched. Methods come from the core module and from modules that register a provider.

### Core methods (every game)

| Method | Access | Params | Result |
|---|---|---|---|
| `rpc.discover` | read | | `protocol`, `version`, the session's `access`, `watchSuffix`, `methods[]` |
| `rpc.unwatch` | read | `id?` | `{removed}` |
| `game.info` (+watch) | read | | `title`, `pid`, `frame`, `stage`, `fixedSteps`, `paused`, `headless`, `headlessRender`, `scene`, `sceneLoading`, `fps`, `allowMutations`, `access` |
| `game.pause` | mutate | | `{paused, frame}` |
| `game.resume` | mutate | | `{paused}` |
| `game.step` | mutate | `frames?` (default 1, up to 1,000,000) | after the frames ran: `{frame, paused}` |
| `game.exit` | mutate | | `{exiting}`; the loop stops after the frame |
| `schedule.get` | read | | `text` (as `Ion:PrintSchedule`), `stages[{stage, steps[{order, kind, name, end, depth}]}]`, `scenes[]` |
| `metrics.get` (+watch) | read | | the last frame in the frame log's shape (`frame`, `frame_ms`, `draw_calls`, ...), `counters`, `gauges`, `histograms`, `profiling` |
| `screenshot` | read | | `{format: "png", width, height, frame, data}` with `data` base64 |
| `input.send` | mutate | `events[]` | `{queued, frame}` |
| `events.tail` (+watch) | read | `since?`, `limit?` (default 256) | `counts[{type, id, thisFrame, lastFrame, total}]`, `events[{seq, frame, type, payload}]`, `next` |
| `log.tail` (+watch) | read | `since?`, `level?` (default `Information`), `limit?` (default 200) | `entries[{seq, time, level, category, message, exception}]`, `next` |
| `resources.list` | read | | `[{name, description, writable, schema}]` |
| `resources.get` (+watch) | read | `name` | the value |
| `resources.set` | mutate | `name`, `value` | the new value |

`screenshot` needs a rendering backend: headless rendering (`--headless-render` or `Ion:Headless:Render=true`) or a
window. The module turns on `GraphicsConfig.RetainLastFrame` so windowed screenshots work.

`events.tail` always returns the counts of every event channel; payloads only for event types registered with
`AddRemoteEvent` (below). `log.tail` and `events.tail` return a `next` sequence number: pass it as `since` on the next
call to get only what is new.

### ECS methods (`AddEcs`)

The ECS module registers these through a provider, so the remote server needs no reference to the ECS.

| Method | Access | Params | Result |
|---|---|---|---|
| `registry.schema` | read | | `components[{name, type, tag, schema}]` |
| `world.list` | read | | `[{index, entities, default}]` |
| `world.query` (+watch) | read | `components?`, `with?`, `without?`, `name?` (exact or `prefix*`), `limit?` (default 1000), `world?` | `{entities[{entity, name, components}], total, truncated}` |
| `world.get_components` (+watch) | read | `entity`, `components?`, `world?` | `{entity, name, components}` |
| `world.list_components` | read | `entity`, `world?` | the remote-visible components by name, others by type name |
| `world.insert_components` | mutate | `entity`, `components` (name to value), `world?` | the entity with those components |
| `world.mutate_components` | mutate | `entity`, `component`, `path` (`Position.0`; empty replaces), `value`, `world?` | `{entity, component, value}` |
| `world.remove_components` | mutate | `entity`, `components[]`, `world?` | `{entity, removed[]}` |
| `world.spawn` | mutate | `components?`, `name?`, `world?` | the new entity (nothing is created if a component name is wrong) |
| `world.despawn` | mutate | `entity`, `world?` | `{entity, despawned}` |

- **Entities** are addressed by id (a number, as `world.query` returns it) or by `EntityName` (a string).
- **Worlds**: the root world and one per loaded scene. Methods take an optional `world` index (from `world.list`) and
  default to the most recently created live world (the active scene's, or the root one).
- **Components**: remote-visible components are exactly those of the world serializer's registry: the built-in ones plus
  what the game registers with `AddEcsSerialization(...)`. Their JSON is the serializer's, except that entity references
  are entity ids (`-1` for none). Other components are listed by type name but cannot be read or written. See
  [Components and serialization](/Ion/ecs/components-and-serialization/).

```csharp title="Program.cs"
builder.AddIon()
	.AddEcs()
	// Registering a component for serialization is the opt-in for world snapshots and the remote protocol.
	.AddEcsSerialization(components => components
		.AddUnmanaged("Velocity", GameJson.Default.Velocity)
		.AddTag<Ball>("Ball"));
```

### Module methods

| Method | Module | Access |
|---|---|---|
| `ui.tree` (+watch), `ui.click`, `ui.set_value`, `ui.focus`, `ui.type`, `ui.back` | `Ion.Extensions.UI.Remote` (`builder.AddUiRemote()`) | `ui.tree` read, the rest mutate |
| `physics2d.bodies` (+watch), `physics2d.raycast` | `Ion.Extensions.Physics2D.Remote` (`builder.AddPhysics2DRemote()`) | read |
| `network.status` (+watch) | `Ion.Extensions.Networking` (`AddNetworking`) | read |

See [UI over the remote protocol](/Ion/interaction/ui/ui-remote/), [2D physics](/Ion/physics/physics-2d/) and
[Networking](/Ion/networking/multiplayer/overview/) for their parameters.

## Injecting input

`input.send` goes through Input v2's scripted path (`ScriptedInput`, attached to the application's `InputTracker`): the
events are applied at the start of the next frame, so they produce the same edges, fixed-step views and recordings as a
real device. While an input playback runs, it owns the input and injected events are ignored. Every event takes an
optional `delay` in frames.

```json
{"type":"key","key":"Space","action":"tap"}
{"type":"key","key":"Right","action":"hold","frames":30}
{"type":"key","key":"S","action":"press","modifiers":["Control"]}
{"type":"pointer","x":100,"y":200,"button":"Left","action":"click"}
{"type":"pointer","x":100,"y":200,"action":"move"}
{"type":"wheel","delta":1}
{"type":"text","text":"hello"}
{"type":"gamepad","index":0,"button":"A","action":"tap"}
{"type":"gamepad","index":0,"axis":"LeftX","value":0.5}
{"type":"gamepad","index":0,"action":"connect"}
```

| Type | Actions | Default action |
|---|---|---|
| `key` | `tap`, `press`, `release`, `hold` (with `frames`, default 1) | `tap` |
| `pointer` | `move`, `click`, `press`, `release` (`x`/`y` move the pointer first) | `click` |
| `wheel` | needs `delta` | |
| `text` | queues one text event per character | |
| `gamepad` | a `button` with `tap`, `press`, `release`; an `axis` with `value`; or `connect`/`disconnect` | `tap` |

Key, button and axis names are the engine's enum names (`Key`, `MouseButton`, `GamepadButton`, `GamepadAxis`), case
insensitive. See [Input](/Ion/interaction/input/overview/).

```bash
ion remote input.send '{"events":[{"type":"key","key":"Space","action":"tap"}]}'
```

## Exposing your own state

### Resources

A resource is a named piece of game state that `resources.get` reads and, when you give a setter, `resources.set`
writes. `AddRemoteResource` builds one from a source-generated `JsonTypeInfo<T>`, so it works under NativeAOT. The 3D
template does this:

```csharp title="Program.cs"
using Ion.Extensions.Remote;

builder.Services.AddSingleton<SpinState>();
builder.Services.AddRemoteResource("Game.Spin", "The cubes' spin angle and speed.", GameJson.Default.SpinState,
	static sp => sp.GetRequiredService<SpinState>(),
	static (sp, value) => sp.GetRequiredService<SpinState>().CopyFrom(value));
```

```csharp title="Game.cs"
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(SpinState))]
public sealed partial class GameJson : JsonSerializerContext
{
}
```

```bash
ion remote resources.list
ion remote resources.get '{"name":"Game.Spin"}'
ion remote resources.set '{"name":"Game.Spin","value":{"Angle":0,"Speed":2,"Steps":0}}'
```

`resources.list` includes each resource's JSON Schema. Two resources are built in: `Ion.GameTime` (read: frame, elapsed
seconds, delta, fixed steps, stage) and `Ion.Metrics.Profiling` (read and write: the span recording toggle).

### Event payloads

Events are unmanaged structs, so their payloads are serialized only for the types you register:

```csharp
builder.Services.AddRemoteEvent(GameJson.Default.BlockHitEvent);   // optional second argument: the name in events.tail
```

### Methods

Any module or game can add methods by implementing `IRemoteMethodProvider`. Handlers run on the game thread at the
remote step, get a `RemoteRequest` (parameters with typed getters that throw `-32602`, the services, the session's
access, whether this is a watch re-evaluation) and return a `JsonNode` or throw `RemoteException(code, message, data)`.

```csharp
using System.Text.Json.Nodes;
using Ion.Extensions.Remote;
using Microsoft.Extensions.DependencyInjection;

public sealed class ScoreRemoteMethods(IServiceProvider services) : IRemoteMethodProvider
{
	public void Register(RemoteMethodRegistry methods) => methods
		.Read("game.score", "The current score.",
			_ => JsonValue.Create(services.GetRequiredService<ScoreSystem>().Score), watchable: true)
		.Mutate("game.add_score", "Adds points to the score.",
			request =>
			{
				services.GetRequiredService<ScoreSystem>().Score += request.GetInt32("points", 1);
				return null;
			},
			RemoteSchema.Object(("points?", RemoteSchema.Integer("Points to add (default 1)."))));
}

// Program.cs
builder.Services.AddRemoteMethods(static sp => new ScoreRemoteMethods(sp));
```

Method names are dotted and lower case (`module.verb`) and cannot contain `+`; only read methods can be watchable. The
registration helpers (`AddRemoteMethods`, `AddRemoteResource`, `AddRemoteEvent`) do nothing when the module is compiled
out, so you can leave them in Release code.

:::tip
Keep handlers cheap and side-effect free for read methods: a watched read method runs again at the end of every frame.
:::

## Testing against the protocol

The remote server is an ordinary service, so tests can turn it on in an `IonTestHost` and call it over HTTP on a free
port. Because the test thread is the game thread, pump frames while waiting for the answer:

```csharp
using var host = new IonTestHost()
	.WithConfiguration(new Dictionary<string, string?>
	{
		["Ion:Remote:Enabled"] = "true",
		["Ion:Remote:Port"] = "0",
		["Ion:Remote:PrintToken"] = "false",
		["Ion:Remote:AllowMutations"] = "true",
		["Ion:Remote:RunDirectory"] = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")),
	})
	.UseEntryPoint<Program>();
host.Start();
var server = host.Get<RemoteServer>();
// POST to $"http://127.0.0.1:{server.Port}/" with server.MutateToken, calling host.Step() until the response arrives.
```

`RemoteServer.AttachStream(input, output)` serves the stdio transport over any pair of streams, which is handy for
in-process tests. See [Testing](/Ion/tooling/testing/).

## See also

- [ion remote](/Ion/tooling/ion-cli/#ion-remote): call a method from a shell.
- [MCP server](/Ion/tooling/mcp-server/): the protocol as agent tools.
- [UI over the remote protocol](/Ion/interaction/ui/ui-remote/): drive menus by path.
- [Components and serialization](/Ion/ecs/components-and-serialization/): what makes a component remote-visible.
- [Stage order](/Ion/reference/stage-order/): where `StageOrder.Remote` sits in the frame.
- The design document, [docs/design/ion-remote.md](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-remote.md).
