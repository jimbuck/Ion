---
title: Configuration keys
description: Every Ion configuration key, grouped by module, with its type, default and meaning, plus the short command line switches.
sidebar:
  order: 3
---

Ion reads its settings from .NET configuration: `appsettings.json`, `appsettings.{Environment}.json`, environment
variables and the command line, later sources winning. The JSON files are read from the executable's folder, whatever
the working directory (`--contentRoot <folder>` reads them from another folder). Every key below can be set in any of
them:

```json title="appsettings.json"
{
  "Ion": {
    "Title": "My Game",
    "MaxFPS": 120,
    "Window": { "Width": 1280, "Height": 720 },
    "Graphics": { "PreferredBackend": "Auto", "VSync": true }
  }
}
```

```bash
dotnet run -- --Ion:Window:Width=1280 --Ion:Graphics:PreferredBackend=OpenGLES
Ion__Window__Width=1280 dotnet run        # environment variables use a double underscore
```

Most sections are bound to an options class, and the `AddX(options => ...)` delegate of the module runs **after**
binding, so code can override or complete what configuration set. Keys are case-insensitive. `TimeSpan` values use the
`hh:mm:ss.fff` form (`00:00:00.040`). Enum values are written by name.

See [Services and configuration](/Ion/concepts/services-and-configuration/) for how binding works.

## Command line switches

`IonApplication.CreateBuilder(args)` rewrites these short switches before binding (`IonCommandLine`):

| Switch | Same as |
|---|---|
| `--headless` | `--Ion:Headless=true` |
| `--headless-render` | `--Ion:Headless=true --Ion:Headless:Render=true` |
| `--remote` | `--Ion:Remote:Enabled=true` |
| `--remote-allow-mutations` | `--Ion:Remote:Enabled=true --Ion:Remote:AllowMutations=true` |
| `--remote-stdio` | `--Ion:Remote:Enabled=true --Ion:Remote:Transport=Stdio` |

## Core (Ion)

Bound to `GameConfig` from the `Ion` section, plus keys read directly by the core and the `Ion` package.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Title` | string | `Ion` | The game's title: the window title and the per-user data folder name. |
| `Ion:MaxFPS` | int | `300` | The most rendered frames per second. `0` (or below 1) is uncapped. Ignored when `Ion:VSync` is true. Does not affect the simulation rate. |
| `Ion:FixedUpdateRate` | double (Hz) | `60` | The rate of the FixedUpdate stage. Values that are not positive fall back to 60. |
| `Ion:MaxFrameTime` | TimeSpan | `00:00:00.100` | Longer frames are clamped to this, so the fixed-step accumulator does not try to catch up after a hitch. |
| `Ion:VSync` | bool | `false` | The game loop does no frame pacing of its own and relies on presentation to block. See also `Ion:Graphics:VSync`. |
| `Ion:PrintSchedule` | bool | `false` | Print every stage's steps (and each scene's) at startup. |
| `Ion:Seed` | int | none | The random seed. Games read it (`IonRun.Seed(config)`); the run summary records it. |
| `Ion:Headless` | bool | `false` | `AddIon` registers the headless graphics and audio backends (no window, GPU or audio device). |
| `Ion:Headless:Render` | bool | `false` | With `Ion:Headless`: render for real into an offscreen target with the selected RHI backend. |

## Run settings (Ion:Run)

Honoured by every game that uses `AddIon`/`UseIon` (what `ion run` passes).

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Run:Frames` | int | none | Run this many frames, then Destroy and exit. |
| `Ion:Run:FixedStep` | bool | on when headless and `Ion:Run:Frames` is set | Use a deterministic `FixedStepClock` (every frame exactly one 60 Hz step). |
| `Ion:Run:Screenshot` | path | none | Write the last rendered frame to this PNG at the end of the run (needs headless rendering or a window). |
| `Ion:Run:Summary` | path | none | Write the run summary JSON (frame stats, counters, warnings, errors, exception, schedule) at the end of the run, also when an exception ends it. |

