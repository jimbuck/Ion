---
title: ECS rendering
description: Draw entities with the 2D sprite extraction (AddEcsRendering) and the 3D extraction (AddEcsRendering3D), spawn glTF models as entity hierarchies, and control visibility, cameras and the scene environment.
sidebar:
  order: 6
---

`Ion.Extensions.Ecs.Rendering` copies your world into the renderers every frame, the extraction model Bevy uses: the
renderers never see entities, they receive plain draw submissions, so they stay usable from non-ECS code too. You add
components; the extraction draws them.

| Module | Register | Add to the schedule | Draws |
|---|---|---|---|
| 2D | `builder.AddEcsRendering(options)` | `game.UseEcsRendering()` | `Sprite` + `GlobalTransform2D` entities into the sprite batch, through the `MainCamera` |
| 3D | `builder.AddEcsRendering3D(options)` | `game.UseEcsRendering3D()` | `MeshRenderer`, `Camera`, lights and the world's `SceneEnvironment` into the 3D renderer |

Each pulls in what it needs. `AddEcsRendering()` registers the engine core (`AddIon`) and the ECS module;
`UseEcsRendering()` adds `UseIon()`, `UseEcs()` and the extraction system. `AddEcsRendering3D()` registers the 3D renderer
(`AddRendering3D`, which includes `AddIon`) and the ECS module; `UseEcsRendering3D()` adds `UseRendering3D()`, `UseEcs()`
and the 3D extraction. The two are independent: register both for a game with sprites and meshes.

Both extractions run in Render at `StageOrder.Extract` (-300): inside the sprite batch and 3D renderer frame scopes,
after the transform propagation's Render pass (-400), and before your own Render steps at order 0, which therefore draw
on top of extracted sprites (a HUD, for example).

## 2D: sprites

```csharp title="Program.cs"
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering().AddSystem<LevelSystem>();

using var game = builder.Build();
game.UseEcsRendering().UseSystem<LevelSystem>();
game.Run();

public sealed class LevelSystem(World world, IAssetManager assets)
{
    [Init]
    public void Init(GameTime dt)
    {
        var tiles = assets.Load<ITexture2D>("tiles.png");
        var ball = assets.Load<ITexture2D>("ball.png");

        // A 192x64 block centered on (200, 100), drawn at depth 0.
        world.Create(new Transform2D(new Vector2(200, 100)), new Sprite(tiles, new Vector2(192, 64)));
        // In front of the blocks (higher depth draws later).
        world.Create(new Transform2D(new Vector2(200, 400)), new Sprite(ball, new Vector2(32, 32), depth: 1));
    }
}
```

### What gets drawn

Every entity with a `Sprite` and a `GlobalTransform2D` (added by the propagation to anything with a `Transform2D`),
without `Hidden`, whose texture is not null and whose bounds intersect the view. Sprites are sorted by `Depth` (lower
first, ties in Arch's query order, so stable). When depths are already in order (the common case, all zero included) the
sprites are drawn straight from the chunks; otherwise they are copied into a flat array and sorted by integer keys. No
allocations per frame once the arrays have grown.

| `Sprite` field | Meaning |
|---|---|
| `Texture` | `ITexture2D`; null draws nothing |
| `Source` | Texel rectangle of the texture; empty for the whole texture (sprite sheets, animation frames) |
| `Size` | World units before scale; zero for the source (or texture) size |
| `Color` | Tint; `default` is untinted |
| `Origin` | The point placed at the entity's position, as fractions of the size; `(0.5, 0.5)` is the center |
| `Depth` | Sort key: 0 behind 1 |
| `Flip` | `SpriteEffect` flags for horizontal and vertical flipping |

`new Sprite(texture, size = default, depth = 0)` centers the origin. `default(Sprite)` has a top-left origin and no
texture. The world position, rotation and scale come from `GlobalTransform2D`, so parented sprites follow their parents.

### Options

| `SpriteExtractionOptions` | Default | Meaning |
|---|---|---|
| `RequireVisible` | `false` | Draw only sprites tagged `Visible` (otherwise every sprite without `Hidden`) |
| `Cull` | `true` | Skip sprites whose `Aabb2D` misses the camera's view |

```csharp
builder.AddEcsRendering(options => options.Cull = false);
```

`SpriteExtractionSystem.LastFrame` (a `SpriteExtractionStats`: `Extracted`, `Culled`, `HasCamera`) reports what the last
frame did; `FrameStats.Sprites` in the frame log counts the sprite batch's draws.

### The 2D camera

The view is the first entity tagged `MainCamera` that has a `Camera2D` (the engine's 2D camera class, used as a
component). Its `GetTransform(viewport)` becomes the sprite batch's transform for the extracted sprites. Without one,
world units are window pixels with the origin at the top left.

```csharp
var camera = world.Create(new MainCamera(), new Camera2D { Position = new Vector2(640, 360), Zoom = 2f });

// Later: follow the player.
world.Get<Camera2D>(camera).Position = world.Get<GlobalTransform2D>(player).Position;
```

`Camera2D.Position` is the world point at the center of the viewport; the camera is not placed by a `Transform2D`. It
also has `ScreenToWorld` and `WorldToScreen` for mouse picking. See [2D cameras](/Ion/rendering/cameras-2d/).

:::note[Your own Render steps are not transformed]
The camera transform applies only to the extracted sprites (the extraction opens its own sprite batch segment with that
transform). A step that draws with `ISpriteBatch` at order 0 draws in window pixels, which is what a HUD wants.
:::

### Sprite animation

`SpriteAnimation` plays a flip-book on the entity's `Sprite`: `SpriteAnimationSystem` (part of `UseEcs()`, Update at
`StageOrder.SpriteAnimation` = 400, after your Update steps) advances it by the frame's delta and sets `Sprite.Source` to
the current frame.

