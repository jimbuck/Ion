---
title: 2D cameras
description: Scroll, zoom and rotate a 2D view with Camera2D, convert between screen and world coordinates, and combine world and screen-space drawing.
sidebar:
  order: 8
---

By default the [sprite batch](/Ion/rendering/sprites/) draws in pixels: `(0, 0)` is the top-left of the window and one
unit is one pixel. For a world larger than the screen (a scrolling level, a zoomable map) use a `Camera2D`. It turns
world coordinates into target pixels, and you pass its transform to the sprite batch for the draws that belong to the
world.

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class WorldSystem(ISpriteBatch sprites, IGraphicsFrame frame)
{
	private readonly Camera2D _camera = new() { Position = new Vector2(0, 0), Zoom = 2f };

	[Render]
	public void Draw(GameTime dt)
	{
		var viewport = new Vector2(frame.Width, frame.Height);

		// World: centered on the camera, zoomed 2x.
		sprites.Begin(SpriteBatchOptions.WithCamera(_camera, viewport));
		sprites.DrawRect(Color.Green, new Vector2(-50, -50), new Vector2(100, 100));   // a 100x100 square at the world origin
		sprites.End();

		// Screen: the HUD stays in pixels.
		sprites.DrawRect(Color.Black, new Vector2(10, 10), new Vector2(200, 20));
	}
}
```

`IGraphicsFrame` gives the size of the target in pixels. It is registered whenever a GPU backend is (windowed or
`--headless-render`). If your code must also run under `--headless`, use `IWindow.Size` instead (the null window's size
equals its target size).

## Camera2D

`Camera2D` is a small class in `Ion.Extensions.Graphics`:

| Member | Default | Meaning |
|---|---|---|
| `Position` | `(0, 0)` | The world point shown at the **center** of the viewport |
| `Zoom` | `1` | Scale factor: 1 is one world unit per pixel, 2 shows everything twice as large |
| `Rotation` | `0` | The rotation of the view, in radians |
| `GetTransform(viewportSize)` | | The world-to-pixel `Matrix3x2` for a viewport of that many pixels |
| `ScreenToWorld(screen, viewportSize)` | | A pixel position (a mouse position) to world coordinates |
| `WorldToScreen(world, viewportSize)` | | A world position to pixels |

The transform is: translate by `-Position`, rotate by `-Rotation`, scale by `Zoom`, then translate by half the
viewport. The world keeps the sprite batch's axes: x to the right, y **down**.

`SpriteBatchOptions.WithCamera(camera, viewportSize, sortMode)` is shorthand for
`new SpriteBatchOptions { SortMode = sortMode, Transform = camera.GetTransform(viewportSize) }`. Set other options with
a `with` expression:

```csharp
var options = SpriteBatchOptions.WithCamera(_camera, viewport) with { SamplerMode = SpriteSamplerMode.PointClamp };
```

## Following a target

Move the camera in Update and read it in Render. Smoothing toward the target gives a softer follow:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class FollowCameraSystem(ISpriteBatch sprites, IGraphicsFrame frame, PlayerState player)
{
	private readonly Camera2D _camera = new() { Zoom = 3f };

	[Update]
	public void Follow(GameTime dt)
	{
		// Exponential smoothing, independent of the frame rate.
		var t = 1f - MathF.Exp(-8f * dt.Delta);
		_camera.Position = Vector2.Lerp(_camera.Position, player.Position, t);
	}

	[Render]
	public void Draw(GameTime dt)
	{
		var viewport = new Vector2(frame.Width, frame.Height);
		sprites.Begin(SpriteBatchOptions.WithCamera(_camera, viewport) with { SamplerMode = SpriteSamplerMode.PointClamp });
		sprites.DrawRect(Color.Orange, player.Position, new Vector2(16, 16), origin: new Vector2(0.5f));
		sprites.End();
	}
}

public sealed class PlayerState
{
	public Vector2 Position { get; set; }
}
```

:::tip[Pixel-perfect scrolling]
For pixel art at an integer zoom, round the camera position to whole world pixels before drawing
(`new Vector2(MathF.Round(p.X), MathF.Round(p.Y))`) and use `SpriteSamplerMode.PointClamp`. Sub-pixel camera positions
make sprites shimmer as the view scrolls.
:::

## Zoom and mouse picking

