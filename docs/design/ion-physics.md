# Ion physics (`Ion.Extensions.Physics2D`, `Ion.Extensions.Physics3D`)

Status: implemented (Stage 5b, physics half, September 2026). The roadmap context is section 4.12 and Stage 5b of
[the engine review](../plans/2026-09-engine-review-and-roadmap.md); the engine choice for 2D is measured in
[benchmarks/2026-09-physics2d](../plans/benchmarks/2026-09-physics2d/README.md).

## 1. Shape

Two modules with the same design, one per dimension, each an ECS adapter around a physics engine:

```
Ion.Extensions.Physics2D.Abstractions   RigidBody2D, Collider2D, Joint2D, Collision2D, Trigger2D, RayHit2D,
                                        IPhysicsWorld2D, Physics2DConfig
Ion.Extensions.Physics2D                PhysicsWorld2D on Box2D v3.1 (C library, Box2D.NET.Bindings.Release 3.1.0),
                                        Physics2DSystem, Physics2DDebugDrawSystem, AddPhysics2D/UsePhysics2D
Ion.Extensions.Physics3D.Abstractions   RigidBody3D, Collider3D, Joint3D, ConvexHullId, Collision3D, Trigger3D, RayHit3D,
                                        IPhysicsWorld3D, Physics3DConfig
Ion.Extensions.Physics3D                PhysicsWorld3D on BepuPhysics 2.5.0-beta.29 (managed), Physics3DSystem,
                                        Physics3DDebugDrawSystem, AddPhysics3D/UsePhysics3D
```

A game library can depend on the abstractions only (components, events, the world interface). Registration, after
`AddIon` and `AddEcs`:

```csharp
builder.Services.AddEcs().AddPhysics2D(builder.Configuration, physics => physics.UnitsPerMeter = 64);
app.UseIon().UseEcs().UsePhysics2D();   // and scene.UsePhysics2D() in scenes that simulate physics

var ball = world.Create(new Transform2D(position), Collider2D.Circle(16) with { Restitution = 1 }, RigidBody2D.Dynamic());
```

The engines stay behind the adapter: game code never sees a Box2D id or a Bepu handle (`PhysicsWorld3D.Simulation` is
exposed for what the adapter does not cover). The 2D module could switch to the managed Box2D port (same C API) for a
target without natives without changing its public surface.

## 2. Components

