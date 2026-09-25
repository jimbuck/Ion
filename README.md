# Ion Engine
A small, positively-charged, schedule-based game engine for C#.

- **Modern:** Built using modern C# features and design patterns, Ion setup closely resembles ASP.NET Core in setup and configuration.
- **Modular:** Ion is a collection of modules that build on the `Ion.Core` module. You can use as many or as few modules as you want.
- **Systems:** Game code is plain classes whose attributed methods are steps of the game loop's stages. Steps are ordered explicitly, validated when the game starts, and the whole schedule can be printed.

----

## Requirements

Ion targets `net10.0` and builds with the .NET 10 SDK (pinned in `global.json`, `rollForward: latestFeature`). The source generators target `netstandard2.0` on Roslyn 4.4, so they load in any compiler from the .NET 8 SDK onwards; the schedule generator's interceptors need the .NET SDK 9.0.200 or later (see [Compile-time schedule](#compile-time-schedule-source-generator)). Games can be published with NativeAOT (`dotnet publish -r <rid> -p:PublishAot=true`).

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

## Compile-time schedule (source generator)

`Ion.Generators` turns the schedule into code at compile time. It ships as an analyzer of the `Ion` and `Ion.Core` packages, whose build props enable its interceptor namespace, so a game that references either package needs no setup. In a project that references the generator by `ProjectReference` instead (as the samples in this repository do), add:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Ion.Generated</InterceptorsNamespaces>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/Ion.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Interceptors need the .NET SDK 9.0.200 or later (Roslyn 4.12); with an older compiler, or without the namespace, the generator reports `ION014` and the game runs on the runtime path.

**What it does.** It intercepts `UseSystem`, function steps (`app.Update(...)`), legacy middleware (`app.UseUpdate(...)`), `UseScene` and `Build()`/`Run()`/`RunFrames()`, and emits:

- **Pre-bound registrations.** Each system is described at compile time (steps, scopes, orders, constraints, diagnostics) with delegates that bind its methods directly, so the runtime plans and binds it without reflection. This is what makes NativeAOT publishing reflection-free.
- **A generated schedule** for each application (the registrations made on it before `Build()`/`Run()` in the same method) and each scene (its configure callback): one method per stage that calls every step directly, in plan order, with a `try/finally` per scope; systems and injected services are resolved once in its constructor. It is sorted by the same code as the runtime planner (`ScheduleSorter`, shared), so `PrintSchedule()` is identical either way.
- **Registration summaries.** For every method that takes an application or scene builder (`UseIon()`, `UseNullGraphics()`, your own `UseMyGame(app)`), an assembly attribute lists what it registers, so an application compiled later sees through helpers in other assemblies. Registrations under an `if` are included and guarded.
- **Diagnostics.** `ION001` to `ION013` become compiler errors and warnings with the runtime's messages, at the offending method (or the registration call).

```csharp
// Generated for the Render stage of a small game (abridged):
public override void Render(global::Ion.GameTime dt)
{
    _s0.BeginRender(dt);                       // -1000 TraceTimerSystem.BeginRender
    try
    {
        _b1(dt);                                // -850 NullSpriteBatchSystem.Begin (internal: bound delegate)
        try
        {
            _s7.Render(dt);                     // 0 SpriteRendererSystem.Render
            _s9.RenderScore(dt);                // 0 ScoreSystem.RenderScore
            if (_g3) _d4(dt);                   // 900 NullWindowSystem.CheckClosed (registered under an if)
        }
        finally { _e1(dt); }
    }
    finally { _s0.EndRender(dt); }
}
```

**When it is used.** At `Build()`, the registrations actually made are aligned with the ones the generator saw (each carries its call site) and the runtime plan is compared with the generated order. If anything differs (a `Type` only known at run time, a plugin, a helper compiled without the generator, a registration made in a loop or through a callback the generator could not follow) the runtime binds the plan itself, from the pre-bound registrations where it has them, and logs why at `Debug` level under `Ion.Schedule`. `loop.Schedule.IsGenerated` tells which path runs. Correctness never depends on the generator seeing everything; only speed does.

