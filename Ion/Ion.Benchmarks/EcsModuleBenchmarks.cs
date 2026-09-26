using System.Reflection;
using System.Runtime.CompilerServices;

using Arch.Core;

using Ion.Benchmarks.GeneratedApp;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;

using Ecs = Ion.Extensions.Ecs;

namespace Ion.Benchmarks;

/// <summary>
/// Stage 5 acceptance: a [Query] method expanded by the generator (<see cref="CornerQuerySystem"/>, compiled in
/// Ion.Benchmarks.GeneratedApp) against the raw chunk-span loop of roadmap 5.7 and Arch's delegate query, on 10,000
/// entities with the extraction-shaped work of <see cref="ArchQueryBenchmarks"/> writing a third component. Also the same
/// generated loop without its structural-change guard (the cost of the guard), and the reflection binder the runtime uses
/// when the generator did not expand the method.
/// </summary>
[MemoryDiagnoser]
public class EcsQueryBenchmarks
{
	private const int Entities = 10_000;

	private World _world = null!;
	private QueryDescription _query;
	private GameTime _time = new() { Delta = 1f / 60 };
	private GameLoopDelegate _reflection = null!;

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		var rand = new Random(42);
		for (var i = 0; i < Entities; i++)
		{
			_world.Create(new BenchTransform(new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080), rand.NextSingle()), new BenchSprite(i % 16, new Vector2(32, 32)), new BenchCorner());
		}

		_query = new QueryDescription().WithAll<BenchTransform, BenchSprite, BenchCorner>();

		var method = typeof(CornerQuerySystem).GetMethod("Corner", BindingFlags.NonPublic | BindingFlags.Static)!;
		var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
		Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddSingleton(services, _world);
		_reflection = method.GetCustomAttribute<Ecs.QueryAttribute>()!.Bind(null, method, Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services), "CornerQuerySystem.Corner");
	}

	[GlobalCleanup]
	public void Cleanup() => World.Destroy(_world);

	[Benchmark(Baseline = true)]
	public void ChunkSpans()
	{
		foreach (ref var chunk in _world.Query(in _query))
		{
			var transforms = chunk.GetSpan<BenchTransform>();
			var sprites = chunk.GetSpan<BenchSprite>();
			var corners = chunk.GetSpan<BenchCorner>();
			for (var i = 0; i < chunk.Count; i++) corners[i].Value = CornerMath.TopLeft(transforms[i], sprites[i]);
		}
	}

	[Benchmark]
	public void WorldQueryDelegate()
	{
		_world.Query(in _query, static (ref BenchTransform transform, ref BenchSprite sprite, ref BenchCorner corner) => corner.Value = CornerMath.TopLeft(transform, sprite));
	}

	[Benchmark]
	public void GeneratedQuery() => CornerQuerySystem.__IonQuery_Corner(_time, _world);

	[Benchmark]
	public void GeneratedQueryUnchecked() => UncheckedCornerQuerySystem.__IonQuery_Corner(_time, _world);

	[Benchmark]
	public void GeneratedLoopWithoutGuard()
	{
		// The generated loop by hand, minus the per-entity structural-change check.
		foreach (ref var chunk in _world.Query(in _query))
		{
			var count = chunk.Count;
			ref var c0 = ref chunk.GetFirst<BenchTransform>();
			ref var c1 = ref chunk.GetFirst<BenchSprite>();
			ref var c2 = ref chunk.GetFirst<BenchCorner>();
			for (var i = 0; i < count; i++) Unsafe.Add(ref c2, i).Value = CornerMath.TopLeft(Unsafe.Add(ref c0, i), Unsafe.Add(ref c1, i));
		}
	}

	[Benchmark]
	public void ReflectionBinder() => _reflection(_time);
}

/// <summary>
/// Transform propagation on a 10,000-entity tree of three levels (100 roots, 9 children each, 10 grandchildren each):
/// every entity recomputed (all roots moved), and nothing changed (the dirty check only).
/// </summary>
[MemoryDiagnoser]
public class TransformPropagationBenchmarks
{
	public const int Roots = 100, Children = 9, Grandchildren = 10;

	/// <summary>The number of entities in the tree.</summary>
	public const int Entities = Roots * (1 + Children * (1 + Grandchildren));

	private World _world = null!;
	private Entity[] _roots = null!;
	private Ecs.TransformPropagationSystem _system = null!;
	private float _offset;

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		_roots = new Entity[Roots];
		for (var r = 0; r < Roots; r++)
		{
			var root = _world.Create(new Ecs.Transform2D(new Vector2(r * 20, 0), 0.1f));
			_roots[r] = root;
			for (var c = 0; c < Children; c++)
			{
				var child = _world.Create(new Ecs.Transform2D(new Vector2(0, c * 10), 0.2f, new Vector2(1.5f)));
				Ecs.HierarchyExtensions.SetParent(_world, child, root);
				for (var g = 0; g < Grandchildren; g++)
				{
					var grandchild = _world.Create(new Ecs.Transform2D(new Vector2(g, 0)));
					Ecs.HierarchyExtensions.SetParent(_world, grandchild, child);
				}
			}
		}