## Storage (Ion:Storage)

Bound to `StorageConfig`. Relative paths resolve against the content root: the executable's folder
(`AppContext.BaseDirectory`), unless `--contentRoot` sets another.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Storage:GamePath` | path | the content root (the executable's folder) | The root of the read-only game content. The mobile heads set it to where they unpacked the assets. |
| `Ion:Storage:AssetsPath` | path | `Assets` under `GamePath` | The folder assets are loaded from. Relative values resolve against `GamePath`. |
| `Ion:Storage:UserPath` | path | a per-game folder named after `Ion:Title` under the user's local application data | Per-user data (settings, saves). |

## Input (Ion:Input)

Bound to `InputConfig`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Input:GamepadDeadZone` | float (0 to 1) | `0.15` | Stick and trigger values below this read as zero; values above are rescaled to span the full range. |

## Assets (Ion:Assets)

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Assets:HotReload` | bool | `true` in Debug builds of `Ion.Extensions.Assets`, `false` in Release | Watch the assets folder and reload changed assets at the start of the next frame. `IonTestHost` turns it off. |

## Window (Ion:Window)

Bound to `WindowConfig`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Window:Width`, `Ion:Window:Height` | int | 960 x 540 (Silk.NET window) | The window size. The headless and offscreen targets are sized from these too. |
| `Ion:Window:WindowX`, `Ion:Window:WindowY` | int | platform default | The window position, used when both are set. |
| `Ion:Window:WindowState` | `WindowState` | `Normal` | `Normal`, `FullScreen` (exclusive, at the window size), `Maximized`, `Minimized`, `BorderlessFullScreen` (borderless at the monitor size) or `Hidden`. |
| `Ion:Window:Fullscreen` | bool | `false` | Shorthand for `WindowState = FullScreen`. |
| `Ion:Window:Resizable` | bool | `true` | Whether the user can resize the window. |
| `Ion:Window:ShowCursor` | bool | `true` | Whether the mouse cursor is visible. |
| `Ion:Window:Platform` | `WindowPlatform` | `Auto` | `Auto` (GLFW on desktop, SDL on Android and iOS), `Glfw` or `Sdl`. |

## Graphics (Ion:Graphics)

Bound to `GraphicsConfig`; `AddIon(graphics => ...)` configures it after binding.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Graphics:PreferredBackend` | `GraphicsBackend` | `Auto` | `Auto` (the first available in platform order: Vulkan then OpenGL ES, OpenGL ES first on Linux arm64), `Vulkan` or `OpenGLES`. `Direct3D12`, `Metal` and `WebGPU` are reserved and throw `NotSupportedException`. |
| `Ion:Graphics:VSync` | bool | `false` | Present with FIFO (wait for vertical blank). Off: mailbox where supported, else immediate. |
| `Ion:Graphics:FramesInFlight` | int | `2` | Frames the CPU may record ahead of the GPU (2 or 3; other values are clamped). |
| `Ion:Graphics:Validation` | bool | on in Debug builds of the backend, off in Release | The graphics API's validation (Vulkan: `VK_LAYER_KHRONOS_validation` and a debug messenger, when installed). |
| `Ion:Graphics:DepthBuffer` | bool | `true` | Create a depth target with the color target. |
| `Ion:Graphics:RetainLastFrame` | bool | `false` | Windowed backends copy every presented frame so screenshots work. Costs a full-frame copy per frame. The remote protocol turns it on. |
| `Ion:Graphics:Adapter` | string | none | Use the first GPU whose name contains this text (case-insensitive). Otherwise discrete, then integrated, then anything. |
| `Ion:Graphics:ClearColorHex` | string | black | The clear color as `RGB`, `RGBA`, `RRGGBB` or `RRGGBBAA`, with or without `#`. In code, set `ClearColor`. |
| `Ion:Graphics:Output` | `GraphicsOutput` | `Window` | `Window`, or `None` to select the headless backends, like `Ion:Headless`. |
| `Ion:Graphics:Gles:MaxFeatureLevel` | `GlesFeatureLevel` | `Es32` | The highest OpenGL ES feature level to use (`Es30`, `Es31` or `Es32`). Lower it to force the ES 3.0 or 3.1 paths. |

