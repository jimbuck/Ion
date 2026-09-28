---
title: Entities and commands
description: Create, change and destroy entities with Arch's World directly or through Ion's deferred Commands buffer, and know exactly when recorded changes are applied.
sidebar:
  order: 2
---

An entity is an Arch `Entity`: an id in one `World` with a set of struct components. You change which components an
entity has (a *structural change*) either directly on the `World` or by recording the change in `Commands`, which the
ECS module plays back at the end of the stage. This page covers both and when to use which.

## Direct changes on the world

Outside a query, call Arch's `World` methods directly. The change happens immediately.

```csharp
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public record struct Health(int Value);
public record struct Frozen;

public sealed class SpawnSystem(World world, IAssetManager assets)
{
    private Entity _player;

    [Init]
    public void Init(GameTime dt)
    {
        var texture = assets.Load<ITexture2D>("player.png");

        // Create an entity with its components (Arch has generic overloads for many components).
        _player = world.Create(new EntityName("player"), new Transform2D(new Vector2(100, 100)), new Sprite(texture), new Health(3));

        world.Add(_player, new Frozen());          // add a component
        world.Remove<Frozen>(_player);             // remove it
        world.Set(_player, new Health(5));         // overwrite a component it has
        ref var health = ref world.Get<Health>(_player);
        health = health with { Value = health.Value - 1 }; // write through the ref

        if (world.Has<Health>(_player) && world.TryGet<Health>(_player, out var current)) { /* read a copy */ }
    }

    [Update]
    public void Update(GameTime dt)
    {
        if (world.IsAlive(_player) && world.Get<Health>(_player).Value <= 0) world.Destroy(_player);
    }
}
```

Arch's entity extension methods in `Arch.Core.Extensions`
(`entity.Get<T>()`, `entity.Has<T>()`, `entity.IsAlive()`) work too; the Breakout ECS sample uses them for the paddle.

| Arch call | Structural | Notes |
|---|---|---|
| `world.Create(c0, c1, ...)` | yes | Returns the new `Entity`. |
| `world.Destroy(entity)` | yes | The id may be reused later: keep `world.IsAlive(entity)` checks on stored entities. |
| `world.Add(entity, component)` / `world.Add<T>(entity)` | yes | Moves the entity to another archetype. |
| `world.Remove<T>(entity)` | yes | |
| `world.Get<T>(entity)` | no | Returns a `ref T`: assign through it to write. |
| `world.Set(entity, component)` | no | The entity must already have `T`. |
| `world.Has<T>(entity)`, `world.TryGet<T>(entity, out T)` | no | |
| `world.IsAlive(entity)`, `world.Size`, `world.CountEntities(in query)` | no | |

:::caution[Never change structure while a query runs]
A structural change moves entities between Arch's chunks. Doing it inside a `[Query]` method (or inside a lambda passed
to `world.Query`) invalidates the chunk being iterated. Ion catches this two ways: the generator reports **ION305** when
a query method's body calls a structural `World` method, and the generated loop checks the chunk count and world size
after every entity and throws a `StructuralChangeException` naming the step and the entity. Record the change with
`Commands` instead.
:::

## Commands: deferred structural changes

