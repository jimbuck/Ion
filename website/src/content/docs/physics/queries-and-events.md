---
title: Queries and events
description: Ray casts, overlap queries, collision and trigger events in Ion's 2D and 3D physics, with their filtering rules.
sidebar:
  order: 4
---

The physics worlds answer spatial questions (what does this ray hit, what is inside this area) and report what happened
during each step (these two started touching, this ball entered that sensor). Queries are methods on `IPhysicsWorld2D`
and `IPhysicsWorld3D`; events arrive on `IEvents` like any other game event.

## Ray casts

A ray cast returns the **closest** hit on a collider whose `Layer` intersects the mask. Sensors are skipped: the ray
goes through them to the closest solid collider behind (pass `includeSensors: true` to hit them too).

```csharp title="2D"
bool RayCast(Vector2 origin, Vector2 translation, out RayHit2D hit, uint mask = uint.MaxValue, bool includeSensors = false);
```

```csharp title="3D"
bool RayCast(Vector3 origin, Vector3 direction, float maxDistance, out RayHit3D hit, uint mask = uint.MaxValue, bool includeSensors = false);
```

| | 2D (`RayHit2D`) | 3D (`RayHit3D`) |
|---|---|---|
| Ray | from `origin` to `origin + translation` | from `origin` along `direction` (normalized for you) up to `maxDistance` |
| `Entity` | the entity whose collider was hit | the same |
| `Point` | the hit point in world units | the same |
| `Normal` | the surface normal at the hit | the same |
| Distance | `Fraction`: 0 at the origin, 1 at the end of the ray | `Distance` along the normalized direction |
| Sensors | skipped unless `includeSensors` | the same |

A 3D ray with a zero direction or a `maxDistance` that is not positive returns `false`.

```csharp
public sealed class LaserSystem(IPhysicsWorld2D physics, World world)
{
	private const uint Walls = 1 << 1;

	[FixedUpdate]
	public void Fire(GameTime dt)
	{
		var origin = new Vector2(100, 300);
		if (physics.RayCast(origin, new Vector2(800, 0), out var hit, mask: Walls))
		{
			// hit.Point is where the beam stops; hit.Fraction * 800 is how far it travelled.
			world.Create(new Spark(hit.Point, hit.Normal));
		}
	}
}

public record struct Spark(Vector2 Position, Vector2 Normal);
```

```csharp title="Ground check in 3D"
var feet = transform.Position;
var grounded = physics.RayCast(feet + new Vector3(0, 0.05f, 0), -Vector3.UnitY, 0.15f, out var ground, mask: GroundLayer);
```

```csharp title="Hitting sensors on purpose"
// A cursor ray that can pick trigger zones as well as walls: the closest of either.
if (physics.RayCast(camera, pointer - camera, out var picked, includeSensors: true)) Select(picked.Entity);
```

