---
title: The application
description: How IonApplication.CreateBuilder, the builder's AddX methods, Build, the application's UseX methods and Run fit together, and which module brings which.
sidebar:
  order: 1
---

Every Ion game starts the same way: create a builder, register what the game needs, build the application, add systems
to its schedule, and run it.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Ecs.Rendering;

var builder = IonApplication.CreateBuilder(args);          // 1. configuration, logging, core services
builder.AddEcsRendering3D().AddSystem<ModelSystem>();      // 2. register modules and systems (services)

using var game = builder.Build();                          // 3. build the service provider
game.UseEcsRendering3D().UseSystem<ModelSystem>();         // 4. add systems to the schedule
game.Run();                                                // 5. plan, validate and run the game loop
```

The split mirrors ASP.NET Core's `WebApplication`: the **builder** owns configuration and the service collection
(`IServiceCollection`), and the **application** owns the service provider and the schedule. Things that are services are
registered on the builder; things that run every frame are added on the application.

## 1. Create the builder

`IonApplication.CreateBuilder(args)` returns an `IonApplicationBuilder`. It wraps .NET's
`Host.CreateApplicationBuilder`, so it reads configuration the standard way, and then adds Ion's core services.

| What | Details |
|---|---|
| Command line | `args`, after Ion's short switches are expanded (`--headless` becomes `--Ion:Headless=true`; see [Services and configuration](/Ion/concepts/services-and-configuration/)). |
| `appsettings.json` | And `appsettings.{Environment}.json`, from the content root. |
| Environment variables | The standard .NET host sources. |
| Logging | A single-line console logger with an `[HH:mm:ss] ` timestamp, plus the debug logger. |
| Core options | `GameConfig` bound from `Ion`, `StorageConfig` from `Ion:Storage`, `InputConfig` from `Ion:Input`. |
| Core services | The clock (`IClock`, a `StopwatchClock`), the loop context (`ILoopContext`), the event bus (`IEvents`), persistent storage (`IPersistentStorage`) and a disabled frame profiler until metrics are added. |

The builder exposes two properties:

| Property | Type | Use |
|---|---|---|
| `builder.Configuration` | `ConfigurationManager` | Read settings while you register (`builder.Configuration["Game:Balls"]`) or add sources. |
| `builder.Services` | `IServiceCollection` | Register your own services (`builder.Services.AddSingleton<GameState>()`). |

`CreateBuilder()` without arguments is the same as `CreateBuilder([])`.

## 2. Register modules and systems

### Modules: `builder.AddX()`

Each engine module has an `AddX` extension method on `IonApplicationBuilder`. It binds the module's options from
configuration, applies an optional delegate after binding, and registers the module's services:

```csharp
builder.AddIon(graphics => graphics.ClearColor = Color.Black);   // GraphicsConfig, after Ion:Graphics is bound
builder.AddRendering3D(options => options.Shadows = false);        // Rendering3DOptions, after Ion:Rendering3D
builder.AddAudio(audio => audio.MaxVoices = 32);                   // AudioConfig, after Ion:Audio
builder.AddPhysics2D(physics => physics.UnitsPerMeter = 64);       // Physics2DConfig
```

Each `AddX` returns the builder, so calls chain.

### Dependencies are pulled in for you

A module registers the modules it depends on. Registering one twice (directly, or through another module) registers it
once: the second call only applies its options delegate. So the order you list modules in does not matter, and
`builder.AddEcsRendering3D()` alone gives you the engine, the 3D renderer and the ECS:

| Builder / application | Registers and adds |
|---|---|
| `AddIon` / `UseIon` | The engine core: metrics, assets, graphics and input (windowed, or headless with `--headless`), the 2D renderer, audio, scenes, coroutines and the remote protocol. `AddAudio`, `AddMetrics` (with their options), `AddScenes`, `AddCoroutines`, `AddAssets` and `AddRemote` register it too. |
| `AddRendering3D` / `UseRendering3D` | The 3D renderer, and the engine core. |
| `AddEcs` / `UseEcs` | The ECS module only. `AddEcsSerialization` adds the world serializers. |
| `AddEcsRendering` / `UseEcsRendering` | The 2D sprite extraction, the ECS module and the engine core. |
| `AddEcsRendering3D` / `UseEcsRendering3D` | The 3D extraction, the ECS module and the 3D renderer (with the engine core). |
| `AddPhysics2D` / `UsePhysics2D` | 2D physics, the ECS module and the engine core. `AddPhysics2DRemote` adds its remote methods. |
| `AddPhysics3D` / `UsePhysics3D` | 3D physics, the ECS module and the 3D renderer. |
| `AddUi` / `UseUi` | The UI module and the engine core. `AddUiRemote` adds its remote methods. |
| `AddNetworking` / `UseNetworking` | Networking and the ECS module. `AddLiteNetLibTransport` and `AddLoopbackTransport` add a transport and networking. |
| `AddWeb` / `UseWeb` | The web server only (it has its own HTTP listener). |
| `AddInputRecording`, `AddInputPlayback`, `AddScriptedInput` | Input recording, playback and scripted input. |

Listing the modules a game uses is still good for the reader, which is why the templates write
`builder.AddIon().AddRendering3D()`.

:::caution[The first AddIon chooses headless or windowed]
`AddIon` picks the graphics output once, from configuration (`Ion:Headless`, `Ion:Graphics:Output`) and its options
delegate. A later `AddIon` whose options select the other output throws `InvalidOperationException`. If you set
`GraphicsConfig.Output` in code, do it in the first `AddIon` call, before modules that depend on the engine.
:::

### Systems: `builder.AddSystem<T>()`

A system is a class with stage methods (see [Systems](/Ion/concepts/systems/)). It must be registered as a service before
it can be added to the schedule. `AddSystem<T>()` (or `AddSystem(Type)`) registers it as a **singleton**, unless it is
already registered:

```csharp
builder.AddSystem<PaddleSystem>().AddSystem<BallSystem>();
```

Your other services go on `builder.Services` as in any .NET app:

```csharp
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));
builder.Services.AddSingleton<PlayState>();
```

### `builder.AddX()` versus `builder.Services.AddX(...)`

Most modules also have an `IServiceCollection` form, which takes the configuration explicitly:

```csharp
builder.Services.AddIon(builder.Configuration);
builder.Services.AddRendering3D(builder.Configuration);
```

The builder forms are the idiomatic way to set up a game: they pass the configuration for you and register
dependencies. The service collection forms remain for composing the engine by hand, for example to run part of the
stack without `AddIon`. The `Ion.Examples.Quad` sample does this to run only the window and the RHI:

```csharp title="Ion.Examples.Quad/Program.cs (abridged)"
var builder = IonApplication.CreateBuilder(args);
var headless = bool.TryParse(builder.Configuration["Ion:Headless"], out var configured) && configured;
if (headless)
{
    builder.Services.AddInputTracker();
    builder.Services.AddRhiGraphics(builder.Configuration, offscreen: true);
}
else
{
    builder.Services.AddSilkWindowing(builder.Configuration);
    builder.Services.AddRhiGraphics(builder.Configuration);
}

