---
title: Scenes
description: Two scenes with their own schedules, function steps with injected services, a Begin/End scope, coroutines, profiling keys, and composing the engine from its parts instead of AddIon.
sidebar:
  order: 8
---

**Source:** [`Ion.Examples/Ion.Examples.Scenes`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Scenes)
and its tests in [`Ion.Examples.Scenes.Tests`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Scenes.Tests).

A small tour of the schedule rather than a game. Two scenes, each with its own steps, switch on Tab: the main menu
scene draws a green square and the gameplay scene a red one. Around them, root-level function steps show injected
services, events, a coroutine and runtime profiling controls. Unlike the other samples, this one composes the engine
from its parts with the `IServiceCollection` registrations instead of `AddIon`/`UseIon`.

| Main menu scene | Gameplay scene (after Tab) |
|---|---|
| ![The main menu scene: a green square on cornflower blue](./images/scenes_menu.png) | ![The gameplay scene: a red square on cornflower blue](./images/scenes_gameplay.png) |

## What it shows

- `UseScene(id, scene => ...)`: scenes with their own schedule, systems and function steps, switched with
  `events.EmitChangeScene(...)`.
- Function steps: `game.Init(...)`, `game.First(...)`, `game.Update(...)`, `game.Render(...)` taking a lambda whose
  parameters after `GameTime` are services, resolved once.
- A `[Begin]`/`[End]` scope that wraps the rest of a scene's Render stage.
- Coroutines started from a step (`ICoroutineRunner.Start`) that wait with `Wait.For(TimeSpan)`.
- Profiling at run time: `IMetrics.IsProfiling` and `WriteTrace()` on key presses.
- Composing the engine by hand: `AddMetrics`, `AddNullGraphics` or `AddGraphics`, `AddHeadlessRendering`, `AddScenes`,
  `AddCoroutines`, and the matching `UseX` calls after the game's own steps.
- An event reader created once, outside the step (the fix for `ION103`).

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Scenes
dotnet run --project Ion.Examples/Ion.Examples.Scenes -- --Ion:PrintSchedule=true
```

| Key | Action |
|---|---|
| Tab | Switch between the main menu and gameplay scenes. |
| Enter | Start a five-second countdown coroutine (printed to the console). |
| F5 / F6 | Start profiling / stop and write the kept frames to `Ion:Metrics:TraceOutput` (`trace.json`). |
| F9 | Capture the next 120 frames as a trace (the metrics module's capture key). |
| Escape | Exit. |

`--Ion:PrintSchedule=true` prints every stage's steps in run order at startup, followed by each scene's schedule.
`--Ion:Headless=true` uses the null graphics backend and `--Ion:Headless:Render=true` adds headless rendering.

## Composing the engine by hand

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
var headless = builder.Configuration.IsHeadless();

builder.Services.AddMetrics(builder.Configuration);
if (headless)
{
	builder.Services.AddNullGraphics(builder.Configuration, graphics => graphics.ClearColor = Color.CornflowerBlue);
	if (builder.Configuration.IsHeadlessRender()) builder.Services.AddHeadlessRendering(builder.Configuration);
}
else
{
	builder.Services.AddGraphics(builder.Configuration, graphics => graphics.ClearColor = Color.CornflowerBlue);
}

builder.Services.AddScenes();
builder.Services.AddCoroutines();
builder.AddSystem<TestMiddleware>();

using var game = builder.Build();
```

`AddGraphics` registers the windowed stack alone: the Silk.NET window and input, the backend-selecting RHI graphics
and the 2D renderer. There is no audio and no remote protocol here, because nothing asked for them. Most games should
use `AddIon`, which picks headless or windowed for you and registers the whole core; this form is for when you want
control over every piece.

At the end, after the game's own steps, the engine systems are added:

```csharp title="Program.cs"
// The engine systems can be added after the game's own steps: engine steps use the reserved order bands.
game.UseMetrics();
game.UseEvents();
if (headless)
{
	game.UseNullGraphics();
	if (game.Configuration.IsHeadlessRender()) game.UseHeadlessRendering();
}
else
{
	game.UseGraphics();
}

// Steps the shared ICoroutineRunner once per frame in the Update stage.
game.UseCoroutines();

game.Run();
```

Registration order only breaks ties between equal orders. Engine steps use `-1000..-500` (setup) and `500..1000`
(teardown), so the game's steps at order 0 run between them whether they were registered before or after. See the
[stage order reference](/Ion/reference/stage-order/).

## Function steps

```csharp title="Program.cs"
game.Init((GameTime dt, IEvents events, IWindow window) =>
{
	window.IsResizable = true;
	events.Emit(42);
});

game.First((GameTime dt, IInputState input, ICoroutineRunner coroutine) =>
{
	if (input.Pressed(Key.Enter)) coroutine.Start(CountDown(5));
});
```

A function step takes an `Action<GameTime>`, or a delegate with up to four service parameters after `GameTime`.
`order` and `name` are optional parameters. The schedule generator compiles these lambdas into direct calls.

### Reading events outside a class

```csharp title="Program.cs"
// A reader is created once, outside the step, so it remembers what it has read (ION103).
var intEvents = game.Services.GetRequiredService<IEvents>().Reader<int>();

game.Update((GameTime dt, IEvents events, IInputState input) =>
{
	while (intEvents.TryRead(out var e)) Console.WriteLine($"Int event! {e}");

	// Tab switches between the two scenes.
	if (input.Pressed(Key.Tab))
	{
		gameplay = !gameplay;
		events.EmitChangeScene(gameplay ? Scene.Gameplay : Scene.MainMenu);
	}
});
```

Creating the reader inside the lambda would give a new reader every frame that starts at the oldest visible event and
re-reads the previous frame's events. The generator reports that as warning `ION103`.

### Profiling on demand

```csharp title="Program.cs"
game.First((GameTime dt, IInputState input, IMetrics metrics) =>
{
	if (input.Pressed(Key.F5)) metrics.IsProfiling = true;
	if (input.Pressed(Key.F6))
	{
		metrics.IsProfiling = false;
		metrics.WriteTrace();
	}
});
```

