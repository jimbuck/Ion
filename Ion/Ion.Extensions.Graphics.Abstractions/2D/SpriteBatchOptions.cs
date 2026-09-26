using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The order in which a sprite batch draws the sprites of one <see cref="ISpriteBatch.Begin"/>/<see cref="ISpriteBatch.End"/> segment.
/// </summary>
public enum SpriteSortMode
{
	/// <summary>
	/// Submission order (painter's algorithm). Consecutive sprites with the same texture share a draw call; interleaving
	/// textures costs one draw call per change. The default.
	/// </summary>
	Deferred,

	/// <summary>Grouped by texture (one draw call per texture), submission order inside a texture. Use it when sprites do not overlap across textures.</summary>
	Texture,

	/// <summary>By depth, ascending: depth 0 is drawn first (behind). Ties keep submission order.</summary>
	FrontToBack,

	/// <summary>By depth, descending: depth 0 is drawn last (in front). Ties keep submission order.</summary>
	BackToFront,
}

/// <summary>
/// Blend presets of a sprite batch. Textures loaded by the renderer are premultiplied at load (as are its glyph atlas and
/// anything rendered into a render target with <see cref="AlphaBlend"/>), and tint colors are straight alpha.
/// </summary>
public enum SpriteBlendMode
{
	/// <summary>Premultiplied alpha blending (the tint is premultiplied in the shader). The default.</summary>
	AlphaBlend,

	/// <summary>Additive blending: the (premultiplied) source is added to the target.</summary>
	Additive,

	/// <summary>No blending: the source replaces the target.</summary>
	Opaque,

	/// <summary>Straight alpha blending (<c>SrcAlpha</c>, <c>OneMinusSrcAlpha</c>) with an untouched tint, for textures whose color is not premultiplied.</summary>
	NonPremultiplied,
}

/// <summary>
/// Sampler presets of a sprite batch: nearest or bilinear filtering, clamped or repeating addressing.
/// </summary>
public enum SpriteSamplerMode
{
	/// <summary>Bilinear (trilinear with mips), clamped to the edge. The default.</summary>
	LinearClamp,

	/// <summary>Nearest-neighbour, clamped to the edge (pixel art).</summary>
	PointClamp,

	/// <summary>Bilinear, repeating (tiling with a source rectangle larger than the texture).</summary>
	LinearWrap,

	/// <summary>Nearest-neighbour, repeating.</summary>
	PointWrap,
}

/// <summary>
/// The render state of one sprite batch segment: <see cref="ISpriteBatch.Begin"/> takes it, <see cref="ISpriteBatch.End"/>
/// returns to the enclosing segment's. <c>default</c> is the state the sprite batch system opens every Render stage with:
/// submission order, premultiplied alpha blending, a linear clamped sampler, no scissor and the pixel-space camera
/// (origin top-left, one unit per pixel of the current target).
/// </summary>
public readonly record struct SpriteBatchOptions
{
	/// <summary>The sprite order.</summary>
	public SpriteSortMode SortMode { get; init; }

	/// <summary>The blend preset.</summary>
	public SpriteBlendMode BlendMode { get; init; }

	/// <summary>The sampler preset.</summary>
	public SpriteSamplerMode SamplerMode { get; init; }

	/// <summary>
	/// A transform from world coordinates to target pixels applied to every sprite of the segment (for example
	/// <see cref="Camera2D.GetTransform"/>); null for the identity (sprites are placed in pixels).
	/// </summary>
	public Matrix3x2? Transform { get; init; }

	/// <summary>The scissor rectangle in target pixels (origin top-left); null for the whole target.</summary>
	public Rectangle? Scissor { get; init; }

	/// <summary>Options with a camera: <see cref="Transform"/> from <paramref name="camera"/> for a target of <paramref name="viewportSize"/> pixels.</summary>
	public static SpriteBatchOptions WithCamera(Camera2D camera, Vector2 viewportSize, SpriteSortMode sortMode = SpriteSortMode.Deferred)
	{
		ArgumentNullException.ThrowIfNull(camera);
		return new SpriteBatchOptions { SortMode = sortMode, Transform = camera.GetTransform(viewportSize) };
	}
}

