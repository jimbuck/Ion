using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;

namespace Ion.Benchmarks;

/// <summary>
/// CPU side of the sprite batch: grouping sprites by texture and writing the 80-byte <see cref="SpriteBatchManager.SpriteInstance"/>
/// (what <c>SpriteRenderer._addSprite</c> does per Draw call, minus GPU upload). The scissor transform variant replicates the two
/// <see cref="Vector4.Transform(Vector4, Matrix4x4)"/> calls the renderer performs per sprite even though scissoring is never exposed by the API.
/// </summary>
[MemoryDiagnoser]
public class SpriteBatchBenchmarks
{
	private sealed class FakeTexture(uint w, uint h, string name) : ITexture2D
	{
		public uint Width => w;
		public uint Height => h;
		public uint MipLevels => 1;
		public nint Id => name.GetHashCode();
		public string Name => name;
		public void Dispose() { }
	}

	private static readonly RectangleF DefaultScissor = new(-(1 << 22), -(1 << 22), 1 << 23, 1 << 23);

	[Params(1, 16)]
	public int Textures { get; set; }

	private const int Sprites = 10_000;

	private SpriteBatchManager _manager = null!;
	private ITexture2D[] _textures = null!;
	private Vector2[] _positions = null!;
	private Matrix4x4 _projection;

	[GlobalSetup]
	public void Setup()
	{
		_manager = new SpriteBatchManager();
		_textures = Enumerable.Range(0, Textures).Select(i => (ITexture2D)new FakeTexture(64, 64, "tex" + i)).ToArray();
		var rand = new Random(1234);
		_positions = Enumerable.Range(0, Sprites).Select(_ => new Vector2(rand.NextSingle() * 1920, rand.NextSingle() * 1080)).ToArray();
		_projection = Matrix4x4.CreateOrthographicOffCenter(0, 1920, 0, 1080, 1000f, -100f);

		// warm the pools so that steady-state (not first-frame growth) is measured
		Add10k(withScissorTransform: true);
		_manager.Clear();
	}

	[Benchmark(Baseline = true)]
	public int Add10kSprites()
	{
		Add10k(withScissorTransform: false);
		var n = 0;
		foreach (var kv in _manager) n += kv.Value.Count;
		_manager.Clear();
		return n;
	}

	[Benchmark]
	public int Add10kSprites_WithScissorTransform()
	{
		Add10k(withScissorTransform: true);
		var n = 0;
		foreach (var kv in _manager) n += kv.Value.Count;
		_manager.Clear();
		return n;
	}

	private void Add10k(bool withScissorTransform)
	{
		var size = new Vector2(32, 32);
		var textures = _textures;
		for (var i = 0; i < Sprites; i++)
		{
			var texture = textures[i % textures.Length];
			ref var instance = ref _manager.Add(texture);
			var scissor = withScissorTransform ? TransformRectF(DefaultScissor, _projection) : DefaultScissor;
			instance.Update(new Vector2(texture.Width, texture.Height), new RectangleF(_positions[i], size), new RectangleF(0, 0, 64, 64), Color.White, 0f, default, 0f, scissor, SpriteEffect.None);
		}
	}

	private static RectangleF TransformRectF(RectangleF rect, Matrix4x4 matrix)
	{
		var pos = Vector4.Transform(new Vector4(rect.X, rect.Y, 0, 1), matrix);
		var size = Vector4.Transform(new Vector4(rect.X + rect.Width, rect.Y + rect.Height, 0, 1), matrix);
		return new(pos.X, pos.Y, size.X - pos.X, size.Y - pos.Y);
	}
}
