using System.Numerics;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// A textured quad drawn by the sprite extraction system at the entity's <see cref="GlobalTransform2D"/>.
/// </summary>
/// <remarks>
/// Create it with a constructor: <c>default(Sprite)</c> has no texture and its origin is the top-left corner.
/// </remarks>
public record struct Sprite
{
	/// <summary>The texture (null: nothing is drawn).</summary>
	public ITexture2D? Texture;

	/// <summary>The part of the texture drawn, in texels; empty for the whole texture.</summary>
	public RectangleF Source;

	/// <summary>The size in world units before the transform's scale; zero for the source (or texture) size.</summary>
	public Vector2 Size;

	/// <summary>The tint (<c>default</c>: untinted).</summary>
	public Color Color;

	/// <summary>The point of the sprite placed at the entity's position, as fractions of its size ((0.5, 0.5): the center).</summary>
	public Vector2 Origin;

	/// <summary>The sort key: sprites are drawn by ascending depth (0 behind 1), ties in query order.</summary>
	public float Depth;

	/// <summary>Horizontal and vertical flipping.</summary>
	public SpriteEffect Flip;

	/// <summary>A sprite of <paramref name="texture"/> centered on the entity, <paramref name="size"/> units large (zero: the texture's size).</summary>
	public Sprite(ITexture2D texture, Vector2 size = default, float depth = 0f)
	{
		Texture = texture;
		Size = size;
		Origin = new Vector2(0.5f);
		Depth = depth;
	}

	/// <summary>The unscaled size: <see cref="Size"/>, else the source rectangle's, else the texture's.</summary>
	public readonly Vector2 ResolveSize()
	{
		if (Size != Vector2.Zero) return Size;
		if (Source.Width != 0 || Source.Height != 0) return new Vector2(Source.Width, Source.Height);
		return Texture is null ? Vector2.Zero : new Vector2(Texture.Width, Texture.Height);
	}
}

/// <summary>
/// Plays a flip-book on the entity's <see cref="Sprite"/>: the sprite animation system advances <see cref="Time"/> every
/// Update and sets <see cref="Sprite.Source"/> to the current frame.
/// </summary>
public record struct SpriteAnimation
{
	/// <summary>The source rectangles of the frames, in order.</summary>
	public RectangleF[]? Frames;

	/// <summary>Frames per second.</summary>
	public float FramesPerSecond;

	/// <summary>Whether the animation starts over after the last frame (otherwise it stops on it).</summary>
	public bool Loop;

	/// <summary>Whether the animation is paused.</summary>
	public bool Paused;

	/// <summary>The time played, in seconds.</summary>
	public float Time;

	/// <summary>The index of the current frame.</summary>
	public int Frame;

	/// <summary>An animation of <paramref name="frames"/> at <paramref name="framesPerSecond"/>, looping unless <paramref name="loop"/> is false.</summary>
	public SpriteAnimation(RectangleF[] frames, float framesPerSecond, bool loop = true)
	{
		Frames = frames;
		FramesPerSecond = framesPerSecond;
		Loop = loop;
	}

	/// <summary>Whether a non-looping animation reached its last frame.</summary>
	public readonly bool IsFinished => !Loop && Frames is { Length: > 0 } frames && Frame == frames.Length - 1 && Time * FramesPerSecond >= frames.Length;

	/// <summary>Advances the animation by <paramref name="delta"/> seconds and returns the current frame's source rectangle (or false when there are no frames).</summary>
	public bool Advance(float delta, out RectangleF source)
	{
		source = default;
		if (Frames is not { Length: > 0 } frames || FramesPerSecond <= 0f) return false;

		if (!Paused) Time += delta;
		var index = (int)(Time * FramesPerSecond);
		if (Loop) index %= frames.Length;
		else if (index >= frames.Length) index = frames.Length - 1;
		if (index < 0) index = 0;

		Frame = index;
		source = frames[index];
		return true;
	}
}

/// <summary>
/// The world-space bounding box of an entity's sprite, computed by the sprite extraction from its size, origin and
/// <see cref="GlobalTransform2D"/> (added with the <see cref="GlobalTransform2D"/> to every sprite).
/// </summary>
/// <param name="Min">The minimum corner.</param>
/// <param name="Max">The maximum corner.</param>
public readonly record struct Aabb2D(Vector2 Min, Vector2 Max)
{
	/// <summary>The size.</summary>
	public Vector2 Size => Max - Min;

	/// <summary>The center.</summary>
	public Vector2 Center => (Min + Max) * 0.5f;

	/// <summary>Whether the boxes overlap (touching edges count).</summary>
	public bool Intersects(in Aabb2D other) => Min.X <= other.Max.X && other.Min.X <= Max.X && Min.Y <= other.Max.Y && other.Min.Y <= Max.Y;

	/// <summary>Whether <paramref name="point"/> is inside (edges included).</summary>
	public bool Contains(Vector2 point) => point.X >= Min.X && point.X <= Max.X && point.Y >= Min.Y && point.Y <= Max.Y;

	/// <summary>
	/// The box of a <paramref name="size"/> quad whose <paramref name="origin"/> (fractions of the size) is at
	/// <paramref name="position"/>, turned by <paramref name="rotation"/> radians around it (how the sprite batch places a
	/// sprite). Unrotated quads take a short path without trigonometry.
	/// </summary>
	public static Aabb2D FromSprite(Vector2 position, float rotation, Vector2 size, Vector2 origin)
	{
		if (rotation == 0f)
		{
			var corner = position - origin * size;
			var opposite = corner + size;
			return new Aabb2D(Vector2.Min(corner, opposite), Vector2.Max(corner, opposite));
		}

		var (sin, cos) = MathF.SinCos(rotation);
		return FromQuad(size, origin, new Matrix3x2(cos, sin, -sin, cos, position.X, position.Y));
	}

	/// <summary>The box of a <paramref name="size"/> quad whose <paramref name="origin"/> (fractions) is at the origin, transformed by <paramref name="matrix"/>.</summary>
	public static Aabb2D FromQuad(Vector2 size, Vector2 origin, in Matrix3x2 matrix)
	{
		var min = -origin * size;
		var max = min + size;
		var a = Vector2.Transform(min, matrix);
		var b = Vector2.Transform(new Vector2(max.X, min.Y), matrix);
		var c = Vector2.Transform(max, matrix);
		var d = Vector2.Transform(new Vector2(min.X, max.Y), matrix);
		return new Aabb2D(Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d)), Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d)));
	}
}

/// <summary>Hides an entity from the renderer's extraction (its sprite is not drawn).</summary>
public record struct Hidden;

/// <summary>
/// Marks an entity as shown. The extraction draws every sprite without <see cref="Hidden"/> by default; with
/// <c>SpriteExtractionOptions.RequireVisible</c> it draws only sprites tagged <see cref="Visible"/>.
/// </summary>
public record struct Visible;

/// <summary>Tags the entity whose <see cref="Camera2D"/> the extraction renders with (the first one found).</summary>
public record struct MainCamera;

/// <summary>A name for an entity (looked up with the ECS module's <c>NameRegistry</c>, and by the remote protocol).</summary>
/// <param name="Value">The name.</param>
public readonly record struct EntityName(string Value)
{
	/// <inheritdoc/>
	public override string ToString() => Value;
}
