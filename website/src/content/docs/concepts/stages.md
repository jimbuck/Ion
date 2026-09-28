---
title: Stages
description: The seven stages of the Ion game loop, what runs in each, how steps are ordered inside a stage, and the StageOrder bands the engine reserves.
sidebar:
  order: 2
---

The game loop runs in **stages**. A stage is a list of steps in a fixed order; your systems contribute steps to stages
with attributes such as `[Update]` and `[Render]`.

| Stage | Attribute | Runs | Time passed | Typical use |
|---|---|---|---|---|
| `Init` | `[Init]` | Once, before the first frame (for a scene: when it loads) | Frame 0, `Delta` 0 | Load assets, create entities, build GPU resources. |
| `First` | `[First]` | At the start of every frame | Variable | Read the frame's input, react to events from the previous frame. |
| `FixedUpdate` | `[FixedUpdate]` | Zero or more times per frame, at a fixed rate (60 Hz by default) | Fixed step | Simulation: movement, physics, gameplay rules. |
| `Update` | `[Update]` | Once per frame | Variable | Per-frame logic, input handling, UI, camera. |
| `Render` | `[Render]` | Once per frame, after `Update` | Variable | Drawing only. Runs inside the engine's graphics and sprite batch scopes. |
| `Last` | `[Last]` | At the end of every frame | Variable | Late bookkeeping. The engine plays back ECS commands, answers web and remote requests and steps the event bus here. |
| `Destroy` | `[Destroy]` | Once, after the last frame (for a scene: when it unloads) | The variable `GameTime` after the last frame | Release resources. |

The `Stage` enum has the same seven values (`Init = 1` to `Destroy = 7`); `GameLoopStage` adds `None` (0) for "between
stages". `ILoopContext.Stage` tells a service which stage is running.

## A frame

```text
Init                                  once
  ┌──────────────────────────────── frame ────────────────────────────────┐
  │ First                                                                  │
  │ FixedUpdate × n    (n = whole fixed steps that fit the elapsed time)   │
  │ Update                                                                 │
  │ Render                                                                 │
  │   (exit requests are checked here: after Render, before Last)          │
  │ Last                                                                   │
  │ frame pacing (sleep until 1 / MaxFPS has passed)                       │
  └────────────────────────────────────────────────────────────────────────┘
Destroy                               once
```

`FixedUpdate` can run zero times in a frame (a fast frame at 300 fps) or several times (a slow frame). It never runs
more than the clamped frame time allows: frames longer than `Ion:MaxFrameTime` (100 ms by default) are clamped, so a
breakpoint or a window drag does not trigger a burst of catch-up steps. Details are in
[Time and determinism](/Ion/concepts/time-and-determinism/) and [The game loop](/Ion/concepts/game-loop/).

:::tip[Which stage should my code go in?]
Put **simulation** in `FixedUpdate` so it behaves the same at any frame rate and replays deterministically. Put
**presentation** (camera smoothing, animation, UI, input that should feel immediate) in `Update`. Put **drawing** in
`Render` and nothing else. Use `First` for things that must happen before any gameplay in the frame, and `Last` for
things that must see the whole frame.
:::

## Order inside a stage

Every step has an `Order` (an `int`, default 0). Within a stage:

1. **Constraints first.** `[After<T>]` and `[Before<T>]` order a step relative to every step of system `T` in the same
   stage and schedule. They take precedence over `Order`. A cycle is error `ION002`.
2. **Then `Order`, ascending.** Lower runs first.
3. **Then registration order.** The order systems were added with `UseSystem` (a system added twice keeps its first
   place).
4. **Then declaration order** of the methods within a system (base class methods first).

At equal order, a scope (`[Begin]`) opens before the steps. See [Systems](/Ion/concepts/systems/) for the attributes.