| Component | 2D | 3D |
|---|---|---|
| Body | `RigidBody2D`: `Type` (`Static`, `Kinematic`, `Dynamic`), `LinearVelocity`, `AngularVelocity`, `Mass` (0: from the collider's density), `LinearDamping`, `AngularDamping`, `GravityScale`, `FixedRotation`, `IsBullet` | `RigidBody3D`: the same, 3D velocities, `Mass` (1 by default; Bepu needs one) |
| Shape | `Collider2D`: `Box` (size, rounding radius), `Circle`, `Capsule` (two points and a radius, or a pill from a size), `Polygon` (up to 8 vertices, inline), with `Offset` and `Angle`; `Density`, `Friction`, `Restitution`, `IsSensor`, `Layer`/`Mask`, `EnableEvents` | `Collider3D`: `Box`, `Sphere`, `Capsule` and `Cylinder` (along local y), `ConvexHull` (from `IPhysicsWorld3D.CreateConvexHull(points)`, for example a mesh's vertices); the same surface fields (`Restitution` is approximated, see 6) |
| Joint | `Joint2D`: `Distance`, `Revolute`, `Prismatic`, `Weld`; anchors, axis, length, limits, motor, spring, `CollideConnected` | `Joint3D`: `BallSocket`, `Hinge`, `Weld`, `Distance`; anchors, axis, distance range, spring (both bodies need a `RigidBody3D`: Bepu constrains bodies only) |

Rules shared by both:

- **A collider makes a body.** Every entity with a collider and a transform (`Transform2D`, or the graphics
  abstractions' 3D `Transform`) is a body; without a rigid body it is static. One collider per entity (compounds through
  children are not supported yet).
- **Units are the transform's.** Positions, sizes, velocities and gravity are in world units. In 2D,
  `Physics2DConfig.UnitsPerMeter` tells Box2D how large a meter is (its tolerances are tuned for 0.1 to 10 m objects); a
  pixel game sets it to the size of a one-meter object (the Breakout sample: 64).
- **Components are the source of truth, the world follows.** Game code changes components; the next physics step applies
  the changes. The step writes back only what the simulation owns (position, rotation, velocities).
- **Filtering.** Two colliders collide when each one's `Layer` intersects the other's `Mask`; queries select colliders
  whose `Layer` intersects the query mask.
- **Defaults need a constructor.** `default(RigidBody2D)` has a zero gravity scale (and `default(RigidBody3D)` a zero
  mass), like `default(Transform2D)` has a zero scale: create components with their constructors or factories.
- Both modules register their components with Arch (and the ECS built-ins) so NativeAOT can store them.

## 3. The step (FixedUpdate at `StageOrder.Physics`)

`StageOrder.Physics = -700` is new: in the engine setup band, before the scenes (-500) and the game's fixed steps (0).
Every fixed step the physics step runs first, so the game's own fixed steps see the result of the step they follow and
what they change is simulated by the next one (Unity's and Bevy's order). Inside a scene, the scene's physics step uses
the same order in the scene's schedule. `PhysicsWorld*.Step(dt)` does:

1. **Push.** One chunk loop over the entities with a collider and a transform. A collider without a body (or whose
   internal slot belongs to another entity, after a component copy) creates one. Otherwise the step compares the
   components with what it last synchronized (kept per body, not in the components): a changed collider rebuilds the
   shape (3D: the body), a changed type or damping or mass updates the body, a changed velocity is set, and a changed
   transform teleports a static or dynamic body. A **kinematic** body whose transform changed is driven there: Box2D's
   `b2Body_SetTargetTransform`, or in 3D the linear velocity (target - position) / dt and the angular velocity of the
   rotation difference (to first order, so the input side needs no trigonometry); when the transform stops changing its
   velocity is zeroed (a kinematic body would otherwise keep moving).
2. **Remove.** Bodies whose entity was not seen (destroyed, or lost its collider or transform) are destroyed, with their
   joints; their contacts end (3D: an `End` event is emitted right away).
3. **Joints.** One loop over `Joint2D`/`Joint3D`: created when both bodies exist, recreated when the definition or a body
   changed, destroyed when the component or a body is gone.
4. **Simulate** with the fixed delta (`GameTime.Delta` of FixedUpdate, never the wall clock): Box2D with
   `Physics2DConfig.SubSteps` (default 4); Bepu with `Physics3DConfig.Iterations` velocity iterations and `SubSteps`.
5. **Pull.** 2D: Box2D's body move events name the bodies that moved; 3D: Bepu's active set. A second chunk loop writes
   their position and rotation into the transform (the scale is left alone) and their velocities into the rigid body.
6. **Events** on `IEvents` (so readers in FixedUpdate get them through the fixed-step backlog, section 4.4 of the
   roadmap): `Collision2D`/`Collision3D` (`A`, `B`, `Phase` `Begin`/`End`, a contact point and the normal from A to B on
   `Begin`) and `Trigger2D`/`Trigger3D` (`Sensor`, `Visitor`, `Phase`). 2D reads Box2D's contact and sensor events; 3D
   records touching pairs (a contact at depth 0 or deeper) in the narrow-phase callback, per worker, then sorts them by
   pair key and diffs them with the previous step.

The debug drawing (`Physics2DDebugDrawSystem`, `Physics3DDebugDrawSystem`) runs in Render at
`StageOrder.PhysicsDebugDraw = 650` (new, under `StageOrder.Ui` at 700): inside the sprite batch scope and after the extraction and the game's drawing.
2D draws every collider and joint as lines through `ISpriteBatch` (static blue, kinematic green, dynamic red, sleeping
gray, sensors yellow, joints white); 3D submits every collider as a translucent unlit mesh (unit cube, sphere and
cylinder scaled, a mesh per hull) to `IRenderer3D`. Toggle it with `Ion:Physics2D:DebugDraw` / `Ion:Physics3D:DebugDraw`
or at run time with `IPhysicsWorld*.DebugDraw`.

## 4. Worlds and scopes

`PhysicsWorld2D`/`PhysicsWorld3D` resolve per scope the way the ECS `World` does (`Physics2DWorlds`/`Physics3DWorlds`):
the root provider gets the root physics world (for the root ECS world), each scene scope its own, created on first use
and disposed with the scope. The systems are transients so the root schedule and each scene get instances bound to their
own world. `IPhysicsWorld*` resolves to the same instance.

## 5. Queries and forces

`RayCast` (closest hit: entity, point, normal, fraction or distance), `OverlapBox`, `OverlapCircle`/`OverlapSphere`,
`OverlapPoint` (2D), filtered by a layer mask, writing entities into a caller-provided span (nothing allocates; at most
256 candidates per query). 2D overlaps are exact for circles and points and bounds-based for boxes; 3D overlaps test
bounding boxes (the sphere against each candidate's box). 2D has `ApplyForce`, `ApplyLinearImpulse`, `ApplyTorque` and
`ApplyAngularImpulse`; 3D the impulses. Queries see the state after the last fixed step.

## 6. Engine specifics

**Box2D v3 (2D).** Single-threaded (`workerCount = 1`), sleeping and continuous collision on by default. Sensors see
dynamic and kinematic bodies and need `EnableEvents` on the visitor (Box2D 3.1's rule). Degenerate geometry is rejected
with an `InvalidOperationException` naming the entity, because Box2D asserts (aborts the process) on it. Box2D's world
table is global: creating and destroying worlds is serialized with a lock. NativeAOT: set `<Box2DStaticLink>true</Box2DStaticLink>`
in the game's project to link the static library into the executable (the Breakout sample does); otherwise
`libbox2d` is copied next to it.

**BepuPhysics v2 (3D).** Pure managed, no native dependency. 2.5.0-beta.29 rather than 2.4.0: 2.4.0 (2022) targets
net6.0 and the 2.5 betas are the maintained line. Bepu has no restitution coefficient: a collider with `Restitution > 0`
gets a softer, less damped contact spring (damping ratio 1 - restitution) with unlimited recovery speed, which bounces
approximately. Kinematic and static bodies do not collide with each other (sensors aside). Per-body gravity scale and
damping are applied in the pose integrator (`v *= 1 / (1 + dt * damping)`, Box2D's form).

**Threads.** The frame never goes async. `Physics3DConfig.ThreadCount` above 1 creates a Bepu `ThreadDispatcher`; the
step blocks while the workers run, the simulation runs in its deterministic mode, and the contact events are sorted, so a
multithreaded run is reproducible (but differs from a single-threaded one: replays must use the same thread count). 2D is
single-threaded.

**Allocations.** Neither adapter allocates per step once its tables have grown to the scene (`StepsAllocateNothingAfterWarmUp`
in 2D). BepuPhysics itself occasionally grows a small managed array (an `int[16]` or `int[32]`) when an internal list
reaches a new high-water mark, which a settling pile still does now and then (the 3D test bounds it to 1 KB per 120
steps).

## 7. Determinism

Both worlds depend only on the components, the fixed delta and the order Arch iterates entities in (itself a function of
the order entities were created in), never on the wall clock. `IPhysicsWorld*.ComputeStateHash()` hashes every body's
pose and velocity bits in creation order; the replay tests (`Replay2D`, `Replay3D`: 10,000 fixed steps of seeded scenes
with joints and kinematic bodies) assert identical results on repeated runs.

Across processor architectures (measured under QEMU, see the benchmark folder):

- **Box2D:** the packaged natives are **not** bit-identical between x64 and arm64: the arm64 library is compiled with
  fused multiply-add contraction (1,562 FMA instructions), which Box2D's own build disables. The same Box2D 3.1.0 built
  with `-ffp-contract=off` (`docs/plans/benchmarks/2026-09-physics2d/native/build.sh`) gives the x64 hash on arm64 bit for
  bit, at 10,000 bodies and on the replay scene. The golden is **linux-x64** (`Replay2DTests.GoldenLinuxX64 =
  0x7DF39E42CA6D46A6`, which the contraction-free arm64 build reproduces; the packaged arm64 natives give
  `0x5780ABC152A90013`). To link a contraction-free build into a NativeAOT game:

  ```xml
  <ItemGroup Condition="'$(PublishAot)' == 'true'">
    <DirectPInvoke Include="box2d" />
    <NativeLibrary Include="path/to/$(RuntimeIdentifier)/libbox2d.a" />
  </ItemGroup>
  ```

  Shipping those builds for every RID from CI (and so cross-architecture lockstep out of the box) is the open item.
- **BepuPhysics:** deterministic across architectures at equal `Vector<float>` width (4 lanes: arm64, NativeAOT on x64,
  or the JIT with `DOTNET_MaxVectorTBitWidth=128`, all `0x0987708E02C11079`; 8 lanes: the JIT on AVX2,
  `0x0A315BD5EFFEF90B`). A game that needs cross-machine replays on x64 JIT and arm64 pins the width to 128 bits.

## 8. Not done

Compound colliders (several shapes per body through child entities), chain and segment shapes, 3D mesh (triangle soup)
colliders, motors and limits on 3D joints, shape casts, per-contact callbacks (pre-solve), exact 3D overlap tests, and
CI-built contraction-free Box2D natives for every RID.
