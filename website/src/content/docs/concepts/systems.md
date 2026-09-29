---
title: Systems
description: Write Ion systems as plain classes with stage attributes, inject services, order steps with Order, After and Before, wrap stages with Begin and End scopes, and add function steps.
sidebar:
  order: 3
---

A **system** is any class registered in dependency injection and added to the schedule with `UseSystem<T>()`. Each of
its public methods marked with a stage attribute is a **step** of that stage. A step runs, returns, and the next step
runs; there is no `next` delegate to call.

```csharp
using Ion;
using Ion.Extensions.Graphics;

public sealed class PaddleSystem(IInputState input, ISpriteBatch sprites, IWindow window)
{
    private float _x;

    [Init]
    public void Start(GameTime dt) => _x = window.Size.X / 2;

    [Update]
    public void Move(GameTime dt)
    {
        if (input.Down(Key.Left)) _x -= 480 * dt.Delta;
        if (input.Down(Key.Right)) _x += 480 * dt.Delta;
    }

    [Render]
    public void Draw(GameTime dt) =>
        sprites.DrawRect(Color.White, new System.Numerics.Vector2(_x - 60, window.Size.Y - 40), new System.Numerics.Vector2(120, 16));
}
```

```csharp title="Program.cs"
builder.AddSystem<PaddleSystem>();      // register (a singleton)
// ...
game.UseSystem<PaddleSystem>();         // add its steps to the schedule
```

Both calls are needed. `AddSystem` makes the system a service; `UseSystem` puts its steps in the schedule. A system that
is used but not registered is error `ION009`.

## Stage attributes

