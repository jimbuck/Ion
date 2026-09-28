---
title: Sprites
description: Draw textures, sprite sheets, rectangles and lines with ISpriteBatch, and control sorting, blending, sampling and batching.
sidebar:
  order: 4
---

`ISpriteBatch` is Ion's 2D drawing API. Inject it into a system and call its `Draw*` methods from any `[Render]` step:
textured sprites, rectangles, lines, points and [text](/Ion/rendering/text/). The engine opens a batch for you around
every Render stage, sorts and batches what you drew, and submits it at the end of the frame.

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class PlayerSystem(ISpriteBatch sprites, IAssetManager assets)
{
	private ITexture2D _ship = null!;
	private Vector2 _position = new(400, 300);

	[Init]
	public void Load(GameTime dt) => _ship = assets.Load<ITexture2D>("ship.png");

	[Render]
	public void Draw(GameTime dt)
	{
		sprites.Draw(_ship, _position, new Vector2(64, 64));
		sprites.DrawRect(Color.Red, new Vector2(10, 10), new Vector2(200, 12));   // a health bar
	}
}
```

The 2D renderer lives in `Ion.Extensions.Rendering2D`, and `AddIon()` registers it. In headless runs (`--headless`),
`ISpriteBatch` is a `NullSpriteBatch` that draws nothing but records every call, so the same code runs in tests.

## Coordinates

By default the sprite batch works in **pixels of the render target**: the origin is the top-left corner, x grows to the
right and y grows downwards. One unit is one pixel of the window's framebuffer (or of the
[render target](#render-targets) you draw into).

To draw in world units with a scrolling, zooming camera, pass a transform in `SpriteBatchOptions`. See
[2D cameras](/Ion/rendering/cameras-2d/).

## Drawing textures

Load textures through the asset manager as `ITexture2D` (see [Assets](/Ion/rendering/assets/)), then draw them with
one of the two `Draw` overloads:

```csharp
void Draw(ITexture2D texture, Vector2 position, Vector2 size,
	RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default,
	float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);

