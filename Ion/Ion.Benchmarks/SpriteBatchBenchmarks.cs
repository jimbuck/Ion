using System.Runtime.InteropServices;

using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Rendering2D;

namespace Ion.Benchmarks;

/// <summary>
/// CPU side of sprite batching, 10,000 sprites per frame across 1 or 16 textures, no GPU:
/// <list type="bullet">
/// <item><c>Legacy_*</c>: the removed Veldrid batcher's per-sprite work (a copy of its <c>SpriteBatchManager</c>): grouping by
/// texture in a dictionary and writing the 80-byte instance, with and without the per-sprite scissor transform it did.</item>
/// <item><c>V2_*</c>: the 2D renderer's <see cref="SpriteBatch"/> for a whole frame (<c>Begin</c>, 10,000 <c>Draw</c>,
/// <c>End</c>): the 40-byte instance, the per-segment sort and the split into draw ranges. <c>_WithUploadCopy</c> adds the
/// copy of the instances into staging memory that <c>IQueue.WriteBuffer</c> does.</item>
/// </list>
/// </summary>
[MemoryDiagnoser]
public class SpriteBatchBenchmarks
{
	private const int Sprites = 10_000;

	[Params(1, 16)]
	public int Textures { get; set; }

	private LegacySpriteBatchManager _legacy = null!;
	private ITexture2D[] _legacyTextures = null!;
	private SpriteBatch _batch = null!;
	private ITexture2D[] _textures = null!;
	private Vector2[] _positions = null!;
	// Per sprite: its texture and depth, precomputed so the loops measure the batchers, not an integer division.
	private ITexture2D[] _spriteTextures = null!;
	private ITexture2D[] _legacySpriteTextures = null!;
	private float[] _depths = null!;
	private Matrix4x4 _projection;
	private byte[] _staging = [];

	private static readonly RectangleF DefaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);

	[GlobalSetup]
	public void Setup()
	{
		_legacy = new LegacySpriteBatchManager();
		_legacyTextures = Enumerable.Range(0, Textures).Select(i => (ITexture2D)new FakeTexture(64, 64, "tex" + i)).ToArray();
		_textures = Enumerable.Range(0, Textures).Select(i => (ITexture2D)SpriteTextures.CpuOnly("tex" + i, 64, 64)).ToArray();
		_batch = new SpriteBatch(new NoGpuFrame());
		var rand = new Random(1234);
		_positions = Enumerable.Range(0, Sprites).Select(_ => new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080)).ToArray();
		_spriteTextures = Enumerable.Range(0, Sprites).Select(i => _textures[i % Textures]).ToArray();
		_legacySpriteTextures = Enumerable.Range(0, Sprites).Select(i => _legacyTextures[i % Textures]).ToArray();
		_depths = Enumerable.Range(0, Sprites).Select(i => (i & 7) / 8f).ToArray();
		_projection = Matrix4x4.CreateOrthographicOffCenter(0, 1920, 0, 1080, 1000f, -100f);
		_staging = new byte[Sprites * 40];

		// Warm the arrays so the steady state (not first-frame growth) is measured.
		LegacyAdd10k(withScissorTransform: true);
		_legacy.Clear();
		V2_Deferred();
		V2_TextureSort();
		V2_BackToFront();
	}

	[Benchmark(Baseline = true)]
	public int Legacy_Add10kSprites()
	{
		LegacyAdd10k(withScissorTransform: false);
		var n = _legacy.Count;
		_legacy.Clear();
		return n;
	}

	[Benchmark]
	public int Legacy_Add10kSprites_WithScissorTransform()
	{
		LegacyAdd10k(withScissorTransform: true);
		var n = _legacy.Count;
		_legacy.Clear();
		return n;
	}

	[Benchmark]
	public int V2_Deferred() => V2Frame(default);

	[Benchmark]
	public int V2_Deferred_WithUploadCopy()
	{
		var n = V2Frame(default);
		MemoryMarshal.AsBytes(_batch.Batcher.Instances).CopyTo(_staging);
		return n;
	}

	[Benchmark]
	public int V2_TextureSort() => V2Frame(new SpriteBatchOptions { SortMode = SpriteSortMode.Texture });

	[Benchmark]
	public int V2_BackToFront() => V2Frame(new SpriteBatchOptions { SortMode = SpriteSortMode.BackToFront });

	/// <summary>
	/// Runs every benchmark of this class interleaved many times in one process and prints the best time per sprite of
	/// each (the minimum is robust against a noisy machine, unlike the mean). For A/B checks while optimizing.
	/// </summary>
	public static void MinOfN(int rounds = 300)
	{
		foreach (var textures in new[] { 1, 16 })
		{
			var b = new SpriteBatchBenchmarks { Textures = textures };
			b.Setup();
			(string Name, Func<int> Run)[] cases =
			[
				(nameof(Legacy_Add10kSprites), b.Legacy_Add10kSprites),
				(nameof(Legacy_Add10kSprites_WithScissorTransform), b.Legacy_Add10kSprites_WithScissorTransform),
				(nameof(V2_Deferred), b.V2_Deferred),
				(nameof(V2_Deferred_WithUploadCopy), b.V2_Deferred_WithUploadCopy),
				(nameof(V2_TextureSort), b.V2_TextureSort),
				(nameof(V2_BackToFront), b.V2_BackToFront),
			];
			var best = cases.Select(_ => double.MaxValue).ToArray();
			for (var round = 0; round < rounds; round++)
			{
				for (var c = 0; c < cases.Length; c++)
				{
					var start = System.Diagnostics.Stopwatch.GetTimestamp();
					cases[c].Run();
					best[c] = Math.Min(best[c], System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalNanoseconds / Sprites);
				}
			}

			for (var c = 0; c < cases.Length; c++) Console.WriteLine($"{textures,2} textures  {cases[c].Name,-42} {best[c],6:F2} ns/sprite");
		}
	}

	private int V2Frame(SpriteBatchOptions options)
	{
		var batch = _batch;
		var textures = _spriteTextures;
		var depths = _depths;
		var positions = _positions;
		var size = new Vector2(32, 32);
		batch.Begin(options);
		for (var i = 0; i < Sprites; i++)
		{
			batch.Draw(textures[i], positions[i], size, new RectangleF(0, 0, 64, 64), Color.White, depth: depths[i]);
		}

		batch.End();
		return batch.Batcher.Count + batch.Batcher.Draws.Length;
	}

	private void LegacyAdd10k(bool withScissorTransform)
	{
		var size = new Vector2(32, 32);
		var textures = _legacySpriteTextures;
		var depths = _depths;
		for (var i = 0; i < Sprites; i++)
		{
			var texture = textures[i];
			ref var instance = ref _legacy.Add(texture);
			var scissor = withScissorTransform ? TransformRectF(DefaultScissor, _projection) : DefaultScissor;
			instance.Update(new Vector2(texture.Width, texture.Height), new RectangleF(_positions[i], size), new RectangleF(0, 0, 64, 64), Color.White, 0f, default, depths[i], scissor, SpriteEffect.None);
		}
	}

	private static RectangleF TransformRectF(RectangleF rect, Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}

	private sealed class FakeTexture(uint w, uint h, string name) : ITexture2D
	{
		public uint Width => w;
		public uint Height => h;
		public uint MipLevels => 1;
		public nint Id => name.GetHashCode();
		public string Name => name;
		public void Dispose() { }
	}

	private sealed class NoGpuFrame : IGraphicsFrame
	{
		public IGraphicsDevice Device => throw new InvalidOperationException("No GPU in this benchmark.");
		public bool IsRendering => false;
		public ITexture? ColorTarget => null;
		public ITexture? DepthTarget => null;
		public TextureFormat ColorFormat => TextureFormat.Rgba8Unorm;
		public TextureFormat DepthFormat => TextureFormat.Undefined;
		public uint Width => 1920;
		public uint Height => 1080;
		public Vector4 ClearColor => Vector4.Zero;
		public RenderPassColorAttachment ColorAttachment() => throw new InvalidOperationException();
		public RenderPassDepthStencilAttachment? DepthAttachment() => null;
	}
}