**What changes for you.** Stack traces through the schedule show only your frames: every generated type and the engine's dispatch methods are `[StackTraceHidden]`.

```text
System.InvalidOperationException: Thrown by a user step.
   at MyGame.ThrowingSystem.Render(GameTime dt) in .../Systems.cs:line 12
   at Program.<Main>$(String[] args) in .../Program.cs:line 30
```

Per-frame dispatch is direct calls (32 systems in a stage: 15.7 ns against 13.3 ns for a hand-written loop of direct calls, and 67 ns for the runtime-bound schedule; see `docs/plans/benchmarks/2026-09-25-stage2-generator`). The runtime path remains the reference behaviour and is what runs when the generator is not referenced.

**Limits.** The generator cannot see the output of other source generators, so registrations whose arguments depend on generated code are left to the runtime (the scenes generator's enum overloads are library methods for this reason). `ION006`, `ION008` and `ION009` are reported only for types declared in the project that no service registration call in the project mentions; if a project registers its systems by assembly scanning, turn them off with `dotnet_diagnostic.ION009.severity = none` (the runtime check still applies).

## Events

Events are unmanaged structs on typed channels. Inject `IEvents`, emit with `Emit`, and read with an `EventReader<T>` created once (in the constructor or a field initializer) and kept in a field that is not `readonly`:

```csharp
public record struct BlockHitEvent(Entity Block);

public class ScoreSystem(IEvents events)
{
    private EventReader<BlockHitEvent> _hits = events.Reader<BlockHitEvent>();
    public int Score { get; private set; }

    [Update] public void Tally(GameTime dt) => Score += 10 * _hits.Read().Length;   // a span of every unread event
}

public class BlockSystem(IEvents events)
{
    [FixedUpdate] public void Hit(GameTime dt) => events.Emit(new BlockHitEvent(block));
}
```

- **Semantics.** An event is visible in the frame it is emitted (after the emit) and in the next one, and each reader sees each event once (two readers each see every event). `TryRead(out e)` reads one, `Read()` returns every unread event as a `ReadOnlySpan<T>` (valid until the end of the frame), `TryReadLatest(out e)` reads all and returns the newest, `Any()` peeks, `Skip()` marks everything read. A reader that reads during `FixedUpdate` also sees older events no fixed step has seen yet, so fixed-step consumers never miss an event on frames that run no fixed step (the backlog is bounded by `EventBus.MaxBacklogFrames`).
- **Cost.** One array per event type, reused frame after frame (it grows when a frame emits more than it holds and never shrinks); a reader is a struct holding the channel and a cursor. Emitting and reading do not allocate. 100 events of 4 types, each read by 8 readers: 650 ns per frame (it was 1.5 ms with the old listeners).
- **Generated bus.** With the source generator, the application's `IonApplication.CreateBuilder` call installs a generated bus: one typed channel field per event type the game (or an Ion assembly it references) uses, with a compile-time integer id per type (`EventId<T>.Value`, used in logs and traces) and an initial capacity chosen from how the type is emitted (larger when emitted in `FixedUpdate`/`Update`, larger still in a loop there). The game's own `Emit`/`Reader` calls are intercepted and write to the field directly. Types the generator cannot see (a plugin's events, calls through generic helpers) get a channel at run time on the same bus, so both coexist.
- **Diagnostics.** The generator checks event usage at compile time:

| Id | Severity | Reported when |
|---|---|---|
| `ION101` | Warning | An event type is emitted but nothing in the application or the Ion assemblies it references reads it. |
| `ION102` | Warning | An event type is read but nothing emits it. |
| `ION103` | Warning | A reader is created inside a per-frame stage method (it would start over and re-read the previous frame every time). |
| `ION104` | Error | An event payload is not an unmanaged struct (the message names the offending field). |
| `ION105` | Info | A reader reads an event in an earlier stage than the only stages that emit it, so it sees each event a frame late. |
| `ION106` | Warning | A reader is stored in a `readonly` field or exposed as a property, so reads advance a copy and never move on. |

Methods that emit or read an event type for their caller (such as `EmitChangeScene`, `Wait.For<T>()` or `IonTestHost.Collect<T>()`) are marked `[EmitsEvent]`/`[ReadsEvent]` so the generator counts their call sites; libraries compiled with the generator publish an `[assembly: EventUsage(...)]` summary of their event types.

`IEventEmitter`, `IEventListener`, `IEventListenerFactory`, `EventEmitter` and `EventListener` still work as obsolete adapters over `IEvents` for one release.

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

**Hot reload.** With `Ion:Assets:HotReload = true` (the default in Debug builds; `IonTestHost` turns it off) an `IAssetWatcher` watches the assets folder, and `AssetReloadSystem` (added by `UseAssets()`, which `UseIon()` calls; First stage, `StageOrder.AssetReload`) reloads every cached asset loaded from a changed file at the start of the next frame, in the global cache and in every live scene scope. Loaders that implement `IReloadableAssetLoader` update the asset in place, so every holder sees the change: the headless texture and font loaders do, and the Veldrid texture loader re-uploads the pixels into the same GPU texture when the image keeps its size. Other assets (a texture whose size changed, Veldrid fonts, sounds) are loaded again and the new instance replaces the old one in the cache. Each reload emits `AssetReloadedEvent(AssetId, PreviousAssetId)` (`InPlace` when they are equal); a failed reload (a half-written or deleted file) logs a warning and keeps the loaded asset. `watcher.Enqueue(path)` queues a reload by hand, with or without the file system watcher.

```csharp
// _reloads = events.Reader<AssetReloadedEvent>(), created once in the constructor.
while (_reloads.TryRead(out var e)) if (!e.InPlace) _tiles = assets.Load<ITexture2D>("tiles.png");
```

### Ion.Extensions.Audio
An engine-owned mixer that runs on Windows, macOS, Linux (x64 and arm64, including handhelds), iOS and Android, with no NAudio and nothing platform-specific above the output:

```csharp
var music = audio.Play(theme, bus: AudioBus.Music, loop: true, fadeIn: 2f);
audio.Play(bonk, volume: 0.8f, pitchShift: 0.1f, pan: -0.5f); // pitch in octaves, pan -1 (left) to 1 (right)
audio.SetBusVolume(AudioBus.Sfx, 0.5f);
audio.MasterVolume = 0.9f;
audio.Stop(music, fadeOut: 1f);
if (!audio.IsPlaying(music)) { /* finished */ }
```

  - **Mixer.** Float32 interleaved stereo at `Ion:Audio:OutputRate` (default 48 kHz). Voices have volume, pitch (resampled with linear or cubic interpolation), pan (linear balance), loop and fade in/out, and feed the master, sfx and music buses. The voice pool is fixed (`MaxVoices`, default 64); when it is full, `VoiceStealing.Oldest` (default) replaces the voice that started first, preferring one-shots over loops, and `VoiceStealing.Refuse` returns an invalid `VoiceHandle`.
  - **Threads.** `IAudioManager` calls only queue commands. The audio system flushes them in the `Last` stage (order `StageOrder.Audio`) into a lock-free single-producer single-consumer queue; the audio thread applies them, mixes and reports finished voices on a second queue. The audio thread never blocks and never allocates after warm-up (checked by a test).
  - **Decoding at load time.** WAV (PCM 8/16/24/32-bit, float 32/64-bit, extensible), OGG Vorbis (NVorbis) and MP3 (NLayer), all managed and NativeAOT-clean, resampled once to the output rate with a windowed-sinc filter. `ISoundEffect` has `Duration`, `Channels` and `SampleRate`. Music is decoded whole: streaming is not implemented yet.
  - **Outputs.** `IAudioOutput` (`Start(format, callback)`, `Stop`, `BufferSize`). `OpenAlAudioOutput` streams through OpenAL with a ring of `BufferCount` queued buffers of `BufferFrames` frames refilled on a dedicated thread; it loads OpenAL Soft (binaries for Windows, macOS and Linux from `Silk.NET.OpenAL.Soft.Native`) or the system OpenAL (iOS and macOS OpenAL.framework, `libopenal.so` bundled by an Android app). `NullAudioOutput` mixes on the game thread, driven by the game clock, so headless runs are deterministic and tests can capture the mix (`CaptureEnabled`, `Captured`). If the device or library is missing, a warning is logged and the null output takes over: startup never fails because of audio.
  - **Registration.** `AddAudio(config)` / `UseAudio()` use `Ion:Audio:Backend` (`Auto`, `OpenAL`, `Null`). `AddNullAudio(config)` / `UseNullAudio()` run the same mixer on the null output with `NullAudioManager`, which also records every play in `Plays`.

```json
{ "Ion": { "Audio": { "OutputRate": 48000, "MaxVoices": 64, "BufferFrames": 512, "BufferCount": 4, "Backend": "Auto", "Interpolation": "Linear", "VoiceStealing": "Oldest" } } }
```

### Ion.Extensions.Coroutines
Adds support for coroutines, allowing for async code to be run in a synchronous manner. `UseCoroutines()` steps the shared `ICoroutineRunner` once per frame in Update. A coroutine yields `null` (next frame), a number of seconds, `Wait.For(seconds)`, `Wait.Until(predicate)`, `Wait.While(predicate)`, `Wait.For<TEvent>()`, a custom `IWait`, or a nested routine. `Wait` is an unboxed struct union stored inline in the runner, so `IEnumerator<Wait>` coroutines allocate nothing per frame (100 coroutines: 0 B per frame):

```csharp
IEnumerator<Wait> Blink()
{
    while (true)
    {
        visible = !visible;
        yield return 0.5f;                       // or Wait.For(TimeSpan.FromSeconds(0.5))
        yield return Wait.For(FadeOut());        // runs a nested routine to completion
        yield return Wait.For<PlayerHitEvent>();
    }
}
```

Plain `IEnumerator` coroutines still work, but the routine boxes every struct it yields.

### Ion.Extensions.Debug
Adds support for debug utils such as a trace profiler and debug renderer.

### Ion.Extensions.Graphics.Veldrid
Adds window, input, and graphics support using the Veldrid API. Includes a built-in sprite batch for easy 2D rendering.

### Ion.Extensions.Graphics.Null
A headless graphics backend with no window, GPU or SDL, for tests, servers and CI. `AddNullGraphics(config)` / `UseNullGraphics()` register everything the Veldrid backend does:
  - `NullWindow`: sized from `Ion:Window`, emits `WindowResizeEvent` at Init and `WindowClosedEvent` from `Close()` (which ends the game loop).
  - `NullInputState`: scripted input applied at the start of the next frame: keys and buttons (`Press`, `Release`, `Tap`, `Repeat`, `Click`, `ReleaseAll`), the mouse (`SetMousePosition`, `Scroll`), text (`Type`) and gamepads (`ConnectGamepad`, `DisconnectGamepad`, `Press(pad, button)`, `Release`, `Tap`, `SetAxis`, `SetLeftStick`, `SetRightStick`).
  - `NullSpriteBatch`: draws nothing and records per-frame statistics (`LastFrame.DrawCalls`, `Sprites`, `Strings`, and the last draw commands).
  - Loaders that read texture sizes from image headers and fonts that measure text with a fixed glyph width.

### Running headless
`AddIon(config)` switches graphics and audio to the headless backends when `Ion:Headless` is `true` or `Ion:Graphics:Output` is `None`, and `UseIon()` adds the matching systems. Any game that depends only on the interfaces (`IWindow`, `IInputState`, `ISpriteBatch`, `IAudioManager`, `ITexture2D`, `IFontSet`, `ISoundEffect`) runs without a GPU, window or audio device:

```sh
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS -- --Ion:Headless=true
```

In tests, resolve `NullInputState` to script input and `NullSpriteBatch` / `NullAudioManager` to assert on what was drawn and played, or use `IonTestHost` (below).

### Input
`IInputState` is captured once per frame at the start of `First` and is read-only for the rest of the frame. Both backends keep it in one shared `InputTracker` (in `Ion.Core.Abstractions`) with fixed-size storage and no per-frame allocation: `ulong` bitsets indexed by `Key` for held, pressed and released keys, a 32-bit mask for mouse buttons, mouse position and delta, the wheel, the frame's text input and eight gamepad slots.

```csharp
if (input.Pressed(Key.S, ModifierKeys.Control)) Save();         // Ctrl+S, whichever key went down first
if (input.Modifiers.HasFlag(ModifierKeys.Shift)) speed *= 2;     // held modifiers, from the held keys
name += input.Text.ToString();                                    // text typed this frame (layout and IME applied)
var pad = input.Gamepad(0);                                       // never null; IsConnected tells
if (pad.Pressed(GamepadButton.A)) Jump();
var move = pad.LeftStick;                                         // dead zone applied (Ion:Input:GamepadDeadZone, default 0.15)
foreach (var p in input.Gamepads) { /* connected gamepads */ }
```

  - **Edges.** A key pressed and released within one frame reports both `Pressed` and `Released` for that frame. Key repeats mark a key held without a `Pressed` edge.
  - **Modifiers.** `Pressed(key, modifiers)` and `Released(key, modifiers)` test the modifiers reported with that key event and match when at least one of the requested flags was held; `ModifierKeys.None` never matches (use `Pressed(key)`). `Modifiers` is the level state derived from the held modifier keys, which are also ordinary keys (`Down(Key.ShiftLeft)`).
  - **Focus loss** releases every held key and mouse button without a `Released` edge, since the key up events go to another window. Gamepads are not affected.
  - **Gamepads.** Buttons use the SDL game controller layout (`GamepadButton`), sticks range from -1 to 1 with a radial dead zone and triggers from 0 to 1. The Veldrid backend reports no gamepads (its SDL2 input snapshot carries no controller events); the headless backend scripts them, and the Silk.NET backend of Stage 4 will feed real ones into the same tracker.

**Recording and playback.** `services.AddInputRecording("input.ioni")` writes every frame's input events to a compact binary file (completed when the application is disposed); `services.AddInputPlayback("input.ioni")` replays it at the recorded frame numbers, replacing device and scripted input until the recording ends. Replaying into an `IonTestHost` reproduces the same `Pressed`/`Down` sequence frame by frame, which makes a recorded play session a deterministic test. `InputRecorder` and `InputPlayer` can also be used directly (`InputTracker.Recorder`, `InputTracker.Playback`, or `InputPlayer.Play(frame, sink)` into any `IInputEventSink`).

#### Input and fixed steps
`IInputState` edges (`Pressed`, `Released`), deltas (`WheelDelta`, `MouseDelta`) and `Text` depend on the stage that reads them. From `First`, `Update`, `Render` and `Last` they describe the current frame. From `FixedUpdate` they describe everything since the previous fixed step, so a click is seen by exactly one fixed step even when `MaxFPS` is above `FixedUpdateRate` and some frames run no fixed step. Events get the same guarantee: an event that leaves the two-frame window before any fixed step ran is still delivered to readers in `FixedUpdate`. The loop publishes the running stage through `ILoopContext`.

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

`host.Audio` is the headless `NullAudioManager`: set `host.Audio.NullOutput.CaptureEnabled = true` before stepping to assert on the mixed samples (`host.Audio.NullOutput.Captured`).

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