`ScreenToWorld` inverts the camera transform, so you can find what the mouse points at and zoom toward the cursor:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class MapCameraSystem(IInputState input, IGraphicsFrame frame, IWindow window)
{
	private readonly Camera2D _camera = new();

	public Vector2 Hovered { get; private set; }

	[Update]
	public void Update(GameTime dt)
	{
		var viewport = new Vector2(frame.Width, frame.Height);
		// Mouse positions are in window coordinates; scale them to framebuffer pixels (they differ on HiDPI displays).
		var mouse = input.MousePosition * (viewport / Vector2.Max(window.Size, Vector2.One));

		// Zoom around the cursor: keep the world point under it in place.
		if (input.WheelDelta != 0)
		{
			var before = _camera.ScreenToWorld(mouse, viewport);
			_camera.Zoom = Math.Clamp(_camera.Zoom * MathF.Pow(1.1f, input.WheelDelta), 0.25f, 8f);
			var after = _camera.ScreenToWorld(mouse, viewport);
			_camera.Position += before - after;
		}

		// Drag with the right mouse button.
		if (input.Down(MouseButton.Right)) _camera.Position -= input.MouseDelta / _camera.Zoom;

		Hovered = _camera.ScreenToWorld(mouse, viewport);
	}
}
```

Dragging by `MouseDelta / Zoom` is exact without rotation; with a rotated camera, rotate the delta too. See
[Keyboard and mouse](/Ion/interaction/input/keyboard-and-mouse/) for the input API.

## Split screen

Each segment can have its own camera and scissor rectangle, which is enough for split screen:

```csharp
var half = new Vector2(frame.Width / 2f, frame.Height);

// Left half: player 1. The transform centers the view in a half-width viewport; shift it into place.
var left = _camera1.GetTransform(half);
sprites.Begin(new SpriteBatchOptions { Transform = left, Scissor = new Rectangle(0, 0, (int)half.X, (int)half.Y) });
DrawWorld();
sprites.End();

// Right half: player 2, shifted right by half the width.
var right = _camera2.GetTransform(half) * Matrix3x2.CreateTranslation(half.X, 0);
sprites.Begin(new SpriteBatchOptions { Transform = right, Scissor = new Rectangle((int)half.X, 0, (int)half.X, (int)half.Y) });
DrawWorld();
sprites.End();
```

Alternatively, draw each view into its own `RenderTarget2D` (see [Render targets](/Ion/rendering/sprites/#render-targets))
and draw the targets side by side.

## Culling off-screen sprites

The sprite batch draws everything you submit. For large worlds, skip what is outside the view yourself. The visible
world rectangle is the inverse transform of the viewport's corners:

```csharp
var viewport = new Vector2(frame.Width, frame.Height);
var topLeft = _camera.ScreenToWorld(Vector2.Zero, viewport);
var bottomRight = _camera.ScreenToWorld(viewport, viewport);
var view = new RectangleF(topLeft, bottomRight - topLeft);   // exact when the camera is not rotated

foreach (var tile in _tiles)
{
	if (view.Intersects(tile.Bounds)) sprites.Draw(_tileset, tile.Bounds, tile.Source);
}
```

## With the ECS

The ECS sprite extraction uses a camera entity: tag one entity with `MainCamera` and give it a `Camera2D` component. The
extraction draws every `Sprite` with that camera's transform (world units are window pixels when there is none) and
culls sprites outside its view.

```csharp
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public sealed class CameraSetupSystem(World world)
{
	private Entity _camera;

	[Init]
	public void Init(GameTime dt) => _camera = world.Create(new MainCamera(), new Camera2D { Zoom = 2f });

	[Update]
	public void Follow(GameTime dt) => world.Get<Camera2D>(_camera).Position = new Vector2(100, 50);
}
```

`Camera2D` is a class, so `world.Get<Camera2D>(entity)` returns the instance and you change it in place. See
[ECS rendering](/Ion/ecs/ecs-rendering/) for the extraction's options.

## 2D cameras and 3D cameras

`Camera2D` only affects the sprite batch. The 3D renderer has its own `Camera` struct (perspective or orthographic,
placed by a `Transform`). An orthographic 3D camera looking down an axis is another way to build a 2D-style view with
meshes and lighting. See [3D cameras](/Ion/rendering/3d/cameras/).

## See also

- [Sprites](/Ion/rendering/sprites/): segments, `SpriteBatchOptions`, scissor
- [Keyboard and mouse](/Ion/interaction/input/keyboard-and-mouse/)
- [ECS rendering](/Ion/ecs/ecs-rendering/)
- [3D cameras](/Ion/rendering/3d/cameras/)
