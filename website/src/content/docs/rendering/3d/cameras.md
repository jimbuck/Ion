---
title: 3D cameras
description: Place perspective and orthographic cameras, render several views per frame with viewports, priorities and layers, and render into textures.
sidebar:
  order: 5
---

A 3D camera is a `Camera` struct (projection, near and far planes, viewport, clear mode, priority, target, culling
mask) placed by a `Transform` or world matrix. Like everything else in the 3D renderer it is submitted every frame. A
camera looks down its local **-Z** axis with **+Y** up.

```csharp
[Render]
public void Draw(GameTime dt)
{
	var camera = new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 200f, ClearColor = new Color(0x87, 0xA9, 0xD6) };
	renderer.SetCamera(camera, Transform.LookAt(eye: new Vector3(0, 6, 12), target: Vector3.Zero));
	// ... lights and meshes
}
```

Without a camera, nothing 3D is drawn (the frame is still cleared to `Ion:Graphics:ClearColor`, and 2D still draws).

## Submitting cameras

| Method | Use |
|---|---|
| `SetCamera(camera, transform)` | Replaces this frame's cameras with one. The simple case |
| `AddCamera(camera, world)` | Adds a camera placed by a world matrix. Use it for several cameras |

The ECS extraction uses `AddCamera` for every entity with a `Camera` component (placed by its `GlobalTransform`).

:::caution
`SetCamera` removes every camera added earlier in the frame, including those the ECS extraction added at
`StageOrder.Extract`. In an ECS game, give the camera an entity instead of calling `SetCamera` from a Render step.
:::

## Camera fields

`new Camera()` is a perspective camera with a 60 degree vertical field of view, near 0.1, far 1000, the whole target,
cleared to black, every layer. Zero fields mean defaults, so even `default(Camera)` renders.

| Field | Default | Meaning |
|---|---|---|
| `Projection` | `Perspective` | `ProjectionKind.Perspective` or `ProjectionKind.Orthographic` |
| `FieldOfView` | 60 degrees (`PI / 3`); 0 means the default | Vertical field of view in radians (perspective) |
| `OrthographicSize` | `5`; 0 means the default | Half the view height in world units (orthographic) |
| `Near` | `0.1`; 0 means the default | Near plane distance |
| `Far` | `1000` when not beyond `Near` | Far plane distance |
| `Viewport` | whole target; empty means whole target | The part of the target rendered into, normalized: `(0, 0)` top-left, `(1, 1)` bottom-right |
| `ClearColor` | `Color.Black` | The background with `CameraClear.Color` |
| `Clear` | `CameraClear.Color` | What is drawn behind the scene |
| `Priority` | `0` | Lower priorities render first; higher ones draw on top |
| `Target` | `RenderTargetHandle.None` (the frame) | Render into a render target instead |
| `CullingMask` | every layer; 0 means every layer | Layers this camera draws |

Factory helpers:

```csharp
var perspective = Camera.CreatePerspective(fieldOfView: MathF.PI / 3, near: 0.1f, far: 500f);
var ortho = Camera.CreateOrthographic(halfHeight: 10f);
```

### Depth precision

Ion uses standard depth (near maps to 0, far to 1, cleared to 1) in a `Depth32Float` buffer. Reverse-Z is not used,
because the OpenGL ES backend has to remap depth to GL's -1 to 1 range, which cancels its benefit. Keep `Near` as large
as your scene allows (0.1 to 0.5 for human-scale scenes) to avoid z-fighting on distant surfaces.

## Placing and moving cameras

The camera's position and orientation come from the transform or matrix you submit it with. `Transform.LookAt` is the
usual way to aim one:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class OrbitCameraSystem(IRenderer3D renderer)
{
	public float StartAngle { get; set; } = 0.6f;
	public float OrbitSpeed { get; set; } = 0.2f;

	[Render]
	public void Draw(GameTime dt)
	{
		var angle = StartAngle + (float)dt.Elapsed.TotalSeconds * OrbitSpeed;
		var eye = new Vector3(MathF.Cos(angle) * 34f, 20f, MathF.Sin(angle) * 34f);
		renderer.SetCamera(new Camera { FieldOfView = MathF.PI / 4, Near = 0.5f, Far = 200f }, Transform.LookAt(eye, Vector3.Zero));
	}
}
```

For a first-person camera, keep yaw and pitch and build the rotation from them:

```csharp
_yaw -= input.MouseDelta.X * 0.003f;
_pitch = Math.Clamp(_pitch - input.MouseDelta.Y * 0.003f, -1.5f, 1.5f);
var rotation = Quaternion.CreateFromYawPitchRoll(_yaw, _pitch, 0f);
_camera = new Transform(_position, rotation);

