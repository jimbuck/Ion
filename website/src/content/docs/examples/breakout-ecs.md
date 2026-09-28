---
title: Breakout ECS
description: Breakout on the ECS module and 2D physics, with entities, [Query] steps, Commands, events, metrics, a seeded headless autopilot and Android and iOS heads.
sidebar:
  order: 2
---

**Source:** [`Ion.Examples/Ion.Examples.Breakout.ECS`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.ECS),
tests in [`Ion.Examples.Breakout.ECS.Tests`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.ECS.Tests),
mobile heads in [`.Android`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.ECS.Android)
and [`.iOS`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Breakout.ECS.iOS).

The flagship sample. The same brick breaker as [Breakout](/Ion/examples/breakout/), rebuilt as entities with
components, small single-purpose systems that talk through events, and Box2D doing the collisions. Balls are bouncy
bullets, blocks are tilted static boxes, the paddle is a kinematic capsule, and you can have up to 300 balls in play.

![Breakout ECS with the physics debug drawing: blocks, balls, the paddle capsule and the walls outlined](./images/breakout-physics-debug.png)

## What it shows

- The game as a **module**: `AddBreakout()`/`UseBreakout()` extension methods that three entry points share (desktop,
  Android, iOS).
- The ECS module with the 2D sprite extraction: `Transform2D`, `Sprite`, Arch's `World`, `Entity` and
  `QueryDescription`.
- `[Query]` steps expanded into chunk loops by the generator, with `All<T>` filters and `Commands` for structural
  changes.
- The 2D physics module: `Collider2D`, `RigidBody2D` (dynamic, kinematic, bullets), `Collision2D` events and the
  debug drawing.
- Events as the glue between systems: `BlockHitEvent`, `PaddleHitEvent`, `WallHitEvent`, `BallLostEvent`,
  `BlocksClearedEvent` and `LaunchBallCommand`.
- Ordering with `Order`, `[After<T>]` and the engine's `StageOrder` bands.
- Game metrics (`MetricsGauge`, `MetricsCounter`) that show up in the frame log, the overlay and `dotnet-counters`.
- A seeded, deterministic game (`Ion:Seed`) and a headless autopilot that plays it with scripted input.
- Touch input for phones, and optional Tracy profiling.

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS
```

Click to capture the mouse, click again to launch a ball (each click adds one), and press Escape to release the mouse.

| Flag | Effect |
|---|---|
| `--Ion:Headless=true` | Headless; the `HeadlessAutopilotSystem` plays and logs what it drew and played about once a second. |
| `--Ion:Headless:Render=true` | With headless: render offscreen. |
| `--Ion:Seed=<n>` | The random seed (default 6014): block tilts and sound pitches. |
| `--Ion:Physics2D:DebugDraw=false` | Hide the collider outlines (they are on by default in this sample). |
| `--Ion:Metrics:Overlay=true` | Draw fps, frame time, draw calls and the game counters (the font is set in `appsettings.json`). |
| `--Ion:Metrics:FrameLog=frames.jsonl` | One JSON line per frame, including the `balls` gauge and `blocks_hit` counter. |
| `--Ion:Run:Frames=<n>` | Run n frames and exit. |
| `-p:IonTracy=true` (build property) | Reference `Ion.Extensions.Metrics.Tracy` and stream spans to a Tracy server. |

Press **F9** to capture a Chrome trace of the next 120 frames (`CaptureKey` and `TraceOutput` in `appsettings.json`).

```bash
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS -- --Ion:Headless=true --Ion:Metrics:FrameLog=frames.jsonl --Ion:Run:Frames=600
jq -s 'map(.gauges.balls) | max' frames.jsonl
```

## Program.cs

`Program.cs` is two calls, because the game is a module:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddBreakout();

using var game = builder.Build();
game.UseBreakout();

#if TRACY
// Built with -p:IonTracy=true: stream every span, frame mark and counter to a Tracy server.
Ion.Extensions.Metrics.Tracy.TracyExtensions.UseMetricsTracy(game);
#endif

game.Run();
```