```csharp
var frames = new[]
{
    new RectangleF(0, 0, 32, 32),
    new RectangleF(32, 0, 32, 32),
    new RectangleF(64, 0, 32, 32),
};
world.Create(new Transform2D(new Vector2(300, 200)), new Sprite(sheet, new Vector2(64, 64)), new SpriteAnimation(frames, framesPerSecond: 12));
```

| `SpriteAnimation` | Meaning |
|---|---|
| `SpriteAnimation(frames, framesPerSecond, loop = true)` | |
| `Frames`, `FramesPerSecond`, `Loop` | The flip-book |
| `Paused` | Stops advancing (keeps the frame) |
| `Time`, `Frame` | Seconds played and the current frame index; reset `Time` to restart |
| `IsFinished` | A non-looping animation reached its last frame |

## 3D: meshes, cameras and lights

```csharp title="Program.cs"
using System.Numerics;
using Arch.Core;
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<ShapesSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<ShapesSystem>();
game.Run();

public readonly record struct Spin(float Speed);

public sealed partial class ShapesSystem(World world, IRenderer3D renderer)
{
    [Init]
    public void Init(GameTime dt)
    {
        var cube = renderer.CreateMesh(MeshPrimitives.Cube(0.8f));
        var red = renderer.CreateMaterial(new PbrMaterial(new Color(0xE0, 0x4A, 0x3B), metallic: 0f, roughness: 0.45f));

        world.SetEnvironment(new SceneEnvironment { AmbientColor = new Color(0x9C, 0xB0, 0xD0), AmbientIntensity = 0.35f });
        world.Create(new Transform(new Vector3(0, 0.5f, 0)), new MeshRenderer(cube, red), new Spin(1f));
        world.Create(Transform.LookAt(new Vector3(0, 3, 6), Vector3.Zero), new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f });
        world.Create(new Transform(Vector3.Zero, Transform.LookRotation(new Vector3(-0.65f, -0.6f, -0.3f), Vector3.UnitY)),
            new DirectionalLight(new Color(0xFF, 0xF4, 0xE0), intensity: 3f));
    }

    [Update, Query]
    private static void Turn(ref Transform transform, in Spin spin, GameTime dt) =>
        transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)dt.Elapsed.TotalSeconds * spin.Speed);
}
```

This is the shape of the Cubes and Model samples (see [Cubes](/Ion/examples/cubes/) and [Model](/Ion/examples/model/)).

### What gets submitted

| Components (without `Hidden`) | Submitted as |
|---|---|
| `MeshRenderer` + `GlobalTransform` | `IMeshBatch.Submit(renderer, world matrix)`; with `RequireVisible`, only when also tagged `Visible` |
| `Camera` + `GlobalTransform` | `AddCamera(camera, world matrix)`: the camera looks down its entity's -Z |
| `DirectionalLight` + `GlobalTransform` | `AddLight(light, world matrix)`: shines along the entity's -Z |
| `PointLight` + `GlobalTransform` | `AddLight(light, position)` |
| `SpotLight` + `GlobalTransform` | `AddLight(light, world matrix)`: at the entity's position, pointing along its -Z |
| The world's `SceneEnvironment` | `SetEnvironment(environment)`, only when it changed (and the default once removed) |

The component types are the 3D renderer's own (`MeshRenderer`, `Camera`, `DirectionalLight`, `PointLight`, `SpotLight`,
`SceneEnvironment` from `Ion.Extensions.Graphics`); their fields are documented in
[Meshes and materials](/Ion/rendering/3d/meshes-and-materials/), [3D cameras](/Ion/rendering/3d/cameras/) and
[Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/). Frustum culling, batching and instancing happen in the
renderer after submission.

| `Scene3DExtractionOptions` | Default | Meaning |
|---|---|---|
| `RequireVisible` | `false` | Submit only mesh renderers tagged `Visible`. Cameras and lights are extracted unless `Hidden` either way. |