```csharp
public sealed class CameraSystem
{
    [Update(Order = 100)] public void Follow(GameTime dt) { }             // after the default steps
}

public sealed class InputMapSystem
{
    [Update(Order = -100)] public void Map(GameTime dt) { }               // before the default steps
}

[After<PhysicsSystem>]                                                    // every step of this class
public sealed class DamageSystem
{
    [FixedUpdate] public void Apply(GameTime dt) { }
}
```

## Engine order bands

Engine steps live in two reserved bands so that your steps at order 0 always run between engine setup and engine
teardown, whether you register your systems before or after `UseIon()`:

| Band | Range | Constants |
|---|---|---|
| Engine setup | -1000 to -500 | `StageOrder.EngineSetupFirst` (-1000), `StageOrder.EngineSetupLast` (-500) |
| User default | 0 | `StageOrder.Default` |
| Engine teardown | 500 to 1000 | `StageOrder.EngineTeardownFirst` (500), `StageOrder.EngineTeardownLast` (1000) |

A few engine steps (transform propagation, extraction, sprite animation) sit between the bands on purpose, so that they
run just before or after your default steps.

### StageOrder values

These are the constants in `StageOrder` (namespace `Ion`) and the stages that use them. Values are from
[`Stage.cs`](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Core.Abstractions/Schedule/Stage.cs).

| Constant | Value | Stage(s) | What runs there |
|---|---|---|---|
| `Trace` | -1000 | | Reserved (the 0.2 trace timer scope; the loop now records a span per stage itself). |
| `Window` | -950 | Init, First | Window creation; event pumping. |
| `Input` | -940 | First | The per-frame input snapshot. |
| `Metrics` | -930 | First | The trace capture key (F9). |
| `AssetReload` | -920 | First | Reloading changed assets (hot reload). |
| `Graphics` | -900 | Init, Render | Device creation; the frame scope. |
| `Audio` | -880 | Init, Last | Device creation; flushing queued audio commands. |
| `Network` | -870 | Init, First, FixedUpdate, Render | Transport start; receiving and applying snapshots; the tick scope; interpolation. Prediction at `Network + 10`. |
| `Rendering3D` | -860 | Init, Render | The 3D renderer and its frame scope (opens before the sprite batch, closes after it). |
| `SpriteBatch` | -850 | Init, Render | The sprite batch scope around every Render step. |
| `Physics` | -700 | FixedUpdate | The 2D and 3D physics steps, before the game's fixed steps. |
| `Coroutines` | -600 | Update | Stepping the shared coroutine runner. |
| `UiFrame` | -550 | Update | The UI frame scope. |
| `Scenes` | -500 | every stage | The active scene's schedule. |
| `TransformPropagation` | -400 | Last, Render | ECS global transforms. |
| `Extract` | -300 | Render | ECS sprite and 3D extraction, before your Render steps. |
| `Default` | 0 | any | Your steps. |
| `SpriteAnimation` | 400 | Update | ECS sprite animation, after your Update steps. |
| `PhysicsDebugDraw` | 650 | Render | Collider outlines, on top of your drawing. |
| `Ui` | 700 | Render | UI drawing, on top of your drawing. |
| `MetricsOverlay` | 800 | Render | The metrics overlay, on top of the UI. |
| `NetworkSend` | 870 | Last, Destroy | Encoding and sending snapshots and messages; disconnecting. |
| `WindowClose` | 900 | Render | Turning a closed window into an exit request. |
| `Ecs` | 950 | every stage | ECS command playback at the end of each stage. |
| `Web` | 960 | Last | Web requests handled on the game thread. |
| `Remote` | 970 | Last | Remote protocol requests handled on the game thread. |
| `Events` | 1000 | Last | Stepping the event bus: the end of the event frame. |

The [stage order reference](/Ion/reference/stage-order/) has the same table with cross-links to each module.

:::caution[Use the constants, not the numbers]
If you need to run relative to an engine step, write `Order = StageOrder.Extract + 10` rather than `Order = -290`. The
values may be adjusted between releases; the constants carry the meaning.
:::

## Reading the schedule

