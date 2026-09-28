---
title: 2D physics
description: Register the Box2D v3 module, create bodies, colliders and joints, apply forces and tune Physics2DConfig.
sidebar:
  order: 2
---

`Ion.Extensions.Physics2D` simulates your ECS entities with [Box2D v3.1](https://box2d.org/), the C library, through
`Box2D.NET.Bindings.Release` 3.1.0. The package brings prebuilt natives for Windows, macOS, Linux, Android and iOS on
x64 and arm64, plus static libraries for NativeAOT.

## Register the module

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Physics2D;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering()
	.AddPhysics2D(physics =>
	{
		physics.GravityY = 0;          // top-down: no gravity
		physics.UnitsPerMeter = 64;    // 64 pixels make a meter
	})
	.AddSystem<PlayerSystem>();

using var game = builder.Build();
game.UseEcsRendering()
	.UsePhysics2D()
	.UseSystem<PlayerSystem>();
game.Run();
```

| Call | What it does |
|---|---|
| `builder.AddPhysics2D(configure)` | Registers the engine core (`AddIon`), the ECS module (`AddEcs`), the physics worlds, the systems and `Physics2DConfig` bound from `Ion:Physics2D`, then `configure`. |
| `services.AddPhysics2D(config, configure)` | The same on an `IServiceCollection` (registers `AddEcs` but not `AddIon`); pass the configuration to bind `Ion:Physics2D`. |
| `game.UsePhysics2D()` | Adds `UseIon`, `UseEcs`, the physics step (FixedUpdate, `StageOrder.Physics`) and the debug drawing (Render, `StageOrder.PhysicsDebugDraw`) to the root schedule. |
| `scene.UsePhysics2D()` | Adds `UseEcs` and both physics systems to a scene's schedule, for the scene's own world. |
| `Physics2DComponents.Register()` | Registers the components with Arch for NativeAOT. `AddPhysics2D` and the world call it for you. |

## Bodies: `RigidBody2D`

An entity with a `Collider2D` and a `Transform2D` is a body. Without a `RigidBody2D` it is static. Add a rigid body to
make it move.

| Type | Moved by | Use for |
|---|---|---|
| `RigidBodyType2D.Static` | nothing; moving its `Transform2D` teleports it | walls, floors, blocks |
| `RigidBodyType2D.Kinematic` | your code: each fixed step it is driven to its `Transform2D` with the velocity that gets it there in one step, so it pushes dynamic bodies | paddles, moving platforms, doors |
| `RigidBodyType2D.Dynamic` | the simulation: gravity, contacts, joints, forces and impulses | balls, crates, characters |

```csharp
world.Create(new Transform2D(new Vector2(0, 300)), Collider2D.Box(new Vector2(800, 20)));             // static
world.Create(new Transform2D(new Vector2(0, 250)), Collider2D.Capsule(new Vector2(120, 24)),
	RigidBody2D.Kinematic());                                                                           // kinematic
world.Create(new Transform2D(new Vector2(0, 0)), Collider2D.Circle(12),
	RigidBody2D.Dynamic(new Vector2(0, -400)) with { IsBullet = true });                                // dynamic
```

### Fields

| Field | Type | Default (from the factories) | Meaning |
|---|---|---|---|
| `Type` | `RigidBodyType2D` | set by the factory | Static, kinematic or dynamic. Changing it switches the body's type at the next step. |
| `LinearVelocity` | `Vector2` | zero (or the factory argument) | World units per second. Written back after every step. |
| `AngularVelocity` | `float` | 0 | Radians per second. Written back after every step. |
| `Mass` | `float` | 0 | 0 computes the mass from the collider's `Density` and area in meters; a positive value overrides it (dynamic bodies only) and scales the rotational inertia with it. |
| `LinearDamping` | `float` | 0 | Slows the linear velocity over time. |
| `AngularDamping` | `float` | 0 | Slows the angular velocity over time. |
| `GravityScale` | `float` | 1 | Multiplier of the world gravity (0 floats). |
| `FixedRotation` | `bool` | false | Prevents rotation. |
| `IsBullet` | `bool` | false | Continuous collision against other dynamic bodies too (static ones always use it), so small fast bodies do not tunnel through thin moving ones. Costs more. |

Factories: `RigidBody2D.Dynamic(Vector2 velocity = default)`, `RigidBody2D.Kinematic()`, `RigidBody2D.Static()`, and the
constructor `new RigidBody2D(RigidBodyType2D type)`. All of them set `GravityScale` to 1.

Rotation follows `Transform2D.Rotation`: radians, clockwise on screen (y points down).

### Reading and writing state

The step writes the position and rotation of every body that moved into its `Transform2D`, and its velocities into its
`RigidBody2D`. Read them anywhere after the step. Write them to change the body at the next step:

```csharp
public record struct Player();

public sealed partial class ThrustSystem(IInputState input)
{
	[FixedUpdate, Query]
	private void Thrust(ref RigidBody2D body, in Player player)
	{
		// Applied at the next physics step.
		if (input.Down(Key.Space)) body.LinearVelocity += new Vector2(0, -20);
	}
}
```

| You write | At the next step |
|---|---|
| `Transform2D.Position` or `Rotation` of a static or dynamic body | The body is teleported there (and woken). |
| `Transform2D.Position` or `Rotation` of a kinematic body | The body is driven there with Box2D's target transform; when the transform stops changing, its velocity is zeroed. |
| `LinearVelocity` / `AngularVelocity` | Set on the body. |
| `Type`, `Mass`, damping, `GravityScale`, `FixedRotation`, `IsBullet` | Updated on the body (mass recomputed when needed). |
| Any `Collider2D` field | The shape is destroyed and rebuilt. |

:::note
`Transform2D.Scale` is never applied to the collider and never written by the step. Size the collider yourself.
:::

## Colliders: `Collider2D`

A collider is the shape and surface of the body, in the entity's local space and in world units.

| Factory | Shape |
|---|---|
| `Collider2D.Box(Vector2 size, Vector2 offset = default, float angle = 0f)` | A rectangle of full width and height `size`, centered on `offset`, rotated by `angle` radians. Set `Radius` for rounded corners. |
| `Collider2D.Circle(float radius, Vector2 offset = default)` | A circle. |
| `Collider2D.Capsule(Vector2 pointA, Vector2 pointB, float radius)` | The segment from `pointA` to `pointB` inflated by `radius`. |
| `Collider2D.Capsule(Vector2 size)` | A pill filling a `size` rectangle: horizontal when it is wider than tall, vertical otherwise. |
| `Collider2D.Polygon(ReadOnlySpan<Vector2> vertices, float radius = 0f)` | The convex hull of 3 to 8 vertices (`Polygon2D.MaxVertices`), optionally rounded. `Offset` and `Angle` apply too. |

```csharp
var paddle = Collider2D.Capsule(new Vector2(244, 64)) with { Restitution = 1, Friction = 0 };
var ramp = Collider2D.Polygon([new(-50, 20), new(50, 20), new(50, -20)]);
var rounded = Collider2D.Box(new Vector2(64, 32)) with { Radius = 4 };
```

### Surface and filtering fields

| Field | Default | Meaning |
|---|---|---|
| `Density` | 1 | Kilograms per square meter (area in meters, see `UnitsPerMeter`). Gives dynamic bodies their mass. |
| `Friction` | 0.6 | The friction coefficient, usually 0 to 1. |
| `Restitution` | 0 | Bounciness: 0 does not bounce, 1 bounces back at the same speed. |
| `IsSensor` | false | Detects overlaps (`Trigger2D`) without colliding. |
| `Layer` | 1 (bit 0) | The layers this collider belongs to, a bit mask. |
| `Mask` | `uint.MaxValue` (all) | The layers it collides with. |
| `EnableEvents` | true | Whether its contacts raise `Collision2D` events and it can enter sensors. |
| `HasBody` | (read only) | Whether the physics world has created its body yet. |

Two colliders collide when each one's `Layer` intersects the other's `Mask`. Contacts combine the two surfaces with Box2D's
default mixing rules.

```csharp
const uint Players = 1 << 0, Enemies = 1 << 1, Pickups = 1 << 2;

// Enemies collide with players but not with each other.
var enemy = Collider2D.Circle(10) with { Layer = Enemies, Mask = Players };

// A pickup is a sensor that only sees players.
var coin = Collider2D.Circle(8) with { IsSensor = true, Layer = Pickups, Mask = Players };
```

### Sensors

A sensor raises `Trigger2D` events (`Sensor`, `Visitor`, `Phase`) when a collider enters or leaves it, and never pushes
anything. Following Box2D 3.1's rules, sensors see **dynamic and kinematic** bodies, not static ones, and the visitor needs
`EnableEvents` (on by default). See [queries and events](/Ion/physics/queries-and-events/).

### Validation

Box2D asserts, which aborts the process, on degenerate geometry. The module checks colliders when it creates their body and
throws an `InvalidOperationException` naming the entity instead:

| Collider | Requirement |
|---|---|
| Box | A positive size larger than twice its rounding radius, and a non-negative radius. |
| Circle, capsule | A positive radius. |
| Polygon | 3 to 8 vertices, a non-negative radius, and a valid convex hull (no collinear or duplicate points, none closer than Box2D's linear slop). |
| Every collider | Non-negative `Density`, `Friction` and `Restitution`. |

The exception comes out of the physics step (FixedUpdate), not out of `world.Create`.

### Copying colliders

A collider stores which body it belongs to. Copying a collider to another entity copies that too; the world notices that
the body belongs to another entity and creates a new one, so copies are safe.

## Joints: `Joint2D`

A joint connects the bodies of two entities, each with a `Collider2D`. Put the `Joint2D` component on any entity (a third
one, or body B). The joint exists while the component and both bodies do: destroying either body removes its joints.
Anchors are in each body's local space and world units. Changing any field recreates the joint at the next step.

| Type | Factory | Behavior |
|---|---|---|
| `Distance` | `Joint2D.Distance(bodyA, bodyB, localAnchorA, localAnchorB, length = 0)` | Keeps the anchors at `Length` (a rod), within `Lower`..`Upper` with `EnableLimit`, or springy with `EnableSpring`. A `Length` of 0 uses the distance when the joint is created. |
| `Revolute` | `Joint2D.Revolute(bodyA, bodyB, localAnchorA, localAnchorB)` | A hinge: the anchors meet and the bodies rotate around them. Limits are angles in radians; the motor is a speed in radians per second and `MaxMotorForce` a torque. |
| `Prismatic` | `Joint2D.Prismatic(bodyA, bodyB, axis, localAnchorA, localAnchorB)` | A slider: body B moves along `Axis` (body A's local space, normalized) without rotating relative to A. Limits are translations. |
| `Weld` | `Joint2D.Weld(bodyA, bodyB, localAnchorA, localAnchorB)` | Glues the bodies. With `EnableSpring` it is soft (`Hertz`, `DampingRatio`). |

| Field | Default | Meaning |
|---|---|---|
| `Axis` | `Vector2.UnitX` | Prismatic sliding axis. |
| `Length` | 0 | Distance joint rest length (0: the current distance). |
| `EnableLimit`, `Lower`, `Upper` | off, 0, 0 | Distance range, angle range or translation range. |
| `EnableMotor`, `MotorSpeed`, `MaxMotorForce` | off, 0, 0 | The motor (revolute: a torque). |
| `EnableSpring`, `Hertz`, `DampingRatio` | off, 0, 1 | Spring stiffness in hertz and damping ratio (1 is critical). |
| `CollideConnected` | false | Whether the two connected bodies still collide with each other. |
| `IsCreated` | (read only) | Whether the joint exists in the physics world. |

The reference angle of revolute, prismatic and weld joints is the bodies' relative rotation when the joint is created.

```csharp title="A pendulum"
// The pivot never collides (Mask = 0), and a static body can anchor a 2D joint.
var pivot = world.Create(new Transform2D(new Vector2(400, 100)), Collider2D.Circle(4) with { Mask = 0 });
var bob = world.Create(new Transform2D(new Vector2(592, 100)), Collider2D.Circle(16), RigidBody2D.Dynamic());

// The hinge is at the pivot's center and 192 units to the left of the bob.
world.Create(Joint2D.Revolute(pivot, bob, localAnchorB: new Vector2(-192, 0)));
```

```csharp title="A motorized wheel and a limited slider"
world.Create(Joint2D.Revolute(chassis, wheel, localAnchorA: new Vector2(-40, 20)) with
{
	EnableMotor = true,
	MotorSpeed = MathF.Tau,   // one turn per second
	MaxMotorForce = 5000,
});

world.Create(Joint2D.Prismatic(frame, lift, Vector2.UnitY) with
{
	EnableLimit = true,
	Lower = -100,
	Upper = 0,
});
```

## Forces and impulses

`IPhysicsWorld2D` applies forces to a body. Each call returns `false` when the entity has no body yet (bodies are created
by the next physics step after the collider is added).

| Method | Effect |
|---|---|
| `ApplyForce(Entity, Vector2 force)` | A force at the center of mass until the next step (world units: newtons scaled by `UnitsPerMeter`). |
| `ApplyLinearImpulse(Entity, Vector2 impulse)` | Changes the velocity immediately: the velocity change in world units per second is `impulse / mass`. |
| `ApplyTorque(Entity, float torque)` | A torque until the next step. |
| `ApplyAngularImpulse(Entity, float impulse)` | Changes the angular velocity immediately. |

```csharp
public sealed class JumpSystem(IPhysicsWorld2D physics, IInputState input, World world)
{
	private Entity _player;

	[Init]
	public void Init(GameTime dt) =>
		_player = world.Create(new Transform2D(new Vector2(100, 100)), Collider2D.Capsule(new Vector2(32, 64)),
			RigidBody2D.Dynamic() with { FixedRotation = true, Mass = 70 });

	[FixedUpdate]
	public void Jump(GameTime dt)
	{
		// 70 kg times a velocity change of 500 units per second, upwards (y down on screen).
		if (input.Pressed(Key.Space)) physics.ApplyLinearImpulse(_player, new Vector2(0, -70 * 500));
	}
}
```

:::tip
For a velocity you know, writing `RigidBody2D.LinearVelocity` is simpler than computing an impulse. Use impulses when
the change should depend on the body's mass, and forces for continuous pushes (thrusters, wind).
:::

## The physics world: `IPhysicsWorld2D`

Inject `IPhysicsWorld2D` (or the concrete `PhysicsWorld2D`) into a system; in a scene you get the scene's world.

| Member | Meaning |
|---|---|
| `Gravity` | Get or set the gravity in world units per second squared (y points down). |
| `BodyCount`, `JointCount` | The numbers of bodies and joints. |
| `StepCount` | The fixed steps simulated so far. |
| `DebugDraw` | Turns the [debug drawing](/Ion/physics/debug-draw/) on or off at run time. |
| `RayCast`, `OverlapBox`, `OverlapCircle`, `OverlapPoint` | [Queries](/Ion/physics/queries-and-events/). |
| `ApplyForce`, `ApplyLinearImpulse`, `ApplyTorque`, `ApplyAngularImpulse` | Forces, above. |
| `ComputeStateHash()` | A hash of every body's state for [determinism checks](/Ion/physics/determinism/). |

`PhysicsWorld2D` adds `Entities` (the ECS world it simulates), `Config`, `AwakeBodyCount` (Box2D skips sleeping bodies)
and `Step(float dt)`, which the physics system calls and which you can call yourself in a standalone simulation.

## Configuration: `Physics2DConfig`

Bound from the `Ion:Physics2D` section (appsettings.json, environment variables, or the command line such as
`--Ion:Physics2D:DebugDraw=true`), then adjusted by the `AddPhysics2D(configure)` callback.

| Key | Default | Meaning |
|---|---|---|
| `GravityX` | 0 | Horizontal gravity, world units per second squared. |
| `GravityY` | 9.81 | Vertical gravity, world units per second squared; positive is down on screen. |
| `UnitsPerMeter` | 1 | World units per meter. Must be positive. Every length is divided by it before reaching Box2D. |
| `SubSteps` | 4 | Solver sub-steps per fixed step (Box2D's recommendation). More is stiffer and slower. At least 1. |
| `EnableSleep` | true | Resting bodies sleep and cost nothing until woken. |
| `EnableContinuous` | true | Dynamic bodies use continuous collision against static bodies (no tunneling through walls). |
| `ContactHertz` | 30 | Contact stiffness in hertz (Box2D's default). |
| `ContactDampingRatio` | 10 | Contact damping ratio (Box2D's default). |
| `RestitutionThreshold` | 0 | Relative speed in world units per second below which contacts do not bounce (0: Box2D's 1 m/s). |
| `MaxLinearSpeed` | 0 | Maximum body speed in world units per second (0: Box2D's 400 m/s). |
| `DebugDraw` | false | Starts with the debug drawing on. |

```json title="appsettings.json"
{
  "Ion": {
    "Physics2D": {
      "UnitsPerMeter": 64,
      "GravityY": 628,
      "SubSteps": 4
    }
  }
}
```

Box2D v3 has no velocity or position iteration counts; it solves with sub-steps (its "soft step" solver), so `SubSteps` is
the accuracy knob.

:::caution[Pixel games need both UnitsPerMeter and gravity]
The defaults suit a game in meters. In a pixel game, `UnitsPerMeter = 1` makes a 32 pixel ball a 32 meter boulder
(sluggish and outside Box2D's tuned range), and `GravityY = 9.81` pulls it at under 10 pixels per second squared. Set
`UnitsPerMeter` to the pixel size of a one meter object and gravity to about `9.81 * UnitsPerMeter`.
:::

## Engine notes

- Box2D runs single-threaded (one worker), with sleeping and continuous collision on by default.
- Box2D's world table is global: the module serializes creating and destroying worlds with a lock.
- **NativeAOT:** set `<Box2DStaticLink>true</Box2DStaticLink>` in the game's project to link Box2D's static library into
  the executable (the Breakout samples do). Otherwise `libbox2d` is copied next to it. See
  [NativeAOT](/Ion/platforms/native-aot/).
- The world is disposed with its scope; using a disposed world throws `ObjectDisposedException`.

## Testing physics

A headless host with the physics module steps one fixed step per frame, so tests are exact:

```csharp
using var host = new IonTestHost()
	.Configure(services => services.AddEcs().AddPhysics2D(configure: c => c.GravityY = 0))
	.ConfigureApp(app => app.UseEcs().UsePhysics2D());

var world = host.Get<World>();
var ball = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.5f), RigidBody2D.Dynamic());
host.Step();

ball.Get<RigidBody2D>().LinearVelocity = new Vector2(6, 0);
host.Step(60);
Assert.InRange(ball.Get<Transform2D>().Position.X, 5.8f, 6.1f);
```

To test the game's own `Program.cs`, use `new IonTestHost().UseEntryPoint<Program>()` or
`IonTestHost.RunEntryPoint<Program>(frames)`. See [testing](/Ion/tooling/testing/).

## See also

- [Physics overview](/Ion/physics/overview/): the step order, scopes and units.
- [Queries and events](/Ion/physics/queries-and-events/).
- [Debug drawing](/Ion/physics/debug-draw/).
- [Determinism](/Ion/physics/determinism/).
- [Breakout ECS example](/Ion/examples/breakout-ecs/): kinematic paddle, bullet balls, static blocks, collision events.
- [Breakout over the network](/Ion/examples/breakout-net/): physics on a server-authoritative game.
- [Configuration reference](/Ion/reference/configuration/).
- Source: [Ion.Extensions.Physics2D](https://github.com/jimbuck/Ion/tree/main/Ion/Ion.Extensions.Physics2D) and
  [its abstractions](https://github.com/jimbuck/Ion/tree/main/Ion/Ion.Extensions.Physics2D.Abstractions).