`Scene3DExtractionSystem.LastFrame` is a `Scene3DExtractionStats` (`MeshRenderers`, `Cameras`, `Lights`,
`EnvironmentUpdates`). The renderer's own culling results are in `IRenderer3D.LastFrameStatistics`. For tests and custom
loops, `ExtractAll(dt)` runs the whole extraction outside a schedule.

Measured: 10,000 mesh entities extract in 82 us (against 64 us for submitting the same from flat arrays) with no
allocations per frame.

### The scene environment

A world's ambient light and skybox are a singleton `SceneEnvironment` component, set with `World` extensions:

| Method | Does |
|---|---|
| `world.SetEnvironment(environment)` | Creates the singleton entity the first time (structural: outside a query), then only writes it |
| `world.TryGetEnvironment(out environment)` | Reads it |
| `world.RemoveEnvironment()` | Removes it; the renderer goes back to the default environment |

```csharp
var skybox = assets.Load<ICubemap>("Skybox");
world.SetEnvironment(new SceneEnvironment
{
    AmbientColor = new Color(0x40, 0x48, 0x58),
    AmbientIntensity = 0.15f,
    Skybox = skybox.Handle,
    SkyboxIntensity = 1f,
});
```

### Spawning models

`SpawnModel` instantiates an `IModel` (a loaded glTF, see [glTF models](/Ion/rendering/3d/models-gltf/)) as ordinary
entities:

- a root entity with the `Transform` you pass (and the model's name as an `EntityName`),
- one entity per glTF node with the node's local transform, parented as the nodes are,
- a `MeshRenderer` on the node's entity when it has one primitive, or one child entity per primitive (identity
  transform) when it has several, since an entity holds one mesh renderer.

```csharp
var model = assets.Load<IModel>("Avocado/Avocado.gltf");

// Immediately (outside a query):
var avocado = world.SpawnModel(model, new Transform(Vector3.Zero, Quaternion.Identity, new Vector3(40f)));
world.Add(avocado, new Spin(0.4f));   // move or turn the root to move the whole model

// Or deferred, from anywhere including a query:
var pending = commands.SpawnModel(model, new Transform(new Vector3(2, 0, 0)), new ModelSpawnOptions { CastShadows = false });
commands.Add(pending, new Spin(1f));  // the root is a placeholder until playback; nodes are created then
```

| `ModelSpawnOptions` | Default | Meaning |
|---|---|---|
| `CastShadows` | `true` | Mesh renderers cast shadows |
| `ReceiveShadows` | `true` | Mesh renderers receive shadows |
| `LayerMask` | `1` | Matched against `Camera.CullingMask` |
| `Names` | `true` | Root and node entities get `EntityName`s |

Create options with `new()` or `ModelSpawnOptions.Default`: `default(ModelSpawnOptions)` casts no shadows and has an
empty layer mask. Destroy a spawned model with `DestroyRecursive(root)`; its meshes and materials stay owned by the model
asset.

## Visibility

| Tool | Effect |
|---|---|
| `Hidden` tag | The entity is not extracted (2D sprites, 3D mesh renderers, cameras and lights) |
| `Visible` tag + `RequireVisible = true` | Only tagged sprites (2D) or mesh renderers (3D) are drawn |
| `world.SetHidden(entity, hidden, recursive = true)` | Adds or removes `Hidden` on an entity and, by default, all its descendants |
| `Camera.CullingMask` / `MeshRenderer.LayerMask` | Per-camera visibility in 3D |
| `SpriteExtractionOptions.Cull` | 2D view culling |

:::caution[Hidden is not inherited]
`Hidden` hides only the entity that carries it. A hidden parent's children are still drawn. Use
`world.SetHidden(root, true)` (recursive by default) to hide a spawned model or any subtree. Inherited visibility is
planned, not built.
:::

## In scenes

Each scene has its own world, so a scene that draws entities adds the extraction to its own schedule. The
`ISceneBuilder` overloads include the scene's ECS systems:

```csharp
game.UseScene(Scene.Level, scene => scene.UseEcsRendering().UseSystem<LevelSystem>());
game.UseScene(Scene.Showroom, scene => scene.UseEcsRendering3D().UseSystem<ShowroomSystem>());
```

The root and a scene can both extract: the scene's sprites are submitted at the scene's slot in the stage
(`StageOrder.Scenes`, -500) and the root world's at -300. See [Scenes](/Ion/ecs/scenes/).

## See also

- [Sprites](/Ion/rendering/sprites/) and [2D cameras](/Ion/rendering/cameras-2d/)
- [3D rendering overview](/Ion/rendering/3d/overview/)
- [Transforms](/Ion/ecs/transforms/)
- [Breakout ECS](/Ion/examples/breakout-ecs/), [Cubes](/Ion/examples/cubes/), [Model](/Ion/examples/model/)