		_system = new Ecs.TransformPropagationSystem(_world);
		_system.Propagate();
		if (_system.LastVisited != Entities) throw new InvalidOperationException($"{_system.LastVisited} entities visited.");
	}

	[GlobalCleanup]
	public void Cleanup() => World.Destroy(_world);

	[Benchmark(Baseline = true)]
	public int Propagate_AllDirty()
	{
		_offset += 1f;
		foreach (var root in _roots) _world.Get<Ecs.Transform2D>(root).Position.Y = _offset;
		_system.Propagate();
		return _system.LastUpdated;
	}

	[Benchmark]
	public int Propagate_Unchanged()
	{
		_system.Propagate();
		return _system.LastUpdated;
	}
}

/// <summary>
/// The 2D extraction of 10,000 sprites (16 textures, all in view) into the 2D renderer's sprite batch (CPU side, no GPU):
/// the whole frame (<c>Begin</c>, extraction, <c>End</c>) against the same 10,000 <c>Draw</c> calls made directly, and the
/// extraction alone (into a batch that discards the draws). With <see cref="Depths"/>, sprites have 8 interleaved depths,
/// so the extraction copies, sorts and then draws.
/// </summary>
[MemoryDiagnoser]
public class SpriteExtractionBenchmarks
{
	private const int Sprites = 10_000;

	[Params(false, true)]
	public bool Depths { get; set; }

	private World _world = null!;
	private SpriteBatch _batch = null!;
	private SpriteExtractionSystem _extraction = null!;
	private SpriteExtractionSystem _extractionOnly = null!;
	private ITexture2D[] _textures = null!;
	private Vector2[] _positions = null!;
	private readonly GameTime _time = new();

	[GlobalSetup]
	public void Setup()
	{
		_world = World.Create();
		_batch = new SpriteBatch(new SpriteBatchBenchmarks.NoGpuFrame());
		_textures = Enumerable.Range(0, 16).Select(i => (ITexture2D)SpriteTextures.CpuOnly("tex" + i, 64, 64)).ToArray();
		var rand = new Random(1234);
		_positions = new Vector2[Sprites];
		for (var i = 0; i < Sprites; i++)
		{
			_positions[i] = new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080);
			_world.Create(new Ecs.Transform2D(_positions[i]), new Ecs.Sprite(_textures[i % 16], new Vector2(32), Depths ? (i & 7) / 8f : 0f));
		}

		new Ecs.TransformPropagationSystem(_world).Propagate();
		_extraction = new SpriteExtractionSystem(_world, _batch, new BenchWindow { Size = new Vector2(1920, 1080) });
		_extractionOnly = new SpriteExtractionSystem(_world, new DiscardingSpriteBatch(), new BenchWindow { Size = new Vector2(1920, 1080) });

		// Warm up the arrays (steady state).
		Extract10k();
		Draw10kDirectly();
	}

	[GlobalCleanup]
	public void Cleanup() => World.Destroy(_world);

	[Benchmark(Baseline = true)]
	public void Draw10kDirectly()
	{
		_batch.Begin();
		for (var i = 0; i < Sprites; i++) _batch.Draw(_textures[i % 16], _positions[i], new Vector2(32), origin: new Vector2(0.5f), depth: Depths ? (i & 7) / 8f : 0f);
		_batch.End();
	}

	[Benchmark]
	public int Extract10k()
	{
		_batch.Begin();
		_extraction.Extract(_time);
		_batch.End();
		return _extraction.LastFrame.Extracted;
	}

	/// <summary>The extraction alone: the same work into a sprite batch that discards the draws.</summary>
	[Benchmark]
	public int Extract10k_DiscardingBatch()
	{
		_extractionOnly.Extract(_time);
		return _extractionOnly.LastFrame.Extracted;
	}

	private sealed class DiscardingSpriteBatch : ISpriteBatch
	{
		public void Begin(SpriteBatchOptions options = default) { }
		public void End() { }
		public void SetRenderTarget(Ion.Extensions.Graphics.Rhi.ITexture? target, Color? clearColor = null) { }
		public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0) { }
		public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0) { }
		public void DrawPoint(Color color, Vector2 position, float depth = 0) { }
		public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0) { }
		public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0) { }
		public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0) { }
		public void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None) { }
		public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) { }
		public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None) { }
	}

	private sealed class BenchWindow : IWindow
	{
		public uint Width { get; set; }
		public uint Height { get; set; }
		public Vector2 Size { get; set; }
		public bool IsClosing => false;
		public bool IsClosed => false;
		public bool IsActive => true;
		public bool IsVisible { get; set; }
		public bool IsMaximized { get; set; }
		public bool IsMinimized { get; set; }
		public bool IsFullscreen { get; set; }
		public bool IsBorderless { get; set; }
		public bool IsMouseGrabbed { get; set; }
		public string Title { get; set; } = "";
		public bool IsResizable { get; set; }
		public bool IsCursorVisible { get; set; }
	}
}