builder.AddSystem<QuadSystem>();

using var app = builder.Build();
app.UseEvents();
if (!headless) app.UseSilkWindowing();
app.UseRhiGraphics();
app.UseSystem<QuadSystem>();
app.Run();
```

:::note
When you compose by hand, `UseX` methods still add the systems of the modules they depend on (for example
`UsePhysics2D()` adds `UseIon()`), so you must register those modules' services too. The test host always registers
`AddIon`.
:::

## 3. Build the application

`builder.Build()` builds the .NET host and returns an `IonApplication`. After this point the service collection is
closed; the application exposes:

| Member | Use |
|---|---|
| `game.Services` | The `IServiceProvider`. Resolve services for setup code (`game.Services.GetRequiredService<T>()`). |
| `game.Configuration` | The final `IConfiguration`. |
| `game.Schedule` | The root `ScheduleModel`: the registrations you add with `UseSystem` and function steps. |
| `game.PrintSchedule()` | Plans and validates the schedule and returns it as text. |
| `game.Dispose()` | Disposes the services (and every disposable singleton). Safe to call twice. |

Always create the application with `using var game = builder.Build();` so that services are disposed when the program
ends.

## 4. Add systems to the schedule

The application's `UseX` methods add systems to the root schedule. `UseSystem<T>()` adds one of yours; module methods
such as `UseIon()` add the module's systems and those of the modules it depends on:

```csharp
game.UseIon()
    .UseSystem<PaddleSystem>()
    .UseSystem<BallSystem>();