Open the written `trace.json` in [Perfetto](https://ui.perfetto.dev).

## Scenes

```csharp title="Program.cs"
public enum Scene
{
	MainMenu = 1,
	Gameplay,
	Test,
}

game.UseScene(Scene.MainMenu, scene =>
{
	scene.Render((GameTime dt, ISpriteBatch spriteBatch) => spriteBatch.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90)));
	scene.UseSystem<TestMiddleware>();
});

game.UseScene(Scene.Gameplay, scene =>
{
	scene.Render((GameTime dt, ISpriteBatch spriteBatch) => spriteBatch.DrawRect(Color.DarkRed, new RectangleF(10, 10, 90, 90)));
});
```

- Each scene has its own dependency injection scope and its own schedule, built with the same ordering rules as the
  root. The `SceneSystem` runs the active scene's schedule at `StageOrder.Scenes` (-500) in every stage.
- `UseScene<TScene>` and `EmitChangeScene<TScene>` take any enum (`where TScene : struct, Enum`); an `int` id works too.
- Scene systems are resolved from the scene's scope, so they may be scoped services. Root systems must not be (`ION006`).
- A step added to a scene after it loaded is error `ION004`: register steps inside the configure callback.

## A scope around the scene's rendering

```csharp title="Program.cs"
public partial class TestMiddleware
{
	private readonly Queue<float> _frameTimes = new();
	private readonly Stopwatch _stopwatch = new();
	private uint _fixedUpdates;

	[FixedUpdate]
	public void CountFixedUpdates(GameTime dt) => _fixedUpdates++;

	// A scope around the rest of the scene's Render stage: EndRenderTimer runs after every scene render step, even if
	// one throws.
	[Begin(Stage.Render, Order = -100)]
	public void StartRenderTimer(GameTime dt) => _stopwatch.Restart();

	[End(Stage.Render, Order = -100)]
	public void EndRenderTimer(GameTime dt)
	{
		_stopwatch.Stop();
		_frameTimes.Enqueue((float)_stopwatch.Elapsed.TotalSeconds);
		while (_frameTimes.Count > 60) _frameTimes.Dequeue();
	}
}
```

A `[Begin]`/`[End]` pair wraps every step and scope that sorts after the begin in that stage; the end runs in a
`finally`. This replaces the old middleware pattern of code before and after `next(dt)`.

## Coroutines

```csharp title="Program.cs"
static IEnumerator CountDown(int from)
{
	while (from > 0)
	{
		Console.WriteLine("Countdown: " + from--);
		yield return Wait.For(TimeSpan.FromSeconds(1));
	}

	Console.WriteLine("Countdown done!");
}
```

`UseCoroutines()` steps the shared `ICoroutineRunner` once per frame in Update at `StageOrder.Coroutines` (-600). This
routine is a plain `IEnumerator`, which boxes what it yields; an `IEnumerator<Wait>` routine allocates nothing per frame.
See [Coroutines](/Ion/ecs/coroutines/).

## The tests

[`ScenesRenderingTests`](https://github.com/jimbuck/Ion/blob/main/Ion.Examples/Ion.Examples.Scenes.Tests/ScenesRenderingTests.cs)
render each scene headless at 320 x 180 and compare it with a golden image, on Vulkan and on OpenGL ES:

```csharp title="ScenesRenderingTests.cs"
using (var host = log.Attach(new IonTestHost().UseEntryPoint<Program>()).WithRendering(Width, Height)
	.WithConfiguration("Ion:Graphics:PreferredBackend", backend.ToString()))
{
	host.Step(3);
	menu = host.Screenshot();

	host.Input.Tap(Key.Tab);
	host.Step(3);
	gameplay = host.Screenshot();
}

Assert.Equal(Color.ForestGreen.ToRgba8(), menu.GetPixel(50, 50));
Assert.Equal(Color.DarkRed.ToRgba8(), gameplay.GetPixel(50, 50));
```

The windowed tests run 120 and 240 frames and check that one sprite (the square) was drawn per frame.

## Ideas to extend it

**A class-based scene system.** Move the gameplay square into a system with state, resolved from the scene's scope:

```csharp
public sealed class Bouncer(ISpriteBatch spriteBatch, IWindow window)
{
	private Vector2 _position = new(10, 10), _velocity = new(120, 90);

	[Update]
	public void Move(GameTime dt)
	{
		_position += _velocity * dt;
		if (_position.X < 0 || _position.X > window.Width - 90) _velocity.X = -_velocity.X;
		if (_position.Y < 0 || _position.Y > window.Height - 90) _velocity.Y = -_velocity.Y;
	}

	[Render]
	public void Draw(GameTime dt) => spriteBatch.DrawRect(Color.DarkRed, new RectangleF(_position.X, _position.Y, 90, 90));
}
```

Register it scoped (`builder.Services.AddScoped<Bouncer>()`) and add it with `scene.UseSystem<Bouncer>()` inside the
gameplay scene: it is created when the scene loads and disposed when it unloads, so it starts over each time.

**An ECS world per scene.** Add the ECS module and call `scene.UseEcs()`: each scene scope gets its own `World`,
disposed with the scene.

**Unboxed waits.** Change `CountDown` to return `IEnumerator<Wait>` and `yield return 1f;` for one second.

## See also

- [Scenes](/Ion/ecs/scenes/), [Systems](/Ion/concepts/systems/), [Stages](/Ion/concepts/stages/).
- [Application](/Ion/concepts/application/): builder, modules and composing by hand.
- [Coroutines](/Ion/ecs/coroutines/) and [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).