| Attribute | Stage | Notes |
|---|---|---|
| `[Init]` | `Init` | Once before the first frame. |
| `[First]` | `First` | Start of every frame. |
| `[FixedUpdate]` | `FixedUpdate` | Zero or more times per frame, with the fixed-step `GameTime`. |
| `[Update]` | `Update` | Once per frame. |
| `[Render]` | `Render` | Once per frame, inside the graphics and sprite batch scopes. |
| `[Last]` | `Last` | End of every frame. |
| `[Destroy]` | `Destroy` | Once after the last frame. |
| `[Begin(stage)]`, `[End(stage)]` | any | A scope around later steps; see [Scopes](#scopes-begin-and-end). |

Every stage attribute has an `Order` property (`[Update(Order = 10)]`, default 0). A method can carry attributes of
several stages (`[Update, Render]`), and becomes one step in each. Attributes are inherited: a system can derive its
steps from a base class.

## Step signatures

| Signature | Meaning |
|---|---|
| `void M(GameTime dt)` | The usual form. `dt` is the frame's time (the fixed step in `FixedUpdate`). |
| `void M()` | A step that does not need the time. |
| `void M(GameTime dt, TService1 s1, ...)` | Extra parameters are services, resolved **once** when the schedule is built. |

What is not allowed, and the diagnostic you get:

| Mistake | Diagnostic |
|---|---|
| The method is not `public` | `ION004` (it could never run) |
| `async`, or returns `Task`/`ValueTask` | `ION005` |
| Returns a value, is generic, takes `ref`/`out` or more than one `GameTime` | `ION007` |
| A parameter service is not registered | `ION008` |
| A root system is registered as scoped, or a root step injects a scoped service | `ION006` |
| The system has no stage or scope method at all | `ION013` (warning) |

With the source generator these are compile-time errors on the offending method; without it, `Build()` or `Run()`
throws an `IonScheduleException` listing every error at once.

`GameTime` converts implicitly to `float` (its `Delta`), so `position += velocity * dt` works.

## Injecting services

Inject what a system needs through its **constructor** (primary constructors keep this short). Constructor services are
resolved when the system itself is resolved, once, when the schedule is built:

```csharp
public sealed class ScoreSystem(IEvents events, ILogger<ScoreSystem> logger, IOptions<GameConfig> config)
{
    // ...
}
```

A step can also take services as **parameters** after `GameTime`. They are resolved once too, not per call, so this is
as cheap as a field. It is convenient for services used by one step only:

```csharp
public sealed class LevelSystem(World world)
{
    [Init]
    public void Load(GameTime dt, IAssetManager assets, IWindow window)
    {
        // assets and window are resolved once, when the schedule is built
    }
}
```

:::note[Lifetimes]
`AddSystem<T>()` registers the system as a singleton, which is what the root schedule needs. The root schedule resolves
from the root service provider, so scoped systems and scoped step parameters are rejected there (`ION006`). Scene
schedules resolve from the scene's scope, so systems added inside `UseScene(...)` may be scoped, and a system
registered as a singleton is created from the scene's scope too (once per load), so it gets the scene's services. See
[Services and configuration](/Ion/concepts/services-and-configuration/).
:::

## Ordering

Within a stage, steps run by constraints, then `Order`, then registration order, then declaration order. See
[Stages](/Ion/concepts/stages/#order-inside-a-stage) for the full rule and the engine's reserved bands.

### `Order`

```csharp
[Update(Order = -10)] public void ReadIntent(GameTime dt) { }   // earlier
[Update] public void Move(GameTime dt) { }                      // 0
[Update(Order = 10)] public void Follow(GameTime dt) { }        // later
```

Your steps should stay near 0; the engine uses -1000 to -500 and 500 to 1000. To run relative to an engine step, add to
the constant: `[Render(Order = StageOrder.Extract + 1)]`.

### `[After<T>]` and `[Before<T>]`

Constraints order a step relative to **every** step (and scope opening) of system `T` in the same stage of the same
schedule, whatever their orders. Put them on a method, or on the class to apply to all of its steps. Several are
allowed:

```csharp
public sealed class BallSystem
{
    [Init, After<PaddleSystem>]
    public void Init(GameTime dt) { }            // the paddle exists first

    [FixedUpdate, After<PhysicsSystem>, Before<ScoreSystem>]
    public void Bounce(GameTime dt) { }
}

[After<InputMapSystem>]
public sealed class PlayerSystem
{
    [Update] public void Move(GameTime dt) { }
    [FixedUpdate] public void Step(GameTime dt) { }
}
```

`T` matches a registered system whose service or implementation type is, derives from or implements `T`, so you can
order against an interface. A constraint naming a system with no step in that stage logs warning `ION012`; a cycle is
error `ION002`, and the message names the steps involved.

:::tip
Prefer constraints over magic numbers when the relationship is between two of your systems. `After<PaddleSystem>` says
why; `Order = 7` does not.
:::

## Scopes: Begin and End

A scope wraps part of a stage. A `[Begin(stage)]` method and an `[End(stage)]` method on the same system form a pair.
The begin runs at its position in the stage, every step and scope that sorts after it runs, and then the end runs, in a
`finally`, even if one of those steps throws:

```csharp
public sealed class FrameTimer
{
    private long _start;

    [Begin(Stage.Render, Order = -100)]
    public void Start(GameTime dt) => _start = System.Diagnostics.Stopwatch.GetTimestamp();

    [End(Stage.Render)]      // Order is optional on End; if set, it must equal the Begin's
    public void Stop(GameTime dt) => Console.WriteLine(System.Diagnostics.Stopwatch.GetElapsedTime(_start));
}
```

Printed, the stage shows the scope with braces:

```text
  Render
      -850  NullSpriteBatchSystem.Begin {
      -100    FrameTimer.Start {
         0      ScoreSystem.RenderScore
      -100    } FrameTimer.Stop
      -850  } NullSpriteBatchSystem.End
```

This is how the engine brackets frames: the graphics frame, the 3D renderer and the sprite batch are scopes in `Render`,
which is why `ISpriteBatch` calls work from any of your `Render` steps.

| Rule | Diagnostic |
|---|---|
| A `[Begin]` without an `[End]` for the same stage (or the reverse) | `ION003` |
| Two unnamed scopes of one system in one stage | `ION011`; give each pair a `ScopeName` |
| An `[End]` whose `Order` differs from its `[Begin]` | `ION011` |

Use `ScopeName` to pair several scopes of one system in the same stage, and put `[Begin]` on one method several times to
open scopes in several stages:

```csharp
public sealed class Profiling
{
    [Begin(Stage.Update, Order = -900, ScopeName = "outer")] public void OuterBegin(GameTime dt) { }
    [End(Stage.Update, ScopeName = "outer")] public void OuterEnd(GameTime dt) { }

    [Begin(Stage.Update, Order = 50, ScopeName = "late")] public void LateBegin(GameTime dt) { }
    [End(Stage.Update, ScopeName = "late")] public void LateEnd(GameTime dt) { }
}
```

### Coming from middleware

Before 0.3, systems were middleware: `void M(GameTime dt, GameLoopDelegate next)` called `next(dt)` to run the rest of
the stage, and `app.UseUpdate(next => dt => ...)` added a delegate in the same shape. The schedule does not run those
forms: a stage method that takes or returns a `GameLoopDelegate` is an unsupported signature (`ION007`), and the
`UseInit` to `UseDestroy` delegate methods are gone. Port them like this:

| Middleware (0.2) | Steps and scopes (0.3) |
|---|---|
| Code before `next(dt)`, then `next(dt)` | A step, or a `[Begin]` if later code must run after the rest of the stage |
| Code after `next(dt)` | A later step (higher `Order` or `[After<T>]`), or an `[End]` |
| `try { next(dt); } finally { ... }` | A `[Begin]`/`[End]` scope (the end runs in a `finally`) |
| Not calling `next(dt)` to skip the rest | Not supported; check a flag in the steps instead |

## Function steps

For a few lines of code, add a delegate instead of a class. `app.Init(...)` through `app.Destroy(...)` take an
`Action<GameTime>` or a delegate with up to four service parameters after `GameTime`, plus optional `order` and `name`:

```csharp
game.Update((GameTime dt, IInputState input, IEvents events) =>
{
    if (input.Pressed(Key.Escape)) events.Emit<ExitGameEvent>();
});

game.Render(Hud.Draw, order: 50);                                    // a static method group
game.Update<ILogger<Hud>>(Hud.Log, name: "Hud.Log");                 // a method group with services needs its types
```

Services are resolved once when the schedule is built, like step parameters. `[After<T>]` and `[Before<T>]` on the method
(or the lambda) are honoured. Function steps print as `Program.lambda(IInputState, IEvents) (function)` unless you give
them a `name`.

## Adding systems: idempotence and order

- `UseSystem<T>()` adds every stage method of `T`. Adding the same system again does nothing and it keeps its first
  place, which lets modules add the systems of the modules they depend on.
- `AddSystem<T>()` uses `TryAddSingleton`, so registering twice is harmless as well.
- `UseSystem<TService, TImplementation>()` resolves the instance as `TService` and takes the steps from
  `TImplementation`.
- The order of `UseSystem` calls only breaks ties between equal orders.

## Conditional systems

There is no run-condition attribute: a step runs every time its stage runs. To enable a system only in some
configurations, register it conditionally in `Program.cs` (the generator includes registrations under an `if` and guards
them):

```csharp
if (builder.Configuration.IsHeadless()) builder.AddSystem<HeadlessAutopilotSystem>();
// ...
if (game.Configuration.IsHeadless()) game.UseSystem<HeadlessAutopilotSystem>();
```

To pause behavior at run time, check state at the top of the step (`if (!state.Playing) return;`). For content that
comes and goes (menus, levels), use [scenes](/Ion/ecs/scenes/): each scene has its own schedule, loaded and unloaded
with the scene.

## ECS query steps

In a `partial` system, a method marked with a stage attribute and `[Query]` runs once per matching entity, as a loop the
generator writes for you. It may be private:

```csharp
public sealed partial class MoveSystem
{
    [FixedUpdate, Query]
    private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt) =>
        transform.Position += velocity.Value * dt;
}
```

See [Queries](/Ion/ecs/queries/).

## Disposal

Systems are services, so the container disposes the ones that implement `IDisposable` when the application is disposed
(`using var game = ...`). Release GPU resources in a `[Destroy]` step instead if they must go before the device, as the
Quad sample does.

## See also

- [Stages](/Ion/concepts/stages/): stage semantics and the reserved order bands.
- [The application](/Ion/concepts/application/): `AddSystem`, `UseSystem` and modules.
- [Source generators](/Ion/concepts/source-generators/): how systems are bound without reflection.
- [Diagnostics](/Ion/reference/diagnostics/): `ION001` to `ION014`.