There is no frame rate key under `Ion:Graphics`: the loop's frame rate cap is `Ion:MaxFPS`, and `Ion:Graphics:VSync` only
sets the present mode (set `Ion:VSync=true` or `Ion:MaxFPS=0` too for a vsynced game).

## Rendering 3D (Ion:Rendering3D)

Bound to `Rendering3DOptions`; `AddRendering3D(options => ...)` runs after binding.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Rendering3D:Shadows` | bool | `true` | The first shadow-casting directional light renders a shadow map. |
| `Ion:Rendering3D:ShadowMapSize` | int | `2048` | The shadow map's width and height in texels. |
| `Ion:Rendering3D:ShadowDistance` | float | `40` | How far from the camera shadows reach, in world units. |
| `Ion:Rendering3D:DepthPrepass` | bool | `false` | Draw opaque objects into the depth buffer first, so the opaque pass shades each pixel once. |
| `Ion:Rendering3D:MaxCameras` | int | `8` | The most cameras rendered in one frame. |

## Audio (Ion:Audio)

Bound to `AudioConfig`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Audio:Backend` | `AudioBackend` | `Auto` | `Auto` (the platform device, falling back to null), `OpenAL`, or `Null` (mixed on the game thread, nothing heard). |
| `Ion:Audio:OutputRate` | int | `48000` | The mixer's output rate; every sound is resampled to it when loaded. |
| `Ion:Audio:MaxVoices` | int | `64` | The fixed voice pool. |
| `Ion:Audio:BufferFrames` | int | `512` | Frames mixed per buffer. Latency is about `BufferCount * BufferFrames / OutputRate` (43 ms with the defaults). |
| `Ion:Audio:BufferCount` | int | `4` | Buffers queued on the device. |
| `Ion:Audio:Device` | string | default device | The OpenAL device to open, by name. |
| `Ion:Audio:Interpolation` | `AudioInterpolation` | `Linear` | Resampling for pitch: `Linear` or `Cubic`. |
| `Ion:Audio:VoiceStealing` | `VoiceStealing` | `Oldest` | When every voice is busy: `Oldest` replaces the voice that started first (preferring one-shots), `Refuse` returns an invalid handle. |
| `Ion:Audio:CommandCapacity` | int | `1024` | Capacity of the lock-free queue from the game thread to the audio thread (rounded up to a power of two). |

## Metrics (Ion:Metrics)

