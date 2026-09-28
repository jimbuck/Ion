---
title: Debug drawing
description: Draw every physics collider and joint over the frame, turn it on from configuration or at run time, and read its colors.
sidebar:
  order: 5
---

Both physics modules can draw every collider over the rendered frame, so you can see where the bodies really are,
which ones are asleep, and where the sensors and joints sit. It is the fastest way to debug a collider that is offset,
too large, or on the wrong layer.

## Turn it on

The drawing is off by default. Turn it on in any of three ways:

```bash title="Command line"
dotnet run -- --Ion:Physics2D:DebugDraw=true
dotnet run -- --Ion:Physics3D:DebugDraw=true
```

```json title="appsettings.json"
{
  "Ion": {
    "Physics2D": { "DebugDraw": true }
  }
}
```

```csharp title="Program.cs"
builder.AddPhysics2D(physics => physics.DebugDraw = true);
```

The configuration value only sets the starting state. At run time, flip `IPhysicsWorld2D.DebugDraw` or
`IPhysicsWorld3D.DebugDraw`:

```csharp
public sealed class PhysicsDebugToggleSystem(IPhysicsWorld2D physics, IInputState input)
{
	[Update]
	public void Toggle(GameTime dt)
	{
		if (input.Pressed(Key.F3)) physics.DebugDraw = !physics.DebugDraw;
	}
}
```

Register it like any system (`builder.AddSystem<PhysicsDebugToggleSystem>()`, `game.UseSystem<PhysicsDebugToggleSystem>()`).
Each physics world has its own flag: a scene's world starts from the configuration too, and a toggle in a scene's schedule
flips the scene's world.

:::tip[Default on while developing]
The Breakout ECS sample turns the drawing on unless the configuration says otherwise, by reading the key before
registering the module:

```csharp
var debugDraw = builder.Configuration[$"{Physics2DConfig.Section}:DebugDraw"] is null;
builder.AddPhysics2D(physics => physics.DebugDraw |= debugDraw);
```

Run it with `--Ion:Physics2D:DebugDraw=false` to hide the outlines.
:::

## When it draws

The drawing systems are part of `UsePhysics2D()` and `UsePhysics3D()` (and the scene versions); there is nothing else to
add. They run in the **Render** stage at `StageOrder.PhysicsDebugDraw` (650):

| Order | Render step |
|---:|---|
| -900 | Graphics frame scope opens (`StageOrder.Graphics`) |
| -860 | 3D renderer scope opens (`StageOrder.Rendering3D`) |
| -850 | Sprite batch scope opens (`StageOrder.SpriteBatch`) |
| -300 | ECS extraction of sprites and meshes (`StageOrder.Extract`) |
| 0 | Your own `[Render]` steps |
| **650** | **Physics debug drawing** (`StageOrder.PhysicsDebugDraw`) |
| 700 | UI (`StageOrder.Ui`) |
| 800 | Metrics overlay (`StageOrder.MetricsOverlay`) |

So the outlines are drawn inside the sprite batch and 3D renderer scopes, after the extraction and your own drawing (on
top of your sprites), and under the UI and the metrics overlay. When the flag is off the step returns immediately.

The drawing shows the physics state of the last fixed step, read from the physics engine itself, not from the
components. If a sprite and its outline disagree, the sprite's `Transform2D` or the collider's size or offset is wrong.

## 2D: lines on the sprite batch

`Physics2DDebugDrawSystem` draws each collider as lines through `ISpriteBatch.DrawLine`, in the same world coordinates as
your sprites:

