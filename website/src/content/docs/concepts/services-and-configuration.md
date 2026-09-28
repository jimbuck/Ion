---
title: Services and configuration
description: Dependency injection in Ion, where configuration comes from (appsettings.json, command line, environment), the Ion:* sections, short switches and the options pattern.
sidebar:
  order: 5
---

Ion is built on the standard .NET hosting stack: `Microsoft.Extensions.DependencyInjection` for services,
`Microsoft.Extensions.Configuration` for settings and `Microsoft.Extensions.Options` for typed options. If you know them
from ASP.NET Core, everything here works the same way.

## Dependency injection

Register services on `builder.Services` before `Build()`, and consume them through constructors (systems) or step
parameters:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddSystem<ScoreSystem>();
builder.Services.AddSingleton<ScoreBoard>();                                   // your own state
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));       // an instance built from config
```

```csharp
public sealed class ScoreSystem(ScoreBoard board, IEvents events, ILogger<ScoreSystem> logger)
{
    // ...
}
```

### Lifetimes

| Lifetime | In the root schedule | In a scene's schedule |
|---|---|---|
| Singleton | Yes. One instance for the application. The default for `AddSystem<T>()`. | Yes, shared with the root. |
| Scoped | **No**: error `ION006`, because the root schedule resolves from the root provider. | Yes. One instance per scene scope, disposed when the scene unloads. |
| Transient | Resolved once, when the schedule is built (like everything a schedule resolves). | Resolved once per scene load. |

Systems and step parameters are resolved **once**, when the schedule is built, never per frame. So there is no per-frame
cost to injection, and a transient service injected into a step behaves like a singleton for that step.

Scenes use scopes for their own services. The ECS `World` and `Commands` and the physics worlds are resolved per scope:
the root provider (root systems) gets the root world, and each scene's scope gets its own world, disposed with the
scene. See [Scenes](/Ion/ecs/scenes/).

### What the engine registers

`CreateBuilder` registers the core services every game has. `AddIon()` and the other modules add theirs. The ones you
inject most often:

| Service | From | Use |
|---|---|---|
| `IEvents` | Core | Emit and read events. |
| `ILoopContext` | Core | The running stage, frame and fixed step count. |
| `IClock` | Core | The loop's time source (`StopwatchClock` by default). |
| `IPersistentStorage` | Core | Game, asset and user data folders. See [Storage](/Ion/concepts/storage/). |
| `ILogger<T>` | Core | Logging. |
| `IOptions<T>`, `IOptionsMonitor<T>` | Core | Typed options (`GameConfig` and every module's config). |
| `IConfiguration` | Core | Raw configuration. |
| `IInputState` | `AddIon` | Keyboard, mouse, gamepads, touch. |
| `IWindow` | `AddIon` | The window (or the null window headless). |
| `ISpriteBatch` | `AddIon` | 2D drawing. |
| `IAssetManager` | `AddIon` | Loading assets. |
| `IAudioManager` | `AddIon` | Playing sounds. |
| `IMetrics` | `AddIon` | Counters, gauges, profiling. |
| `ICoroutineRunner` | `AddIon` | The shared coroutine runner. |
| `IRenderer3D` | `AddRendering3D` | The 3D renderer. |
| `World`, `Commands` | `AddEcs` | The ECS world of the current scope and its command buffer. |

Replacing a core service is ordinary DI: register your own implementation after the engine's (the last registration
wins for single-service resolution). The test host does exactly this to install a deterministic clock.

## Where configuration comes from

`IonApplication.CreateBuilder(args)` uses `Host.CreateApplicationBuilder`, so the standard sources apply, later ones
overriding earlier ones:

1. `appsettings.json`
2. `appsettings.{Environment}.json` (the environment comes from `DOTNET_ENVIRONMENT`; the default is `Production`)
3. Environment variables (`Ion__MaxFPS=60` sets `Ion:MaxFPS`, using a double underscore for `:`)
4. The command line (`--Ion:MaxFPS=60`, after Ion's short switches are expanded)

Read values while you register with `builder.Configuration`, and at run time through `IConfiguration` or options.

:::note[appsettings.json is read from the executable's folder]
Ion reads `appsettings.json` and `appsettings.{Environment}.json` from the **content root**, which `CreateBuilder` sets
to the executable's folder (`AppContext.BaseDirectory`) instead of the .NET host's default, the current directory. So
`dotnet run --project Game` from another folder, or launching the executable from anywhere, finds the settings that
were copied next to it. Keep them copied to the output:

```xml
<Content Include="appsettings*.json" CopyToOutputDirectory="PreserveNewest" />
```

To read them from another folder, pass `--contentRoot <folder>` (or set `DOTNET_CONTENTROOT`); an explicit content root
always wins. As in any .NET host, a relative content root resolves against the executable's folder, not the current
directory. Game content, assets and relative `Ion:Storage` paths resolve against the same content root (see
[Storage](/Ion/concepts/storage/)).
:::

### Short switches

`CreateBuilder` rewrites a few short switches into configuration keys before binding (`IonCommandLine.Normalize`):

| Switch | Becomes |
|---|---|
| `--headless` | `--Ion:Headless=true` |
| `--headless-render` | `--Ion:Headless=true --Ion:Headless:Render=true` |
| `--remote` | `--Ion:Remote:Enabled=true` |
| `--remote-allow-mutations` | `--Ion:Remote:Enabled=true --Ion:Remote:AllowMutations=true` |
| `--remote-stdio` | `--Ion:Remote:Enabled=true --Ion:Remote:Transport=Stdio` |

Every other argument passes through unchanged, so any key can be set as `--Section:Key=value`.

## The Ion configuration sections

All engine settings live under the `Ion` section. The core ones are bound into `GameConfig`:

| Key | Type | Default | Meaning |
|---|---|---|---|
| `Ion:Title` | string | `Ion` | The window title and the name of the per-user data folder. |
| `Ion:MaxFPS` | int | `300` | Render rate cap. `0` (or below 1) means uncapped. Ignored when `Ion:VSync` is true. |
| `Ion:FixedUpdateRate` | double | `60` | `FixedUpdate` steps per second. Non-positive values fall back to 60. |
| `Ion:MaxFrameTime` | TimeSpan | `00:00:00.1` | Longer frames are clamped to this. Non-positive values fall back to 100 ms. |
| `Ion:VSync` | bool | `false` | The loop does no pacing of its own and relies on presentation to block. |
| `Ion:Seed` | int | none | The game's random seed; read with `IonRun.Seed(config)`. |
| `Ion:PrintSchedule` | bool | `false` | Print the schedule at startup. |
| `Ion:Headless` | bool | `false` | Use the headless graphics and audio backends. |
| `Ion:Run:Frames` | int | none | `Run()` stops after this many frames. |

Each module binds its own subsection:

| Section | Options type | Module |
|---|---|---|
| `Ion:Storage` | `StorageConfig` | Core; see [Storage](/Ion/concepts/storage/) |
| `Ion:Input` | `InputConfig` (`GamepadDeadZone`, default 0.15) | Core |
| `Ion:Window` | `WindowConfig` | Windowing; see [Windowing](/Ion/rendering/windowing/) |
| `Ion:Graphics` | `GraphicsConfig` | Graphics; see [Graphics backends](/Ion/rendering/graphics-backends/) |
| `Ion:Headless:Render` | | Headless rendering |
| `Ion:Assets` | | Assets (`HotReload`) |
| `Ion:Audio` | `AudioConfig` | Audio |
| `Ion:Metrics` | `MetricsConfig` | Metrics |
| `Ion:Rendering3D` | `Rendering3DOptions` | 3D renderer |
| `Ion:Physics2D`, `Ion:Physics3D` | `Physics2DConfig`, `Physics3DConfig` | Physics |
| `Ion:Network` | `NetworkConfig` | Networking |
| `Ion:Web` | `WebOptions` | Web server |
| `Ion:Remote` | | Remote protocol |
| `Ion:Run` | | Agent and CI runs (`Frames`, `FixedStep`, `Screenshot`, `Summary`) |

The [configuration reference](/Ion/reference/configuration/) lists every key with its default.

```json title="appsettings.json"
{
  "Logging": { "LogLevel": { "Default": "Information", "Ion": "Information" } },
  "Ion": {
    "Title": "Arena",
    "MaxFPS": 120,
    "FixedUpdateRate": 60,
    "Window": { "Width": 960, "Height": 540 },
    "Graphics": { "PreferredBackend": "Auto", "VSync": false },
    "Audio": { "MaxVoices": 64 },
    "Metrics": { "Profiling": false }
  },
  "Game": { "Balls": 8 }
}
```

:::note[Two VSync settings]
`Ion:VSync` (`GameConfig.VSync`) tells the **game loop** not to pace frames itself. `Ion:Graphics:VSync`
(`GraphicsConfig.VSync`) chooses the **presentation mode** (FIFO when true, mailbox where supported otherwise). For
display-synced frames without the loop also sleeping, set both.
:::

## Options: typed settings

Modules bind their section into an options class and let you override it in code. The `configure` delegate of a
builder method runs **after** the section is bound, so code wins over files:

```csharp
builder.AddIon(graphics => graphics.ClearColor = new Color(0x1B, 0x26, 0x3B));
builder.AddRendering3D(options => options.Shadows = false);
builder.AddAudio(audio => audio.MaxVoices = 32);
```

Consume options with `IOptions<T>` (read once) or `IOptionsMonitor<T>` (current value, updated when the file changes):

```csharp
public sealed class TitleSystem(IOptions<GameConfig> game, IWindow window)
{
    [Init]
    public void SetTitle(GameTime dt) => window.Title = $"{game.Value.Title} (v1.2)";
}
```

The game loop reads `IOptionsMonitor<GameConfig>.CurrentValue` every frame, so edits to `MaxFPS` or `FixedUpdateRate`
in `appsettings.json` apply to a running game when the configuration reloads.

### Your own settings

Bind your own section the same way:

```csharp
public sealed class ArenaOptions
{
    public int Balls { get; set; } = 8;
    public float Speed { get; set; } = 200;
}