Bound to `MetricsConfig`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Metrics:HistoryFrames` | int | `300` | Completed frames the profiler keeps (the ring depth). |
| `Ion:Metrics:SpansPerFrame` | int | `512` | Spans each frame can hold. |
| `Ion:Metrics:Profiling` | bool | `false` | Record spans from the start; the kept frames are written to `TraceOutput` at shutdown. |
| `Ion:Metrics:TraceOutput` | path | `trace.json` | Where Chrome traces are written. Empty disables writing. |
| `Ion:Metrics:FrameLog` | path | none | A JSON Lines file with one line per frame. |
| `Ion:Metrics:FrameLogFlushFrames` | int | `60` | How often the frame log is flushed, in frames. |
| `Ion:Metrics:CaptureKey` | `Key` | `F9` | The key that captures a trace of the next `CaptureFrames` frames. |
| `Ion:Metrics:CaptureFrames` | int | `120` | Frames a capture records. |
| `Ion:Metrics:Meter` | bool | `true` | Publish the `Ion` meter (for `dotnet-counters`). |
| `Ion:Metrics:Overlay` | bool | `false` | Draw the metrics overlay (needs `OverlayFont`). |
| `Ion:Metrics:OverlayFont` | string | none | The font asset the overlay uses. |
| `Ion:Metrics:OverlayFontSize` | float | `16` | The overlay's font size. |
| `Ion:Metrics:OverlayRefreshSeconds` | double | `0.25` | How often the overlay text refreshes. |

Obsolete (removed in 0.4): `Ion:Debug:TraceEnabled` maps to `Ion:Metrics:Profiling`, and `Ion:Debug:TraceOutput`
(default `./trace.json`) to `Ion:Metrics:TraceOutput`, when the game still calls `AddDebugUtils`.

## Remote protocol (Ion:Remote)

Bound to `RemoteOptions`. The server is compiled out of Release builds unless the game sets `IonRemote=true`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Remote:Enabled` | bool | `false` | Whether the server runs (or `--remote`). |
| `Ion:Remote:Transport` | `RemoteTransport` | `Http` | `Http` (HTTP and WebSocket), `Stdio` (newline-delimited JSON-RPC on standard input and output) or `Both`. |
| `Ion:Remote:Bind` | string | `127.0.0.1` | The HTTP bind address. Non-loopback addresses are refused unless `AllowNonLoopback`. |
| `Ion:Remote:AllowNonLoopback` | bool | `false` | The explicit, logged opt-in for a non-loopback bind. |
| `Ion:Remote:Port` | int | `15702` | The HTTP port; `0` picks a free one (written to the token file). |
| `Ion:Remote:AllowMutations` | bool | `false` | Whether the mutate scope exists (`--remote-allow-mutations`). |
| `Ion:Remote:RunDirectory` | path | `.ion/run` | Where the token file is written. |
| `Ion:Remote:TokenFile` | string | `remote.json` | The token file name inside the run directory. |
| `Ion:Remote:PrintToken` | bool | `true` | Print the endpoint and tokens to standard error at startup. |
| `Ion:Remote:AllowedOrigins` | list | empty | Browser origins allowed to call the HTTP transport; any other request with an `Origin` header is refused. |
| `Ion:Remote:MaxConnections` | int | `8` | The most concurrent connections. |
| `Ion:Remote:MaxRequestBytes` | int | 4 MiB | The largest request body or WebSocket message. |
| `Ion:Remote:MaxRequestsPerFrame` | int | `64` | Requests applied per frame. |
| `Ion:Remote:RequestTimeoutMs` | int | `30000` | How long an HTTP request waits for the game thread. |
| `Ion:Remote:IdempotencyCacheSize` | int | `1024` | Completed mutation responses kept for idempotent replays. |
| `Ion:Remote:StartPaused` | bool | `false` | Start paused, serving requests at the end of the first frame until `game.resume`. |
| `Ion:Remote:PauseAtFrame` | long | `-1` | Pause at the end of this frame number; negative never pauses. |

## Web server (Ion:Web)