## The module: BreakoutGame.cs

```csharp title="BreakoutGame.cs"
public static IonApplicationBuilder AddBreakout(this IonApplicationBuilder builder)
{
	RegisterComponents();

	var seed = int.TryParse(builder.Configuration[SeedKey], out var configured) ? configured : DefaultSeed;
	var debugDraw = builder.Configuration[$"{Physics2DConfig.Section}:DebugDraw"] is null;

	builder.AddIon(graphics => graphics.ClearColor = new Color(0x333))
		.AddEcsRendering()
		.AddPhysics2D(physics =>
		{
			physics.GravityY = 0;
			physics.UnitsPerMeter = BreakoutPhysics.PixelsPerMeter;
			physics.DebugDraw |= debugDraw;
		})
		.AddSystem<MouseCaptureSystem>()
		.AddSystem<ScoreSystem>()
		.AddSystem<SoundEffectsSystem>()
		.AddSystem<PaddleSystem>()
		.AddSystem<BallSystem>()
		.AddSystem<BlockSystem>()
		.AddSystem<CollisionEventSystem>()
		.AddSystem<LevelSystem>();
	builder.Services.AddSingleton(new BreakoutSettings(seed));

	if (builder.Configuration.IsHeadless()) builder.AddSystem<HeadlessAutopilotSystem>();

	return builder;
}
```

- `AddEcsRendering()` brings the ECS module (the root `World`, `Commands` played back at the end of every stage,
  transform propagation) and the sprite extraction; `AddPhysics2D` brings the ECS module too. Listing `AddIon` first is
  for the reader: modules register their dependencies themselves, and a module registered twice is registered once.
- Physics runs with no gravity and 64 pixels to the meter, so a 32 px ball is half a meter and a block three meters,
  the sizes Box2D is tuned for.
- The systems are singletons: the game runs in the root schedule (no scenes), where a scoped system is error `ION006`.

`UseBreakout` adds the systems. `MouseCaptureSystem` is added **before** the engine's systems on purpose, to show that
registration order only breaks ties: engine steps use the reserved order bands, so a game step at order 0 still runs
after the window, input and sprite batch setup.

```csharp title="BreakoutGame.cs"
public static IIonApplication UseBreakout(this IIonApplication app)
{
	app.UseSystem<MouseCaptureSystem>()
		.UseEcsRendering()
		.UsePhysics2D()
		.UseSystem<CollisionEventSystem>()
		.UseSystem<LevelSystem>()
		.UseSystem<SoundEffectsSystem>()
		.UseSystem<PaddleSystem>()
		.UseSystem<BallSystem>()
		.UseSystem<BlockSystem>()
		.UseSystem<ScoreSystem>();

	if (app.Configuration.IsHeadless()) app.UseSystem<HeadlessAutopilotSystem>();

	return app;
}
```

`RegisterComponents()` registers the component types the game only creates (`Block`, `Paddle`, `Ball`, `Wall`) with
`EcsComponents.Register<T>()`, which NativeAOT needs; see [NativeAOT](/Ion/platforms/native-aot/).

### Seeded randomness

```csharp title="BreakoutGame.cs"
public sealed record BreakoutSettings(int Seed)
{
	public Random CreateRandom(int stream) => new(unchecked(Seed * 7919 + stream * 104729));
}
```

Each consumer passes its own stream number, so the random sequences do not depend on the order systems run in. The
seed is combined without `HashCode` (which is randomized per process), so a seed gives the same game in every run. The
golden-image tests rely on it.

## The systems

