---
title: Transforms
description: Place entities with Transform2D and Transform, build parent/child hierarchies, and understand when the transform propagation computes GlobalTransform2D and GlobalTransform.
sidebar:
  order: 5
---

Entities are placed with a local transform component. The ECS module's `TransformPropagationSystem` walks the
parent/child hierarchy and writes the world transform into a global component, which the render extraction and your own
code read. You write local transforms; you read global ones.

| Dimension | You write (local) | Propagation writes (world) |
|---|---|---|
| 2D | `Transform2D`: `Position` (`Vector2`), `Rotation` (radians), `Scale` (`Vector2`) | `GlobalTransform2D`: `Matrix` (`Matrix3x2`), `Position`, `Rotation`, `Scale` |
| 3D | `Transform` (from `Ion.Extensions.Graphics`): `Position` (`Vector3`), `Rotation` (`Quaternion`), `Scale` (`Vector3`) | `GlobalTransform`: `Matrix` (`Matrix4x4`), `Position` |

The 3D local transform is the graphics abstractions' own `Transform`, so the same type you pass to `IRenderer3D` is the
ECS component.

## 2D transforms

```csharp
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Ecs;

public sealed class ShipSystem(World world)
{
    private Entity _ship;

    [Init]
    public void Init(GameTime dt)
    {
        _ship = world.Create(new EntityName("ship"), new Transform2D(new Vector2(640, 360)));
        world.Create(new Transform2D(new Vector2(100, 0), rotation: MathF.PI / 4, scale: new Vector2(2, 2)));
        world.Create(Transform2D.Identity);   // at the origin, scale 1
    }

    [Update]
    public void Turn(GameTime dt) => world.Get<Transform2D>(_ship).Rotation += dt.Delta;
}
```

| `Transform2D` member | Meaning |
|---|---|
| `Transform2D()`, `Transform2D.Identity` | Origin, no rotation, scale 1 |
| `Transform2D(position, rotation = 0)` | Scale 1 |
| `Transform2D(position, rotation, scale)` | |
| `Position` | World units (pixels unless a `Camera2D` zooms) |
| `Rotation` | Radians, clockwise on screen (y points down) |
| `Scale` | Multiplies the sprite's size |
| `ToMatrix()` | Scale, then rotation, then translation (row vectors, as `System.Numerics`) |

:::caution[default(Transform2D) has a zero scale]
`default(Transform2D)` and `new Transform2D { Position = p }` leave `Scale` at zero, so sprites vanish. Always use a
constructor: `new Transform2D(p)`.
:::

`GlobalTransform2D` exposes `Matrix` (the exact world matrix: local times the parent's), `Position`, `Rotation` and
`Scale` (its decomposition, which the sprite extraction draws from), `IsComputed` and `TransformPoint(local)`. The
decomposition composes rotations by addition and scales by multiplication, so under a parent with a non-uniform scale and
a rotated child it ignores the resulting shear; the `Matrix` keeps it. For a root entity it is exactly the entity's
`Transform2D`. Its setters are internal: you cannot write a global transform.

## 3D transforms

```csharp
using System.Numerics;
using Ion.Extensions.Graphics;

world.Create(new Transform(new Vector3(0, 1, 0)), new MeshRenderer(mesh, material));
world.Create(Transform.LookAt(eye: new Vector3(0, 2, 6), target: Vector3.Zero), new Camera { FieldOfView = MathF.PI / 4 });
world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.6f, -0.6f, -0.3f), Vector3.UnitY)),
    new DirectionalLight(Color.White, intensity: 3f));
```

| `Transform` member | Meaning |
|---|---|
| `Transform()`, `Transform.Identity` | Origin, identity rotation, scale 1 |
| `Transform(position, rotation = null, scale = null)` | Defaults: identity rotation, scale 1 |
| `Transform.LookAt(eye, target, up = null)` | A transform at `eye` looking at `target` |
| `Transform.LookRotation(forward, up)` | A rotation whose -Z points along `forward` |
| `Forward`, `Right`, `Up` | -Z, +X and +Y rotated |
| `LookAtTarget(target, up = null)` | Turns in place |
| `TransformPoint`, `TransformDirection`, `ToMatrix()`, `FromMatrix(matrix)` | Conversions |

Cameras look down their entity's -Z, and directional and spot lights shine along it (see
[ECS rendering](/Ion/ecs/ecs-rendering/)). `GlobalTransform.Matrix` is `local.ToMatrix() * parent.Matrix` (row vectors);
`GlobalTransform.Position` is its translation.

## Hierarchies

Parent an entity to another with `SetParent`. The child's transform becomes relative to its parent's world transform:
move, turn or scale the parent and every descendant follows.

```csharp
public sealed class TankSystem(World world)
{
    private Entity _hull;
    private Entity _turret;

    [Init]
    public void Init(GameTime dt)
    {
        _hull = world.Create(new EntityName("hull"), new Transform2D(new Vector2(300, 300)));
        _turret = world.Create(new EntityName("turret"), new Transform2D(new Vector2(0, -10)));   // 10 px above the hull's origin
        world.SetParent(_turret, _hull);
    }

    [Update]
    public void Drive(GameTime dt)
    {
        world.Get<Transform2D>(_hull).Position += new Vector2(40 * dt.Delta, 0);  // the turret moves with it
        world.Get<Transform2D>(_turret).Rotation += dt.Delta;                      // and turns on its own
    }
}
```