void Draw(ITexture2D texture, RectangleF destinationRectangle,
	RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default,
	float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
```

| Parameter | Meaning |
|---|---|
| `position`, `size` or `destinationRectangle` | Where the sprite goes and how large it is, in target units |
| `sourceRectangle` | The part of the texture to draw, **in texels**. `default` draws the whole texture |
| `color` | A tint multiplied with the texture (straight alpha). `default` means no tint (white) |
| `origin` | The pivot as a **fraction of the size**: `(0, 0)` is the top-left (the default), `(0.5f, 0.5f)` the center, `(1, 1)` the bottom-right. The pivot is placed at `position` |
| `rotation` | Radians, clockwise on screen (y down), around the pivot |
| `depth` | A sort key (see [Sorting](#sorting)), not a depth test |
| `options` | `SpriteEffect.FlipHorizontally` and/or `SpriteEffect.FlipVertically` |

```csharp
// Centered on the ship's position, rotated, tinted half-transparent blue, mirrored.
sprites.Draw(_ship, _position, new Vector2(64, 64),
	color: new Color(0.5f, 0.7f, 1f, 0.5f),
	origin: new Vector2(0.5f, 0.5f),
	rotation: _angle,
	options: SpriteEffect.FlipHorizontally);
```

:::caution[Transparent tint]
`Color` has no separate "unset" value, and `default(Color)` is bitwise the same as `Color.Transparent`. For textured
`Draw` calls (and `DrawString`), a `default` or `Color.Transparent` tint therefore draws **untinted**. To draw a sprite
fully transparent, use a color with alpha 0 and a non-zero RGB, such as `new Color(Color.White, 0f)`, or skip the draw.
:::

## Sprite sheets and animation

A sprite sheet is one texture holding many frames. Draw one frame by passing its `sourceRectangle` in texels:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class RunnerSystem(ISpriteBatch sprites, IAssetManager assets)
{
	private const int FrameWidth = 32, FrameHeight = 32, FrameCount = 8;
	private const float FramesPerSecond = 12f;

	private ITexture2D _sheet = null!;
	private float _time;

	[Init]
	public void Load(GameTime dt) => _sheet = assets.Load<ITexture2D>("runner.png");   // 8 frames in a row

	[Update]
	public void Animate(GameTime dt) => _time += dt.Delta;

	[Render]
	public void Draw(GameTime dt)
	{
		var frame = (int)(_time * FramesPerSecond) % FrameCount;
		var source = new RectangleF(frame * FrameWidth, 0, FrameWidth, FrameHeight);

		sprites.Begin(new SpriteBatchOptions { SamplerMode = SpriteSamplerMode.PointClamp });   // crisp pixel art
		sprites.Draw(_sheet, new Vector2(200, 200), new Vector2(FrameWidth * 4, FrameHeight * 4), sourceRectangle: source);
		sprites.End();
	}
}
```

Ion has no sprite sheet or texture atlas asset type: the frame rectangles are yours to define. For pixel art, use
`SpriteSamplerMode.PointClamp` so scaled frames stay crisp and neighbouring frames do not bleed in.

### Animation with the ECS

With the ECS module, give an entity a `Sprite` and a `SpriteAnimation` and the built-in `SpriteAnimationSystem` advances
it every Update (at `StageOrder.SpriteAnimation`, 400, after your own Update steps) by setting `Sprite.Source` to the
current frame. The sprite extraction then draws it.

```csharp
using System.Numerics;

using Arch.Core;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

public sealed class SpawnSystem(World world, IAssetManager assets)
{
	[Init]
	public void Spawn(GameTime dt)
	{
		var sheet = assets.Load<ITexture2D>("runner.png");
		var frames = new RectangleF[8];
		for (var i = 0; i < frames.Length; i++) frames[i] = new RectangleF(i * 32, 0, 32, 32);

		world.Create(
			new Transform2D(new Vector2(200, 200)),
			new Sprite(sheet, size: new Vector2(128, 128)),
			new SpriteAnimation(frames, framesPerSecond: 12f));    // loops by default
	}
}
```

`SpriteAnimation` has `Loop`, `Paused`, `Time`, `Frame` and `IsFinished`. See [ECS rendering](/Ion/ecs/ecs-rendering/)
for the sprite components and the extraction.

## Shapes

Rectangles, lines and points use a built-in white texture, so they batch with each other:

```csharp
sprites.DrawRect(Color.DarkGreen, new RectangleF(0, 500, 800, 100));
sprites.DrawRect(Color.Orange, new Vector2(400, 300), new Vector2(40, 40), origin: new Vector2(0.5f), rotation: _angle);
sprites.DrawLine(Color.White, new Vector2(0, 0), new Vector2(800, 600), thickness: 2f);
sprites.DrawLine(Color.Yellow, start: new Vector2(100, 100), length: 50f, angle: MathF.PI / 4);
sprites.DrawPoint(Color.Red, new Vector2(320, 240), new Vector2(4, 4));   // centered on the position

// Extensions available on every ISpriteBatch:
sprites.DrawCircle(Color.Cyan, center: new Vector2(600, 200), radius: 40f, thickness: 2f);   // segments: 0 picks from the radius
sprites.DrawRectOutline(Color.White, new RectangleF(50, 50, 200, 100), thickness: 3f);
```

Unlike textured draws, `DrawRect`, `DrawLine` and `DrawPoint` take the color as given: `default` is transparent.

## Segments: Begin and End

The sprite batch system opens an outermost segment with `default` options at the start of every Render stage and
closes it at the end. Inside it, wrap draws in `Begin(options)` and `End()` to change render state for a group of
sprites. Segments nest: `End()` returns to the enclosing segment's options. Every `Begin` needs an `End`.

```csharp
sprites.Draw(_background, Vector2.Zero, new Vector2(1280, 720));      // default options

sprites.Begin(new SpriteBatchOptions { BlendMode = SpriteBlendMode.Additive });
foreach (var p in _particles) sprites.Draw(_spark, p.Position, new Vector2(8), origin: new Vector2(0.5f));
sprites.End();

sprites.DrawString(_font, "Score", new Vector2(16), Color.White);     // back to default options
```

Segments are drawn in the order they were opened, and each is sorted on its own.

`SpriteBatchOptions` is a record struct. `default` means: deferred sort, premultiplied alpha blending, linear clamped
sampling, no transform (pixels) and no scissor.

| Option | Type | Default | Meaning |
|---|---|---|---|
| `SortMode` | `SpriteSortMode` | `Deferred` | The draw order inside the segment |
| `BlendMode` | `SpriteBlendMode` | `AlphaBlend` | How sprites combine with the target |
| `SamplerMode` | `SpriteSamplerMode` | `LinearClamp` | Filtering and addressing |
| `Transform` | `Matrix3x2?` | `null` | World to target pixels (a camera). `null`: pixels |
| `Scissor` | `Rectangle?` | `null` | Clip to this rectangle in target pixels (origin top-left). `null`: the whole target |

`SpriteBatchOptions.WithCamera(camera, viewportSize, sortMode)` builds options with a camera's transform.

### Sorting

| `SpriteSortMode` | Order | Draw calls |
|---|---|---|
| `Deferred` (default) | Submission order (painter's algorithm) | One per run of consecutive sprites with the same texture |
| `Texture` | Grouped by texture, submission order inside a texture | One per texture |
| `FrontToBack` | By `depth` ascending: depth 0 is drawn first (behind) | Depends on texture interleaving |
| `BackToFront` | By `depth` descending: depth 0 is drawn last (in front) | Depends on texture interleaving |

The depth sorts are stable: sprites with equal depth keep submission order. `depth` is only a sort key (clamped to 0 to 1
on the GPU); there is no depth test between sprites.

Use `Texture` when sprites of different textures do not overlap (a particle field, a tile map with separate layers): it
gives the fewest draw calls. Keep `Deferred` when order matters and you already draw in back-to-front order.

### Blending

Textures loaded by the 2D renderer are **premultiplied** at load (so are its glyph atlas and anything rendered into a
render target with `AlphaBlend`). Tint colors are straight alpha; the shader premultiplies them.

| `SpriteBlendMode` | Formula | Use for |
|---|---|---|
| `AlphaBlend` (default) | Premultiplied alpha | Almost everything |
| `Additive` | Premultiplied source added to the target | Glows, sparks, lasers |
| `Opaque` | Source replaces the target | Backgrounds, tiles without transparency |
| `NonPremultiplied` | `SrcAlpha, OneMinusSrcAlpha` with the tint untouched | Textures whose color is not premultiplied (for example created with `TextureFactory.Create(..., premultiply: false)`) |

### Sampling

| `SpriteSamplerMode` | Filtering | Addressing |
|---|---|---|
| `LinearClamp` (default) | Bilinear (trilinear with mips) | Clamped to the edge |
| `PointClamp` | Nearest neighbour | Clamped (pixel art) |
| `LinearWrap` | Bilinear | Repeating |
| `PointWrap` | Nearest neighbour | Repeating |

With a `Wrap` mode, a source rectangle larger than the texture tiles it:

```csharp
sprites.Begin(new SpriteBatchOptions { SamplerMode = SpriteSamplerMode.PointWrap });
sprites.Draw(_grass, new Vector2(0, 600), new Vector2(1280, 120), sourceRectangle: new RectangleF(0, 0, 1280, 120));
sprites.End();
```

### Scissor

```csharp
// Only the inside of the panel is drawn.
sprites.Begin(new SpriteBatchOptions { Scissor = new Rectangle(20, 20, 300, 200) });
foreach (var line in _log) sprites.DrawString(_font, line.Text, line.Position, Color.White);
sprites.End();
```

## Render targets

`SetRenderTarget` renders the following draws into a texture instead of the frame. `RenderTarget2D` (in
`Ion.Extensions.Rendering2D`) is a texture the batch can render into and draw like any other `ITexture2D`, which is
useful for minimaps, low-resolution pixel art upscaling, and cached layers.

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;

public sealed class PixelArtSystem(IGraphicsFrame frame, ISpriteBatch sprites) : IDisposable
{
	private RenderTarget2D? _canvas;

	[Init]
	public void Init(GameTime dt) => _canvas = new RenderTarget2D(frame.Device, 320, 180);   // the device exists from Init order 0

	[Render]
	public void Draw(GameTime dt)
	{
		if (_canvas is null) return;

		sprites.SetRenderTarget(_canvas.Texture, clearColor: Color.CornflowerBlue);   // draws below use 320x180 pixels
		sprites.DrawRect(Color.Yellow, new Vector2(150, 80), new Vector2(20, 20));
		sprites.SetRenderTarget(null);                                                 // back to the frame

		sprites.Begin(new SpriteBatchOptions { SamplerMode = SpriteSamplerMode.PointClamp });
		sprites.Draw(_canvas, Vector2.Zero, new Vector2(frame.Width, frame.Height));   // upscale
		sprites.End();
	}

	[Destroy]
	public void Destroy(GameTime dt) => Dispose();

	public void Dispose()
	{
		_canvas?.Dispose();
		_canvas = null;
	}
}
```

- The target stays set until you change it or the outermost segment ends. Segments drawn into a target use its pixel
  space.
- `clearColor` clears the target before the first draw into it; `null` keeps its contents from earlier frames.
- A render target drawn with `AlphaBlend` holds premultiplied color, so draw it back with `AlphaBlend`.
- `RenderTarget2D` objects you create are yours to dispose, in a `[Destroy]` step (default order), before the device
  goes. Textures from the asset manager and `TextureFactory` are released by the renderer.

## Textures from memory

`TextureFactory` (in `Ion.Extensions.Rendering2D`) creates sprite textures from RGBA8 pixels: procedural textures,
generated atlases, noise. Textures are premultiplied and get a full mip chain, like loaded ones, and they are released
with the renderer's other GPU resources.

```csharp
using Microsoft.Extensions.DependencyInjection;

using Ion;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;

public sealed class CheckerSystem(IServiceProvider services, ISpriteBatch sprites)
{
	private ITexture2D? _checker;

	[Init]
	public void Init(GameTime dt)
	{
		var pixels = new byte[8 * 8 * 4];
		for (var i = 0; i < 64; i++)
		{
			var on = ((i % 8) + (i / 8)) % 2 == 0;
			pixels[i * 4 + 0] = pixels[i * 4 + 1] = pixels[i * 4 + 2] = on ? (byte)255 : (byte)40;
			pixels[i * 4 + 3] = 255;
		}

		// TextureFactory exists only with the 2D renderer (not under --headless); fall back to a size-only texture.
		_checker = services.GetService<TextureFactory>()?.Create("checker", 8, 8, pixels) ?? new NullTexture2D("checker", 8, 8);
	}

	[Render]
	public void Draw(GameTime dt) => sprites.Draw(_checker!, new System.Numerics.Vector2(32, 32), new System.Numerics.Vector2(256, 256));
}
```

The Sprites100k sample uses the same pattern for its 16 generated textures.

## Batching and performance

The 2D renderer is designed so that tens of thousands of sprites cost little CPU:

- Each sprite is recorded as a 40-byte instance (the quad's corner and two edge vectors with position, scale and
  rotation folded in, a 16-bit UV rectangle, an RGBA8 color and a depth). An unrotated sprite needs no sine or cosine.
- All instances of a frame go into one array, uploaded once per frame into the frame slot's instance buffer (a ring of
  `FramesInFlight` buffers, so nothing waits for the GPU).
- Each run of consecutive instances with the same texture is one instanced draw call.
- Recording allocates nothing once the arrays have grown to the frame's size.

Measured on a 2.1 GHz Xeon VM (`SpriteBatchBenchmarks`): about 5.7 ns per sprite with one texture and 6.9 ns with 16
textures, 0 bytes allocated per frame.

To keep draw calls low:

- **Group by texture.** With `Deferred`, drawing A, B, A, B costs four draw calls; A, A, B, B costs two. Use
  `SpriteSortMode.Texture` when order does not matter.
- **Pack small images into one texture** and draw them with source rectangles.
- **Avoid needless segments.** Each `Begin`/`End`, `SetRenderTarget` or options change closes a segment, and runs never
  span segments.
- **Draw text in few fonts.** Glyphs share atlas pages, so text batches well with other text of the same font.

### The Sprites100k stress test

`Ion.Examples.Sprites100k` draws 100,000 moving 16x16 sprites across 16 textures with `SpriteSortMode.Texture`, which
is 16 draw calls per frame. It logs frames per second, frame time and draw calls every second:

```bash
dotnet run --project Ion.Examples/Ion.Examples.Sprites100k -c Release
dotnet run --project Ion.Examples/Ion.Examples.Sprites100k -c Release -- --Sprites:Count=20000 --Sprites:Textures=4
```

Recording the 100,000 sprites takes about 2.3 ms of CPU. On CPU rasterizers (Mesa lavapipe at 1280x720) a whole frame
takes about 103 ms headless, because rasterizing 25 million blended pixels dominates; on a real GPU the recording cost
is what remains. See [the Sprites100k example](/Ion/examples/sprites-100k/).

### Statistics

The sprite batch reports what it drew in the last frame through `ISpriteBatchStatistics`:

```csharp
if (sprites is ISpriteBatchStatistics statistics)
{
	var last = statistics.LastFrameStatistics;   // Frame, DrawCalls, Sprites (quads incl. glyphs), Triangles
	_debugText = $"{last.Sprites} sprites in {last.DrawCalls} draw calls";
}
```

The [metrics module](/Ion/tooling/metrics-and-tracing/) reads the same numbers into `FrameStats.DrawCalls`, `Sprites` and
`Triangles` every frame.

## When the sprite batch is not the RHI one

`ISpriteBatch` has two implementations:

| Implementation | When | Behavior |
|---|---|---|
| `SpriteBatch` (`Ion.Extensions.Rendering2D`) | Windowed, or `--headless-render` | Draws on the GPU. Textures must come from its loader, `TextureFactory` or `RenderTarget2D`; fonts from its font loader |
| `NullSpriteBatch` (`Ion.Extensions.Graphics.Null`) | `--headless` | Draws nothing; `LastFrame` records counts (`DrawCalls`, `Sprites`, `Strings`, `Rects`, `Points`, `Lines`, `Glyphs`) and the last 256 commands |

Code written against `ISpriteBatch`, `ITexture2D` and `IFontSet` runs on both. `SetRenderTarget` and `RenderTarget2D`
need the GPU implementation.

## See also

- [Text](/Ion/rendering/text/): fonts and `DrawString`
- [2D cameras](/Ion/rendering/cameras-2d/): world coordinates, scrolling and zoom
- [Assets](/Ion/rendering/assets/): loading and hot reloading textures
- [ECS rendering](/Ion/ecs/ecs-rendering/): `Sprite`, `SpriteAnimation` and the sprite extraction
- [Sprites100k example](/Ion/examples/sprites-100k/)
