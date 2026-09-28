---
title: Physics overview
description: How Ion's 2D (Box2D v3) and 3D (BepuPhysics v2) physics modules plug into the ECS, when they step, and which one to pick.
sidebar:
  order: 1
---

Ion ships two physics modules with the same design, one per dimension. Each is an ECS adapter around a proven physics
engine: you describe bodies with components on your entities, and a system in the fixed step keeps the engine's world in
step with them.

| | 2D | 3D |
|---|---|---|
| Package | `Ion.Extensions.Physics2D` | `Ion.Extensions.Physics3D` |
| Engine | Box2D v3.1 (the C library, through `Box2D.NET.Bindings.Release` 3.1.0) | BepuPhysics 2.5.0-beta.29 (pure managed) |
| Registration | `builder.AddPhysics2D()` / `game.UsePhysics2D()` | `builder.AddPhysics3D()` / `game.UsePhysics3D()` |
| Transform | `Transform2D` (from `Ion.Extensions.Ecs`) | `Transform` (from `Ion.Extensions.Graphics`) |
| Components | `RigidBody2D`, `Collider2D`, `Joint2D` | `RigidBody3D`, `Collider3D`, `Joint3D` |
| World service | `IPhysicsWorld2D` / `PhysicsWorld2D` | `IPhysicsWorld3D` / `PhysicsWorld3D` |
| Events | `Collision2D`, `Trigger2D` | `Collision3D`, `Trigger3D` |
| Configuration section | `Ion:Physics2D` | `Ion:Physics3D` |
| Debug drawing | lines through the sprite batch | translucent meshes through the 3D renderer |
| Threads | single-threaded | single-threaded by default, optional worker threads |

Game code never sees a Box2D id or a Bepu handle. A game library can depend on the `*.Abstractions` packages only
(components, events, the world interface and the config class), and the host game references the module itself.

## A minimal 2D game with physics

```csharp title="Program.cs"
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Physics2D;

var builder = IonApplication.CreateBuilder(args);
builder.AddPhysics2D(physics =>
{
	physics.UnitsPerMeter = 64;  // a 64 pixel object is one meter to the solver
	physics.GravityY = 981;      // world units (pixels) per second squared; y points down on screen
	physics.DebugDraw = true;    // draw every collider, so there is something to see without sprites
}).AddSystem<DropSystem>();

using var game = builder.Build();
game.UsePhysics2D().UseSystem<DropSystem>();
game.Run();

public sealed class DropSystem(World world)
{
	[Init]
	public void Init(GameTime dt)
	{
		// A collider without a rigid body is a static body: the floor.
		world.Create(new Transform2D(new Vector2(400, 560)), Collider2D.Box(new Vector2(800, 32)));

		// A collider with a dynamic rigid body falls, bounces and comes to rest.
		world.Create(new Transform2D(new Vector2(400, 100)),
			Collider2D.Circle(16) with { Restitution = 0.6f },
			RigidBody2D.Dynamic());
	}
}
```

`AddPhysics2D` registers what it depends on (the engine core with `AddIon` and the ECS module with `AddEcs`), and
`UsePhysics2D` adds the systems it needs (`UseIon`, `UseEcs`) before its own. Every `Add*` and `Use*` call is idempotent,
so calling `AddEcsRendering()` or `UseEcs()` again elsewhere is harmless.

The 3D module works the same way; see [3D physics](/Ion/physics/physics-3d/).

## How the modules plug into the ECS

Three rules describe the whole adapter:

1. **A collider makes a body.** Every entity with a collider and a transform (`Collider2D` plus `Transform2D`, or
   `Collider3D` plus `Transform`) is a physics body. Without a rigid body component it is static. There is one collider
   per entity.
2. **Components are the source of truth.** You change components; the next physics step applies the change. Moving the
   transform of a static or dynamic body teleports it, moving a kinematic body's transform drives it there, writing a
   velocity sets it, and changing a collider rebuilds its shape.