| Shape | Drawn as |
|---|---|
| Box, polygon | Its outline (the Box2D polygon's vertices; a rounding radius is not drawn) |
| Circle | A 16 segment circle, plus a radius line that shows the body's rotation |
| Capsule | Two 16 segment circles joined by two side lines |
| Joint | White lines from body A's center to its anchor, from anchor to anchor, and from body B's anchor to its center |

| Color | Meaning |
|---|---|
| Blue | Static body |
| Green | Kinematic body |
| Red | Dynamic body, awake |
| Gray | Dynamic body, asleep |
| Yellow | Sensor (whatever its body type) |
| White | Joint |

Lines are 1 pixel thick (the system's `Thickness` property; there is no configuration key for it). The 2D drawing needs a
sprite batch: `AddPhysics2D` registers the engine core with `AddIon`, which provides one. Without it (a custom headless
setup) the drawing does nothing.

![The Breakout ECS sample with the physics debug drawing on](./breakout-physics-debug.png)

In the Breakout sample above, the walls and blocks are static (blue), the paddle is kinematic (green), and the balls are
dynamic (red). The blocks' outlines follow their random tilt, because the collider takes the rotation from `Transform2D`.

## 3D: translucent meshes on the 3D renderer

`Physics3DDebugDrawSystem` submits every collider to `IRenderer3D` as a translucent, unlit, double-sided mesh that casts
and receives no shadows:

| Shape | Drawn as |
|---|---|
| Box | A unit cube scaled to the box size |
| Sphere | A unit sphere scaled to the diameter |
| Cylinder | A unit cylinder scaled to the radius and length |
| Capsule | A cylinder for the middle and two spheres for the caps |
| Convex hull | A mesh built from the hull's faces, created the first time it is drawn and cached |

The colors follow the 2D ones, all translucent: blue static, green kinematic, red awake dynamic, gray sleeping, yellow
sensors. Joints are not drawn in 3D.

The unit meshes and materials are created on the first drawn frame and released with the renderer. The meshes are drawn
through the scene's cameras like any other mesh, so you need a camera entity (or your own camera setup) as for any 3D
rendering. `AddPhysics3D` registers the 3D renderer for you.

## Headless runs and tests

The drawing is safe to leave on in headless runs and tests. The 2D module's test counts the lines it draws through the test host's recording sprite batch:

```csharp
using var host = new IonTestHost()
	.Configure(services => services.AddEcs().AddPhysics2D(configure: c => c.DebugDraw = false))
	.ConfigureApp(app => app.UseEcs().UsePhysics2D());
var world = host.Get<World>();
world.Create(new Transform2D(new Vector2(0, 10)), Collider2D.Box(new Vector2(40, 1)));
world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(0.5f), RigidBody2D.Dynamic());
host.Step();
Assert.Equal(0, host.SpriteBatch.LastFrame.Lines);

host.Get<IPhysicsWorld2D>().DebugDraw = true;
host.Step();
Assert.Equal(4 + 17, host.SpriteBatch.LastFrame.Lines);   // a box: 4 lines; a circle: 16 + 1
```

For a picture, render headless and save a screenshot; see [snapshots and goldens](/Ion/tooling/snapshots-and-goldens/):

```bash
ion run --headless --render --frames 60 --screenshot out/frame60.png -- --Ion:Physics2D:DebugDraw=true
```

:::caution[Golden images]
Golden-image tests compare pixels. If a game turns the debug drawing on by default (as Breakout does), its goldens include
the outlines; pin `Ion:Physics2D:DebugDraw` explicitly in the test's configuration so a default change does not break
them.
:::

## Performance

The drawing walks every body each frame (and every joint in 2D), so it costs a few lines or one mesh per collider. It is
meant for development: with thousands of bodies, keep it off unless you need it. When `DebugDraw` is false the step does
nothing else.

## See also

- [Physics overview](/Ion/physics/overview/).
- [2D physics](/Ion/physics/physics-2d/) and [3D physics](/Ion/physics/physics-3d/): the `DebugDraw` configuration keys.
- [Stage order reference](/Ion/reference/stage-order/).
- [Sprites](/Ion/rendering/sprites/): the sprite batch the 2D drawing uses.
- [Breakout ECS example](/Ion/examples/breakout-ecs/).