| System | Steps | Job |
|---|---|---|
| `MouseCaptureSystem` | Init, Last | Sizes the window to the play field (2030 x 984) and grabs or releases the mouse. |
| `LevelSystem` | Init | Creates four static walls: `Wall` + `Transform2D` + a box collider, no sprite. |
| `PaddleSystem` | Init, FixedUpdate, Update | Creates the paddle (a kinematic capsule); moves it with the mouse in fixed steps and with the first finger in Update; emits `LaunchBallCommand`. |
| `BallSystem` | Init `[After<PaddleSystem>]`, Update, two `[Query]` steps | Launches balls, removes lost ones from play, clears balls when the level is cleared. |
| `BlockSystem` | Init `[After<PaddleSystem>]`, FixedUpdate, Update, Last | Creates the 100 tilted blocks, destroys hit blocks, resets the level. |
| `CollisionEventSystem` | FixedUpdate (Order -10) | Turns the physics module's `Collision2D` events into game events. |
| `ScoreSystem` | Init, First, Update, Render | Counts score, balls and losses; updates metrics; draws the HUD. |
| `SoundEffectsSystem` | Init, Update (Order 10) | Plays a bonk for wall and paddle hits and a ping for block hits. |
| `HeadlessAutopilotSystem` | Init, First (three steps), Last | Headless only: clicks, launches and steers the paddle under the lowest ball. |

Print the resulting schedule with `--Ion:PrintSchedule=true` (or `ion schedule`) to see them among the engine's steps.

### Entities with physics

The paddle is a kinematic body: the physics step drives it to its `Transform2D`, which the game moves.

```csharp title="BreakoutSystems.cs"
_paddle = world.Create(new Paddle(true), new Transform2D(paddlePosition), new Sprite(paddleTexture, BreakoutConstants.PADDLE_SIZE),
	BreakoutPhysics.PaddleCollider(BreakoutConstants.PADDLE_SIZE), RigidBody2D.Kinematic());
```

Balls are dynamic bullets (continuous collision against other moving bodies too), drawn in front with `depth: 1`:

```csharp title="BreakoutSystems.cs"
return world.Create(new Ball(), new Transform2D(position), new Sprite(_ballTexture, BreakoutConstants.BALL_SIZE, depth: 1),
	BreakoutPhysics.BallCollider(radius), RigidBody2D.Dynamic(new Vector2(0, -100f)) with { IsBullet = true });
```

The colliders are frictionless; balls have a restitution of 1.05, so they speed up slightly on every bounce:

```csharp title="BreakoutSystems.cs"
public static Collider2D BallCollider(float radius) => Collider2D.Circle(radius) with { Restitution = 1.05f, Friction = 0f };
public static Collider2D BoxCollider(Vector2 size) => Collider2D.Box(size) with { Restitution = 1f, Friction = 0f };
public static Collider2D PaddleCollider(Vector2 size) => Collider2D.Capsule(size) with { Restitution = 1f, Friction = 0f };
```

### From collisions to game events

The physics step runs in `FixedUpdate` at `StageOrder.Physics` (-700) and emits `Collision2D` events.
`CollisionEventSystem` runs right after it, at order -10, and before the game's own fixed steps at order 0, which react
to what it emits:

```csharp title="BreakoutSystems.cs"
public class CollisionEventSystem(World world, IEvents events)
{
	private EventReader<Collision2D> _collisions = events.Reader<Collision2D>();

	[FixedUpdate(Order = -10)]
	public void Translate(GameTime dt)
	{
		foreach (ref readonly var collision in _collisions.Read())
		{
			if (collision.Phase != ContactPhase.Begin) continue;
			if (!world.IsAlive(collision.A) || !world.IsAlive(collision.B)) continue;

			var ball = world.Has<Ball>(collision.A) ? collision.A : world.Has<Ball>(collision.B) ? collision.B : Entity.Null;
			if (ball == Entity.Null) continue;
			var other = collision.Other(ball);

			if (world.Has<Block>(other)) events.Emit(new BlockHitEvent(other));
			else if (world.Has<Paddle>(other)) { /* PaddleHitEvent with the hit offset */ }
			else if (world.Has<Wall>(other)) events.Emit(new WallHitEvent());
		}
	}
}
```

`BlockSystem` destroys the hit blocks in its own fixed step; destroying an entity removes its body at the next physics
step. `ScoreSystem` and `SoundEffectsSystem` read the same events independently: each reader sees each event once.