3. **The step writes back only what the simulation owns.** After the step the module writes the position and rotation
   of the bodies that moved into their transform (the scale is left alone) and their velocities into the rigid body.

In one physics step the module does, in order:

1. **Push.** One chunk loop over the entities with a collider and a transform. New colliders create bodies; for the
   others, the step compares the components with what it last synchronized and applies only what changed.
2. **Remove.** Bodies whose entity was not seen (destroyed, or lost its collider or transform) are destroyed with their
   joints.
3. **Joints.** Joint components are created, recreated when their definition or a body changed, or destroyed.
4. **Simulate** one fixed step of the engine.
5. **Pull.** The bodies that moved get their transform and velocities written back.
6. **Events.** `Collision2D`/`Trigger2D` (or the 3D ones) are emitted on `IEvents`.

Removing an entity's collider, removing its transform or destroying the entity removes its body (and its joints) at the
next step. The contacts it had end, with `End` events.

:::caution[Create components with their constructors]
`default(RigidBody2D)` has a gravity scale of 0 and `default(RigidBody3D)` a mass and gravity scale of 0, just as
`default(Transform2D)` has a zero scale. Always use the factories (`RigidBody2D.Dynamic()`, `Collider2D.Box(...)`,
`RigidBody3D.Dynamic()`, `Collider3D.Sphere(...)`) or the constructors, then adjust with `with { ... }`.
:::

:::caution[Put bodies on root entities]
The physics step reads and writes the entity's `Transform2D` or `Transform` directly and treats it as a world transform.
A transform is relative to its [parent](/Ion/ecs/transforms/), so a body on a child entity is simulated in the wrong
place. Compound bodies made of child entities are not supported yet.
:::

## When physics runs: the fixed step

The physics step runs in the **FixedUpdate** stage at `StageOrder.Physics` (-700). That order sits in the engine setup
band: before the active scene's steps (`StageOrder.Scenes`, -500) and before your own fixed steps (order 0 by default).

| Order | Step | Stage |
|---:|---|---|
| -700 | `Physics2DSystem.Step` / `Physics3DSystem.Step` | FixedUpdate |
| -500 | the active scene's schedule (its own physics step runs at -700 inside it) | every stage |
| 0 | your `[FixedUpdate]` steps | FixedUpdate |
| 650 | `Physics2DDebugDrawSystem.Draw` / `Physics3DDebugDrawSystem.Draw` (`StageOrder.PhysicsDebugDraw`) | Render |
| 700 | the UI (`StageOrder.Ui`) | Render |

So in every fixed step the simulation advances first, and your fixed steps see its results; whatever they change is
simulated by the next fixed step. This is the order Unity and Bevy use. If you need a change to be simulated in the
**same** fixed step (for example a networked paddle driven by prediction), run that step before physics:

```csharp
[FixedUpdate(Order = StageOrder.Physics - 10)]
public void DrivePaddles(GameTime dt)
{
	// Runs just before the physics step: this transform is simulated in this fixed step.
}
```

The step always uses the fixed delta (`GameTime.Delta` in FixedUpdate, `1 / Ion:FixedUpdateRate`, 60 Hz by default),
never the frame's wall-clock time. A frame can run zero, one or several fixed steps, so physics speed does not depend on
the frame rate. See [the game loop](/Ion/concepts/game-loop/) and [time and determinism](/Ion/concepts/time-and-determinism/).

:::tip
Read input in `Update` and apply it to components there, or read the fixed-step input view in `FixedUpdate`. Either way
the next physics step picks the new values up. See [stages](/Ion/concepts/stages/) for what each stage is for.
:::

## Worlds and scopes

Each physics world belongs to one ECS `World`, resolved per service scope exactly like the ECS world:

- The root service provider gets the **root physics world**, which simulates the root ECS world and is stepped by the
  root schedule (`game.UsePhysics2D()`).
