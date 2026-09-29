---
title: 3D physics
description: Register the BepuPhysics module, create 3D bodies, colliders, convex hulls and joints, and know what is not supported yet.
sidebar:
  order: 3
---

`Ion.Extensions.Physics3D` simulates your ECS entities with [BepuPhysics v2](https://github.com/bepu/bepuphysics2)
(2.5.0-beta.29), a pure managed engine: no native library to ship. It follows the same rules as the
[2D module](/Ion/physics/physics-2d/): a collider plus a transform is a body, components are the source of truth, and the
step runs in FixedUpdate at `StageOrder.Physics`.

## Register the module

```csharp title="Program.cs"
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Physics3D;

var builder = IonApplication.CreateBuilder(args);
builder.AddPhysics3D().AddEcsRendering3D().AddSystem<CrateSystem>();

using var game = builder.Build();
game.UsePhysics3D().UseEcsRendering3D().UseSystem<CrateSystem>();
game.Run();

public sealed class CrateSystem(World world, IRenderer3D renderer)
{
	[Init]
	public void Init(GameTime dt)
	{
		var cube = renderer.CreateMesh(MeshPrimitives.Cube(1f));
		var plane = renderer.CreateMesh(MeshPrimitives.Plane(40f));
		var wood = renderer.CreateMaterial(new PbrMaterial(new Color(0xB0, 0x80, 0x50), roughness: 0.8f));
		var grass = renderer.CreateMaterial(new PbrMaterial(new Color(0x60, 0x90, 0x50), roughness: 0.9f));

		world.Create(Transform.LookAt(new Vector3(8, 6, 14), new Vector3(0, 2, 0)),
			new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f });
		world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.5f, -1f, -0.3f), Vector3.UnitY)),
			new DirectionalLight(Color.White, intensity: 3f));

		// The ground: a visible plane, and a static box collider whose top face is at y = 0.
		world.Create(Transform.Identity, new MeshRenderer(plane, grass));
		world.Create(new Transform(new Vector3(0, -0.5f, 0)), Collider3D.Box(new Vector3(40, 1, 40)));

		// A stack of dynamic crates: mesh, collider and rigid body on the same entity.
		for (var i = 0; i < 8; i++)
		{
			world.Create(new Transform(new Vector3(0.1f * i, 0.5f + i * 1.05f, 0)),
				new MeshRenderer(cube, wood), Collider3D.Box(Vector3.One), RigidBody3D.Dynamic(mass: 10));
		}
	}
}
```

| Call | What it does |
|---|---|
| `builder.AddPhysics3D(configure)` | Registers the 3D renderer (`AddRendering3D`, with the engine core), the ECS module, the physics worlds, the systems and `Physics3DConfig` bound from `Ion:Physics3D`, then `configure`. |
| `services.AddPhysics3D(config, configure)` | The same on an `IServiceCollection` (registers `AddEcs` and `AddRendering3D`). |
| `game.UsePhysics3D()` | Adds `UseRendering3D` (with `UseIon`), `UseEcs`, the physics step and the debug drawing to the root schedule. |
| `scene.UsePhysics3D()` | Adds `UseEcs` and both physics systems to a scene's schedule, for the scene's own world. |
| `Physics3DComponents.Register()` | Registers the components with Arch for NativeAOT; called for you. |

The 3D module pulls in the 3D renderer because its debug drawing submits meshes to it. The physics itself does not render
anything: to see bodies, give the same entities a `MeshRenderer` and add the ECS 3D extraction (`AddEcsRendering3D`), as
above. See [ECS rendering](/Ion/ecs/ecs-rendering/) and [meshes and materials](/Ion/rendering/3d/meshes-and-materials/).

## Bodies: `RigidBody3D`

| Type | Moved by | Notes |
|---|---|---|
| `RigidBodyType3D.Static` | nothing; moving its `Transform` teleports it | A Bepu static. |
| `RigidBodyType3D.Kinematic` | your code: driven to its `Transform` every fixed step | Infinite mass; pushes dynamic bodies; not affected by contacts or forces. |
| `RigidBodyType3D.Dynamic` | the simulation | Gravity, contacts, joints and impulses. |

| Field | Type | Default (from the factories) | Meaning |
|---|---|---|---|
| `Type` | `RigidBodyType3D` | set by the factory | Changing it rebuilds the body at the next step. |
| `LinearVelocity` | `Vector3` | zero | World units per second; written back after every step. |
| `AngularVelocity` | `Vector3` | zero | Radians per second around each axis; written back after every step. |
| `Mass` | `float` | 1 | The mass; the inertia comes from it and the collider's shape. A dynamic body needs a non-negative mass, and 0 is treated as 1. |
| `LinearDamping`, `AngularDamping` | `float` | 0 | Applied per body in the integrator as `v *= 1 / (1 + dt * damping)`, Box2D's form. |
| `GravityScale` | `float` | 1 | Multiplier of the world gravity. |
| `FixedRotation` | `bool` | false | Infinite inertia: the body never rotates. |
| `IsBullet` | `bool` | false | Continuous collision detection for small fast bodies. Changing it rebuilds the body. |

Factories: `RigidBody3D.Dynamic(float mass = 1f, Vector3 velocity = default)`, `RigidBody3D.Kinematic()`,
`RigidBody3D.Static()`, and `new RigidBody3D(RigidBodyType3D type)`, which sets `Mass` and `GravityScale` to 1.

There is no density in 3D: the mass comes from `RigidBody3D.Mass` alone.

### Reading and writing state

The step writes the position and rotation of every active (awake) body into its `Transform` and its velocities into
`RigidBody3D`. Writing them from game code works like in 2D:

| You write | At the next step |
|---|---|
| `Transform.Position` or `Rotation` of a static or dynamic body | Teleported there (and woken). |
| `Transform.Position` or `Rotation` of a kinematic body | Driven there: its linear velocity becomes (target - position) / dt and its angular velocity the rotation difference (to first order); when the transform stops changing, the velocities are zeroed. |
| `LinearVelocity` / `AngularVelocity` | Set on the body (and woken). |
| `Mass` / `FixedRotation` | The inertia is recomputed. |
| `Type`, `IsBullet`, or any `Collider3D` field | The body is destroyed and created again (its contacts end with `End` events). |

:::note
`Transform.Scale` scales the mesh you render but is never applied to the collider. A `MeshRenderer` of a unit cube with a
scale of 2 needs `Collider3D.Box(new Vector3(2))`.
:::

## Colliders: `Collider3D`

A collider is centered on the entity's `Transform` position and rotation. Unlike 2D, there is no `Offset` or `Angle`.

| Factory | Shape |
|---|---|
| `Collider3D.Box(Vector3 size)` | A box of full width, height and depth `size`. |
| `Collider3D.Sphere(float radius)` | A sphere. |
| `Collider3D.Capsule(float radius, float length)` | Along local y: a cylinder part of `length` capped by half spheres of `radius` (total height `length + 2 * radius`). |
| `Collider3D.Cylinder(float radius, float length)` | Along local y. |
| `Collider3D.ConvexHull(ConvexHullId hull)` | A convex hull registered with `IPhysicsWorld3D.CreateConvexHull`. |

| Field | Default | Meaning |
|---|---|---|
| `Friction` | 0.6 | Friction coefficient. A pair uses the geometric mean of the two. |
| `Restitution` | 0 | Bounciness from 0 to 1, approximated (see below). A pair uses the larger of the two. |
| `IsSensor` | false | Detects overlaps (`Trigger3D`) without colliding. |
| `Layer` | 1 (bit 0) | The layers this collider belongs to. |
| `Mask` | `uint.MaxValue` | The layers it collides with. |
| `EnableEvents` | true | Whether contacts raise `Collision3D` events and it can enter sensors. |
| `HasBody` | (read only) | Whether the body exists yet. |

Two colliders collide when each one's `Layer` intersects the other's `Mask`. Kinematic and static bodies never collide
with each other (sensors aside: a sensor on a kinematic body still sees other kinematic and static bodies).

Validation throws an `InvalidOperationException` naming the entity from the physics step: box sizes and radii must be
positive, capsule and cylinder lengths non-negative, and a hull must come from this world's `CreateConvexHull`.

### Convex hulls

Register a hull once (at load time) from a point cloud, for example a mesh's vertex positions, and share its id between
colliders:

```csharp
public sealed class RockSystem(World world, IPhysicsWorld3D physics, IRenderer3D renderer)
{
	[Init]
	public void Init(GameTime dt)
	{
		var mesh = MeshPrimitives.Sphere(0.5f, segments: 8, rings: 4);   // a low-poly rock
		var hull = physics.CreateConvexHull(mesh.Positions);             // at least 4 points
		var rendered = renderer.CreateMesh(mesh);
		var stone = renderer.CreateMaterial(new PbrMaterial(new Color(0x80, 0x80, 0x88)));

		for (var i = 0; i < 20; i++)
		{
			world.Create(new Transform(new Vector3(i % 5, 3 + i / 5, 0)), new MeshRenderer(rendered, stone),
				Collider3D.ConvexHull(hull), RigidBody3D.Dynamic());
		}
	}
}
```

The hull lives as long as its physics world. A scene has its own physics world, so a scene registers its own hulls. The
entity's origin stays the mesh's origin: the module offsets the body by the hull's center for you.

### Restitution is approximated

BepuPhysics has no restitution coefficient. A pair with `Restitution > 0` gets a softer, less damped contact spring
(30 Hz with a damping ratio of `1 - restitution`) and an unlimited recovery speed, which bounces approximately. Pairs
without restitution use a 30 Hz, critically damped spring and a maximum recovery speed of 2 units per second. Do not
expect a restitution of 1 to conserve energy exactly; if you need exact bounces, set the velocity yourself in a collision
handler.

## Joints: `Joint3D`

Joints connect two bodies that both have a `Collider3D` **and a `RigidBody3D`**: Bepu constrains bodies only, so to
anchor a joint to the world, use a kinematic body. Put the component on any entity; it exists while the component and
both bodies do, and changing a field recreates it. Anchors and the axis are in each body's local space.

| Type | Factory | Behavior |
|---|---|---|
| `BallSocket` | `Joint3D.BallSocket(bodyA, bodyB, localAnchorA, localAnchorB)` | The anchors stay together; free rotation. |
| `Hinge` | `Joint3D.Hinge(bodyA, bodyB, axis, localAnchorA, localAnchorB)` | The anchors stay together; rotation only around `Axis` (body A's local space; body B's axis is fixed at creation). |
| `Weld` | `Joint3D.Weld(bodyA, bodyB)` | Keeps the bodies' relative position and orientation from when the joint is created. |
| `Distance` | `Joint3D.Distance(bodyA, bodyB, minDistance, maxDistance, localAnchorA, localAnchorB)` | The anchors stay between `MinDistance` and `MaxDistance` apart (a rope or a rod). A `MaxDistance` of 0 uses the distance at creation. |

| Field | Default | Meaning |
|---|---|---|
| `Axis` | `Vector3.UnitY` | The hinge axis. |
| `MinDistance`, `MaxDistance` | 0, 0 | The distance range. |
| `Frequency` | 30 | Constraint stiffness in hertz (rigid for a 60 Hz step). |
| `DampingRatio` | 1 | Constraint damping ratio. |
| `IsCreated` | (read only) | Whether the constraint exists. |

```csharp title="A pendulum and a door"
var anchor = world.Create(new Transform(new Vector3(0, 10, 0)), Collider3D.Sphere(0.1f) with { Mask = 0 }, RigidBody3D.Kinematic());
var bob = world.Create(new Transform(new Vector3(2, 10, 0)), Collider3D.Sphere(0.2f) with { Mask = 0 }, RigidBody3D.Dynamic());
world.Create(Joint3D.BallSocket(anchor, bob, localAnchorB: new Vector3(-2, 0, 0)));

var frame = world.Create(new Transform(new Vector3(0, 10, -5)), Collider3D.Sphere(0.1f) with { Mask = 0 }, RigidBody3D.Kinematic());
var door = world.Create(new Transform(new Vector3(1, 10, -5)), Collider3D.Box(new Vector3(2, 2, 0.1f)), RigidBody3D.Dynamic());
world.Create(Joint3D.Hinge(frame, door, Vector3.UnitY, localAnchorB: new Vector3(-1, 0, 0)));
```

3D joints have no motors and no angle limits yet.

## Impulses and the world: `IPhysicsWorld3D`

| Member | Meaning |
|---|---|
| `Gravity` | Get or set the gravity in world units per second squared. |
| `BodyCount` (static included), `JointCount`, `StepCount` | Counters. |
| `DebugDraw` | Turns the [debug drawing](/Ion/physics/debug-draw/) on or off. |
| `CreateConvexHull(ReadOnlySpan<Vector3>)` | Registers a hull. |
| `RayCast`, `OverlapBox`, `OverlapSphere` | [Queries](/Ion/physics/queries-and-events/). |
| `ApplyLinearImpulse(Entity, Vector3)` | Changes a dynamic body's velocity immediately (and wakes it). |
| `ApplyAngularImpulse(Entity, Vector3)` | Changes a dynamic body's angular velocity immediately. |
| `ComputeStateHash()` | A hash of every body's pose and velocity bits. |

The impulse methods return `false` when the entity has no **dynamic** body yet. There is no continuous force or torque
method in 3D: apply an impulse every fixed step (`force * dt`) or change the velocity.

```csharp
[FixedUpdate]
public void Kick(GameTime dt)
{
	if (input.Pressed(Key.Space)) physics.ApplyLinearImpulse(_ball, new Vector3(0, 5, -8));
}
```

`PhysicsWorld3D` adds `Entities`, `Config`, `AwakeBodyCount`, `Step(float dt)` and `Simulation`: the BepuPhysics
`Simulation` itself, for what the adapter does not cover. Do not add or remove what the adapter owns through it.

## Configuration: `Physics3DConfig`

Bound from `Ion:Physics3D`, then adjusted by `AddPhysics3D(configure)`.

| Key | Default | Meaning |
|---|---|---|
| `GravityX` | 0 | Gravity along x. |
| `GravityY` | -9.81 | Gravity along y (y is up). |
| `GravityZ` | 0 | Gravity along z. |
| `SubSteps` | 1 | Bepu solver sub-steps per fixed step. At least 1. |
| `Iterations` | 8 | Velocity iterations per sub-step. At least 1. |
| `ThreadCount` | 0 | 0 or 1 steps on the game thread. More creates a Bepu `ThreadDispatcher` with that many workers; the step still waits for them. |
| `SleepThreshold` | 0.01 | The speed under which a body may sleep; 0 disables sleeping. |
| `DebugDraw` | false | Starts with the debug drawing on. |

```bash
dotnet run -- --Ion:Physics3D:ThreadCount=4 --Ion:Physics3D:DebugDraw=true
```

A multithreaded step runs Bepu in its deterministic mode and sorts the contact events, so it is reproducible, but its
results differ from a single-threaded run. See [determinism](/Ion/physics/determinism/).

## What is supported, and the known gaps

| Feature | Status |
|---|---|
| Box, sphere, capsule, cylinder, convex hull colliders | Supported |
| Static, kinematic and dynamic bodies, per-body gravity scale and damping | Supported |
| Sensors, layers and masks, collision and trigger events | Supported |
| Ball socket, hinge, weld and distance joints | Supported |
| Ray casts | Supported |
| Box and sphere overlaps | Bounding-box based, not exact |
| Linear and angular impulses | Supported; no continuous forces |
| Exact restitution | Approximated by the contact spring |
| **Compound colliders** (several shapes on one body, or child entities) | Not supported yet: one collider per entity |
| **Triangle mesh colliders** (static level geometry from a mesh) | Not supported yet: use convex hulls or boxes |
| Collider offsets inside the entity | Not supported (hull centers aside) |
| Joint motors and limits | Not supported yet |
| Shape casts, pre-solve contact callbacks | Not supported yet |

:::tip[Level geometry without mesh colliders]
Until triangle mesh colliders exist, build static level geometry from several static entities with box colliders, or
split concave props into convex pieces, each a `Collider3D.ConvexHull` on its own entity.
:::

## Allocations

The adapter allocates nothing per step once its tables have grown. BepuPhysics itself occasionally grows a small internal
array (an `int[16]` or `int[32]`) when one of its lists reaches a new high-water mark; the test bounds it to 1 KB per 120
steps on a settling pile.

## See also

- [Physics overview](/Ion/physics/overview/).
- [2D physics](/Ion/physics/physics-2d/).
- [Queries and events](/Ion/physics/queries-and-events/).
- [Debug drawing](/Ion/physics/debug-draw/).
- [Determinism](/Ion/physics/determinism/): vector width and thread count.
- [3D rendering overview](/Ion/rendering/3d/overview/).
- Source: [Ion.Extensions.Physics3D](https://github.com/jimbuck/Ion/tree/main/Ion/Ion.Extensions.Physics3D).