The hierarchy is two components, maintained for you:

| Component | Holds |
|---|---|
| `Parent(Entity Value)` | The parent entity (on the child) |
| `Children` | The children in attach order (on the parent): `Count`, indexer, `AsSpan()` |

Do not add or edit `Parent` and `Children` yourself; use the operations, which keep both sides in step and reject cycles:

| `World` extension | `Commands` equivalent | Does |
|---|---|---|
| `SetParent(child, parent)` | `commands.SetParent(child, parent)` | Attach (moving from a previous parent). Throws on a cycle or a dead entity. |
| `RemoveParent(child)` | `commands.RemoveParent(child)` | Detach: the child becomes a root and its local transform is now relative to the world. |
| `DestroyRecursive(entity)` | `commands.DestroyRecursive(entity)` | Destroy the entity and all its descendants. |
| `TryGetParent(entity, out parent)` | | Read the parent. |
| `GetChildren(entity)` | | Read the children as a span. |

`commands.SetParent` accepts a child created by the same commands (a placeholder); the parent must already exist. Inside
a `[Query]` method, only the `Commands` forms are allowed (ION305).

:::note[Detaching keeps the local values]
`RemoveParent` does not convert the child's local transform into world space. A child at local `(0, -10)` under a
parent at `(300, 300)` jumps to `(0, -10)` when detached. Copy `GlobalTransform2D.Position` into the `Transform2D` first
if you want it to stay put.
:::

Children need a transform of the same kind as their parent: a `Transform2D` child under a `Transform2D` parent, a
`Transform` child under a `Transform` parent. An entity whose parent lacks that transform is not reached by the
propagation. Destroying a parent with `world.Destroy` does not destroy its children (they keep a stale `Parent`); use
`DestroyRecursive`.

## When the propagation runs

`TransformPropagationSystem` (added by `UseEcs()`) runs at `StageOrder.TransformPropagation` (-400) twice per frame:

| Stage | Why |
|---|---|
| **Render**, before the extraction (-300) | So a frame draws what its own Update did, and entities created this frame are not drawn at the origin. Also updates `Aabb2D` of sprites. |
| **Last** | So the next frame's First, FixedUpdate and Update read fresh global transforms. |

Ion's frame runs First, FixedUpdate, Update, Render, Last. So when you move an entity in Update and read its
`GlobalTransform2D` later in the same Update, you get the value from the end of the previous frame. Read `Transform2D`
(the local value you just wrote) or compute from the parent if you need the current world position mid-Update.

Each pass first adds any missing `GlobalTransform2D`, `GlobalTransform` and (for sprites, in Render) `Aabb2D` components
with Arch's bulk operations, then walks the tree: roots (entities without a `Parent`) in query order, children in their
`Children` order, so the order is deterministic.

### Dirty tracking

The walk recomputes an entity only when its local transform differs from the one its global transform was computed
from, or its parent was recomputed. No change flags are needed: the global component remembers the local values and the
parent version it was built from. The Render pass after an unchanged Last pass is just the check. `LastUpdated` and
`LastVisited` on the system report how much work the last pass did.

Measured on 10,000 entities in a three-level tree: 232 us with everything dirty (23 ns per entity) and 152 us with
nothing changed (15 ns per entity), no allocations.

## Sprite bounds

In Render the propagation also writes `Aabb2D` (world-space `Min`, `Max`, `Size`, `Center`) for every entity with a
`Sprite`, from its size, origin, and global transform. The sprite extraction culls with it, and you can use it for cheap
hit tests:

```csharp
public sealed partial class ClickSystem(IInputState input)
{
    [Update, Query, All<Sprite>]
    private void Pick(Entity entity, in Aabb2D bounds)
    {
        if (input.Pressed(MouseButton.Left) && bounds.Contains(input.MousePosition))
        {
            Console.WriteLine($"Clicked {entity}");
        }
    }
}
```

The bounds are from the previous Render (they are computed before drawing), and in window pixels only when no
`Camera2D` transforms the view. `TransformPropagationSystem.BoundsOf(sprite, global)` computes them on demand.

## Physics and networking

The 2D and 3D physics modules read `Transform2D`/`Transform` of bodies when they change and write the simulated values
back at `StageOrder.Physics` in FixedUpdate, so game code keeps moving kinematic bodies through their transforms (see
[Physics overview](/Ion/physics/overview/)). The multiplayer module replicates and interpolates the same components (see
[Multiplayer overview](/Ion/networking/multiplayer/overview/)).

## See also

- [ECS rendering](/Ion/ecs/ecs-rendering/)
- [Entities and commands](/Ion/ecs/entities-and-commands/)
- [2D cameras](/Ion/rendering/cameras-2d/) and [3D cameras](/Ion/rendering/3d/cameras/)
- [Game loop](/Ion/concepts/game-loop/)
