---
title: Sprites 100k
description: The sprite batch stress test, 100,000 moving sprites across 16 textures drawn in 16 instanced draw calls, with frame timing and textures created from pixels in memory.
sidebar:
  order: 9
---

**Source:** [`Ion.Examples/Ion.Examples.Sprites100k`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Sprites100k)
and its tests in [`Ion.Examples.Sprites100k.Tests`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Sprites100k.Tests).

100,000 small ring sprites bounce around a 1280 x 720 window. They use 16 textures, and the Render step draws them with
`SpriteSortMode.Texture`, so the sprite batch submits exactly one instanced draw call per texture. The sample logs frames
per second, frame time, recording time, sprites and draw calls every second. Use it to measure the 2D renderer on your
hardware and to check changes to the batcher.

## What it shows

- The 2D renderer's batching: one instance array per frame, one instanced draw per run of equal textures.
- `spriteBatch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.Texture, SamplerMode = SpriteSamplerMode.PointClamp })`
  and `End()` around a segment with its own options.
- Creating textures from pixels in memory with `TextureFactory.Create(name, width, height, pixels)`, with a
  `NullTexture2D` fallback when running headless without rendering.
- Settings read from configuration into a record registered as a singleton.
- Structure-of-arrays game state (positions, velocities, texture indices) for a tight update loop.
- Reading `ISpriteBatchStatistics.LastFrameStatistics` (sprites, triangles, draw calls).

## Run it

```bash
dotnet run -c Release --project Ion.Examples/Ion.Examples.Sprites100k
dotnet run -c Release --project Ion.Examples/Ion.Examples.Sprites100k -- --Sprites:Count=250000 --Sprites:Textures=4
dotnet run -c Release --project Ion.Examples/Ion.Examples.Sprites100k -- --Sprites:Frames=600
```

| Setting | Default | Meaning |
|---|---|---|
| `Sprites:Count` | 100,000 | The number of sprites. |
| `Sprites:Textures` | 16 | The number of textures (clamped to 1 to 64), and so the number of draw calls. |
| `Sprites:Frames` | 0 | Exit after this many frames and log the average (0 runs until the window closes). |

`appsettings.json` sets a 1280 x 720 window with `MaxFPS` 0 and VSync off, so the loop runs as fast as it can.

:::tip[Measure in Release]
Run with `-c Release` (or publish with a preset) when you care about the numbers. Debug builds of the engine and the
game are much slower in the per-sprite loops.
:::

