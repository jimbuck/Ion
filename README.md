# Ion Engine
A small, positively-charged, schedule-based game engine for C#.

- **Modern:** Built using modern C# features and design patterns, Ion setup closely resembles ASP.NET Core in setup and configuration.
- **Modular:** Ion is a collection of modules that build on the `Ion.Core` module. You can use as many or as few modules as you want.
- **Systems:** Game code is plain classes whose attributed methods are steps of the game loop's stages. Steps are ordered explicitly, validated when the game starts, and the whole schedule can be printed.

----

## Requirements

Ion targets `net10.0` and builds with the .NET 10 SDK (pinned in `global.json`, `rollForward: latestFeature`). The source generators target `netstandard2.0` on Roslyn 4.4, so they load in any compiler from the .NET 8 SDK onwards. Games can be published with NativeAOT (`dotnet publish -r <rid> -p:PublishAot=true`).

## Systems and the schedule

The game loop runs seven stages: `Init` (once), then every frame `First`, `FixedUpdate` (zero or more times, fixed step), `Update`, `Render`, `Last`, and `Destroy` (once). A **system** is any class registered in DI and added with `UseSystem<T>()`; each public method with a stage attribute is a **step** of that stage. A step runs and returns: there is no `next`.

```csharp
public sealed class PaddleSystem(World world, IInputState input)
{
    [Init] public void Load(GameTime dt, IWindow window) { ... }          // extra parameters are services, resolved once
    [FixedUpdate(Order = 10), After<PhysicsSystem>] public void Move(GameTime dt) { ... }
    [Render] public void Draw(GameTime dt) { ... }
}

public sealed class FrameTimer
{
    [Begin(Stage.Render, Order = -100)] public void Start(GameTime dt) { ... }
    [End(Stage.Render, Order = -100)] public void Stop(GameTime dt) { ... }   // always runs, in a finally
}

builder.Services.AddSingleton<PaddleSystem>().AddSingleton<FrameTimer>();
app.UseSystem<PaddleSystem>().UseSystem<FrameTimer>().UseIon();          // registration order is only a tie breaker
app.Update((GameTime dt, IInputState input) => { ... });                 // function step with injected services
app.Render(Hud.Draw, order: 50);                                         // a static method group
```

- **Order.** `[Update(Order = n)]` (every stage attribute has `Order`, default 0). Lower runs first; ties are broken by registration order, then declaration order. `[After<T>]` and `[Before<T>]` (on a method, or on the class for all its steps; several allowed) order a step relative to every step of system `T` in the same stage, and take precedence over `Order`.
- **Scopes.** A `[Begin(stage)]`/`[End(stage)]` pair on one system wraps every step and scope that sorts after the begin in that stage; the end runs in a `finally`. Use `ScopeName` to pair several scopes of one system. This is what used to be code before and after `next(dt)` (frame begin/end, sprite batch begin/end, profiling).
- **Engine order bands.** Engine steps use `-1000..-500` (setup: trace, window, input, graphics frame, audio, sprite batch, coroutines, scenes) and `500..1000` (teardown: window close check, event stepping), so user steps at order 0 always run between them, whether they were registered before or after `UseIon()`. The values are in `StageOrder`.
- **Function steps.** `app.Init(...)` to `app.Destroy(...)` (and `scene.Update(...)` etc.) take an `Action<GameTime>` or a delegate with up to four service parameters after `GameTime`; `order` and `name` are optional. A method group with services needs its service types spelled out: `app.Update<ILogger<Hud>>(Hud.Log)`.
- **Scenes.** `UseScene(id, scene => scene.UseSystem<T>())` builds each scene's own schedule with the same rules; the `SceneSystem` runs it at order `StageOrder.Scenes` (-500) in every stage. Scene systems are resolved from the scene's scope, so they may be scoped; root systems must not be (ION006).
- **Validation.** `app.Build()` (and `Run`) plans the schedule and throws `IonScheduleException` listing every error: `ION001` unknown stage, `ION002` Before/After cycle (naming the steps), `ION003` Begin without End or the reverse, `ION004` a stage attribute on a non-public method (or a step added to a scene after it loaded), `ION005` async or `Task`-returning step, `ION006` scoped system or scoped step parameter in the root schedule, `ION007` unsupported signature, `ION008` unregistered parameter service, `ION009` unregistered system, `ION011` ambiguous scope. Warnings are logged under `Ion.Schedule`: `ION010` legacy middleware, `ION012` constraint on a system that is not in the schedule, `ION013` system without steps.
- **Printing.** `app.PrintSchedule()` returns every stage in run order with orders, `System.Method`, constraints and braces for scopes, followed by each scene's schedule; `--Ion:PrintSchedule=true` prints it at startup. This is the first thing to read when a system does not run when expected.

```text
  Render
      -850  NullSpriteBatchSystem.Begin {
         0    SpriteRendererSystem.Render
         0    ScoreSystem.RenderScore
        10    PhysicsSystem.Render
       900    NullWindowSystem.CheckClosed
      -850  } NullSpriteBatchSystem.End
```

**Legacy middleware.** Methods of the form `void M(GameTime dt, GameLoopDelegate next)` or `GameLoopDelegate M(GameLoopDelegate next)`, and `app.UseUpdate(next => dt => ...)` delegates (including the `UseUpdate<TService...>` overloads), keep working for one release as opaque middleware placed by their order: they wrap every step after them. Building logs `ION010` with the rewrite.

## Modules

### Ion.Core
A simple, modern code-first game engine inspired by Monogame and Bevy with an API like ASP.NET Core. This core module contains the core engine and all of the core features:
  - Application builder
  - Game loop
  - Events
  - Storage

### Ion.Extensions.Assets
Adds support for loading assets such as textures, models, and sounds. Load assets through their interfaces so the game runs on any backend:

```csharp
var tiles = assets.Load<ITexture2D>("tiles.png");
var font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(24); // or assets.LoadFontSet(name, files...)
var bonk = assets.Load<ISoundEffect>("bonk.wav");
```

Loading the same path twice returns the cached instance.

### Ion.Extensions.Audio
Adds support for audio playback and manipulation. `AddAudio()` plays through DirectSound (NAudio); `AddNullAudio()` is a headless `IAudioManager` that records every play in `NullAudioManager.Plays` and reads only WAV headers.

### Ion.Extensions.Coroutines
Adds support for coroutines, allowing for async code to be run in a synchronous manner.

### Ion.Extensions.Debug
Adds support for debug utils such as a trace profiler and debug renderer.

### Ion.Extensions.Graphics.Veldrid
Adds window, input, and graphics support using the Veldrid API. Includes a built-in sprite batch for easy 2D rendering.

### Ion.Extensions.Graphics.Null
A headless graphics backend with no window, GPU or SDL, for tests, servers and CI. `AddNullGraphics(config)` / `UseNullGraphics()` register everything the Veldrid backend does:
  - `NullWindow`: sized from `Ion:Window`, emits `WindowResizeEvent` at Init and `WindowClosedEvent` from `Close()` (which ends the game loop).
  - `NullInputState`: scripted input (`Press`, `Release`, `Tap`, `Click`, `SetMousePosition`, `Scroll`) applied at the start of the next frame.
  - `NullSpriteBatch`: draws nothing and records per-frame statistics (`LastFrame.DrawCalls`, `Sprites`, `Strings`, and the last draw commands).
  - Loaders that read texture sizes from image headers and fonts that measure text with a fixed glyph width.

### Running headless
`AddIon(config)` switches graphics and audio to the headless backends when `Ion:Headless` is `true` or `Ion:Graphics:Output` is `None`, and `UseIon()` adds the matching systems. Any game that depends only on the interfaces (`IWindow`, `IInputState`, `ISpriteBatch`, `IAudioManager`, `ITexture2D`, `IFontSet`, `ISoundEffect`) runs without a GPU, window or audio device:

```sh
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS -- --Ion:Headless=true
```

In tests, resolve `NullInputState` to script input and `NullSpriteBatch` / `NullAudioManager` to assert on what was drawn and played, or use `IonTestHost` (below).

### Input and fixed steps
`IInputState` edges (`Pressed`, `Released`) and deltas (`WheelDelta`, `MouseDelta`) depend on the stage that reads them. From `First`, `Update`, `Render` and `Last` they describe the current frame. From `FixedUpdate` they describe everything since the previous fixed step, so a click is seen by exactly one fixed step even when `MaxFPS` is above `FixedUpdateRate` and some frames run no fixed step. Events get the same guarantee: an event that leaves the two-frame window before any fixed step ran is still delivered to `FixedUpdate` listeners. The loop publishes the running stage through `ILoopContext`.

## Testing
`Ion.Testing` runs a game headless on a deterministic `FixedStepClock` (one 60 Hz fixed step per frame by default), so tests can step it frame by frame and assert on services, draw counts, sounds and events:

```csharp
using Ion.Testing;

using var host = new IonTestHost()          // or new IonTestHost(TimeSpan.FromSeconds(1.0 / 120))
    .WithConfiguration("Ion:Seed", "42")
    .Configure(services => services.AddSingleton<ScoreSystem>())
    .WithSystem<PlayerSystem>();              // steps ordered by Order, then in the order systems were added

var scores = host.Collect<ScoredEvent>();     // records every ScoredEvent the game emits

host.Step(10);                                // builds the app, runs Init, then 10 frames
host.Input.Click(MouseButton.Left);           // applied at the start of the next frame
Assert.True(host.RunUntil(() => scores.Count > 0, maxFrames: 600));

Assert.True(host.SpriteBatch.LastFrame.Sprites > 0);
Assert.NotEmpty(host.Audio.Plays);
Assert.Equal(1, host.Get<ScoreSystem>().Lives);
```

`Configure` and `ConfigureApp` add service registrations and schedule setup, `WithConfiguration` adds settings, and `UseGame(configure, use)` builds a whole game that calls `AddIon`/`UseIon` itself (see `Ion.Examples/Ion.Examples.Breakout.ECS.Tests`, which plays 600 frames of Breakout with the autopilot and checks the run is deterministic). Disposing the host runs Destroy and disposes the application. `Screenshot()` throws `NotSupportedException` until the headless renderer lands.

### Ion.Extensions.Scenes
Adds support for scenes that each have their own scope for dependency injection and their own schedule, run by the `SceneSystem` step (see "Systems and the schedule").

----

## Planned Modules
 - Web-based UI Framework
 - Low-level Networking
 - Plugin-in Physics Engine Support
 - Multi-platform build support

## Built Using/Inspired By
  - [Veldrid](https://github.com/veldrid/veldrid) for Graphics
  - [Peridot by Ezequias Silva](https://github.com/ezequias2d/peridot) for Sprite Batch
  - [Coroutines by ChevyRay](https://github.com/ChevyRay/Coroutines)

## Roadmap

See [docs/plans/2026-09-engine-review-and-roadmap.md](./docs/plans/2026-09-engine-review-and-roadmap.md) for the current review, benchmark baseline and staged plan. Micro-benchmarks live in `Ion/Ion.Benchmarks`.

## Contributing

Feel free to check out the samples and open any issues or pull requests. If you have any questions, feel free to ask in the discussions tab.

## Examples

Check out the Breakout ECS example for a simple game using the Ion Engine.
![Breakout ECS Screenshot](./breakout-physics-debug.png)

----

## License
Ion Engine is [MIT licensed](./LICENSE).