### [Query] steps and Commands

`BallSystem` is `partial` so the generator can add chunk loops for its `[Query]` methods. Structural changes inside a
query go through `Commands`, which the ECS module plays back at the end of the stage (`StageOrder.Ecs`, 950):

```csharp title="BreakoutSystems.cs"
public partial class BallSystem(IWindow window, World world, IEvents events, IAssetManager assets)
{
	/// A ball that fell below the window loses its body (the paddle gets a ball back).
	[Update(Order = 1), Query, All<Ball>]
	private void CheckLost(Entity entity, in Transform2D transform, in RigidBody2D body, Commands commands)
	{
		if (transform.Position.Y <= window.Height) return;

		commands.Remove<RigidBody2D>(entity);
		commands.Remove<Collider2D>(entity);
		_paddle.Get<Paddle>().HasBall = true;
		events.Emit(new BallLostEvent());
	}

	/// When every block is gone, the balls in play are removed.
	[Update(Order = 2), Query, All<Ball>]
	private void ClearBall(Entity entity, in RigidBody2D body, Commands commands)
	{
		if (!_clearing) return;
		commands.Destroy(entity);
	}
}
```

Calling `world.Destroy` directly inside a query would invalidate the chunks being iterated: the generator reports it as
`ION305`, and at run time a `StructuralChangeException` names the step and the entity. Launching a ball, in contrast,
happens in a normal `[Update]` step outside any query, so it calls `world.Create` directly and the score counts the new
ball the same frame.

### Ordering

- `[Init, After<PaddleSystem>]` on `BallSystem.Init` and `BlockSystem.SetupBlocks`: they look up the paddle entity, which
  exists once `PaddleSystem`'s Init step has run.
- `SoundEffectsSystem.Update` uses `Order = 10` so it runs after the frame's gameplay steps and its sounds match what
  they did. It reads both hit channels every frame, since a short-circuit would leave paddle hits for the next frame.
- `ScoreSystem` tallies in `First`, so it counts the previous frame's fixed-step hits before this frame's steps run.

### Metrics

```csharp title="BreakoutSystems.cs"
public class ScoreSystem(IEvents events, IAssetManager assets, ISpriteBatch spriteBatch, World world, IMetrics metrics)
{
	private readonly MetricsGauge _ballsMetric = metrics.Gauge("balls");
	private readonly MetricsCounter _blocksHitMetric = metrics.Counter("blocks_hit");
	// ...
	[Update]
	public void Update(GameTime dt)
	{
		_ballCount = world.CountEntities(in _ballQuery);
		_ballsMetric.Set(_ballCount);
	}
}
```

Instruments are registered once by name and updated through their handles, with no lookup per frame. They appear in the
frame log, the overlay and `dotnet-counters monitor -n Ion.Examples.Breakout.ECS --counters Ion`.

### The headless autopilot

When `Ion:Headless` is true, `HeadlessAutopilotSystem` scripts the mouse through `NullInputState`: it clicks on frame 5
to grab the mouse, launches a ball every 60 frames from frame 10 (up to 10), and steers the paddle under the lowest ball,
which a `[Query]` step finds:

```csharp title="Common/HeadlessAutopilotSystem.cs"
[First(Order = 1), Query, All<Ball>]
private void TrackLowestBall(in Transform2D transform)
{
	if (transform.Position.Y > _lowest)
	{
		_lowest = transform.Position.Y;
		_target = transform.Position.X;
	}
}

[First(Order = 2)]
public void Steer(GameTime dt) => input.SetMousePosition(new Vector2(_target, window.Height / 2f));
```

It makes the headless run a real game, which the tests and the publish startup measurement use.

## The tests