/// <summary>
/// The removed Veldrid backend's sprite grouping (<c>SpriteBatchManager</c>), kept verbatim as the benchmark baseline:
/// a dictionary of per-texture arrays of 80-byte instances.
/// </summary>
internal sealed class LegacySpriteBatchManager
{
	private readonly Stack<Group> _pool = new();
	private readonly Dictionary<ITexture2D, Group> _groups = [];

	public int Count
	{
		get
		{
			var n = 0;
			foreach (var group in _groups.Values) n += group.Count;
			return n;
		}
	}

	public ref Instance Add(ITexture2D texture)
	{
		if (!_groups.TryGetValue(texture, out var group))
		{
			if (!_pool.TryPop(out group)) group = new Group();
			group.Count = 0;
			_groups[texture] = group;
		}

		return ref group.Add();
	}

	public void Clear()
	{
		foreach (var group in _groups.Values) _pool.Push(group);
		_groups.Clear();
	}

	private sealed class Group
	{
		private Instance[] _items = new Instance[64];
		public int Count;

		public ref Instance Add()
		{
			if (Count >= _items.Length) Array.Resize(ref _items, (_items.Length + _items.Length / 2 + 63) & ~63);
			return ref _items[Count++];
		}
	}

	public struct Instance
	{
		public Vector4 UV;
		public Color Color;
		public Vector2 Scale;
		public Vector2 Origin;
		public Vector3 Location;
		public float Rotation;
		public RectangleF Scissor;

		public void Update(Vector2 textureSize, RectangleF destinationRectangle, RectangleF sourceRectangle, Color color, float rotation, Vector2 origin, float layerDepth, RectangleF scissor, SpriteEffect options)
		{
			var sourceSize = new Vector2(sourceRectangle.Width, sourceRectangle.Height) / textureSize;
			var pos = new Vector2(sourceRectangle.X, sourceRectangle.Y) / textureSize;
			if ((options & SpriteEffect.FlipHorizontally) != 0) { pos.X += sourceSize.X; sourceSize.X *= -1; }
			if ((options & SpriteEffect.FlipVertically) != 0) { pos.Y += sourceSize.Y; sourceSize.Y *= -1; }
			UV = new(pos.X, pos.Y, sourceSize.X, sourceSize.Y);
			Color = color;
			Scale = destinationRectangle.Size;
			Origin = origin * Scale;
			Location = new(destinationRectangle.Location, layerDepth);
			Rotation = rotation;
			Scissor = scissor;
		}
	}
}
