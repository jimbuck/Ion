using System.Numerics;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Ecs.Rendering;

/// <summary>Options of the <see cref="SpriteExtractionSystem"/>.</summary>
public sealed class SpriteExtractionOptions
{
	/// <summary>Draw only sprites tagged <see cref="Visible"/> (by default every sprite without <see cref="Hidden"/> is drawn).</summary>
	public bool RequireVisible { get; set; }

	/// <summary>Skip sprites whose bounds are outside the camera's view (default true).</summary>
	public bool Cull { get; set; } = true;
}

/// <summary>What the last extraction did.</summary>
/// <param name="Extracted">Sprites submitted to the sprite batch.</param>
/// <param name="Culled">Sprites skipped because they were outside the view.</param>
/// <param name="HasCamera">Whether a <see cref="MainCamera"/> with a <see cref="Camera2D"/> was used.</param>
public readonly record struct SpriteExtractionStats(int Extracted, int Culled, bool HasCamera);

/// <summary>
/// The 2D render extraction (Render, at <see cref="StageOrder.Extract"/>, inside the sprite batch scope and after the
/// transform propagation's Render pass): copies every entity with a <see cref="Sprite"/> and a
/// <see cref="GlobalTransform2D"/> (without <see cref="Hidden"/>) into a flat array, drops those whose bounds miss the
/// view, sorts by <see cref="Sprite.Depth"/> (stable: ties keep Arch's query order) and draws them with the
/// <see cref="ISpriteBatch"/>. The view is the first entity tagged <see cref="MainCamera"/> with a <see cref="Camera2D"/>
/// component (its <see cref="Camera2D.GetTransform"/> becomes the segment's transform); without one, world units are
/// pixels of the window. Allocates nothing per frame once its arrays have grown.
/// </summary>
public sealed class SpriteExtractionSystem(World world, ISpriteBatch spriteBatch, IWindow window, SpriteExtractionOptions options)
{
	private static readonly QueryDescription Sprites = new QueryDescription().WithAll<Sprite, GlobalTransform2D>().WithNone<Hidden>();
	private static readonly QueryDescription VisibleSprites = new QueryDescription().WithAll<Sprite, GlobalTransform2D, Visible>().WithNone<Hidden>();
	private static readonly QueryDescription Cameras = new QueryDescription().WithAll<MainCamera, Camera2D>();

	private SpriteDraw[] _draws = new SpriteDraw[256];
	private ulong[] _keys = new ulong[256];

	private static uint OrderableBits(float value)
	{
		var bits = BitConverter.SingleToUInt32Bits(value);
		return (bits & 0x8000_0000u) != 0 ? ~bits : bits | 0x8000_0000u;
	}

	/// <summary>Creates the system with default options.</summary>
	public SpriteExtractionSystem(World world, ISpriteBatch spriteBatch, IWindow window) : this(world, spriteBatch, window, new SpriteExtractionOptions())
	{
	}

	/// <summary>What the last <see cref="Extract"/> did.</summary>
	public SpriteExtractionStats LastFrame { get; private set; }

	/// <summary>Extracts the sprites and draws them.</summary>
	[Render(Order = StageOrder.Extract)]
	public void Extract(GameTime dt)
	{
		var viewport = window.Size;
		var camera = FindCamera();
		Matrix3x2? transform = camera?.GetTransform(viewport);
		var view = ViewBounds(transform, viewport);
		var query = options.RequireVisible ? VisibleSprites : Sprites;
		var cull = options.Cull;
		int count, culled;

		if (transform is { } matrix) spriteBatch.Begin(new SpriteBatchOptions { Transform = matrix });
		try
		{
			// Depths already in query order (the common case, all zero included) are drawn straight from the chunks;
			// otherwise the sprites are copied into a flat array, sorted (stable) and drawn from it.
			(count, culled) = IsSortedByDepth(query) ? DrawInQueryOrder(query, view, cull) : DrawSorted(query, view, cull);
		}
		finally
		{
			if (transform is not null) spriteBatch.End();
		}

		LastFrame = new SpriteExtractionStats(count, culled, camera is not null);
	}

	private bool IsSortedByDepth(in QueryDescription query)
	{
		var last = float.NegativeInfinity;
		foreach (ref var chunk in world.Query(in query))
		{
			var sprites = chunk.GetSpan<Sprite>();
			for (var i = chunk.Count - 1; i >= 0; i--)
			{
				var depth = sprites[i].Depth;
				if (depth < last) return false;
				last = depth;
			}
		}

		return true;
	}

	private (int Count, int Culled) DrawInQueryOrder(in QueryDescription query, in Aabb2D view, bool cull)
	{
		var count = 0;
		var culled = 0;
		foreach (ref var chunk in world.Query(in query))
		{
			var sprites = chunk.GetSpan<Sprite>();
			var globals = chunk.GetSpan<GlobalTransform2D>();
			// Arch's query order (last to first within a chunk), so ties in depth draw in the order World.Query visits them.
			for (var i = chunk.Count - 1; i >= 0; i--)
			{
				ref readonly var sprite = ref sprites[i];
				if (sprite.Texture is not { } texture) continue;
				ref readonly var global = ref globals[i];
				var size = sprite.ResolveSize() * global.Scale;

				if (cull && !Aabb2D.FromSprite(global.Position, global.Rotation, size, sprite.Origin).Intersects(view))
				{
					culled++;
					continue;
				}

				spriteBatch.Draw(texture, global.Position, size, sprite.Source, sprite.Color, sprite.Origin, global.Rotation, sprite.Depth, sprite.Flip);
				count++;
			}
		}

		return (count, culled);
	}

