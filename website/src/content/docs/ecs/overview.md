---
title: ECS overview
description: How Ion's entity component system works, built on Arch 2.1, with a world per scope, generated queries, deferred commands and built-in transform, sprite and 3D components.
sidebar:
  order: 1
---

Ion's ECS module (`Ion.Extensions.Ecs`) gives your game a data-oriented world of entities and components that lives
inside Ion's normal schedule. You keep writing systems as plain classes with stage attributes; the ECS adds a `World` you
can inject, `[Query]` methods the source generator turns into tight chunk loops, a `Commands` buffer for structural
changes, and a set of built-in components (2D and 3D transforms with a parent/child hierarchy, sprites, cameras, lights,
names) that the render extraction draws for you.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering().AddSystem<BallSystem>();

using var game = builder.Build();
game.UseEcsRendering().UseSystem<BallSystem>();
game.Run();
```

`AddEcsRendering()` registers the engine core, the ECS module and the 2D sprite extraction; `UseEcsRendering()` adds
their systems to the schedule. Modules pull in what they depend on, and the `Add*`/`Use*` calls are idempotent, so
listing `AddIon().AddEcs()` as well changes nothing.

## Arch under the hood

The module is built on [Arch](https://github.com/genaray/Arch) 2.1, picked by measurement against Friflo.Engine.ECS 3.6
(Arch created entities 1.7x faster with 2.7x less garbage, and iteration was a tie at about 8 ns per entity). Ion does
not wrap Arch: you use Arch's own `World`, `Entity`, `QueryDescription` and `World.Query` directly, and Ion adds what an
engine needs around them.

| Ion adds | What it is | Page |
|---|---|---|
| A world per scope | The root `World` for the root schedule, and one `World` per loaded scene, disposed with it | This page |
| `Commands` | Arch's `CommandBuffer` plus hierarchy and model commands, played back at the end of every stage | [Entities and commands](/Ion/ecs/entities-and-commands/) |
| `[Query]` | Methods expanded at compile time into chunk loops with no delegates, boxing or allocations | [Queries](/Ion/ecs/queries/) |
| Built-in components | `Transform2D`, `Transform`, `Parent`/`Children`, `Sprite`, `SpriteAnimation`, `EntityName`, tags | [Components and serialization](/Ion/ecs/components-and-serialization/) |
| Transform propagation | World transforms computed down the hierarchy, with dirty tracking | [Transforms](/Ion/ecs/transforms/) |
| Render extraction | Sprites, meshes, cameras and lights copied from the world into the renderers every frame | [ECS rendering](/Ion/ecs/ecs-rendering/) |
| World serialization | JSON and binary snapshots, also the component set the remote protocol can read and write | [Components and serialization](/Ion/ecs/components-and-serialization/) |
| `FrameStats.Entities` | The live entity count of every world, in the frame log, overlay and test results | [Metrics and tracing](/Ion/tooling/metrics-and-tracing/) |

:::note[Why not Arch.Persistence or Arch.AOT.SourceGenerator]
`Arch.Persistence` 2.0.0 fails to load against Arch 2.1 and depends on packages that emit IL at run time, so Ion ships
its own serializers. `Arch.AOT.SourceGenerator` targets Arch 1.x, so Ion's generator registers query components with Arch
itself. The result publishes with NativeAOT with no warnings from Ion or Arch (see [Native AOT](/Ion/platforms/native-aot/)).
:::

## Registering the module

| Call | On | What it does |
|---|---|---|
| `builder.AddEcs()` | `IonApplicationBuilder` | Registers `EcsWorlds`, `World`, `Commands` and `NameRegistry` (per scope), the built-in systems, the entity counter and the ECS remote methods. |
| `services.AddEcs()` | `IServiceCollection` | The same, for code that composes services directly. |
| `game.UseEcs()` | `IIonApplication` | Adds `EcsCommandsSystem`, `TransformPropagationSystem` and `SpriteAnimationSystem` to the root schedule. |
| `scene.UseEcs()` | `ISceneBuilder` | Adds the same three systems to a scene's schedule, bound to the scene's world. |
| `builder.AddEcsSerialization(...)` | `IonApplicationBuilder` | Adds the ECS module plus the world serializers and the component registry. |
| `builder.AddEcsRendering(...)` / `game.UseEcsRendering()` | | The 2D sprite extraction, plus the engine and the ECS module. |
| `builder.AddEcsRendering3D(...)` / `game.UseEcsRendering3D()` | | The 3D extraction, plus the 3D renderer, the engine and the ECS module. |

The ECS module needs no other module. The modules that simulate or draw entities register it themselves:
`AddEcsRendering`, `AddEcsRendering3D`, `AddPhysics2D`, `AddPhysics3D` and `AddNetworking` all call `AddEcs()` for you.

:::caution[Add is not Use]
`AddEcs()` only registers services. Without `UseEcs()` (or a `Use*` call that includes it, such as `UseEcsRendering()`)
nothing plays back your `Commands` and no global transforms are computed. If entities you create with `Commands` never
appear, check that `UseEcs()` is in the schedule.
:::

## Getting the world

Inject `World` (from `Arch.Core`) into any system's constructor. Which world you get depends on where the system runs:

| Resolved from | `World` you get |
|---|---|
| The root service provider (systems in the root schedule, singletons) | The root world, created on first use |
| A scene's service scope (systems added with `scene.UseSystem<T>()`, registered scoped or transient) | That scene's own world, created on first use and disposed when the scene unloads |

`Commands` and `NameRegistry` follow the same rule, so a system always gets the commands and names of its own world.
`World` is registered as a transient whose factory returns the scope's world (not as a scoped service), because the root
schedule needs a world too and a scoped service in the root schedule is error ION006.

```csharp title="BallSystem.cs"
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public record struct Velocity(float X, float Y);

