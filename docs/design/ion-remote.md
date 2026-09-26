# Ion remote inspection protocol

`Ion.Extensions.Remote` lets tools and coding agents inspect and drive a running Ion game: query and change ECS entities,
read and write resources, read the schedule, metrics, events and logs, take screenshots, inject input, pause and step.
It is JSON-RPC 2.0 modelled on the [Bevy Remote Protocol](https://github.com/bevyengine/bevy/blob/main/crates/bevy_remote/src/lib.rs)
(BRP), carried over HTTP/1.1, WebSocket and stdio. The MCP server of the `ion` tool (`ion mcp`) is a client of it. This is
item 6 of section 4.10 of `docs/plans/2026-09-engine-review-and-roadmap.md`, delivered in Stage 6.

## 1. Using it

```sh
dotnet run -- --remote                       # read-only session: queries, schema, metrics, screenshots, watches
dotnet run -- --remote-allow-mutations       # also writes: components, spawn/despawn, resources, input, pause/step
dotnet run -- --remote-stdio                 # JSON-RPC on stdin/stdout instead of HTTP (a parent process drives the game)
ion remote world.query '{"name":"Ball*"}'    # from a shell, with the token file
```

`AddIon`/`UseIon` register the module in every game, and it does nothing unless enabled. Without the `Ion` meta package:
`services.AddRemote(configuration)` and `app.UseRemote()`.

Configuration (`Ion:Remote`, class `RemoteOptions`):

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Run the server (`--remote`). |
| `Transport` | `Http` | `Http` (HTTP and WebSocket), `Stdio`, or `Both` (`--remote-stdio` sets `Stdio`). |
| `Bind` | `127.0.0.1` | HTTP bind address; non-loopback needs `AllowNonLoopback`. |
| `AllowNonLoopback` | `false` | The explicit, logged opt-in for a non-loopback bind. |
| `Port` | `15702` | HTTP port (BRP's); `0` picks a free one (written to the token file). |
| `AllowMutations` | `false` | Create the mutate scope (`--remote-allow-mutations`). |
| `RunDirectory` | `.ion/run` | Where the token file is written. |
| `TokenFile` | `remote.json` | The token file name. |
| `PrintToken` | `true` | Print the endpoint and tokens once to standard error. |
| `AllowedOrigins` | none | Browser origins allowed to call (exact `Origin` values). |
| `MaxConnections` | `8` | Concurrent HTTP and WebSocket connections. |
| `MaxRequestBytes` | 4 MiB | Largest request body or WebSocket message. |
| `MaxRequestsPerFrame` | `64` | Requests applied per frame while running. |
| `RequestTimeoutMs` | `30000` | How long an HTTP request waits for the game thread. |
| `IdempotencyCacheSize` | `1024` | Mutation responses kept for replays. |
| `StartPaused` | `false` | Pause at the end of the first frame. |
| `PauseAtFrame` | `-1` | Pause after this many frames (the MCP `ion_run` live mode uses it). |

## 2. Threading: requests are applied at a stage boundary

No async runs in the frame. The transports run on their own threads with blocking `System.Net.Sockets` I/O (no Kestrel):
one accept thread, one thread per HTTP connection, a reader and a writer thread per WebSocket, and a reader and a writer
thread for stdio. A transport thread parses the message, authenticates it, answers what needs no game state (parse
errors, unknown methods, missing scope, watches over plain HTTP) and puts valid requests on a `ConcurrentQueue`.

The game thread applies the queue in `RemoteSystem.Process`, a Last step at **`StageOrder.Remote` = 970**:

- after every gameplay, render and ECS step of the frame, including the ECS command playback at `StageOrder.Ecs` (950),
  so a read sees the finished frame, a screenshot the rendered image, and a structural change never lands inside a query;
- before the event stepping at `StageOrder.Events` (1000), so `events.tail` still sees the frame's events;
- a mutation is therefore visible from the next frame's `First` stage on.

Every handler, read or mutate, runs there. At most `MaxRequestsPerFrame` requests are applied per frame; the rest wait
for the next one. Responses go back through the connection: an HTTP exchange hands its response to the waiting
connection thread, WebSocket and stdio connections queue messages for their writer thread, so a slow client never blocks
the frame (a client that stops reading loses its connection).

**Pause.** `game.pause` (or `PauseAtFrame`, `StartPaused`) makes the remote step block the game thread at the end of the
frame, serving requests as they arrive (a 50 ms wait between checks), until `game.resume`, `game.step` or `game.exit`.
While paused the window is not pumped. `game.step {"frames": n}` answers after the n frames have run, paused again.

## 3. Security

The policy is roadmap 4.10 item 6, and it matches the networking module's (`docs/design/ion-networking.md` section 10):
no listening socket unless configured, loopback by default, an explicit logged opt-in for anything else.

- **Off by default.** Nothing listens unless `Ion:Remote:Enabled` (or `--remote`).
- **Loopback bind.** `Bind` defaults to `127.0.0.1`. Any address that is not loopback (`0.0.0.0`, a LAN address) makes
  the server throw `RemoteSecurityException` at Init unless `AllowNonLoopback` is also set, which is logged as a warning.
- **Per-run bearer tokens.** Each run creates a read token and, only with `AllowMutations`, a mutate token (32 random
  bytes, base64url). They are printed once to standard error and written with the endpoint to the token file
  (`<RunDirectory>/remote.json`), created with owner-only permissions (mode 600 on Unix, `UnixCreateMode` at creation so
  there is no window where it is readable) and deleted at shutdown. HTTP and WebSocket clients send
  `Authorization: Bearer <token>`; a missing or unknown token gets HTTP 401 with error -32001 before the body is read.
  Tokens are compared in constant time. Tokens are never accepted in URLs.
- **Read and mutate scopes.** Read methods: `rpc.discover`, `game.info`, `schedule.get`, `metrics.get`, `screenshot`,
  `events.tail`, `log.tail`, `resources.list`, `resources.get`, `registry.schema`, `world.list`, `world.query`,
  `world.get_components`, `world.list_components`, and every `+watch`. Mutate methods: `game.pause`, `game.resume`,
  `game.step`, `game.exit`, `input.send`, `resources.set`, `world.insert_components`, `world.mutate_components`,
  `world.remove_components`, `world.spawn`, `world.despawn` (and any provider method registered as mutate). A read session
  calling a mutate method gets HTTP 403 with error -32002, checked on the transport thread and again on the game thread.
- **Stdio needs no token**: it inherits the parent process's trust. It still gets the mutate scope only with
  `AllowMutations`. With the stdio transport, standard output carries the protocol: `Console.Out` is redirected to
  standard error and console logs go to standard error.
- **Browsers.** A request with an `Origin` header is refused (403) unless the origin is in `AllowedOrigins`, and when
  bound to loopback the `Host` header must name a loopback host (`localhost`, `127.0.0.1`, `[::1]`), which defeats DNS
  rebinding. The P2 web module (Stage 6b) reuses this policy for its browser clients.
- **Idempotent mutations.** A mutation's response is kept (LRU, `IdempotencyCacheSize`) under the key (request id, method,
  params). A request with the same key is answered with the stored response without applying it again, so a retried or
  interrupted request cannot double-apply. The same id with different params is a new request. Clients should use unique
  ids (the `ion` tools use a GUID per request). Notifications (no id) are applied without this protection.
- **Scene loading.** A mutation is rejected with -32003 while a scene change is under way (`SceneSystem.IsLoading`: a scene
  is loading or unloading, or a change is pending or requested this frame). It is not cached, so the client retries with
  the same id and it applies once.
- **Bounds.** Request heads are limited to 16 KiB, bodies and messages to `MaxRequestBytes`, connections to
  `MaxConnections`; malformed HTTP or WebSocket framing closes the connection.
- **Compiled out of Release.** The `Ion.Remote.IsSupported` feature switch (`RemoteFeature.IsSupported`) comes from the
  `IonRemote` MSBuild property, `false` by default for `-c Release` and `true` otherwise
  (`buildTransitive/Ion.Extensions.Remote.targets`; the repository imports it from `Directory.Build.targets`). With
  `false`, a trimmed or NativeAOT publish removes the server, the transports and every provider registered through
  `AddRemoteMethods`/`AddRemoteResource`/`AddRemoteEvent` (they are guarded by the switch); an untrimmed Release build keeps
  the code but `AddRemote` registers nothing and says so on standard error if `--remote` is passed. Publish with
  `-p:IonRemote=true` to keep it (the Breakout sample sets it so the tool's end-to-end tests can drive it).

## 4. Messages

One JSON-RPC 2.0 request per HTTP `POST /` (or `/rpc`) body, per WebSocket text message (`GET /ws` with the upgrade), or
per stdio line. Batches are not supported. `params` must be an object or absent.

```json
{"jsonrpc":"2.0","id":"q1","method":"world.query","params":{"with":["Ball"],"components":["Transform2D"]}}
{"jsonrpc":"2.0","id":"q1","result":{"entities":[{"entity":0,"name":"Ball0","components":{"Transform2D":{"Position":[282.2,175.7],"Rotation":0,"Scale":[1,1]}}}],"total":1,"truncated":false}}
```

**Watches.** A read method registered as watchable has a `+watch` variant (`world.get_components+watch`) over WebSocket
and stdio (plain HTTP gets -32005). The server answers at once, then re-evaluates the handler at the end of every frame
and sends a new response with the same id whenever the result differs from the last one sent (BRP's model).
`rpc.unwatch {"id": <watch id>}` stops one watch of the session (all of them without an id); closing the connection stops
them all.

**Entities** are addressed by id (a number, as `world.query` returns it) or by `EntityName` (a string). Worlds: the root
world and one per loaded scene; methods take an optional `world` index (`world.list`) and default to the most recent live
world (the active scene's, or the root's).

**Components.** Remote-visible components are exactly the world serializer's `ComponentSerializerRegistry`: the built-in
ones (`Transform2D`, `Transform`, `Parent`, `Children`, `EntityName`, `SpriteAnimation`, `Camera2D`, tags `Hidden`,
`Visible`, `MainCamera`) plus what the game registers with `AddEcsSerialization(r => r.AddUnmanaged("Velocity",
GameJson.Default.Velocity))`. Their JSON is the world serializer's (source-generated `JsonTypeInfo`, NativeAOT-safe),
except that entity references are entity ids (-1 for none) rather than saved positions. Other components are listed by
type name in `world.list_components` but cannot be read or written. No `[RemoteVisible]` attribute or extra generator is
needed: registering a component for serialization is the opt-in, and it is the one place that owns its JSON form.

### Error codes

| Code | HTTP | Meaning |
|---|---|---|
| -32700 | 200 | Parse error. |
| -32600 | 200 | Invalid request (not an object, no `jsonrpc: "2.0"`, a batch, an unwatchable `+watch`). |
| -32601 | 200 | Method not found. |
| -32602 | 200 | Invalid params. |
| -32603 | 200 | The handler threw (message is the exception's). |
| -32001 | 401 | Missing or unknown bearer token. |
| -32002 | 403 | Read session calling a mutate method, or a refused origin or Host. |
| -32003 | 200 | Mutation while a scene is loading; retry with the same id. |
| -32004 | 200 | Entity, component, resource or path not found. |
| -32005 | 200 | Unsupported here (no ECS, no screenshot source, read-only resource, watch over HTTP). |
| -32006 | 504 | The game thread did not answer within `RequestTimeoutMs`. |

## 5. Methods

| Method | Access | Params | Result |
|---|---|---|---|
| `rpc.discover` | read | | protocol, version, the session's access, every method with access, description, params schema, watchable |
| `rpc.unwatch` | read | `id?` | `{removed}` |
| `game.info` (+watch) | read | | title, pid, frame, stage, fixedSteps, paused, headless, headlessRender, scene, sceneLoading, fps, allowMutations, access |
| `game.pause` | mutate | | `{paused, frame}` |
| `game.resume` | mutate | | `{paused}` |
| `game.step` | mutate | `frames?` (1) | after the frames ran: `{frame, paused}` |
| `game.exit` | mutate | | `{exiting}` (the loop stops after the frame) |
| `schedule.get` | read | | `text` (as `--Ion:PrintSchedule`), `stages[{stage, steps[{order, kind, name, end, depth}]}]`, `scenes[]` |
| `metrics.get` (+watch) | read | | the last frame in the JSONL frame log's shape (`frame`, `frame_ms`, `draw_calls`, `sprites`, `entities`, ...), `counters`, `gauges`, `histograms`, `profiling` |
| `screenshot` | read | | `{format: "png", width, height, frame, data: base64}` of the last rendered frame (headless rendering, or a window with frame retention, which `AddRemote` turns on) |
| `input.send` | mutate | `events[]` | `{queued, frame}`; see below |
| `events.tail` (+watch) | read | `since?`, `limit?` | `counts[{type, id, thisFrame, lastFrame, total}]` for every channel, `events[{seq, frame, type, payload}]` for types registered with `AddRemoteEvent`, `next` |
| `log.tail` (+watch) | read | `since?`, `level?`, `limit?` | `entries[{seq, time, level, category, message, exception}]`, `next` |
| `resources.list` | read | | `[{name, description, writable, schema}]` |
| `resources.get` (+watch) | read | `name` | the value |
| `resources.set` | mutate | `name`, `value` | the new value |
| `registry.schema` | read | | `components[{name, type, tag, schema}]` (JSON Schema from `JsonSchemaExporter`) |
| `world.list` | read | | `[{index, entities, default}]` |
| `world.query` (+watch) | read | `components?`, `with?`, `without?`, `name?` (exact or `prefix*`), `limit?`, `world?` | `{entities[{entity, name, components}], total, truncated}` |
| `world.get_components` (+watch) | read | `entity`, `components?`, `world?` | `{entity, name, components}` |
| `world.list_components` | read | `entity`, `world?` | `{entity, components[], other[]}` |
| `world.insert_components` | mutate | `entity`, `components{name: value}`, `world?` | the entity with those components |
| `world.mutate_components` | mutate | `entity`, `component`, `path` (`Position.0`; empty replaces), `value`, `world?` | `{entity, component, value}` |
| `world.remove_components` | mutate | `entity`, `components[]`, `world?` | `{entity, removed[]}` |
| `world.spawn` | mutate | `components?`, `name?`, `world?` | the new entity (nothing is created if a component name is wrong) |
| `world.despawn` | mutate | `entity`, `world?` | `{entity, despawned}` |

Built-in resources: `Ion.GameTime` (read: frame, elapsed seconds, delta, fixed steps, stage) and `Ion.Metrics.Profiling`
(read and write: the span recording toggle). Games add theirs with `AddRemoteResource(name, description, jsonTypeInfo,
get, set?)`.

**`input.send`** goes through Input v2's scripted path: `ScriptedInput` (new, `Ion.Core.Abstractions`) is attached to the
application's `InputTracker` as `InputTracker.Script` and applies its queue at `BeginFrame`, after a playback and
alongside device input, so injected input produces the same edges, fixed-step views and recordings (`InputRecorder`) as
a device; while an `InputPlayer` plays, it owns the input and injected events are ignored. Events (each takes an
optional `delay` in frames):

```json
{"type":"key","key":"Space","action":"tap|press|release|hold","frames":30,"modifiers":["Control"]}
{"type":"pointer","x":100,"y":200,"button":"Left","action":"move|click|press|release"}
{"type":"wheel","delta":1}
{"type":"text","text":"hello"}
{"type":"gamepad","index":0,"button":"A","action":"tap|press|release"}
{"type":"gamepad","index":0,"axis":"LeftX","value":0.5}
{"type":"gamepad","index":0,"action":"connect|disconnect"}
```

## 6. Extension point

Modules add methods without the remote server referencing them, through `Ion.Extensions.Remote.Abstractions` (which
depends only on `Ion.Core.Abstractions`):

```csharp
public interface IRemoteMethodProvider { void Register(RemoteMethodRegistry methods); }

services.AddRemoteMethods(sp => new UiRemoteMethods(sp.GetRequiredService<IUiTree>()));

sealed class UiRemoteMethods(IUiTree tree) : IRemoteMethodProvider
{
    public void Register(RemoteMethodRegistry methods) => methods
        .Read("ui.tree", "The UI tree.", _ => tree.ToJson(), watchable: true)
        .Mutate("ui.click", "Clicks a node by path.", r => { tree.Click(r.GetString("path")); return null; },
            RemoteSchema.Object(("path", RemoteSchema.String("The node path."))))
        .Mutate("ui.set_value", "Sets a widget's value.", r => { tree.SetValue(r.GetString("path"), r.Get("value")); return null; })
        .Mutate("ui.focus", "Focuses a node.", r => { tree.Focus(r.GetString("path")); return null; })
        .Mutate("ui.type", "Types text into the focused node.", r => { tree.Type(r.GetString("text")); return null; });
}
```

Handlers run on the game thread at the remote step, get a `RemoteRequest` (params with typed getters that throw
-32602, the services, the session's access, whether this is a watch re-evaluation) and return a `JsonNode` or throw
`RemoteException(code, message, data)`. The registration helpers are no-ops when the module is compiled out. The ECS
module registers its `world.*` methods this way (`EcsRemoteMethods`, from `AddEcs`).

## 7. Files and tests

- `Ion/Ion.Extensions.Remote.Abstractions`: `IRemoteMethodProvider`, `RemoteMethodRegistry`, `RemoteMethod`,
  `RemoteRequest`, `RemoteException`, `RemoteErrorCodes`, `RemoteSchema`, `RemoteResource`, `RemoteEventSource`,
  `RemoteFeature`, `RemoteServiceCollectionExtensions`.
- `Ion/Ion.Extensions.Remote`: `RemoteServer`, `RemoteSystem`, `HttpTransport` (HTTP/1.1 and RFC 6455 WebSocket),
  `StreamTransport` (stdio), `CoreRemoteMethods`, `RemoteLogBuffer`, `RemoteOptions`, `AddRemote`/`UseRemote`, and
  `buildTransitive/Ion.Extensions.Remote.targets`.
- `Ion/Ion.Extensions.Ecs/Remote/EcsRemoteMethods.cs`: `world.*` and `registry.schema`.
- `Ion/Ion.Extensions.Remote.Tests`: every method (`ProtocolTests`), the access control (`SecurityTests`: no token 401
  and -32001, the read token cannot call any mutate method, no mutate token without `AllowMutations`, non-loopback bind
  refused unless allowed, origin and Host checks, owner-only token file, replayed mutation ids apply once, mutations
  rejected while a scene loads), and the transports (`TransportTests`: stdio with and without mutations, stdio watches and
  `rpc.unwatch`, WebSocket authentication and watches).