- Each scene scope gets **its own physics world**, created on first use and disposed with the scene. Add the systems to
  the scene's schedule with `scene.UsePhysics2D()` (or `scene.UsePhysics3D()`).

```csharp
game.UseScene(Level.One, scene => scene.UsePhysics2D().UseSystem<LevelOneSystem>());
```

`IPhysicsWorld2D` and `PhysicsWorld2D` resolve to the same instance in a scope. The physics systems are transients, so the
root schedule and every scene get their own instances bound to their own world. See [scenes](/Ion/ecs/scenes/).

## Units

Positions, sizes, velocities and gravity are in **world units**, whatever your transform uses (pixels in most 2D games,
meters in most 3D games).

- **2D:** Box2D's tolerances are tuned for objects of 0.1 to 10 meters. `Physics2DConfig.UnitsPerMeter` tells the module
  how many world units make one meter, and it divides every length by it before handing it to Box2D. A pixel game sets
  it to the size in pixels of a one meter object (the Breakout sample uses 64). Gravity stays in world units, so a pixel
  game also sets `GravityY` in pixels per second squared.
- **3D:** there is no scale setting; use meters (the default gravity is -9.81 along y).

## Choosing between the modules

| You need | Use |
|---|---|
| A 2D game (platformer, top-down, Breakout) | `Ion.Extensions.Physics2D` |
| Joint motors, limits, prismatic sliders | `Ion.Extensions.Physics2D` (3D joints have no motors or limits yet) |
| Exact restitution (bounciness) | `Ion.Extensions.Physics2D` (3D approximates it) |
| A 3D game with boxes, spheres, capsules, cylinders and convex hulls | `Ion.Extensions.Physics3D` |
| No native library at all | `Ion.Extensions.Physics3D` (BepuPhysics is managed) |
| Multithreaded stepping | `Ion.Extensions.Physics3D` (`Ion:Physics3D:ThreadCount`) |

Both modules can be registered in the same game; they are independent.

## Performance

Neither adapter allocates managed memory per step once its tables have grown to fit the scene. Box2D was picked for 2D
by a 10,000 body benchmark on x64 and arm64, where it was about 5 times faster than the managed Box2D port and 23 times
faster than Aether.Physics2D. Measured module costs (JIT, one thread):

| Benchmark | Mean |
|---|---:|
| 2D step, 1,000 bodies in a pile, with the ECS sync | 0.77 ms |
| 2D step, 10,000 bodies | 14.3 ms |
| 3D step, 1,000 bodies | 1.73 ms |
| ECS push and pull overhead per moved body (2D / 3D) | about 68 ns / 40 ns |

The full numbers are in
[the physics benchmark report](https://github.com/jimbuck/Ion/blob/main/docs/plans/benchmarks/2026-09-physics2d/README.md);
see [benchmarks](/Ion/tooling/benchmarks/) to run them.

## Not done yet

These are planned, not built: compound colliders (several shapes per body through child entities), chain and segment
shapes in 2D, triangle mesh colliders in 3D, motors and limits on 3D joints, shape casts, per-contact callbacks
(pre-solve), exact 3D overlap tests, and contraction-free Box2D natives built on CI for every platform.

## See also

- [2D physics](/Ion/physics/physics-2d/): bodies, colliders, joints, forces and every `Physics2DConfig` option.
- [3D physics](/Ion/physics/physics-3d/): the BepuPhysics module and its gaps.
- [Queries and events](/Ion/physics/queries-and-events/): ray casts, overlaps, collisions and triggers.
- [Debug drawing](/Ion/physics/debug-draw/): seeing the colliders.
- [Determinism](/Ion/physics/determinism/): replays, state hashes and cross-platform limits.
- [Breakout ECS example](/Ion/examples/breakout-ecs/): a complete game on the 2D module.
- [Stage order reference](/Ion/reference/stage-order/).
- Design document: [docs/design/ion-physics.md](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-physics.md).