public sealed partial class BallSystem(World world, IWindow window)
{
    [Init]
    public void Spawn(GameTime dt)
    {
        // Outside a query, creating entities directly is fine.
        for (var i = 0; i < 8; i++)
        {
            world.Create(new EntityName($"Ball{i}"), new Transform2D(window.Size / 2), new Velocity(100 + i * 20, 80));
        }
    }

    // Runs once per entity that has a Transform2D and a Velocity.
    [FixedUpdate, Query]
    private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt)
        => transform.Position += new Vector2(velocity.X, velocity.Y) * dt;
}
```

Outside systems, `EcsWorlds` (a singleton) lists every live world: `Root` is the root world, `Worlds` the root one plus
the worlds of the live scene scopes in creation order, and `EntityCount` their total. Tests use it to inspect state:

```csharp title="GameTests.cs"
using var run = IonTestHost.RunEntryPoint<Program>(600);
var world = run.Get<EcsWorlds>().Root;
Assert.Equal(8, world.CountEntities(new QueryDescription().WithAll<Velocity>()));
```

## How the ECS fits with systems

There is no separate ECS scheduler. ECS systems are ordinary Ion systems: constructor injection, stage attributes
(`[Init]`, `[First]`, `[FixedUpdate]`, `[Update]`, `[Render]`, `[Last]`, `[Destroy]`), `Order`, `[Before<T>]` and
`[After<T>]` all work the same (see [Systems](/Ion/concepts/systems/) and [Stages](/Ion/concepts/stages/)). A `[Query]`
method is a step like any other: it is placed in its stage by its attributes, shows up in `--Ion:PrintSchedule=true` under
its own name, and is called directly by the generated schedule.

A single system can mix plain steps and query steps, and a query step can take services from the system's constructor:

```csharp
public record struct Ball;

public sealed partial class BallCountSystem(World world)
{
    private readonly QueryDescription _balls = new QueryDescription().WithAll<Ball>();
    private int _ballCount;

    public int BallCount => _ballCount;

    [Update]
    public void Count(GameTime dt) => _ballCount = world.CountEntities(in _balls);