| Test class | What it checks |
|---|---|
| `BreakoutHeadlessTests` | 600 frames at 120 fps with a 60 Hz fixed step: balls launch, blocks break, the score rises, something is drawn every frame. Two runs with seed 42 are identical frame by frame, down to the bits of every ball position (Box2D is deterministic). Touch moves the paddle and lifting the finger launches a ball. The seed is read from configuration. |
| `BreakoutRenderingTests` | 120 frames rendered headless on Vulkan and OpenGL ES match `Golden/breakout_ecs_120.png`; fewer draw calls than sprites; windowed runs without validation errors. |
| `BreakoutMobileTests` | The mobile arguments select SDL, fullscreen and the content root; the mobile entry point runs 30 frames headless. |
| `GeneratedScheduleTests` | The program runs the generated schedule; every registration is pre-bound; `PrintSchedule()` is identical to the reflection-bound runtime; scope ends run when a step throws; stack traces show only user frames. |

```csharp title="BreakoutHeadlessTests.cs"
private static IonTestHost CreateHost(int? seed)
{
	var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 120)).UseEntryPoint<Program>();
	if (seed is int s) host.WithConfiguration(BreakoutGame.SeedKey, s.ToString(CultureInfo.InvariantCulture));
	return host;
}

[Fact]
public void TwoRunsWithTheSameSeedAreIdentical()
{
	var first = Play(seed: 42);
	var second = Play(seed: 42);
	Assert.Equal(first.Frames, second.Frames);
}
```

`host.Collect<LaunchBallCommand>()` records every event of a type the game emits, which is how the tests count launches
and block hits without touching the game's code.

## Mobile heads

`Ion.Examples.Breakout.ECS.Android` and `.iOS` link `BreakoutGame.cs`, `BreakoutSystems.cs`, `BreakoutMobile.cs` and
`Common/` and call `BreakoutMobile.Run`, which makes the same `AddBreakout()`/`UseBreakout()` calls with SDL, fullscreen
and the platform's content root. Build them with `-p:IonMobileHeads=true`; without it they build as `net10.0` libraries
so the shared code keeps compiling. See [Mobile](/Ion/platforms/mobile/).

## Publishing

```bash
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=linux-x64      # 12.6 MB, about 50 ms to the first headless frame
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=r36s -p:IonArm64SysRoot=$HOME/sysroot-bionic-arm64
```

The project sets `<Box2DStaticLink>true</Box2DStaticLink>`, so Box2D is linked into the executable instead of shipped
as a separate library. This is the sample CI measures on every pull request.

## Ideas to extend it

**Power-ups.** Give some blocks a component and spawn a falling power-up when they break:

```csharp
public record struct PowerUp(int Kind);

// In BlockSystem.FixedUpdate, before destroying a hit block:
if (entity.Has<PowerUp>())
{
	var at = entity.Get<Transform2D>().Position;
	world.Create(new Transform2D(at), new Sprite(_powerUpTexture, new Vector2(32)),
		Collider2D.Circle(16) with { IsSensor = true }, RigidBody2D.Dynamic(new Vector2(0, 150)));
}
```

Load `_powerUpTexture` in the system's Init step and add `EcsComponents.Register<PowerUp>()` to `RegisterComponents()`.
The power-up is a sensor, so catch it by reading `Trigger2D` events (`Sensor`, `Visitor`, `Phase`) whose visitor is the
paddle.

**A level counter on the HUD.** `BlocksClearedEvent` is already emitted in `Last` when no block is left; read it in
`ScoreSystem` and draw the level with the score.

**Pause.** Stop the physics step and the gameplay when a key is pressed by skipping your fixed steps on a flag, and
draw "Paused" in `Render`. Keep reading event channels while paused (or call `Skip()` on the readers) so they do not
deliver a burst of stale events on resume.

## See also

- [ECS overview](/Ion/ecs/overview/), [Queries](/Ion/ecs/queries/) and [Entities and commands](/Ion/ecs/entities-and-commands/).
- [2D physics](/Ion/physics/physics-2d/) and [Physics debug drawing](/Ion/physics/debug-draw/).
- [Events](/Ion/concepts/events/) and [Time and determinism](/Ion/concepts/time-and-determinism/).
- [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).
- [Breakout Net](/Ion/examples/breakout-net/): the same game over the network.
