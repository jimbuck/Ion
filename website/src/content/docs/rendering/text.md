---
title: Text
description: Load TrueType and OpenType fonts as IFontSet, create sizes with CreateStyle, and draw and measure text with the sprite batch.
sidebar:
  order: 5
---

Ion draws text with the [sprite batch](/Ion/rendering/sprites/). Fonts are TrueType or OpenType files rasterized at run
time by [FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) into a glyph atlas owned by the 2D renderer.
There are no pre-baked bitmap fonts and no content pipeline step: put a `.ttf` or `.otf` file in your assets folder and
load it.

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

public sealed class HudSystem(ISpriteBatch sprites, IAssetManager assets)
{
	private IFont _font = null!;
	private int _score;

	[Init]
	public void Load(GameTime dt) => _font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(24);

	[Render]
	public void Draw(GameTime dt) => sprites.DrawString(_font, $"Score: {_score}", new Vector2(20, 20), Color.White);
}
```

## Font sets and fonts

Two types are involved:

| Type | What it is | How you get one |
|---|---|---|
| `IFontSet` | One or more font files (a face plus fallbacks) from which fonts of any size are made. An asset | `assets.Load<IFontSet>("MyFont.ttf")` or `assets.LoadFontSet(name, files...)` |
| `IFont` | A font set at one pixel size, used for drawing and measuring | `fontSet.CreateStyle(size)` |

`CreateStyle(size)` is cheap: every size of a set shares the set's glyph atlas. Create the sizes you need once (in an
`[Init]` step) and keep them in fields.

`IFont` members:

| Member | Meaning |
|---|---|
| `FontSet` | The set it was created from |
| `FontSize` | The size in pixels |
| `LineHeight` | The distance in pixels between the baselines of two lines |
| `MeasureString(text)` | The width and height in pixels of `text` drawn at scale 1 |

### Fallback fonts

A single font file rarely covers every script or emoji. Combine several files into one set with `LoadFontSet`. Glyphs
missing from the first file are taken from the next:

```csharp
using Ion.Extensions.Graphics;   // FontSetAssetManagerExtensions.LoadFontSet

var set = assets.LoadFontSet("UI", "Inter-Regular.ttf", "NotoSansJP-Regular.otf", "NotoEmoji-Regular.ttf");
var body = set.CreateStyle(18);
var title = set.CreateStyle(36);
```

The cache key is the name you pass (`"UI"`): loading the same name again returns the cached set, and so does
`assets.Load<IFontSet>("UI")` afterwards.

## Drawing text

```csharp
void DrawString(IFont font, string text, Vector2 textPosition,
	Color color = default, float depth = 0, Vector2 origin = default,
	float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None);
```

| Parameter | Meaning |
|---|---|
| `textPosition` | Where the text's origin goes, in target units. With the default origin this is the top-left of the text |
| `color` | The text color. `default` draws white |
| `depth` | Sort key, as for sprites |
| `origin` | The pivot in **unscaled text pixels** (not a fraction, unlike sprite origins). Use `MeasureString` to center |
| `rotation` | Radians around the origin |
| `scale` | A multiplier of the font size. Scaling a small font up blurs it; prefer a larger `CreateStyle` for large text |
| `options` | Ignored by the 2D renderer (flipping text is not supported) |

Newlines in the string start new lines, spaced by the font's line height.

### Centering and alignment

`MeasureString` returns the size at scale 1, so an origin at half the measured size centers the text on
`textPosition`:

```csharp
var text = "PAUSED";
var size = _title.MeasureString(text);
var center = new Vector2(frame.Width, frame.Height) / 2;
sprites.DrawString(_title, text, center, Color.White, origin: size / 2);

// Right-aligned at x = 780:
var score = $"{_score:N0}";
sprites.DrawString(_body, score, new Vector2(780, 20), Color.White, origin: new Vector2(_body.MeasureString(score).X, 0));
```

### Rotated and scaled text

```csharp
var label = "BOSS";
sprites.DrawString(_title, label, new Vector2(400, 300), Color.Red,
	origin: _title.MeasureString(label) / 2,
	rotation: MathF.Sin(_time) * 0.2f,
	scale: 1f + 0.1f * MathF.Sin(_time * 4));
```

### Text with a camera, in a panel, or in a render target

`DrawString` is an ordinary sprite batch draw, so every segment option applies: a camera `Transform` places text in
world space, `Scissor` clips it, `SetRenderTarget` draws it into a texture. Glyph textures are premultiplied, so use the
default `AlphaBlend` (or `Additive` for glow).

## Performance: layout caching

Laying out a string (shaping, kerning, finding glyphs in the atlas) is the expensive part of text. The 2D renderer lays
each string out **once per font** and caches the resulting glyph quads by the string's content. Drawing the same text
again, every frame, costs no FontStashSharp call and allocates nothing; only the quads are transformed and recorded.

The cache holds layouts per font. Once a font's cache grows past 256 entries, layouts not drawn for 120 frames are
evicted.

This has a practical consequence: **text that changes every frame builds a new layout every frame** (and a new string
is allocated by your interpolation). For a score or an FPS counter, rebuild the string only when the value changes:

```csharp
private int _shownScore = -1;
private string _scoreText = "";

[Render]
public void Draw(GameTime dt)
{
	if (_score != _shownScore)
	{
		_shownScore = _score;
		_scoreText = $"Score: {_score}";
	}

	sprites.DrawString(_font, _scoreText, new Vector2(20, 20), Color.White);
}
```

The Cubes sample uses the same pattern for its statistics line.

Glyphs are rasterized on first use into atlas pages (RGBA8, premultiplied white with coverage in alpha) that belong to
the renderer and are released at its teardown. New glyphs are uploaded with `IQueue.WriteTexture` without waiting for
the GPU.

## Headless text

Under `--headless`, `IFontSet` is a `NullFontSet` and its fonts are monospaced stand-ins: every character is half the
font size wide (`NullFont.GlyphWidthRatio` is 0.5) and every line is `FontSize` tall. `MeasureString` returns the same
result on every machine, so layout code is testable without a GPU or font rasterizer. `NullSpriteBatch` records each
`DrawString` call (with the text) in `LastFrame.Commands`.

```csharp
using var host = new IonTestHost().UseEntryPoint<Program>();
host.Step(5);
var strings = host.SpriteBatch.LastFrame.Commands.Where(c => c.Kind == SpriteBatchCommandKind.String);
Assert.Contains(strings, c => c.Text == "Score: 0");
```

:::caution
With the GPU sprite batch, fonts must come from the 2D renderer's font loader. Passing an `IFont` from another source
to `DrawString` throws `ArgumentException`.
:::

## The UI module and the metrics overlay

The [UI module](/Ion/interaction/ui/overview/) draws its labels with the same fonts and sprite batch. The metrics
overlay takes its font from `Ion:Metrics:OverlayFont` (a font file in the assets folder). See
[Metrics and tracing](/Ion/tooling/metrics-and-tracing/).

## See also

- [Sprites](/Ion/rendering/sprites/): segments, blending, cameras and render targets
- [Assets](/Ion/rendering/assets/): the asset folder, caching and hot reload
- [UI widgets](/Ion/interaction/ui/widgets/)