	private (int Count, int Culled) DrawSorted(in QueryDescription query, in Aabb2D view, bool cull)
	{
		var count = 0;
		var culled = 0;
		foreach (ref var chunk in world.Query(in query))
		{
			var sprites = chunk.GetSpan<Sprite>();
			var globals = chunk.GetSpan<GlobalTransform2D>();
			for (var i = chunk.Count - 1; i >= 0; i--)
			{
				ref readonly var sprite = ref sprites[i];
				if (sprite.Texture is null) continue;
				ref readonly var global = ref globals[i];
				var size = sprite.ResolveSize() * global.Scale;

				if (cull && !Aabb2D.FromSprite(global.Position, global.Rotation, size, sprite.Origin).Intersects(view))
				{
					culled++;
					continue;
				}

				if (count == _draws.Length) Array.Resize(ref _draws, count * 2);
				_draws[count] = new SpriteDraw
				{
					Texture = sprite.Texture,
					Position = global.Position,
					Size = size,
					Source = sprite.Source,
					Color = sprite.Color,
					Origin = sprite.Origin,
					Rotation = global.Rotation,
					Depth = sprite.Depth,
					Flip = sprite.Flip,
				};
				count++;
			}
		}

		// Sort keys, not the draws: the depth's bits made orderable in the high half, the query position in the low half
		// (which makes the sort stable), sorted as integers.
		if (_keys.Length < count) _keys = new ulong[_draws.Length];
		var keys = _keys.AsSpan(0, count);
		for (var i = 0; i < count; i++) keys[i] = ((ulong)OrderableBits(_draws[i].Depth) << 32) | (uint)i;
		keys.Sort();

		var draws = _draws.AsSpan(0, count);
		foreach (var key in keys)
		{
			ref readonly var draw = ref draws[(int)(uint)key];
			spriteBatch.Draw(draw.Texture, draw.Position, draw.Size, draw.Source, draw.Color, draw.Origin, draw.Rotation, draw.Depth, draw.Flip);
		}

		// Drop the texture references so unloaded textures can be collected.
		draws.Clear();
		return (count, culled);
	}

	private Camera2D? FindCamera()
	{
		foreach (ref var chunk in world.Query(in Cameras))
		{
			if (chunk.Count > 0) return chunk.GetArray<Camera2D>()[0];
		}

		return null;
	}

	/// <summary>The world-space rectangle seen through <paramref name="transform"/> (world to pixels) in a viewport of <paramref name="viewport"/> pixels.</summary>
	public static Aabb2D ViewBounds(Matrix3x2? transform, Vector2 viewport)
	{
		if (transform is not { } matrix) return new Aabb2D(Vector2.Zero, viewport);
		if (!Matrix3x2.Invert(matrix, out var inverse)) return new Aabb2D(new Vector2(float.NegativeInfinity), new Vector2(float.PositiveInfinity));
		return Aabb2D.FromQuad(viewport, Vector2.Zero, inverse);
	}

	private struct SpriteDraw
	{
		public ITexture2D Texture;
		public Vector2 Position;
		public Vector2 Size;
		public RectangleF Source;
		public Color Color;
		public Vector2 Origin;
		public float Rotation;
		public float Depth;
		public SpriteEffect Flip;
	}
}

/// <summary>Registration of the ECS render extraction.</summary>
public static class EcsRenderingBuilderExtensions
{
	/// <summary>
	/// Registers the <see cref="SpriteExtractionSystem"/> (a transient, so the root schedule and each scene get one bound to
	/// their own world) and its <see cref="SpriteExtractionOptions"/>. Needs <c>AddEcs()</c> and a sprite batch.
	/// </summary>
	public static IServiceCollection AddEcsRendering(this IServiceCollection services, Action<SpriteExtractionOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var options = new SpriteExtractionOptions();
		configure?.Invoke(options);
		services.TryAddSingleton(options);
		services.TryAddTransient(static sp => new SpriteExtractionSystem(sp.GetRequiredService<World>(), sp.GetRequiredService<ISpriteBatch>(), sp.GetRequiredService<IWindow>(), sp.GetRequiredService<SpriteExtractionOptions>()));
		return services;
	}

	/// <summary>Adds the sprite extraction to the root schedule (the root world's sprites).</summary>
	public static IIonApplication UseEcsRendering(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app.UseSystem<SpriteExtractionSystem>();
	}

	/// <summary>Adds the sprite extraction to a scene's schedule (the scene's world).</summary>
	public static ISceneBuilder UseEcsRendering(this ISceneBuilder scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		return scene.UseSystem<SpriteExtractionSystem>();
	}
}