`ion schedule`, `game.PrintSchedule()` or `--Ion:PrintSchedule=true` print every stage in run order. This is the
Model sample running headless (`AddEcsRendering3D()` plus one game system):

```text
Schedule root
  Init
      -950  NullWindowSystem.Init
      -880  AudioSystem.Init
      -860  Rendering3DSystem.Init
         0  ModelSystem.Init
       950  EcsCommandsSystem.FlushInit
  First
      -940  NullInputSystem.First
      -930  MetricsSystem.CaptureKey
      -920  AssetReloadSystem.First
       950  EcsCommandsSystem.FlushFirst
  FixedUpdate
       950  EcsCommandsSystem.FlushFixedUpdate
  Update
      -600  CoroutineSystem.Update
         0  ModelSystem.MoveCamera
         0  ModelSystem.Turn
       400  SpriteAnimationSystem.Animate
       950  EcsCommandsSystem.FlushUpdate
  Render
      -860  Rendering3DSystem.Begin {
      -850    NullSpriteBatchSystem.Begin {
      -400      TransformPropagationSystem.PropagateBeforeRender
      -300      Scene3DExtractionSystem.Extract
      -300      Scene3DExtractionSystem.ExtractCamera
      -300      Scene3DExtractionSystem.ExtractDirectionalLight
      -300      Scene3DExtractionSystem.ExtractPointLight
      -300      Scene3DExtractionSystem.ExtractSpotLight
         0      ModelSystem.Render
       800      MetricsOverlaySystem.Draw
       900      NullWindowSystem.CheckClosed
       950      EcsCommandsSystem.FlushRender
      -850    } NullSpriteBatchSystem.End
      -860  } Rendering3DSystem.End
  Last
      -880  AudioSystem.Last
      -400  TransformPropagationSystem.PropagateLast
         0  ModelSystem.Last
       950  EcsCommandsSystem.FlushLast
      1000  EventSystem.StepEvents
  Destroy
       780  Rendering3DSystem.Destroy
       880  AudioSystem.Destroy
       950  EcsCommandsSystem.FlushDestroy
      1000  MetricsSystem.Shutdown
```

Braces show scopes: everything between `Rendering3DSystem.Begin {` and `} Rendering3DSystem.End` runs inside the 3D
frame, and the end always runs, even if a step throws. Constraints show as `[after PaddleSystem]`. Scenes print their
own schedules after the root, marked `(run by SceneSystem)`. The exact list depends on which modules you use and on
headless or windowed mode (a windowed run shows the Silk.NET window and the RHI graphics systems instead of the null
ones).

:::tip
When a step does not run when you expect, print the schedule first. It is the ground truth for what runs, in what
order, inside which scopes.
:::

## Scenes and stages

A scene has its own schedule with the same seven stages and the same ordering rules. The `SceneSystem` runs the active
scene's stage at `StageOrder.Scenes` (-500) inside each root stage, so scene steps run after engine setup and before
your root steps at order 0. A scene's `Init` and `Destroy` run when the scene loads and unloads, not with the
application. See [Scenes](/Ion/ecs/scenes/).

## Input and events across stages

Per-frame state depends on the stage that reads it:

- `IInputState` edges (`Pressed`, `Released`), deltas and text describe the current frame from `First`, `Update`,
  `Render` and `Last`. From `FixedUpdate` they describe everything since the previous fixed step, so a click is seen by
  exactly one fixed step even on frames that run none.
- Events get the same guarantee: an event is visible in the frame it is emitted and the next one, and readers in
  `FixedUpdate` also see older events that no fixed step has seen yet. See [Events](/Ion/concepts/events/).

## See also

- [Systems](/Ion/concepts/systems/): stage attributes, `Order`, constraints and scopes in detail.
- [The game loop](/Ion/concepts/game-loop/): how a frame is timed, paced and ended.
- [Stage order reference](/Ion/reference/stage-order/).
- [Input overview](/Ion/interaction/input/overview/).