// Move along the camera's axes (Forward is -Z, Right is +X).
if (input.Down(Key.W)) _position += _camera.Forward * speed * dt.Delta;
if (input.Down(Key.D)) _position += _camera.Right * speed * dt.Delta;
```

Set `window.IsMouseGrabbed = true` to capture the mouse for mouse look (see [Windowing](/Ion/rendering/windowing/)).

A camera's scale is ignored: the view matrix is built from the orthonormalized rotation and the translation, so a camera
parented under a scaled entity still renders correctly.

## Orthographic cameras

An orthographic camera has no perspective: parallel lines stay parallel and objects keep their size with distance. Use
it for top-down and isometric views, level editors and 2.5D games.

```csharp
// An isometric view: 20 units high, looking down at 35 degrees from the diagonal.
var iso = Camera.CreateOrthographic(halfHeight: 10f, near: 0.1f, far: 200f);
renderer.SetCamera(iso, Transform.LookAt(new Vector3(20, 16, 20), Vector3.Zero));
```

`OrthographicSize` is half the view height; the width follows the viewport's aspect ratio.

## Clearing

| `CameraClear` | Behind the scene |
|---|---|
| `Color` | `ClearColor` |
| `Skybox` | The environment's skybox (`SceneEnvironment.Skybox`); `ClearColor` when there is none |
| `None` | Nothing: what earlier cameras (or passes) drew stays. The depth is still cleared |

The first camera that renders into a target clears the whole target; later cameras clear only their own viewport
(`Color` or `Skybox`) or nothing (`None`).

## Several cameras

Submit several cameras with `AddCamera`. They render in ascending `Priority` (then submission order), each culled,
sorted and batched on its own. Statistics (`Visible`, `Culled`) are summed over cameras. Up to
`Ion:Rendering3D:MaxCameras` (8 by default) render per frame; extra cameras are dropped.

### Split screen

```csharp
var left = new Camera { Viewport = new RectangleF(0f, 0f, 0.5f, 1f), ClearColor = Color.DarkSlateGray };
var right = new Camera { Viewport = new RectangleF(0.5f, 0f, 0.5f, 1f), ClearColor = Color.DarkSlateGray };
renderer.AddCamera(left, _player1Camera.ToMatrix());
renderer.AddCamera(right, _player2Camera.ToMatrix());
```

Each viewport gets its own aspect ratio, so the projection is not stretched.

### Picture in picture

A rear-view mirror drawn on top of the main view: a higher priority draws later, and a small viewport keeps it in the
corner.

```csharp
renderer.AddCamera(new Camera { Priority = 0, Clear = CameraClear.Skybox }, _driver.ToMatrix());

var mirror = new Transform(_driver.Position, _driver.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI));
renderer.AddCamera(new Camera { Priority = 1, Viewport = new RectangleF(0.35f, 0.02f, 0.3f, 0.2f), ClearColor = Color.Black }, mirror.ToMatrix());
```

## Layers and culling masks

`MeshRenderer.LayerMask` puts an object on layers (bits), and `Camera.CullingMask` selects the layers a camera draws.
An object is drawn when the two share a bit. Objects default to layer 1 (bit 0); cameras default to every layer.

```csharp
const uint World = 1 << 0, FirstPerson = 1 << 1, Minimap = 1 << 2;

renderer.Submit(new MeshRenderer(_arms, _skin) { LayerMask = FirstPerson, CastShadows = false }, armsWorld);
renderer.Submit(new MeshRenderer(_marker, _red) { LayerMask = Minimap }, markerWorld);

renderer.AddCamera(new Camera { CullingMask = World | FirstPerson }, _eye.ToMatrix());
renderer.AddCamera(new Camera { CullingMask = World | Minimap, Priority = 1, Viewport = new RectangleF(0.75f, 0f, 0.25f, 0.25f) }, _above.ToMatrix());
```

Cameras also frustum-cull every object against their view, so objects outside the view cost only a bounds test.

## Render targets

A camera can render into a texture instead of the frame. Create a render target once, point a camera at it, and use
its color texture on a material (a security monitor, a portal) or in the sprite batch.

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;
using Ion.Extensions.Rendering3D;

public sealed class MonitorSystem(Renderer3D renderer, ISpriteBatch sprites)
{
	private RenderTargetHandle _feed;
	private MeshHandle _screen;
	private MaterialHandle _screenMaterial;
	private RenderTarget2D? _feedSprite;

	[Init]
	public void Init(GameTime dt)
	{
		_feed = renderer.CreateRenderTarget(512, 288, "security feed");   // color plus its own depth
		_screen = renderer.CreateMesh(MeshPrimitives.Plane(2f));
		_screenMaterial = renderer.CreateMaterial(new UnlitMaterial(Color.White, renderer.GetRenderTargetTexture(_feed)));

		// The same texture as a sprite (the caller keeps owning the wrapped texture).
		if (renderer.GetTexture(renderer.GetRenderTargetTexture(_feed)) is { } texture) _feedSprite = new RenderTarget2D(texture);
	}

	[Render]
	public void Draw(GameTime dt)
	{
		// The security camera renders first into the target...
		renderer.AddCamera(new Camera { Target = _feed, Priority = -1, ClearColor = Color.Black }, Transform.LookAt(new Vector3(5, 4, 5), Vector3.Zero).ToMatrix());
		// ...then the main camera sees the monitor showing it.
		renderer.AddCamera(new Camera(), Transform.LookAt(new Vector3(0, 2, 6), Vector3.Zero).ToMatrix());
		renderer.Draw(_screen, _screenMaterial, Matrix4x4.CreateRotationX(MathF.PI / 2) * Matrix4x4.CreateTranslation(0, 1.5f, -2));

		if (_feedSprite is not null) sprites.Draw(_feedSprite, new Vector2(16, 16), new Vector2(256, 144));
	}
}
```

