---
title: Testing
description: Test Ion games headless and deterministically with Ion.Testing's IonTestHost, running your own Program.cs, scripting input, collecting events and capturing frames.
sidebar:
  order: 4
---

`Ion.Testing` runs an Ion game inside a test: headless (no GPU, window or audio device), on a deterministic clock (every
frame lasts exactly one 60 Hz step), stepped frame by frame from the test thread. You script input, step, and assert on
services, counters, draw counts, sounds, events, the ECS world and, with headless rendering, the pixels.

```csharp title="GameTests.cs"
using Ion.Testing;

public sealed class GameTests
{
	[Fact]
	public void BallsBounce()
	{
		using var run = IonTestHost.RunEntryPoint<Program>(600);

		Assert.Equal(600, run.Frames);
		Assert.True(run.Counters["bounces"] > 0);
	}
}
```

## Setting up a test project

Every template has one. By hand: reference the game project, `Ion.Testing` and a test framework (the templates use
xUnit):

```xml title="MyGame.Tests.csproj"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageReference Include="Ion.Testing" Version="0.3.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MyGame\MyGame.csproj" />
    <!-- Snapshots and golden images committed next to the tests. -->
    <None Include="Golden\**\*" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
</Project>
```

## Testing your Program.cs

The recommended way is to run the game's own entry point. `IonTestHost.UseEntryPoint<Program>()` starts `Main` of the
assembly that declares `Program` on a dedicated thread, lets it register its modules and systems, and takes over the
application at its `game.Run()` call. The test then drives exactly what production runs, including the schedule the
[source generator](/Ion/concepts/source-generators/) compiled for `Program.cs`.

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon()
	.AddEcs()
	.AddEcsSerialization(components => components
		.AddUnmanaged("Velocity", GameJson.Default.Velocity)
		.AddTag<Ball>("Ball"))
	.AddSystem<BallSystem>()
	.AddSystem<DrawSystem>();
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon()
	.UseEcs()
	.UseSystem<BallSystem>()
	.UseSystem<DrawSystem>();
game.Run();
```

For top-level statements the Ion generator declares the `Program` class public, so the test project can name it.

### RunEntryPoint: run N frames and inspect

`IonTestHost.RunEntryPoint<TProgram>(frames, configure?, frameTime?)` creates the host, applies `configure`, runs the
frames and returns an `IonRunResult`. Dispose it to run Destroy and release the game.

```csharp
[Fact]
public void TheSameSeedGivesTheSameWorld()
{
	string Run()
	{
		using var run = IonTestHost.RunEntryPoint<Program>(120, host => host.WithConfiguration("Ion:Seed", "7"));
		return run.WorldJson()!;
	}

	Assert.Equal(Run(), Run());
}
```

| `IonRunResult` member | Meaning |
|---|---|
| `Host` | The still running `IonTestHost`: step it further, script input, resolve services. |
| `Frames` | Frames run (fewer than asked when the game exited early). |
| `Exited` | Whether the game asked to exit. |
| `LastFrame` | The last frame's `FrameStats` (draw calls, sprites, entities, events, allocations). |
| `Counters` | Every counter (value), gauge (value) and histogram (total count) registered with `IMetrics`, by name. |
| `Image` | The last rendered frame as a `Screenshot` with headless rendering on, else `null`. |
| `Get<T>()` | A required service. |
| `WorldJson(decimals = 3)` | The most recent live ECS world as normalized JSON, or `null` without the ECS module. |

### UseEntryPoint: step by hand

```csharp
[Fact]
public void HoldingRightMovesThePaddleRight()
{
	using var run = IonTestHost.RunEntryPoint<Program>(1);
	var state = run.Get<PlayState>();
	var start = state.PaddleX;

	run.Host.Input.Press(Key.Right);
	run.Host.Step(30);
	run.Host.Input.Release(Key.Right);
	run.Host.Step(1);

	Assert.InRange(state.PaddleX - start, 200, 260);
}
```

Or build the host yourself:

```csharp
using var host = new IonTestHost()
	.WithConfiguration("Ion:Seed", "1")
	.UseEntryPoint<Program>("--Game:Balls=4");   // arguments for Main
var bounces = host.Collect<BounceEvent>();   // any event type your game emits