Bound to `WebOptions`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Web:Enabled` | bool | `false` | Whether the server runs. |
| `Ion:Web:Bind` | string | `127.0.0.1` | An IP address, `localhost`, or `*`. Non-loopback is refused unless `AllowNonLoopback`. |
| `Ion:Web:AllowNonLoopback` | bool | `false` | The explicit, logged opt-in for a LAN bind. |
| `Ion:Web:Port` | int | `15780` | The port; `0` picks a free one. |
| `Ion:Web:Token` | string | none | The bearer token for mutating endpoints (every method but GET and HEAD, and WebSocket endpoints). On a non-loopback bind one is generated per run unless set. |
| `Ion:Web:GenerateToken` | bool | `false` | Generate a per-run token even on loopback. |
| `Ion:Web:AllowAnonymousMutations` | bool | `false` | On a non-loopback bind without a token, let anyone call mutating endpoints (logged). |
| `Ion:Web:AllowedOrigins` | list | empty | Browser origins allowed besides the server's own; allowed cross origins get CORS headers. |
| `Ion:Web:StaticFiles` | path | none | The folder static files are served from, relative to the executable's folder. |
| `Ion:Web:DefaultFile` | string | `index.html` | The file served for a folder. |
| `Ion:Web:MaxStaticFileBytes` | int | 16 MiB | The largest static file served. |
| `Ion:Web:HostEndpoints` | bool | `true` | Mount endpoints other modules register (the remote protocol at `/rpc`). |
| `Ion:Web:MaxConnections` | int | `32` | Concurrent HTTP and WebSocket connections. |
| `Ion:Web:MaxRequestBytes` | int | 1 MiB | The largest request body. |
| `Ion:Web:MaxWebSocketMessageBytes` | int | 64 KiB | The largest WebSocket message from a client. |
| `Ion:Web:MaxPendingMessagesPerClient` | int | `256` | Messages of one client waiting for the game thread; more closes it (1008). |
| `Ion:Web:QueueCapacity` | int | `1024` | The queue into the game thread. |
| `Ion:Web:MaxRequestsPerFrame` | int | `256` | Requests and messages handled per frame. |
| `Ion:Web:RequestTimeoutMs` | int | `10000` | How long a request waits for the game thread. |
| `Ion:Web:RateLimit` | double | `100` | Requests per second per client address; 0 or less disables it. |
| `Ion:Web:RateLimitBurst` | double | `200` | The burst allowed above the rate. |
| `Ion:Web:UseGeneratedRoutes` | bool | `true` | Serve every assembly's generated route table; false serves only tables added with `AddWebRoutes`. |
| `Ion:Web:PrintUrl` | bool | `true` | Print the URL (and a generated token) to standard error at startup. |

## Physics 2D (Ion:Physics2D)

Bound to `Physics2DConfig`; `AddPhysics2D(physics => ...)` runs after binding.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Physics2D:GravityX` | float | `0` | Horizontal gravity, world units per second squared. |
| `Ion:Physics2D:GravityY` | float | `9.81` | Vertical gravity (positive is down on screen). |
| `Ion:Physics2D:UnitsPerMeter` | float | `1` | World units per meter (for a pixel game, the pixel size of a one-meter object, such as 64). |
| `Ion:Physics2D:SubSteps` | int | `4` | Solver sub-steps per fixed step. |
| `Ion:Physics2D:EnableSleep` | bool | `true` | Resting bodies sleep. |
| `Ion:Physics2D:EnableContinuous` | bool | `true` | Continuous collision of dynamic bodies against static ones. |
| `Ion:Physics2D:ContactHertz` | float | `30` | Contact stiffness. |
| `Ion:Physics2D:ContactDampingRatio` | float | `10` | Contact damping ratio. |
| `Ion:Physics2D:RestitutionThreshold` | float | `0` (Box2D default, 1 m/s) | Relative speed below which contacts do not bounce. |
| `Ion:Physics2D:MaxLinearSpeed` | float | `0` (Box2D default, 400 m/s) | The maximum body speed. |
| `Ion:Physics2D:DebugDraw` | bool | `false` | Draw every collider and joint over the frame. |

## Physics 3D (Ion:Physics3D)

