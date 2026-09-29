using System.Numerics;
using System.Runtime.CompilerServices;

using Ion.Extensions.Ecs;

namespace Ion.Benchmarks.GeneratedApp;

/// <summary>The components of the ECS query benchmark: a 2D transform, a sprite reference and the corner written per entity.</summary>
public record struct BenchTransform(Vector2 Position, float Rotation);

/// <summary>A sprite reference (texture id and size).</summary>
public record struct BenchSprite(int TextureId, Vector2 Size);

/// <summary>The output of the per-entity work: the sprite's rotated top-left corner.</summary>
public record struct BenchCorner(Vector2 Value);

/// <summary>The per-entity work of <c>EcsQueryBenchmarks</c> (the extraction-shaped math of roadmap 5.7).</summary>
public static class CornerMath
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static Vector2 TopLeft(in BenchTransform transform, in BenchSprite sprite)
	{
		var half = sprite.Size / 2f;
		var (sin, cos) = MathF.SinCos(transform.Rotation);
		return new Vector2(-half.X * cos + half.Y * sin, -half.X * sin - half.Y * cos) + transform.Position;
	}
}

/// <summary>The benchmark's [Query] system: this assembly is compiled with the generator, which expands it into <c>__IonQuery_Corner</c>.</summary>
public sealed partial class CornerQuerySystem
{
	[Update, Query]
	private static void Corner(in BenchTransform transform, in BenchSprite sprite, ref BenchCorner corner) => corner.Value = CornerMath.TopLeft(transform, sprite);
}

/// <summary>The same query without the structural-change check (<c>[Query(Unchecked = true)]</c>).</summary>
public sealed partial class UncheckedCornerQuerySystem
{
	[Update, Query(Unchecked = true)]
	private static void Corner(in BenchTransform transform, in BenchSprite sprite, ref BenchCorner corner) => corner.Value = CornerMath.TopLeft(transform, sprite);
}