```

- **Order of `UseSystem` calls rarely matters.** Steps run by their `Order` value; registration order only breaks ties.
  Engine steps use reserved bands below -500 and above 500, so your steps at the default order 0 always run between
  engine setup and teardown. See [Stages](/Ion/concepts/stages/).
- **`UseSystem` is idempotent.** Adding a system that is already in the schedule does nothing and it keeps its first
  place, at run time and in the generated schedule. That is what lets `UseEcsRendering3D()` add `UseIon()` safely.
- **Function steps** add a delegate instead of a class: `game.Update((GameTime dt, IInputState input) => ...)`. See
  [Systems](/Ion/concepts/systems/#function-steps).
- **Scenes** have their own schedules: `game.UseScene(id, scene => scene.UseSystem<T>())`. See
  [Scenes](/Ion/ecs/scenes/).

### Your own module

Group a game's registrations into a pair of extension methods, the way the engine does. The Breakout ECS sample is a
module (`AddBreakout`/`UseBreakout`) because its Android and iOS heads run it from their own entry points:

```csharp title="BreakoutGame.cs (abridged)"
public static class BreakoutGame
{
    public static IonApplicationBuilder AddBreakout(this IonApplicationBuilder builder)
    {
        builder.AddIon(graphics => graphics.ClearColor = new Color(0x333))
            .AddEcsRendering()
            .AddPhysics2D(physics => physics.GravityY = 0)
            .AddSystem<PaddleSystem>()
            .AddSystem<BallSystem>();

        if (builder.Configuration.IsHeadless()) builder.AddSystem<HeadlessAutopilotSystem>();
        return builder;
    }

    public static IIonApplication UseBreakout(this IIonApplication app)
    {
        app.UseEcsRendering()
            .UsePhysics2D()
            .UseSystem<PaddleSystem>()
            .UseSystem<BallSystem>();

        if (app.Configuration.IsHeadless()) app.UseSystem<HeadlessAutopilotSystem>();
        return app;
    }
}
```

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddBreakout();

using var game = builder.Build();
game.UseBreakout();
game.Run();
```

The source generator follows calls into methods that take a builder or an application (and registrations under an
`if`), so this stays on the generated, reflection-free path. See [Source generators](/Ion/concepts/source-generators/).

## 5. Run

| Method | Behavior |
|---|---|
| `game.Run()` | Plans, validates and binds the schedule, then runs `Init`, frames until the game exits, and `Destroy`. |
| `game.Run(cancellationToken)` | The same, and also stops (after the current frame) when the token is cancelled. |
| `game.RunFrames(frames)` | Runs `Init`, at most `frames` frames, then `Destroy`. Useful for scripted and deterministic runs. |

`Run()` honours `Ion:Run:Frames`: with `--Ion:Run:Frames=600` it behaves like `RunFrames(600)`. This is how
`ion run --frames` works for every game without code changes.

Building the schedule validates it. Every error is collected and thrown together as an `IonScheduleException` (with
`Diagnostics` and `Codes`), for example an unregistered system (`ION009`) or an ordering cycle (`ION002`). With the
source generator these are compile-time errors instead. Set `--Ion:PrintSchedule=true` to print the schedule to
standard output at startup.

For how frames are run, paced and ended, see [The game loop](/Ion/concepts/game-loop/).

## Headless

Every game built on `AddIon` runs headless without code changes. When `Ion:Headless` is `true` (`--headless`) or
`Ion:Graphics:Output` is `None`, `AddIon` registers the null graphics and audio backends (no window, GPU or audio
device) and `UseIon` adds their systems:

```bash
dotnet run -- --headless
dotnet run -- --headless-render      # also render offscreen (needs a Vulkan or EGL driver)
```

Your code keeps depending on the same interfaces (`IWindow`, `IInputState`, `ISpriteBatch`, `IAudioManager`,
`ITexture2D`, `IFontSet`, `ISoundEffect`). Use `builder.Configuration.IsHeadless()` (an extension in the `Ion`
namespace) to register something only in headless runs, as Breakout does with its autopilot.

## Tests run the same Program.cs

`Ion.Testing` runs your entry point as it is, the way ASP.NET Core's `WebApplicationFactory<Program>` does. The program
runs up to `game.Run()`, which hands the fully configured application to the test host instead of starting the loop:

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
host.Step(10);

using var run = IonTestHost.RunEntryPoint<Program>(600);
Assert.Equal(600, run.Frames);
```

The host's configuration (headless, a fixed 60 Hz clock, `WithConfiguration`, `WithArgs`) is in place when your program
creates its builder, and the host's own services are added after yours and win. A program is expected to create one
application: only the first `CreateBuilder` call is taken over. See [Testing](/Ion/tooling/testing/).

## See also

- [Stages](/Ion/concepts/stages/) and [Systems](/Ion/concepts/systems/): what the schedule is made of.
- [Services and configuration](/Ion/concepts/services-and-configuration/): DI lifetimes, `appsettings.json` and options.
- [Module map](/Ion/reference/module-map/): every package and its dependencies.
- [Configuration reference](/Ion/reference/configuration/): every `Ion:*` key.