    [Update(Order = 10), Query, All<Ball>]
    private void ClampToScreen(ref Transform2D transform) =>
        transform.Position = Vector2.Clamp(transform.Position, Vector2.Zero, new Vector2(1280, 720));
}
```

Legacy middleware steps (`game.UseUpdate(next => dt => { ...; next(dt); })`) still run and can use the world through
closures, but they produce warning ION010; prefer systems or function steps.

## Stage order

The ECS module's steps sit at fixed orders (constants on `StageOrder`), so they interleave with your steps the same way
whatever order you register modules in. Your own steps default to order `0`.

| Order | Constant | Stage(s) | ECS step |
|---|---|---|---|
| -700 | `StageOrder.Physics` | FixedUpdate | Physics pushes changed transforms, steps, pulls results (when a physics module is used) |
| -500 | `StageOrder.Scenes` | every stage | The active scene's whole schedule, including its own ECS steps |
| -400 | `StageOrder.TransformPropagation` | Last, Render | `TransformPropagationSystem`: global transforms (and sprite bounds in Render) |
| -300 | `StageOrder.Extract` | Render | `SpriteExtractionSystem`, `Scene3DExtractionSystem` |
| 0 | `StageOrder.Default` | any | Your steps |
| 400 | `StageOrder.SpriteAnimation` | Update | `SpriteAnimationSystem` advances flip-books |
| 950 | `StageOrder.Ecs` | every stage | `EcsCommandsSystem` plays back `Commands` |
| 970 | `StageOrder.Remote` | Last | Remote protocol requests (see the world after the frame's commands) |
| 1000 | `StageOrder.Events` | Last | Event buffers stepped |

Two consequences worth remembering:

- Structural changes recorded with `Commands` in a stage are visible from the next stage on (commands recorded in
  Update are applied before Render). See [Entities and commands](/Ion/ecs/entities-and-commands/).
- The transform propagation runs in Render before the extraction, so a frame draws what its own Update did, and again in
  Last so the next frame's First and FixedUpdate read fresh world transforms. See [Transforms](/Ion/ecs/transforms/).

The full engine table is on [Stage order](/Ion/reference/stage-order/).

## ECS or plain services?

The ECS is optional. Many Ion games (the plain Breakout sample, the menu sample) keep their state in services and
systems and draw with the sprite batch directly. Use the ECS when its strengths matter:

| Use the ECS when | Use plain services and systems when |
|---|---|
| You have many similar things (bullets, particles, blocks, enemies, tiles) processed the same way every frame | You have a handful of unique objects (the player, a score, a menu state) |
| You want the built-in hierarchy, sprite extraction, 3D extraction or physics components | You draw with the sprite batch or `IRenderer3D` directly |
| You want remote inspection (`world.query`) and JSON world snapshots in tests | State is naturally a few fields on a singleton |
| You want per-scene worlds that are thrown away when a scene unloads | State must survive every scene change |
| You use the physics or multiplayer modules, which simulate and replicate entities | You do not need them |

Mixing is normal: a game can keep its score in a singleton service, its balls and blocks in the world, and its menus in
[the UI module](/Ion/interaction/ui/overview/). The Breakout ECS sample does exactly that (see
[Breakout ECS](/Ion/examples/breakout-ecs/)).

:::tip[Start from the template]
`ion new ecs` (or `dotnet new ion-ecs`) scaffolds a game with the ECS module, a `[Query]` system, serialized components
and headless snapshot tests. See [Templates](/Ion/getting-started/templates/).
:::

## Planned

These are on the roadmap and not built yet: parallel queries (Arch's job-scheduled chunk queries are not exposed through
`[Query]`), tilemaps, serialization of the 3D components (they hold renderer handles) and inherited visibility (a `Hidden`
parent does not hide its children; see [ECS rendering](/Ion/ecs/ecs-rendering/)).

## See also

- [Entities and commands](/Ion/ecs/entities-and-commands/)
- [Queries](/Ion/ecs/queries/)
- [Scenes](/Ion/ecs/scenes/)
- [Systems](/Ion/concepts/systems/) and [Stages](/Ion/concepts/stages/)
- [Breakout ECS example](/Ion/examples/breakout-ecs/)
- [Module map](/Ion/reference/module-map/)