- Create render targets once the device exists (an `[Init]` step with the default order or later); earlier throws.
- Render targets are `Rgba8Unorm` with their own depth buffer, owned and released by the renderer.
- Give cameras that fill a render target a lower `Priority` than the cameras that sample it, so the texture is ready
  when it is used.
- Without a GPU, render targets are placeholders and the camera still runs its CPU pipeline.

## Projection and picking

`Camera` exposes its matrices, which you need for mouse picking or placing 2D labels over 3D objects:

| Method | Returns |
|---|---|
| `camera.GetProjectionMatrix(aspectRatio)` | Right-handed projection into the RHI's clip space (depth 0 at near, 1 at far) |
| `Camera.GetViewMatrix(transform)` / `Camera.GetViewMatrix(world)` | The view matrix (scale ignored) |
| `EffectiveNear`, `EffectiveFar`, `EffectiveViewport` | The values with defaults applied |

A ray from the mouse position into the scene:

```csharp
using System.Numerics;

using Ion.Extensions.Graphics;

public static class Picking
{
	/// <summary>A world-space ray through a pixel (origin top-left) of a full-target camera.</summary>
	public static (Vector3 Origin, Vector3 Direction) ScreenRay(in Camera camera, in Transform placement, Vector2 pixel, Vector2 targetSize)
	{
		var viewProjection = Camera.GetViewMatrix(placement) * camera.GetProjectionMatrix(targetSize.X / targetSize.Y);
		Matrix4x4.Invert(viewProjection, out var inverse);

		// Pixels to clip space: x -1..1 left to right, y 1..-1 top to bottom, depth 0 (near) and 1 (far).
		var x = pixel.X / targetSize.X * 2f - 1f;
		var y = 1f - pixel.Y / targetSize.Y * 2f;
		var near = Vector4.Transform(new Vector4(x, y, 0f, 1f), inverse);
		var far = Vector4.Transform(new Vector4(x, y, 1f, 1f), inverse);
		var origin = new Vector3(near.X, near.Y, near.Z) / near.W;
		var end = new Vector3(far.X, far.Y, far.Z) / far.W;
		return (origin, Vector3.Normalize(end - origin));
	}
}
```

Intersect the ray with your objects' bounds (`Aabb`, `BoundingSphere`), or with the physics world's ray casts (see
[Queries and events](/Ion/physics/queries-and-events/)). The mouse position is in window coordinates; scale it to
framebuffer pixels on HiDPI displays (see [2D cameras](/Ion/rendering/cameras-2d/#zoom-and-mouse-picking)).

`Frustum.FromMatrix(viewProjection)` builds the camera's frustum for your own culling: `Contains(point)`,
`Intersects(aabb)` and `Intersects(sphere)`.

## With the ECS

A camera entity has a `Transform` and a `Camera` component; the extraction adds it with its world matrix every frame.
Move the camera by changing its `Transform`:

```csharp
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public sealed class CameraRigSystem(World world)
{
	private Entity _camera;

	[Init]
	public void Init(GameTime dt) =>
		_camera = world.Create(Transform.LookAt(new Vector3(0, 5, 10), Vector3.Zero),
			new Camera { FieldOfView = MathF.PI / 4, Near = 0.1f, Far = 100f, Clear = CameraClear.Skybox },
			new EntityName("camera"));

	[Update]
	public void Move(GameTime dt)
	{
		var t = (float)dt.Elapsed.TotalSeconds * 0.15f;
		var eye = new Vector3(MathF.Sin(t) * 6.5f, 2f, MathF.Cos(t) * 6.5f);
		if (world.IsAlive(_camera)) world.Get<Transform>(_camera) = Transform.LookAt(eye, new Vector3(0, 1.1f, 0));
	}
}
```

Parent the camera under another entity (a vehicle, a player) to have it follow; see [Transforms](/Ion/ecs/transforms/).
Cameras tagged `Hidden` are not extracted.

## See also

- [3D rendering overview](/Ion/rendering/3d/overview/)
- [Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/): skyboxes for `CameraClear.Skybox`
- [2D cameras](/Ion/rendering/cameras-2d/)
- [Cubes example](/Ion/examples/cubes/)