## Program.cs

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x202020)).AddSystem<StressSystem>();
builder.Services.AddSingleton(StressSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon().UseSystem<StressSystem>();
game.Run();
```

```csharp title="Program.cs"
public sealed record StressSettings(int Count, int Textures, int Frames)
{
	public static StressSettings From(IConfiguration config) => new(
		int.TryParse(config["Sprites:Count"], out var count) ? count : 100_000,
		int.TryParse(config["Sprites:Textures"], out var textures) ? Math.Clamp(textures, 1, 64) : 16,
		int.TryParse(config["Sprites:Frames"], out var frames) ? frames : 0);
}
```

## Creating textures from pixels

```csharp title="Program.cs"
[Init]
public void Init(GameTime dt)
{
	var random = new Random(100_000);
	var factory = services.GetService<TextureFactory>();
	_textures = new ITexture2D[settings.Textures];
	for (var t = 0; t < _textures.Length; t++)
	{
		_textures[t] = factory?.Create($"sprite{t}", 16, 16, Pattern(t)) ?? new NullTexture2D($"sprite{t}", 16, 16);
	}
	// ... random positions and velocities for every sprite ...
}
```

`TextureFactory` (from `Ion.Extensions.Rendering2D`) uploads RGBA8 pixels to the GPU as an `ITexture2D`. It only exists
when the 2D renderer is registered, so the sample resolves it optionally and falls back to a `NullTexture2D` of the
same size when running headless without rendering. `Pattern(index)` draws a 16 x 16 ring in one of 16 hues with
transparent corners.

## The update loop

```csharp title="Program.cs"
[Update]
public void Update(GameTime dt)
{
	var max = new Vector2(window.Width - SpriteSize, window.Height - SpriteSize);
	var delta = dt.Delta;
	var positions = _positions;
	var velocities = _velocities;
	for (var i = 0; i < positions.Length; i++)
	{
		var p = positions[i] + velocities[i] * delta;
		ref var v = ref velocities[i];
		if (p.X < 0 || p.X > max.X) { v.X = -v.X; p.X = Math.Clamp(p.X, 0, max.X); }
		if (p.Y < 0 || p.Y > max.Y) { v.Y = -v.Y; p.Y = Math.Clamp(p.Y, 0, max.Y); }
		positions[i] = p;
	}
}
```

Plain arrays copied into locals keep the loop free of bounds-check and field-load overhead. It allocates nothing.

## The render loop

```csharp title="Program.cs"
[Render]
public void Render(GameTime dt)
{
	var start = Stopwatch.GetTimestamp();
	var size = new Vector2(SpriteSize);
	spriteBatch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.Texture, SamplerMode = SpriteSamplerMode.PointClamp });
	var textures = _textures;
	var positions = _positions;
	var textureOf = _textureOf;
	for (var i = 0; i < positions.Length; i++) spriteBatch.Draw(textures[textureOf[i]], positions[i], size);
	spriteBatch.End();
	var recorded = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
	// ... timing and the once-a-second log line ...
}
```

The sprite batch system already opens a segment with default options around every Render stage.
`Begin(SpriteBatchOptions)` / `End()` nest a segment with other render state:

| Option | Here | Why |
|---|---|---|
| `SortMode` | `Texture` | Groups sprites by texture with a counting sort, so 16 textures cost 16 draw calls whatever the submission order. The default, `Deferred`, keeps submission order and would draw one call per texture change. |
| `SamplerMode` | `PointClamp` | Crisp 16 x 16 pixel art, no filtering. |
| `BlendMode` | default (`AlphaBlend`) | Premultiplied alpha for the transparent corners. |

Each sprite is a 40-byte instance (corner and edge vectors with scale and rotation folded in, a 16-bit UV rectangle,
RGBA8 color and depth) uploaded once per frame into a ring of `FramesInFlight` instance buffers, so the CPU never waits
for the GPU.

## The tests

| Test | What it checks |
|---|---|
| `DrawsOneCallPerTextureUnderValidation` | Rendered headless on Vulkan with validation: 100,000 sprites, 200,000 triangles, 16 draw calls, and sprites covering most of the frame. |
| `ReportsHeadlessFrameTime`, `ReportsHeadlessFrameTimeOnGles` | 60 offscreen frames on lavapipe and on llvmpipe; the time is reported, not gated (both rasterize on the CPU). |
| `ReportsWindowedFrameTime` | 120 windowed frames under Xvfb; 16 draw calls. |

```csharp title="StressTests.cs"
private static IonTestHost Host(int count) => new IonTestHost()
	.UseEntryPoint<Program>()
	.WithConfiguration("Sprites:Count", count.ToString(CultureInfo.InvariantCulture));

var shot = SampleRendering.Capture(Host(100_000), Width, Height, 3, inspect: host =>
{
	var stats = host.Get<SpriteBatch>().LastFrameStatistics;
	Assert.Equal(100_000, stats.Sprites);
	Assert.Equal(200_000, stats.Triangles);
	Assert.Equal(16, stats.DrawCalls);
});
```

On the CPU side, `SpriteBatchBenchmarks` in `Ion.Benchmarks` measures the recording cost per sprite; see
[Benchmarks](/Ion/tooling/benchmarks/).

## Ideas to extend it

**Rotation and color.** Pass a rotation and a tint per sprite through one of the `ISpriteBatch.Draw` overloads that take
them, and watch the recording time: the instance layout already carries rotation and color, so the draw count does not
change.

**Compare sort modes.** Switch to `SpriteSortMode.Deferred` and interleave textures (`i % 16`, as now): the draw count
jumps to one per sprite run. Then sort the arrays by texture once at Init and see `Deferred` go back to 16 calls.

**ECS version.** Make each sprite an entity with `Transform2D`, `Sprite` and a velocity component, move them with a
`[Query]` step, and let `AddEcsRendering()` extract them. Compare the frame time with the array version.

**Metrics.** Run with `--Ion:Metrics:Overlay=true --Ion:Metrics:OverlayFont=...` (add a font to `Assets/`) or
`--Ion:Metrics:FrameLog=frames.jsonl` to get per-frame numbers in a file.

## See also

- [Sprites](/Ion/rendering/sprites/): the sprite batch API and options.
- [Rendering overview](/Ion/rendering/overview/).
- [Benchmarks](/Ion/tooling/benchmarks/) and [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).
