using System.Runtime.CompilerServices;

using Arch.Core;
using Arch.Core.Extensions;

namespace Ion.Benchmarks;

public record struct Transform2D(Vector2 Position, float Rotation);
public record struct SpriteRef(int TextureId, Vector2 Size);

/// <summary>
/// Baseline for the ECS integration work: iterating 10k renderable entities in Arch 2.1 (the ECS sample still uses 1.2.8)
/// with the delegate-based query the sample uses versus the delegate-free alternatives (struct IForEach inline query and raw chunk spans).
/// The body mirrors SpriteRendererSystem: compute a rotated top-left corner from Transform2D + size.
/// </summary>
[MemoryDiagnoser]
public class ArchQueryBenchmarks
{
	private const int Entities = 10_000;

	private World _world = null!;
	private QueryDescription _query;

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		var rand = new Random(42);
		for (var i = 0; i < Entities; i++)
		{
			_world.Create(new Transform2D(new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080), rand.NextSingle()), new SpriteRef(i % 16, new Vector2(32, 32)));
		}
		_query = new QueryDescription().WithAll<Transform2D, SpriteRef>();
	}

	[GlobalCleanup]
	public void Cleanup() => World.Destroy(_world);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Work(ref Transform2D transform, ref SpriteRef sprite)
	{
		var half = sprite.Size / 2f;
		var (sin, cos) = MathF.SinCos(transform.Rotation);
		var topLeft = new Vector2(-half.X * cos + half.Y * sin, -half.X * sin - half.Y * cos) + transform.Position;
		return topLeft.X + topLeft.Y;
	}

	[Benchmark(Baseline = true)]
	public float DelegateQuery()
	{
		var sum = 0f;
		_world.Query(in _query, (ref Transform2D t, ref SpriteRef s) => sum += Work(ref t, ref s));
		return sum;
	}

	private struct WorkForEach : IForEach<Transform2D, SpriteRef>
	{
		public float Sum;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Update(ref Transform2D t, ref SpriteRef s) => Sum += Work(ref t, ref s);
	}

	[Benchmark]
	public float InlineStructQuery()
	{
		var job = new WorkForEach();
		_world.InlineQuery<WorkForEach, Transform2D, SpriteRef>(in _query, ref job);
		return job.Sum;
	}

	[Benchmark]
	public float ChunkSpans()
	{
		var sum = 0f;
		var query = _world.Query(in _query);
		foreach (ref var chunk in query)
		{
			var transforms = chunk.GetSpan<Transform2D>();
			var sprites = chunk.GetSpan<SpriteRef>();
			for (var i = 0; i < chunk.Count; i++) sum += Work(ref transforms[i], ref sprites[i]);
		}
		return sum;
	}
}