Sensors are skipped inside the cast (in the Box2D callback in 2D, before Bepu's narrow phase in 3D), not by dropping a
sensor hit afterwards, so a sensor in front of a wall never hides the wall. Combine `includeSensors` with a mask to hit
only some sensors: put them on their own layer and cast with that layer's bit.

## Overlap queries

Overlap queries write the entities they find into a span **you** provide and return how many they wrote. Nothing
allocates. Each entity appears once, and the span's length bounds the results. Unlike ray casts, overlap queries report
sensors too (asking what is inside an area usually includes the trigger zones there); keep sensors on their own layer
and leave it out of the mask to skip them.

| 2D method | Test |
|---|---|
| `OverlapBox(Vector2 min, Vector2 max, Span<Entity> results, uint mask)` | Colliders whose bounding box overlaps the box (bounds-based, not exact for rotated or round shapes). |
| `OverlapCircle(Vector2 center, float radius, Span<Entity> results, uint mask)` | Exact: colliders whose shape overlaps the circle. |
| `OverlapPoint(Vector2 point, Span<Entity> results, uint mask)` | Exact: colliders that contain the point. |

| 3D method | Test |
|---|---|
| `OverlapBox(Vector3 min, Vector3 max, Span<Entity> results, uint mask)` | Colliders whose bounding box overlaps the box. |
| `OverlapSphere(Vector3 center, float radius, Span<Entity> results, uint mask)` | Colliders whose **bounding box** intersects the sphere (the sphere against each candidate's box, not the exact shape). |

3D results are sorted in a deterministic order. At most 256 candidates are considered per query in both modules.

```csharp title="An explosion"
public sealed class ExplosionSystem(IPhysicsWorld2D physics, World world)
{
	private readonly Entity[] _hits = new Entity[64];   // reused: no allocation per query

	public void Explode(Vector2 center, float radius, float strength)
	{
		var count = physics.OverlapCircle(center, radius, _hits);
		foreach (var entity in _hits.AsSpan(0, count))
		{
			if (!world.Has<RigidBody2D>(entity)) continue;   // static bodies do not move
			var direction = Vector2.Normalize(world.Get<Transform2D>(entity).Position - center);
			physics.ApplyLinearImpulse(entity, direction * strength);
		}
	}
}
```

```csharp title="Clicking on a body"
Span<Entity> found = stackalloc Entity[4];
var count = physics.OverlapPoint(input.MousePosition, found);
if (count > 0) Select(found[0]);
```

:::note
Queries see the state after the **last fixed step**. A body you created this frame has no shape until the next physics
step, and a transform you changed is not seen by queries until then either.
:::

## Collision events

When two colliders start or stop touching during a step, the module emits a `Collision2D` or `Collision3D` on `IEvents`
right after the step.

```csharp
public readonly record struct Collision2D(Entity A, Entity B, ContactPhase Phase, Vector2 Point, Vector2 Normal);
public readonly record struct Collision3D(Entity A, Entity B, ContactPhase3D Phase, Vector3 Point, Vector3 Normal);
```

| Member | Meaning |
|---|---|
| `A`, `B` | The two entities. |
| `Phase` | `Begin` (started touching) or `End` (stopped touching, or one of them was destroyed or lost its collider). |
| `Point` | A contact point in world units on `Begin`; zero on `End`. |
| `Normal` | The contact normal pointing from A to B on `Begin`; zero on `End`. |
| `Involves(entity)` | Whether the entity is A or B. |
| `Other(entity)` | The other entity of the pair. |

Each contact is reported once per phase, in an order that depends only on the simulation, so events are deterministic.

Read them with an `EventReader`, created once (a field) so it remembers what it has read. This system from the Breakout
ECS sample turns physics contacts into game events in the fixed step, right after the physics step:

```csharp
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
			else if (world.Has<Wall>(other)) events.Emit(new WallHitEvent());
		}
	}
}
```

Order -10 runs after the physics step (-700) and before the game's own fixed steps (0), which react to the game events in
the same fixed step. You can also read physics events from `Update` or later stages. See [events](/Ion/concepts/events/)
for how readers, frames and the fixed-step backlog interact.

:::caution[Entities in events can be gone]
An `End` event is often emitted because an entity was destroyed, and a `Begin` handler may destroy an entity another
handler still reads about. Check `world.IsAlive(entity)` before touching components, as above.
:::

### Which contacts raise events

| | 2D | 3D |
|---|---|---|
| Collision events | A pair raises events when either collider has `EnableEvents` (on by default); sensors never raise `Collision*` | A pair raises events when either collider has `EnableEvents` |
| Touching means | Box2D's begin and end touch events | A contact at depth 0 or deeper in the narrow phase; speculative contacts do not count |
| Order | Box2D's (deterministic) | Sorted by body pair, then diffed with the previous step (deterministic with any thread count) |

Turn `EnableEvents` off on colliders you never react to (debris, particles) to save work.

## Trigger (sensor) events

A collider with `IsSensor = true` detects overlaps without colliding and reports them as `Trigger2D` or `Trigger3D`:

```csharp
public readonly record struct Trigger2D(Entity Sensor, Entity Visitor, ContactPhase Phase);
public readonly record struct Trigger3D(Entity Sensor, Entity Visitor, ContactPhase3D Phase);
```

| Rule | 2D | 3D |
|---|---|---|
| Who can visit | any collider on any body type, other sensors included (each sensor reports the other) | any body the pair rules allow: a dynamic body, or a kinematic body (a sensor on a kinematic body also sees kinematic and static bodies); two overlapping sensors raise one event, with one of them as the sensor |
| Events need | `EnableEvents` on both the sensor and the visitor | `EnableEvents` on both |
| Layers | `Layer` and `Mask` filter sensors like any collider | the same |
| Detection | at the end of the step, from the final positions (no continuous detection) | a contact at depth 0 or deeper in the narrow phase |

```csharp title="A goal zone"
public sealed class GoalSystem(World world, IEvents events)
{
	private EventReader<Trigger2D> _triggers = events.Reader<Trigger2D>();
	private Entity _goal;

	[Init]
	public void Init(GameTime dt) =>
		_goal = world.Create(new Transform2D(new Vector2(780, 300)), Collider2D.Box(new Vector2(40, 200)) with { IsSensor = true });

	[FixedUpdate]
	public void Check(GameTime dt)
	{
		foreach (ref readonly var trigger in _triggers.Read())
		{
			if (trigger.Sensor == _goal && trigger.Phase == ContactPhase.Begin) events.Emit(new GoalScored());
		}
	}
}

public record struct GoalScored();
```

## Testing queries and events

`IonTestHost.Collect<T>()` records every event of a type, which makes physics behavior easy to assert:

```csharp
using var host = new IonTestHost()
	.Configure(services => services.AddEcs().AddPhysics2D())
	.ConfigureApp(app => app.UseEcs().UsePhysics2D());
var world = host.Get<World>();
var collisions = host.Collect<Collision2D>();

var floor = world.Create(new Transform2D(new Vector2(0, 3)), Collider2D.Box(new Vector2(40, 1)));
var ball = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.5f), RigidBody2D.Dynamic());

Assert.True(host.RunUntil(() => collisions.Any(c => c.Phase == ContactPhase.Begin), 120));
Assert.Equal(floor, collisions.First(c => c.Phase == ContactPhase.Begin).Other(ball));
```

See [testing](/Ion/tooling/testing/).

## Remote queries

With `Ion.Extensions.Physics2D.Remote` (`builder.AddPhysics2DRemote()`), a running game answers `physics2d.raycast` and
`physics2d.bodies` over the remote protocol, so a tool or a coding agent can inspect the simulation. The remote ray cast
skips sensors like `RayCast`; add `"includeSensors": true` to hit them:

```bash
ion remote physics2d.raycast '{"origin": [0, 300], "to": [800, 300]}'
```

See [determinism and remote inspection](/Ion/physics/determinism/) and the
[remote protocol](/Ion/tooling/remote-protocol/).

## See also

- [2D physics](/Ion/physics/physics-2d/): layers, masks and sensors on `Collider2D`.
- [3D physics](/Ion/physics/physics-3d/).
- [Events](/Ion/concepts/events/).
- [Breakout ECS example](/Ion/examples/breakout-ecs/).
- [Physics overview](/Ion/physics/overview/): when the step runs.