host.Step(120);

Assert.NotEmpty(bounces);
```

### What the host adds to your program

| Host setting | When it is applied | So that |
|---|---|---|
| Configuration (`WithConfiguration`, headless) | When the program calls `CreateBuilder` | The program reads it while it registers. |
| Arguments (`WithArgs`, `UseEntryPoint(args)`) | Passed to `Main` | The program sees them in `args`. |
| Services (`Configure`, `WithSystem` registrations, the fixed-step clock) | In `builder.Build()`, after the program's registrations | The host's registrations win. |
| Schedule (`ConfigureApp`, `WithSystem`) | After the program's `Use...` calls | Test systems run alongside the game's. |

Without host services or systems, the game runs the generated schedule (`host.Loop.Schedule.IsGenerated` is true).
Adding a system the program did not add switches to the runtime-bound schedule.

The entry point fails the test clearly when the program throws before `Run()` ("threw before it called Run()"), returns
without calling it ("returned without calling Run()"), or throws after `Run()` returns (reported when the host is
disposed). Hosts running in parallel each get their own application.

## IonTestHost reference

### Configuring (before the first frame)

| Member | Meaning |
|---|---|
| `new IonTestHost(TimeSpan? frameTime = null)` | A host whose frames last `frameTime` (`DefaultFrameTime`, one 60 Hz step rounded up to a whole tick, when omitted). |
| `UseEntryPoint<TProgram>(params string[] args)` | Run the game's `Program.cs` (above). Also `UseEntryPoint(Assembly, args)`. |
| `UseGame(configure, use)` | Build a game from two setup delegates instead (legacy, below). |
| `WithSystem<T>()`, `WithSystem(Type)` | Register `T` as a singleton and add it to the schedule, after the game's own setup. |
| `Configure(Action<IServiceCollection>)` | Add service registrations; they run after the engine's and the game's, so they can replace them. |
| `ConfigureApp(Action<IIonApplication>)` | Add schedule setup (`app.UseSystem<T>()`, function steps) after the game's and before `WithSystem`'s. |
| `WithConfiguration(key, value)`, `WithConfiguration(IEnumerable<KeyValuePair<string, string?>>)` | Add configuration values; later values win. |
| `WithArgs(params string[])` | Command line arguments for `CreateBuilder` (for `Main` with `UseEntryPoint`). |
| `WithRendering(uint? width = null, uint? height = null)` | Turn on headless rendering (below). |

Configuring after the host has started throws `InvalidOperationException`. The host starts on the first `Step`,
`RunUntil`, `Start()` or access to a member that needs the running game (such as `Services`, `Get<T>()` or `Input`).

Defaults the host always applies: `Ion:Headless` is `true` (it cannot be turned off), asset hot reload
(`Ion:Assets:HotReload`) is `false`, console logging is off (add a provider in `Configure` to see logs), and the clock is
a `FixedStepClock`. Without `UseEntryPoint` or `UseGame`, the host registers `AddIon` (headless) and wires `UseIon`, then
the `WithSystem` systems.

### Driving

| Member | Meaning |
|---|---|
| `Start()` | Build the application and run Init. |
| `Step(int frames = 1)` | Run frames; returns how many ran (fewer when the game asks to exit). Exceptions thrown by systems propagate. |
| `RunUntil(Func<bool> condition, int maxFrames = 10_000)` | Run until the condition holds (checked before the first frame and after each), the game exits or `maxFrames` ran. Returns whether the condition was met. |
| `Collect<T>()` | Record every `T` event emitted from now on, with its frame number (below). |
| `Dispose()` | Run Destroy and dispose the application. |

### Inspecting

| Member | Meaning |
|---|---|
| `Frame` | Frames run so far. |
| `FrameTime`, `Clock` | The frame duration and the `FixedStepClock` driving the loop. |
| `Application`, `Services`, `Loop` | The application, its services and the game loop. |
| `Get<T>()` | A required service. |
| `Input` | The scripted input (`NullInputState`). |
| `Window` | The headless window (`NullWindow`), for its `Size`. |
| `SpriteBatch` | The recording null sprite batch: `LastFrame` has the last frame's draw counts and commands. |
| `Audio` | The headless audio manager: `Plays` lists every sound played. |
| `Metrics` | The `IMetrics`: counters, frame profiler, trace capture. |
| `LastFrame` | The last frame's `FrameStats`. |
| `Events` | The event bus, to emit events into the game. |
| `IsExitRequested` | Whether the game asked to exit (`ExitGameEvent` or `GameLoop.Stop`). |
| `Screenshot()`, `SaveScreenshot(path)` | The last rendered frame (needs `WithRendering`). |

## Scripting input

`host.Input` is a `NullInputState`. Input queued before a frame is applied at the start of that frame, in `First`, like
device input, so edges (`Pressed`, `Released`) and the fixed-step views behave as they do on hardware.

| Method | Effect |
|---|---|
| `Press(Key, modifiers)`, `Release(Key, modifiers)`, `Tap(Key, modifiers)`, `Repeat(Key, modifiers)` | Keyboard. `Tap` presses and releases within one frame. |
| `Press(MouseButton)`, `Release(MouseButton)`, `Click(MouseButton = Left)` | Mouse buttons. |
| `SetMousePosition(Vector2)`, `Scroll(float)` | Pointer position and wheel. |
| `Type(string)` | Text input only (no key events). |
| `ConnectGamepad(index)`, `DisconnectGamepad(index)` | Gamepad connection. |
| `Press(gamepad, GamepadButton)`, `Release(...)`, `Tap(...)` | Gamepad buttons. |
| `SetAxis(gamepad, GamepadAxis, value)`, `SetLeftStick(gamepad, Vector2)`, `SetRightStick(gamepad, Vector2)` | Gamepad axes. |
| `TouchDown(id, pos)`, `TouchMove(id, pos)`, `TouchUp(id, pos)`, `TouchTap(id, pos)` | Touch. |
| `ReleaseAll()` | Release everything without a `Released` edge (as on focus loss). |

```csharp
using var host = new IonTestHost().WithSystem<ProbeSystem>();
var probe = host.Get<ProbeSystem>();