/// <summary>
/// A 2D camera: <see cref="Position"/> is the world point shown at the center of the viewport, <see cref="Zoom"/> scales
/// (2 shows everything twice as large) and <see cref="Rotation"/> turns the view (radians). Pass
/// <see cref="GetTransform"/> as <see cref="SpriteBatchOptions.Transform"/>.
/// </summary>
public sealed class Camera2D
{
	/// <summary>The world point at the center of the viewport.</summary>
	public Vector2 Position { get; set; }

	/// <summary>The zoom factor (1: one world unit per pixel).</summary>
	public float Zoom { get; set; } = 1f;

	/// <summary>The rotation of the view in radians.</summary>
	public float Rotation { get; set; }

	/// <summary>The world-to-pixel transform for a viewport of <paramref name="viewportSize"/> pixels.</summary>
	public Matrix3x2 GetTransform(Vector2 viewportSize) =>
		Matrix3x2.CreateTranslation(-Position)
		* Matrix3x2.CreateRotation(-Rotation)
		* Matrix3x2.CreateScale(Zoom)
		* Matrix3x2.CreateTranslation(viewportSize / 2f);

	/// <summary>Converts a point in viewport pixels (a mouse position) to world coordinates.</summary>
	public Vector2 ScreenToWorld(Vector2 screen, Vector2 viewportSize) =>
		Matrix3x2.Invert(GetTransform(viewportSize), out var inverse) ? Vector2.Transform(screen, inverse) : screen;

	/// <summary>Converts a world point to viewport pixels.</summary>
	public Vector2 WorldToScreen(Vector2 world, Vector2 viewportSize) => Vector2.Transform(world, GetTransform(viewportSize));
}

/// <summary>
/// Debug shapes built from <see cref="ISpriteBatch.DrawLine(Color, Vector2, Vector2, float, float)"/> and
/// <see cref="ISpriteBatch.DrawRect(Color, RectangleF, Vector2, float, float)"/>, so they work on every sprite batch.
/// </summary>
public static class SpriteBatchShapeExtensions
{
	/// <summary>Draws the outline of a circle as <paramref name="segments"/> lines (0: chosen from the radius).</summary>
	public static void DrawCircle(this ISpriteBatch spriteBatch, Color color, Vector2 center, float radius, float thickness = 1f, int segments = 0, float depth = 0)
	{
		ArgumentNullException.ThrowIfNull(spriteBatch);
		if (segments <= 0) segments = Math.Clamp((int)MathF.Ceiling(radius * 0.75f), 12, 128);
		var step = MathF.Tau / segments;
		var previous = center + new Vector2(radius, 0);
		for (var i = 1; i <= segments; i++)
		{
			var (sin, cos) = MathF.SinCos(i * step);
			var next = center + new Vector2(cos * radius, sin * radius);
			spriteBatch.DrawLine(color, previous, next, thickness, depth);
			previous = next;
		}
	}

	/// <summary>Draws the outline of <paramref name="rectangle"/> with lines of <paramref name="thickness"/> pixels inside it.</summary>
	public static void DrawRectOutline(this ISpriteBatch spriteBatch, Color color, RectangleF rectangle, float thickness = 1f, float depth = 0)
	{
		ArgumentNullException.ThrowIfNull(spriteBatch);
		spriteBatch.DrawRect(color, new RectangleF(rectangle.X, rectangle.Y, rectangle.Width, thickness), depth: depth);
		spriteBatch.DrawRect(color, new RectangleF(rectangle.X, rectangle.Bottom - thickness, rectangle.Width, thickness), depth: depth);
		spriteBatch.DrawRect(color, new RectangleF(rectangle.X, rectangle.Y + thickness, thickness, rectangle.Height - 2 * thickness), depth: depth);
		spriteBatch.DrawRect(color, new RectangleF(rectangle.Right - thickness, rectangle.Y + thickness, thickness, rectangle.Height - 2 * thickness), depth: depth);
	}
}