builder.Services.Configure<ArenaOptions>(builder.Configuration.GetSection("Game"));
```

```csharp
public sealed class BallSystem(IOptions<ArenaOptions> options)
{
    private readonly ArenaOptions _options = options.Value;
}
```

Reflection-based binding works for games running on the JIT. For NativeAOT, enable the configuration binding source
generator (`<EnableConfigurationBindingGenerator>true</EnableConfigurationBindingGenerator>`, which the engine's own
libraries use) or read keys by hand. The templates do the latter, with a small record:

```csharp title="Game.cs (2D template)"
public sealed record GameSettings(float PaddleSpeed, float BallSpeed, int Seed)
{
    public static GameSettings From(IConfiguration config) => new(
        float.TryParse(config["Game:PaddleSpeed"], NumberStyles.Float, CultureInfo.InvariantCulture, out var paddle) ? paddle : 480f,
        float.TryParse(config["Game:BallSpeed"], NumberStyles.Float, CultureInfo.InvariantCulture, out var ball) ? ball : 300f,
        IonRun.Seed(config, fallback: 1));
}
```

Parse with `CultureInfo.InvariantCulture` so `1.5` means the same on every machine (templates publish with invariant
globalization).

## Logging

`CreateBuilder` configures a single-line console logger with an `[HH:mm:ss] ` timestamp and the debug logger. Adjust
levels in `Logging:LogLevel` as usual. Useful categories:

| Category | Logs |
|---|---|
| `Ion.Schedule` | Schedule warnings (`ION010`, `ION012`, `ION013`); at `Debug`, whether the generated schedule is used and why not. |
| `Ion.Extensions.Graphics.*` | Backend selection, window and device creation. |
| `Ion.Extensions.Assets.*` | Asset loads (at `Debug`) and hot reloads. |

## Configuration in tests

`IonTestHost` puts its configuration in place before your `Program.cs` creates its builder, so the program sees it while
it registers:

```csharp
using var run = IonTestHost.RunEntryPoint<Program>(120, host => host
    .WithConfiguration("Ion:Seed", "7")
    .WithArgs("--Game:Balls=3"));
```

## See also

- [The application](/Ion/concepts/application/): builder methods and module dependencies.
- [Configuration reference](/Ion/reference/configuration/): every key.
- [Storage](/Ion/concepts/storage/): where files are read and written.
- [Testing](/Ion/tooling/testing/).