host.Step(3);
host.Input.Click();
host.Step(6);

Assert.Equal(1, probe.UpdateClicks);
Assert.Single(host.Audio.Plays);
```

For longer sequences, record real play once and replay it: see
[Recording and playback](/Ion/interaction/input/recording-and-playback/).

## Collecting events

`Collect<T>()` returns an `EventCollector<T>`, a read-only list of every `T` event the game emitted since the call,
polled after each frame the host runs (outside the schedule, so the game's schedule stays exactly its own).

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
var ticks = host.Collect<Tick>();

host.Step(3);

Assert.Equal([1, 2, 3], ticks.Select(t => t.Count));
Assert.Equal([0u, 1u, 2u], ticks.Frames);   // the frame each event was recorded in
```

`Last` is the most recent event (or `null`), `Clear()` forgets what was recorded, and `Dispose()` stops recording. You
can emit into the game with `host.Events.Emit(new PingEvent(7))`. See [Events](/Ion/concepts/events/).

## The deterministic clock

The host replaces the game's clock with a `FixedStepClock`: each frame advances time by exactly `FrameTime`, and pacing
never sleeps. With the default frame time every frame runs exactly one `FixedUpdate` step at the default 60 Hz fixed
rate, so 600 frames are 10 simulated seconds whatever the machine. Pass a different frame time to test frame-rate
independence:

```csharp
// Two frames per fixed step.
using var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 120)).UseEntryPoint<Program>();
```

Seed every source of randomness from configuration (`IonRun.Seed(config, fallback)` reads `Ion:Seed`) and never use
`Random.Shared`, and the same seed gives the same state. See [Time and determinism](/Ion/concepts/time-and-determinism/).

## Headless rendering and screenshots

By default the headless backend records draw calls but does not rasterize them. `WithRendering()` turns on
`Ion:Headless:Render`: an RHI backend renders every frame into an offscreen target (960x540 unless you pass a size or
set `Ion:Window`), the 2D renderer replaces the recording sprite batch, and `Screenshot()` captures frames.

```csharp
using var run = IonTestHost.RunEntryPoint<Program>(60, host => host.WithRendering(640, 360));
GoldenImage.AssertMatches(run.Image!, RenderingEnvironment.GoldenPath("frame-60.png"), tolerance: 2);
```

