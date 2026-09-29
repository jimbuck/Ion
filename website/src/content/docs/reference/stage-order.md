---
title: Stage order
description: Every StageOrder constant with its value and the engine system that uses it, and the resulting run order of each stage.
sidebar:
  order: 1
---

Steps in a stage run by ascending `Order`. User steps default to `StageOrder.Default` (0). Engine steps use two reserved
bands so that your steps always run between engine setup and engine teardown, whatever the registration order:

| Band | Orders | Holds |
|---|---|---|
| Engine setup | `EngineSetupFirst` (-1000) to `EngineSetupLast` (-500), inclusive | Window, input, graphics frame, audio, networking, physics, coroutines, UI frame, scenes |
| User band | between the bands, 0 by default | Your steps; also ECS transform propagation (-400), extraction (-300) and sprite animation (400) |
| Engine teardown | `EngineTeardownFirst` (500) to `EngineTeardownLast` (1000), inclusive | Debug drawing, UI drawing, overlays, window close, network send, ECS playback, web, remote, events |

Ties are broken by registration order, then declaration order. `[Before<T>]` and `[After<T>]` take precedence over
`Order`. A `[Begin]`/`[End]` scope at order *n* wraps every step and scope that sorts after it in that stage, and its
end runs in a `finally`.

The constants live in `StageOrder` in `Ion.Core.Abstractions`
([source](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Core.Abstractions/Schedule/Stage.cs)).

## Every constant

| Constant | Value | Stage(s) | Used by |
|---|---|---|---|
| `EngineSetupFirst` | -1000 | Init, Destroy | First order of the setup band. `RunReportSystem` (the `Ion` package, when `Ion:Run:Screenshot` or `Ion:Run:Summary` is set) runs its Init and Destroy steps here. |
| `Trace` | -1000 | none | Reserved: the 0.2 trace timer scope. The game loop now records a span per stage itself. |
| `Window` | -950 | Init, First | `SilkWindowSystem` (`Ion.Extensions.Windowing.SilkNet`): creates the window at Init and pumps its events in First. `NullWindowSystem` (`Ion.Extensions.Graphics.Null`) at Init. |
| `Input` | -940 | First | `SilkInputSystem` and `NullInputSystem`: the per-frame input snapshot. |
| `Metrics` | -930 | First | `MetricsSystem` (`Ion.Extensions.Metrics`): the trace capture key, after input. |
| `AssetReload` | -920 | First | `AssetReloadSystem` (`Ion.Extensions.Assets`): reloads changed assets before any user step. |
| `Graphics` | -900 | Init, Render | `VulkanGraphicsSystem`, `GlesGraphicsSystem`, `RhiGraphicsSystem`: device creation at Init and the frame scope (`[Begin]`/`[End]`) in Render. |
| `Audio` | -880 | Init, Last | `AudioSystem` (`Ion.Extensions.Audio`): device start at Init; queued commands flushed to the audio thread in Last (`AudioSystem.FlushOrder`). Destroy runs at its mirror, +880. |
| `Network` | -870 | Init, First, FixedUpdate, Render | `NetworkSystem` (`Ion.Extensions.Networking`): transport start (Init); receive, decode and apply snapshots (First); the tick scope around every fixed step, capturing the snapshot when it closes (FixedUpdate); interpolation of remote entities (Render). Prediction and reconciliation run at `Network + 10` (-860) in First and FixedUpdate. |
| `Rendering3D` | -860 | Init, Render | `Rendering3DSystem` (`Ion.Extensions.Rendering3D`): the 3D frame scope opens after the graphics frame and closes after the sprite batch's, then draws the 3D passes and the 2D overlay. |
| `SpriteBatch` | -850 | Init, Render | `SpriteBatchSystem` (`Ion.Extensions.Rendering2D`) and `NullSpriteBatchSystem`: the batch scope around the Render stage. |
| `Physics` | -700 | FixedUpdate | `Physics2DSystem` and `Physics3DSystem`: push changed transforms, step, pull results, emit collision and trigger events. |
| `Coroutines` | -600 | Update | `CoroutineSystem` (`Ion.Extensions.Coroutines`): steps the shared runner. |
| `UiFrame` | -550 | Update | `UiSystem` (`Ion.Extensions.UI`): the UI frame scope; tree commands and input at the start, layout and trees at the end of Update. |
| `EngineSetupLast` | -500 | none | Last order of the setup band. |
| `Scenes` | -500 | every stage | `SceneSystem` (`Ion.Extensions.Scenes`): runs the active scene's schedule. |
| `TransformPropagation` | -400 | Last, Render | `TransformPropagationSystem` (`Ion.Extensions.Ecs`): global transforms after the frame's gameplay, and again before extraction. |
| `Extract` | -300 | Render | `SpriteExtractionSystem` and `Scene3DExtractionSystem` (`Ion.Extensions.Ecs.Rendering`): entities into the sprite batch and the 3D renderer. |
| `Default` | 0 | every stage | The default order of a step. |
| `SpriteAnimation` | 400 | Update | `SpriteAnimationSystem` (`Ion.Extensions.Ecs`), after the game's Update steps. |
| `EngineTeardownFirst` | 500 | none | First order of the teardown band. |
| `PhysicsDebugDraw` | 650 | Render | `Physics2DDebugDrawSystem` (sprite batch) and `Physics3DDebugDrawSystem` (3D renderer): collider outlines on top of the game's drawing. |
| `Ui` | 700 | Render | `UiSystem`: submits the frame's widgets to the sprite batch. |
| `SceneTransition` | 750 | Render | `SceneFadeSystem` (the `Ion` package): the fade scene transition, over the scene, the game's drawing and the UI. Draw custom transitions here too. |
| `MetricsOverlay` | 800 | Render | `MetricsOverlaySystem`: the overlay stays on top of the UI. |
| `NetworkSend` | 870 | Last, Destroy | `NetworkSystem`: delta-encode snapshots, pack messages and flush (Last); disconnect peers (Destroy). |
| `WindowClose` | 900 | Render, Destroy | `SilkWindowSystem` and `NullWindowSystem`: a closed window becomes an exit request (Render). `SilkWindowSystem` also releases the native window here in Destroy. |
| `Ecs` | 950 | every stage | `EcsCommandsSystem` (`Ion.Extensions.Ecs`): plays back `Commands` at the end of every stage. |
| `Web` | 960 | Init, Last, Destroy | `WebSystem` (`Ion.Extensions.Web`): starts the server; hands queued requests to `[Http]`/`[WebSocket]` methods on the game thread; stops it. |
| `Remote` | 970 | Init, Last | `RemoteSystem` (`Ion.Extensions.Remote`): applies queued remote protocol requests at the end of the frame. |
| `Events` | 1000 | Last | `EventSystem` (`Ion.Core`): steps the event buffers after every other Last step. |
| `EngineTeardownLast` | 1000 | Destroy | Last order of the teardown band. `MetricsSystem` shuts the metrics down here. |

