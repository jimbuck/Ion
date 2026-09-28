---
title: Queries
description: Write [Query] methods that the Ion source generator expands into allocation-free chunk loops, with the supported parameter shapes, All/Any/None filters, diagnostics ION301 to ION307 and performance numbers.
sidebar:
  order: 3
---

A `[Query]` method runs once per entity that has the components it asks for. You write the per-entity body; the Ion
source generator (`Ion.Generators`) expands it at compile time into a loop over Arch's chunks that the schedule calls
directly, with no delegates, no boxing and no allocations.

```csharp
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Ecs;

public record struct Velocity(Vector2 Value);
public record struct Frozen;

public sealed partial class MoveSystem
{
    [Update, Query, None<Frozen>]
    private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt)
        => transform.Position += velocity.Value * dt;
}
```

Register and add the system like any other (`builder.AddSystem<MoveSystem>()`, `game.UseSystem<MoveSystem>()`). The
method runs in its stage (here Update, at the default order 0) against the world of the schedule it is in: the root
world, or the scene's world when the system is in a scene.

## Rules

1. The containing class (and every class it is nested in) is `partial`, so the generator can add the loop (ION301).
2. The method has `[Query]` and a stage attribute (`[Update]`, `[FixedUpdate]`, ...). Without a stage it never runs
   (warning ION307).
3. It returns `void`, is not generic and is not `async` (ION306).
4. Every parameter is one of the shapes below.
5. It may be `private`, and it may be `static`.

## Parameter shapes

| Parameter | Receives | Notes |
|---|---|---|
| `ref T` | Component `T`, read and write | `T` must be a struct (ION303). Implies the entity has `T`. |
| `in T` | Component `T`, read only | Same as `ref` for matching; documents intent and avoids copies. |
| `Entity` or `in Entity` | The entity being processed | For `Commands` calls or lookups. |
| `[Data] float` or `[Data] in float` | The frame's delta time in seconds (`GameTime.Delta`) | In FixedUpdate this is the fixed step. |
| `GameTime` or `[Data] GameTime` | The whole `GameTime` (`Delta`, `Elapsed`, `Frame`, `Alpha`) | |
| `Commands` | The schedule's command buffer | Record structural changes here. |
| `World` | The schedule's world | For reads of other entities. Not for structural changes (ION305). |

Anything else is an error:

| Mistake | Diagnostic |
|---|---|
| A struct component passed by value (`Health health`) | ION302: changes would be lost and the component copied |
| A class passed by `ref`/`in` (`ref string name`) | ION303: query components are structs |
| An `out` parameter, a service type, a component listed twice, `[Data]` on a type other than `float`/`GameTime` | ION306 |

Services come from the system's constructor, not from query parameters:

```csharp
public sealed partial class BounceSystem(IWindow window, IEvents events)
{
    [FixedUpdate, Query]
    private void Bounce(Entity entity, ref Transform2D transform, ref Velocity velocity)
    {
        var size = window.Size;
        if (transform.Position.X < 0 || transform.Position.X > size.X)
        {
            velocity = velocity with { Value = velocity.Value * new Vector2(-1, 1) };
            events.Emit(new BouncedEvent(entity.Id));
        }
    }
}

public record struct BouncedEvent(int EntityId);
```

## Filters

The components of `ref` and `in` parameters are required. Add more conditions with filter attributes on the method:

| Attribute | Arity | Matches entities that |
|---|---|---|
| `All<T0, ...>` | 1 to 4 types | also have every listed component (without reading it) |
| `Any<T0, ...>` | 1 to 4 types | have at least one of the listed components |
| `None<T0, ...>` | 1 to 4 types | have none of the listed components |

Attributes can be repeated and combined. Filter types must be structs (ION303), and a component cannot be both required
(parameter or `All`) and excluded (`None`): that query would match nothing (ION304).

```csharp
public record struct Ball;
public record struct Burning;
public record struct Poisoned;

public sealed partial class StatusSystem
{
    // Balls only; reads nothing from the Ball tag itself.
    [Update, Query, All<Ball>]
    private void Spin(ref Transform2D transform, [Data] float dt) => transform.Rotation += dt;

    // Entities with Burning or Poisoned (or both), unless Frozen.
    [Update(Order = 10), Query, Any<Burning, Poisoned>, None<Frozen>]
    private void Hurt(Entity entity, ref Health health, Commands commands)
    {
        health = new Health(health.Value - 1);
        if (health.Value <= 0) commands.Destroy(entity);
    }
}

public record struct Health(int Value);
```

## Structural changes inside a query

Creating or destroying entities and adding or removing components moves entities between chunks, which would corrupt the
loop. Record those changes with a `Commands` parameter; the ECS module applies them at the end of the stage
(see [Entities and commands](/Ion/ecs/entities-and-commands/)).

```csharp
public sealed partial class BallSystem(IWindow window, IEvents events)
{
    // From the Breakout ECS sample: a ball that fell below the window leaves the physics simulation
    // (RigidBody2D and Collider2D come from Ion.Extensions.Physics2D; Ball and BallLostEvent are the game's).
    [Update(Order = 1), Query, All<Ball>]
    private void CheckLost(Entity entity, in Transform2D transform, in RigidBody2D body, Commands commands)
    {
        if (transform.Position.Y <= window.Height) return;
        commands.Remove<RigidBody2D>(entity);
        commands.Remove<Collider2D>(entity);
        events.Emit(new BallLostEvent());
    }
}
```

Ion guards this at two levels:

- **At compile time (ION305).** When a query method does not take `Commands`, the generator scans its body for
  structural calls: `World.Create`, `Destroy`, `Add`, `Remove`, `AddRange`, `RemoveRange`, `Clear`, `TrimExcess`,
  `Dispose`, Arch's entity extensions `Add`/`Remove`, `SetParent`/`RemoveParent`/`DestroyRecursive`, and explicit
  playback (`CommandBuffer.Playback`, `Commands.Flush`). The message tells you which `commands.*` call to use.
- **At run time.** The body may call a helper the generator cannot see. After every entity, the generated loop checks
  that the chunk's entity count and the world's size did not change, and throws a `StructuralChangeException` whose
  `Step` (`System.Method`) and `Entity` properties name the culprit.

:::tip[Unchecked queries]
`[Query(Unchecked = true)]` drops the run-time check (two comparisons per entity, about 10 percent of a trivial body and
nothing measurable for a real one). A structural change then corrupts the iteration silently instead of throwing. Use it
only on hot loops whose bodies you control.
:::

Writing to components (through `ref` parameters, `world.Get<T>(other)` or `world.Set`) is not structural and is always
allowed.

## Order of iteration and of steps

- Entities are visited in Arch's own order: archetypes and chunks in order, entities from last to first within a chunk,
  the same order as `World.Query`. It is deterministic for a given sequence of operations, which is what golden tests
  rely on.
- Query steps are ordered with the stage's other steps by `Order`, `[Before<T>]` and `[After<T>]`. At equal `Order`, a
  query step runs after the same system's non-query steps.
- The step keeps the method's name in the schedule: `MoveSystem.Move` in `--Ion:PrintSchedule=true`, in traces and in
  exceptions. The generated method (`__IonQuery_Move`) is hidden.

## What the generator emits

For each valid query method, the generator adds a public method, hidden from IntelliSense, to your partial class:

```csharp
// Shape of the generated code (simplified)
[Update, ExpandedStep("Move")]
public void __IonQuery_Move(GameTime dt, World world)
{
    foreach (ref var chunk in world.Query(in description))
    {
        ref var t0 = ref chunk.GetFirst<Transform2D>();
        ref var t1 = ref chunk.GetFirst<Velocity>();
        for (var i = chunk.Count - 1; i >= 0; i--)
        {
            Move(ref Unsafe.Add(ref t0, i), in Unsafe.Add(ref t1, i), dt.Delta);
            // structural-change check here unless Unchecked
        }
    }
}
```

It copies the method's stage and ordering attributes, so the schedule (generated or reflection-bound) runs it in the
original's place. A module initializer also registers every query component (parameters and filters) with Arch through
`EcsComponents.Register<T>()`, which NativeAOT needs (see
[Components and serialization](/Ion/ecs/components-and-serialization/)).

### Without the generator

If a project is compiled without `Ion.Generators` (it ships as an analyzer of the `Ion` package, so this is rare),
`QueryAttribute` binds the method by reflection instead. The rules are the same (checked at schedule build time, with the
same messages), the results are identical, but every component is boxed: about 28 times slower and allocating.

## Performance

Measured on 10,000 entities with a small per-entity body (`EcsQueryBenchmarks`, roadmap section 5.9):

| Form | Mean | Ratio | Allocated |
|---|---|---|---|
| Hand-written chunk spans (baseline) | 73.9 us | 1.00 | 0 B |
| Arch `World.Query` with a lambda | 69.5 us | 0.94 | 0 B |
| Generated `[Query]` (checked) | 78.4 us | 1.06 (0.99 in an earlier run) | 0 B |
| Generated `[Query(Unchecked = true)]` | 64.1 us | 0.87 | 0 B |
| Reflection binder (no generator) | 2,121 us | 28.7 | 1.76 MB |

The generated loop matches the hand-written baseline within noise and allocates nothing per frame. See
[Benchmarks](/Ion/tooling/benchmarks/) to run them yourself.

## Using Arch queries directly

`[Query]` covers the common shape. For anything else, use Arch's API on the injected world. Cache the
`QueryDescription` in a field so it is not rebuilt every frame:

```csharp
public sealed class ClosestEnemySystem(World world)
{
    private readonly QueryDescription _enemies = new QueryDescription().WithAll<Enemy, GlobalTransform2D>().WithNone<Hidden>();

    public Entity Closest { get; private set; }

    [Update]
    public void Find(GameTime dt)
    {
        var best = float.MaxValue;
        Closest = Entity.Null;
        foreach (ref var chunk in world.Query(in _enemies))
        {
            var globals = chunk.GetSpan<GlobalTransform2D>();
            for (var i = 0; i < chunk.Count; i++)
            {
                var d = globals[i].Position.LengthSquared();
                if (d < best) { best = d; Closest = chunk.Entity(i); }
            }
        }
    }
}

public record struct Enemy;
```

`world.CountEntities(in description)` and `world.Query(in description, (ref T a, ...) => ...)` work as in Arch's
documentation. The same structural-change rule applies inside them, but only `[Query]` methods get the ION305 check and
the run-time guard.

## Planned: parallel queries

Parallel queries (running a query's chunks on several threads through Arch's job scheduler) are not built. Every query
runs on the game thread today. The roadmap lists them as open work for the ECS module.

## See also

- [Entities and commands](/Ion/ecs/entities-and-commands/)
- [Source generators](/Ion/concepts/source-generators/)
- [Diagnostics](/Ion/reference/diagnostics/)
- [Systems](/Ion/concepts/systems/)
- [Benchmarks](/Ion/tooling/benchmarks/)