`Commands` (namespace `Ion.Extensions.Ecs`) records structural changes now and applies them later. Inject it next to the
`World`; each world (the root one and each scene's) has its own `Commands`, and a query method can also take it as a
parameter.

```csharp
public sealed partial class DamageSystem
{
    // Entities whose Health runs out are destroyed at the end of Update, after every Update step ran.
    [Update, Query, None<Frozen>]
    private void Die(Entity entity, in Health health, Commands commands)
    {
        if (health.Value <= 0) commands.Destroy(entity);
    }
}
```

### What you can record

| Method | Records |
|---|---|
| `Create<T0>(in c0)` ... `Create<T0, T1, T2, T3>(in c0, ..., in c3)` | Creating an entity with one to four components. Returns a placeholder entity (see below). |
| `Create(ComponentType[] types)` | Creating an entity with a signature; set the values with `Set<T>`. |
| `Destroy(entity)` | Destroying an entity. |
| `DestroyRecursive(entity)` | Destroying an existing entity and all its descendants in the hierarchy (applied after the other commands). |
| `Add<T>(entity, in component = default)` | Adding a component. |
| `Remove<T>(entity)` | Removing a component. |
| `Set<T>(entity, in component)` | Setting a component the entity has, or gets from a `Create`/`Add` of the same commands. |
| `SetParent(child, parent)` | Parenting (the child may be an entity these commands create; the parent must already exist). |
| `RemoveParent(child)` | Detaching an existing entity from its parent. |
| `commands.SpawnModel(model, transform, options)` | Instantiating an `IModel` as an entity hierarchy (see [ECS rendering](/Ion/ecs/ecs-rendering/)). |

And to inspect or control the buffer:

| Member | Meaning |
|---|---|
| `World` | The world the commands play back on. |
| `Count`, `IsEmpty` | Commands recorded since the last `Flush`. |
| `Playbacks` | How many times `Flush` applied commands. |
| `Flush()` | Applies everything now. Called by the ECS module; call it yourself outside a query to apply early. |
| `Clear()` | Discards everything recorded. |
| `Buffer` | Arch's underlying `CommandBuffer`, for operations `Commands` does not wrap. |

`Commands` is not thread safe: record from the game thread.

### When commands are applied

`EcsCommandsSystem` (added by `UseEcs()`) calls `Flush()` in **every stage** at order `StageOrder.Ecs` (950): after every
other step of the stage, and before the event stepping at 1000. In FixedUpdate it flushes after each fixed step, so the
next fixed step sees the changes. The practical rule:

> Changes recorded during a stage are visible from the next stage on.

The ECS tests pin this down. A system that records a creation in Update and counts entities in Update and Render sees:

```csharp
public sealed class Spawner(World world, Commands commands)
{
    private readonly QueryDescription _all = new QueryDescription().WithAll<Health>();

    [Update]
    public void Spawn(GameTime dt)
    {
        commands.Create(new Transform2D(new Vector2(dt.Frame, 0)), new Health(1));
        Console.WriteLine($"update {world.CountEntities(in _all)}");   // frame 1: 0, frame 2: 1
    }

    [Update(Order = 100)]
    public void LaterInUpdate(GameTime dt) => Console.WriteLine($"later {world.CountEntities(in _all)}"); // 0, then 1

    [Render]
    public void Render(GameTime dt) => Console.WriteLine($"render {world.CountEntities(in _all)}");      // 1, then 2
}
```

Within one `Flush`, the order is fixed: Arch's buffer first (creates, sets, adds, removes, destroys), then the parents of
created entities, then models recorded with `SpawnModel`, then the other hierarchy commands (`SetParent` on existing
entities, `RemoveParent`), then `DestroyRecursive`.

A scene's world has its own `EcsCommandsSystem` inside the scene's schedule (added by `scene.UseEcs()`). The scene's
schedule runs at `StageOrder.Scenes` (-500) of the root stage, so scene commands are applied at the end of the scene's
part of each stage, before the root schedule's own steps at order 0.

### Placeholder entities

The entity returned by `commands.Create(...)` is a placeholder until playback. Use it only with the same `Commands`
before the flush: to `Add`, `Set`, `SetParent` or `SpawnModel` onto it. Do not store it or pass it to `World` methods;
after playback the real entity has a different id.

```csharp
public sealed class TurretSystem(World world, Commands commands)
{
    private Entity _base;

    [Init]
    public void Init(GameTime dt) => _base = world.Create(new Transform2D(new Vector2(200, 300)));

    [Update]
    public void SpawnTurret(GameTime dt)
    {
        var turret = commands.Create(new Transform2D(new Vector2(0, -16)), new EntityName("turret"));
        commands.Add(turret, new Health(10));   // fine: same Commands, before the flush
        commands.SetParent(turret, _base);      // the child may be a placeholder; the parent exists
        // To find the turret later, look it up by name after the stage: names.Find("turret").
    }
}
```

If you need the real entity immediately and you are not inside a query, create it on the `World` directly. The Breakout
ECS sample does that when it launches a ball, so the score counts it in the same frame.

## Choosing direct or deferred

| Situation | Use |
|---|---|
| Inside a `[Query]` method or a `world.Query` lambda | `Commands` (required) |
| A plain step that iterates nothing (Init, reacting to an event) | Either. `World` if you need the entity now. |
| Spawning from a physics collision handler | `Commands` or `World`, as long as you are not inside a query |
| Many entities at once, from many places | `Commands`: one playback per stage |
| Destroying a parent and its children | `commands.DestroyRecursive(entity)` or `world.DestroyRecursive(entity)` |

## Finding entities by name

`EntityName(string Value)` names an entity. `NameRegistry` (resolved per scope, like `World`) finds entities by name:

```csharp
public sealed class DoorSystem(NameRegistry names, World world)
{
    [Update]
    public void Open(GameTime dt)
    {
        if (names.TryFind("door", out var door)) world.Get<Transform2D>(door).Rotation = MathF.PI / 2;
        var player = names.Find("player");          // Entity.Null when missing
        string? name = names.NameOf(player);        // null when unnamed or dead
    }
}
```

The registry rebuilds its index when a lookup misses or finds a stale entry, so it needs no bookkeeping when names are
added, changed or removed. With duplicate names the first entity in query order wins. `Snapshot()` returns every named
entity. The [remote protocol](/Ion/tooling/remote-protocol/) addresses entities by id or by `EntityName`, so naming
the entities you want to inspect is worth it.

## Hierarchy helpers

`HierarchyExtensions` adds parent/child operations to `World` (all structural, so use the `Commands` equivalents inside a
query):

| Method | Does |
|---|---|
| `world.SetParent(child, parent)` | Parents `child`, moving it from a previous parent. Throws on cycles or dead entities. |
| `world.RemoveParent(child)` | Makes `child` a root. |
| `world.TryGetParent(entity, out parent)` | The parent, or false for a root. |
| `world.GetChildren(entity)` | A `ReadOnlySpan<Entity>` of children, valid until the hierarchy changes. |
| `world.DestroyRecursive(entity)` | Destroys an entity and all its descendants. |

See [Transforms](/Ion/ecs/transforms/) for what the hierarchy does to positions.

## Testing structural changes

`IonTestHost` steps frames deterministically, which makes command timing easy to assert:

```csharp title="DamageTests.cs"
using var host = new IonTestHost()
    .Configure(services => services.AddEcs().AddSingleton<DamageSystem>())
    .ConfigureApp(app => app.UseEcs().UseSystem<DamageSystem>());

var world = host.Get<World>();
var enemy = world.Create(new Health(0));

host.Step();

Assert.False(world.IsAlive(enemy));
```

See [Testing](/Ion/tooling/testing/).

## See also

- [Queries](/Ion/ecs/queries/)
- [Components and serialization](/Ion/ecs/components-and-serialization/)
- [Transforms](/Ion/ecs/transforms/)
- [Stages](/Ion/concepts/stages/) and [Stage order](/Ion/reference/stage-order/)
- [Diagnostics](/Ion/reference/diagnostics/)