### Derived orders

Some systems use offsets from the constants, exposed as their own constants:

| Order | Value | Where |
|---|---|---|
| `StageOrder.Network + 10` | -860 | Prediction and reconciliation (First, FixedUpdate). |
| `Rendering3DBuilderExtensions.DestroyOrder` = `WindowClose - 120` | 780 | The 3D renderer releases its GPU resources. |
| `Rendering2DBuilderExtensions.DestroyOrder` = `WindowClose - 100` | 800 | The sprite batch releases its GPU resources. |
| `VulkanGraphics.DestroyOrder`, `GlesGraphics.DestroyOrder` = `WindowClose - 50` | 850 | The graphics device is destroyed, before the window. |
| `-StageOrder.Audio` | 880 | The audio output stops (Destroy). |
| `IonTestHost.CollectorOrder` = `Events - 10` | 990 | Kept for compatibility, unused: the test host now polls its event collectors after every frame. |

## Run order of each stage

With every module installed, a stage runs in this order. Steps at the same order run in registration order. Your steps
at order 0 appear as "user".

### Init (once)

| Order | Step |
|---|---|
| -1000 | `RunReportSystem` (run report setup, when configured) |
| -950 | window creation |
| -900 | graphics device |
| -880 | audio output |
| -870 | network transport |
| -860 | 3D renderer |
| -850 | sprite batch |
| -500 | scenes (the active scene's Init) |
| 0 | user |
| 950 | ECS command playback |
| 960 | web server start |
| 970 | remote protocol start |

### First (every frame)

| Order | Step |
|---|---|
| -950 | window event pump |
| -940 | input snapshot |
| -930 | metrics capture key |
| -920 | asset hot reload |
| -870 | network receive and snapshot apply |
| -860 | network reconciliation |
| -500 | scenes |
| 0 | user |
| 950 | ECS command playback |

### FixedUpdate (zero or more times per frame)

| Order | Step |
|---|---|
| -870 | network tick scope `{` (closes after every other fixed step, capturing the snapshot) |
| -860 | network prediction |
| -700 | physics step (2D and 3D) |
| -500 | scenes |
| 0 | user |
| 950 | ECS command playback |
| -870 | `}` network tick scope end |

### Update

| Order | Step |
|---|---|
| -600 | coroutines |
| -550 | UI frame scope `{` |
| -500 | scenes |
| 0 | user |
| 400 | sprite animation |
| 950 | ECS command playback |
| -550 | `}` UI frame scope end (layout, hit-test and inspectable trees) |

### Render

| Order | Step |
|---|---|
| -900 | graphics frame scope `{` |
| -870 | network interpolation |
| -860 | 3D renderer scope `{` |
| -850 | sprite batch scope `{` |
| -500 | scenes |
| -400 | transform propagation |
| -300 | ECS extraction (sprites and 3D) |
| 0 | user |
| 650 | physics debug drawing |
| 700 | UI drawing |
| 750 | scene transition (fade) |
| 800 | metrics overlay |
| 900 | window close check |
| 950 | ECS command playback |
| -850 | `}` sprite batch end |
| -860 | `}` 3D renderer end (draws the 3D passes, then the 2D overlay) |
| -900 | `}` graphics frame end (submit and present) |

### Last

| Order | Step |
|---|---|
| -880 | audio command flush |
| -500 | scenes |
| -400 | transform propagation |
| 0 | user |
| 870 | network send |
| 950 | ECS command playback |
| 960 | web requests and WebSocket messages |
| 970 | remote protocol requests |
| 1000 | event stepping |

### Destroy (once)

| Order | Step |
|---|---|
| -1000 | run report: screenshot of the last frame and the summary, before any engine teardown |
| -500 | scenes |
| 0 | user |
| 780 | 3D renderer resources |
| 800 | sprite batch resources |
| 850 | graphics device |
| 870 | network disconnect |
| 880 | audio output |
| 900 | window |
| 950 | ECS command playback |
| 960 | web server stop |
| 1000 | metrics shutdown (writes the kept frames when profiling is on) |

## Printing the real schedule

Your game's actual order, with the steps you registered, is always one call away:

```bash
dotnet run --project MyGame -- --Ion:PrintSchedule=true
ion schedule MyGame
```

```csharp
using var game = builder.Build();
game.UseIon().UseSystem<GameSystem>();
Console.WriteLine(game.PrintSchedule());
```

```text
  Render
      -850  NullSpriteBatchSystem.Begin {
      -400    TransformPropagationSystem.PropagateBeforeRender
      -300    SpriteExtractionSystem.Extract
         0    ScoreSystem.RenderScore
       650    Physics2DDebugDrawSystem.Draw
       750    SceneFadeSystem.Draw
       800    MetricsOverlaySystem.Draw
       900    NullWindowSystem.CheckClosed
       950    EcsCommandsSystem.FlushRender
      -850  } NullSpriteBatchSystem.End
```

## Choosing an order for your own steps

```csharp
public sealed class PlayerSystem
{
	// After the physics step, before the game's other fixed steps.
	[FixedUpdate(Order = -10)]
	public void ReadContacts(GameTime dt) { }

	// Before the physics step: drive kinematic bodies from input.
	[FixedUpdate(Order = StageOrder.Physics - 10)]
	public void DriveBodies(GameTime dt) { }

	// Draw over the extracted sprites but under the UI.
	[Render(Order = 100)]
	public void DrawEffects(GameTime dt) { }
}
```

- Stay inside the user band (above -500 and below 500) unless you mean to run among the engine's steps.
- Prefer `[After<T>]`/`[Before<T>]` when the constraint is about another system rather than the engine.
- Placing a step relative to an engine constant (`StageOrder.Physics - 10`, `StageOrder.TransformPropagation - 10`)
  is fine and is what the samples do; see [Breakout Net](/Ion/examples/breakout-net/).

## See also

- [Stages](/Ion/concepts/stages/) and [Systems](/Ion/concepts/systems/).
- [Game loop](/Ion/concepts/game-loop/).
- [Diagnostics](/Ion/reference/diagnostics/): ordering errors such as `ION002` and `ION011`.