Bound to `Physics3DConfig`.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Physics3D:GravityX`, `GravityY`, `GravityZ` | float | `0`, `-9.81`, `0` | Gravity (y is up). |
| `Ion:Physics3D:SubSteps` | int | `1` | Solver sub-steps per fixed step. |
| `Ion:Physics3D:Iterations` | int | `8` | Velocity iterations per sub-step. |
| `Ion:Physics3D:ThreadCount` | int | `0` | 0 or 1 steps on the game thread; more uses that many workers (still waited for). Results differ from single-threaded ones, so a replay must use the same count. |
| `Ion:Physics3D:SleepThreshold` | float | `0.01` | Speed under which a body may sleep (0 disables sleeping). |
| `Ion:Physics3D:DebugDraw` | bool | `false` | Draw every collider as a translucent mesh. |

## Networking (Ion:Network)

Bound to `NetworkConfig`; `AddNetworking(network => ...)` runs after binding.

| Key | Type | Default | Description |
|---|---|---|---|
| `Ion:Network:Mode` | `NetworkMode` | `Offline` | `Offline`, `Client`, `Server` (dedicated) or `ListenServer`. |
| `Ion:Network:Bind` | string | `127.0.0.1` | The address a server listens on; others are logged as a public bind. |
| `Ion:Network:Port` | int | `7777` | The server port, and the port a client connects to (`0` on a server: any free port). |
| `Ion:Network:Connect` | string | `127.0.0.1` | The server a client connects to. |
| `Ion:Network:TickRate` | double | `0` (`Ion:FixedUpdateRate`) | Simulation ticks per second; compared in the handshake. |
| `Ion:Network:SnapshotHistory` | int | `128` | Ticks the snapshot ring keeps. |
| `Ion:Network:SendRate` | double | `0` (every tick) | Snapshots per second per client. |
| `Ion:Network:MaxPeers` | int | `16` | The most clients (at most 254). |
| `Ion:Network:GameId` | string | empty (`Ion:Title`) | A game identifier compared in the handshake. |
| `Ion:Network:JoinSecret` | string | none | Require a secret to join (HMAC-SHA256 challenge; the secret never crosses the network). |
| `Ion:Network:MaxRewindTicks` | int | `12` | How far back lag compensation rewinds. |
| `Ion:Network:InterpolationDelay` | double | `2` | Ticks behind the newest snapshot a client draws remote entities. |
| `Ion:Network:MaxExtrapolationTicks` | double | `2` | Ticks a remote entity is extrapolated before it freezes. |
| `Ion:Network:InputLeadTicks` | int | `2` | Ticks a client runs ahead of the server estimate. |
| `Ion:Network:MtuBytes` | int | `1200` | The largest packet; snapshots are split into parts of this size. |
| `Ion:Network:IdleTimeout` | TimeSpan | `00:00:10` | Disconnect a silent peer after this long. |
| `Ion:Network:HandshakeTimeout` | TimeSpan | `00:00:05` | Close an incomplete handshake after this long. |
| `Ion:Network:PingInterval` | TimeSpan | `00:00:00.250` | How often the round trip is measured. |
| `Ion:Network:MaxMessagesPerSecond` | int | `600` | Messages a client may send per second. |
| `Ion:Network:MaxBytesPerSecond` | int | `262144` | Bytes a client may send per second. |
| `Ion:Network:MaxViolations` | int | `50` | Protocol violations before a client is disconnected. |
| `Ion:Network:Simulate:Latency` | TimeSpan | `0` | Simulated one-way delay per packet. |
| `Ion:Network:Simulate:Jitter` | TimeSpan | `0` | A random extra delay up to this. |
| `Ion:Network:Simulate:Loss` | double (0 to 1) | `0` | Probability an unreliable or sequenced packet is dropped. |
| `Ion:Network:Simulate:Reorder` | double (0 to 1) | `0` | Probability an unreliable packet is held back so later ones overtake it. |
| `Ion:Network:Simulate:Seed` | int | `1` | Seed of the simulated decisions. |

The loopback transport always applies the simulation settings.

## Modules without configuration sections

The ECS module, scenes, coroutines and the UI have no configuration section. Their options are code only:
`AddUi(options => ...)` (`UiOptions`: `AutoFocus`, `Gamepad`, `Theme`), `AddEcsRendering(...)` and
`AddEcsRendering3D(...)` (`SpriteExtractionOptions`, `Scene3DExtractionOptions`).

## Sample-specific keys

The samples read a few keys of their own, not part of the engine: `Cubes:Frames`, `Cubes:Screenshot`, `Model:Frames`,
`Model:Screenshot`, `Quad:Frames`, `Quad:Screenshot`, `Quad:Spin`, `Sprites:Count`, `Sprites:Textures` and
`Sprites:Frames`. See the [examples](/Ion/examples/).

## See also

- [Services and configuration](/Ion/concepts/services-and-configuration/).
- [Module map](/Ion/reference/module-map/): which package owns each section.
- [Desktop](/Ion/platforms/desktop/) and [R36S](/Ion/platforms/r36s/): per-platform configuration files.
