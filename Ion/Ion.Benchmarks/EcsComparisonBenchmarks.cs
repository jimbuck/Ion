using System.Runtime.CompilerServices;

using Arch.Core;

using Friflo.Engine.ECS;

namespace Ion.Benchmarks;

/// <summary>
/// Head-to-head of the two candidates for the built-in ECS (Arch 2.1 and Friflo.Engine.ECS 3.6) on the operations a renderer
/// and a game loop actually do: iterate 10k entities with two components (delegate, struct functor, raw chunk spans),
/// create 10k entities, and a structural churn pass (add then remove a tag component on every entity).
/// The per-entity work is the same rotated-corner math as <see cref="ArchQueryBenchmarks"/>.
/// </summary>
[MemoryDiagnoser]
public class EcsComparisonBenchmarks
{
	private const int Entities = 10_000;

	// Friflo components must implement IComponent / ITag.
	public struct FPosition : IComponent { public Vector2 Position; public float Rotation; }
	public struct FSprite : IComponent { public int TextureId; public Vector2 Size; }
	public struct FTag : ITag { }

	// Arch components are plain structs.
	public struct APosition { public Vector2 Position; public float Rotation; }
	public struct ASprite { public int TextureId; public Vector2 Size; }
	public struct ATag { }

	private World _arch = null!;
	private QueryDescription _archQuery;
	private EntityStore _friflo = null!;
	private ArchetypeQuery<FPosition, FSprite> _frifloQuery = null!;

	[GlobalSetup]
	public void Setup()
	{
		var rand = new Random(42);
		_arch = World.Create();
		_archQuery = new QueryDescription().WithAll<APosition, ASprite>();
		_friflo = new EntityStore();
		for (var i = 0; i < Entities; i++)
		{
			var pos = new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080);
			var rot = rand.NextSingle();
			_arch.Create(new APosition { Position = pos, Rotation = rot }, new ASprite { TextureId = i % 16, Size = new Vector2(32, 32) });
			_friflo.CreateEntity(new FPosition { Position = pos, Rotation = rot }, new FSprite { TextureId = i % 16, Size = new Vector2(32, 32) });
		}
		_frifloQuery = _friflo.Query<FPosition, FSprite>();
	}

	[GlobalCleanup]
	public void Cleanup() => World.Destroy(_arch);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	private static float Work(in Vector2 position, float rotation, in Vector2 size)
	{
		var half = size / 2f;
		var (sin, cos) = MathF.SinCos(rotation);
		var topLeft = new Vector2(-half.X * cos + half.Y * sin, -half.X * sin - half.Y * cos) + position;
		return topLeft.X + topLeft.Y;
	}

	// ---------------------------------------------------------------- iterate: delegate

	[Benchmark(Baseline = true)]
	public float Arch_Iterate_Delegate()
	{
		var sum = 0f;
		_arch.Query(in _archQuery, (ref APosition p, ref ASprite s) => sum += Work(in p.Position, p.Rotation, in s.Size));
		return sum;
	}

	[Benchmark]
	public float Friflo_Iterate_Delegate()
	{
		var sum = 0f;
		_frifloQuery.ForEachEntity((ref FPosition p, ref FSprite s, Friflo.Engine.ECS.Entity _) => sum += Work(in p.Position, p.Rotation, in s.Size));
		return sum;
	}

	// ---------------------------------------------------------------- iterate: struct functor

	private struct ArchEach : IForEach<APosition, ASprite>
	{
		public float Sum;
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Update(ref APosition p, ref ASprite s) => Sum += Work(in p.Position, p.Rotation, in s.Size);
	}

	[Benchmark]
	public float Arch_Iterate_StructFunctor()
	{
		var job = new ArchEach();
		_arch.InlineQuery<ArchEach, APosition, ASprite>(in _archQuery, ref job);
		return job.Sum;
	}

	// Friflo 3.6.0 declares IEach<T1,T2> but ships no query method that consumes it (that arrives with 4.0), so the
	// struct-functor row exists only for Arch; Friflo's delegate-free path is the chunk-span loop below.

	// ---------------------------------------------------------------- iterate: chunk spans (what a [Query] generator emits)

	[Benchmark]
	public float Arch_Iterate_ChunkSpans()
	{
		var sum = 0f;
		var query = _arch.Query(in _archQuery);
		foreach (ref var chunk in query)
		{
			var positions = chunk.GetSpan<APosition>();
			var sprites = chunk.GetSpan<ASprite>();
			for (var i = 0; i < chunk.Count; i++) sum += Work(in positions[i].Position, positions[i].Rotation, in sprites[i].Size);
		}
		return sum;
	}

	[Benchmark]
	public float Friflo_Iterate_ChunkSpans()
	{
		var sum = 0f;
		foreach (var (positions, sprites, entities) in _frifloQuery.Chunks)
		{
			var p = positions.Span;
			var s = sprites.Span;
			for (var i = 0; i < p.Length; i++) sum += Work(in p[i].Position, p[i].Rotation, in s[i].Size);
		}
		return sum;
	}

	// ---------------------------------------------------------------- create 10k

	[Benchmark]
	public int Arch_Create10k()
	{
		var world = World.Create();
		for (var i = 0; i < Entities; i++) world.Create(new APosition(), new ASprite());
		var n = world.Size;
		World.Destroy(world);
		return n;
	}

	[Benchmark]
	public int Friflo_Create10k()
	{
		var store = new EntityStore();
		for (var i = 0; i < Entities; i++) store.CreateEntity(new FPosition(), new FSprite());
		return store.Count;
	}

	// ---------------------------------------------------------------- structural churn: add + remove a tag on every entity

	[Benchmark]
	public int Arch_AddRemoveTag10k()
	{
		_arch.Add<ATag>(in _archQuery);
		_arch.Remove<ATag>(in _archQuery);
		return _arch.Size;
	}

	[Benchmark]
	public int Friflo_AddRemoveTag10k()
	{
		var buffer = _friflo.GetCommandBuffer();
		foreach (var entity in _frifloQuery.Entities) buffer.AddTag<FTag>(entity.Id);
		buffer.Playback();
		buffer = _friflo.GetCommandBuffer();
		foreach (var entity in _frifloQuery.Entities) buffer.RemoveTag<FTag>(entity.Id);
		buffer.Playback();
		return _friflo.Count;
	}
}