The backend follows `Ion:Graphics:PreferredBackend`: `Auto` (the default) takes the first available of Vulkan (Mesa
lavapipe on CI) and OpenGL ES through EGL (Mesa llvmpipe, no display needed). With rendering on, `host.SpriteBatch` no
longer sees the game's draws. `Screenshot()` throws `NotSupportedException` without rendering and
`InvalidOperationException` before the first frame.

`RenderingEnvironment` tells a test what the machine offers, so it can skip instead of failing:

| Member | Meaning |
|---|---|
| `HasVulkan` | A Vulkan driver with a device. |
| `HasHeadlessGles` | EGL with an OpenGL ES 3 driver for headless contexts. |
| `HasDisplay` | A display to open windows on (always true on Windows and macOS). |
| `GoldenPath(name)` | `Golden/<name>` next to the calling source file. |

`ErrorLog` collects Error and Critical log entries; `errorLog.Attach(host)` also turns on graphics validation, and
`AssertClean()` fails the test if anything was logged:

```csharp
var log = new ErrorLog();
using (var host = log.Attach(new IonTestHost().UseEntryPoint<Program>()).WithRendering())
{
	host.Step(30);
}

log.AssertClean();
```

The samples wrap these in xUnit attributes (`VulkanFact`, `GlesFact`, `WindowedVulkanFact`) and two helpers in
`Ion.Examples/Shared/SampleRenderingTest.cs`: `SampleRendering.Capture` (headless rendering with validation on a chosen
backend) and `SampleWindowed.Run<Program>(frames, settings)`, which runs `Program.cs` through `IonEntryPoint` in a real
window on the real clock and fails on any logged error. They are sample code, not part of `Ion.Testing`; copy them if
you need them.

## IonEntryPoint: driving the program yourself

`IonEntryPoint.Start<TProgram>(args, configure, beforeBuild, timeout)` is the piece `UseEntryPoint` is built on: it runs
`Main` up to `game.Run()` and hands you the `IonApplication` it built, without a test host. Use it for a windowed run or
a real clock:

```csharp
using var program = IonEntryPoint.Start<Program>(
	configure: builder => builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["Ion:MaxFPS"] = "0" }));
var loop = program.Application.Build();
loop.Initialize();
for (var i = 0; i < 120 && !loop.IsExitRequested; i++) loop.Step();
loop.Shutdown();
```

`configure` runs on the builder as soon as it is created, `beforeBuild` after the program's registrations. The default
timeout for reaching `Run()` is 60 seconds.

## Legacy: IIonGame and UseGame

Before 0.3, games put their setup in a class implementing `IIonGame` (two static methods) so tests could build the same
game. It still works, but keeping the setup in `Program.cs` and using `UseEntryPoint` is preferred.

```csharp
public sealed class Game : IIonGame
{
	public static void Configure(IonApplicationBuilder builder)
	{
		builder.Services.AddIon(builder.Configuration);
		builder.Services.AddSingleton<PlayerSystem>();
	}

	public static void Use(IIonApplication app) => app.UseIon().UseSystem<PlayerSystem>();
}

// In a test:
using var run = IonTestHost.Run<Game>(600);
using var host = new IonTestHost().UseGame(Game.Configure, Game.Use);
```

`UseGame` still forces headless mode and the deterministic clock.

## Testing an ad hoc setup

With neither `UseEntryPoint` nor `UseGame`, the host is a small engine of its own: `AddIon` plus whatever you add. This
suits unit tests of a single system:

```csharp
using var host = new IonTestHost()
	.Configure(services => services.AddSingleton(new CounterSettings(3)))
	.WithSystem<Counter>();

host.Step(2);

Assert.Equal(6, host.Get<Counter>().Count);
```

## See also

- [Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/): compare state and pictures with committed files.
- [The ion command line](/Ion/tooling/ion-cli/): `ion run --headless` is the out-of-process equivalent.
- [Remote protocol](/Ion/tooling/remote-protocol/#testing-against-the-protocol): testing through the protocol.
- [Time and determinism](/Ion/concepts/time-and-determinism/).
- [Recording and playback](/Ion/interaction/input/recording-and-playback/).
