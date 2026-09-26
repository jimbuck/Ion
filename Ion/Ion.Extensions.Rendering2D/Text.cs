using System.Numerics;
using System.Runtime.InteropServices;

using FontStashSharp;
using FontStashSharp.Interfaces;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// The glyph atlas of the 2D renderer: FontStashSharp rasterizes glyphs into pages created here (RGBA8, premultiplied white
/// with coverage in alpha), uploaded with <see cref="IQueue.WriteTexture"/>. The pages belong to the renderer and are
/// released at its teardown.
/// </summary>
internal sealed class GlyphAtlas(IGraphicsFrame frame, GpuResourceTracker tracker) : ITexture2DManager
{
	private int _pages;

	/// <summary>The number of atlas pages created.</summary>
	public int PageCount => _pages;

	public object CreateTexture(int width, int height)
	{
		var device = frame.Device;
		var name = $"Glyph atlas {_pages++}";
		var texture = device.CreateTexture(new TextureDescriptor((uint)width, (uint)height, TextureFormat.Rgba8Unorm,
			TextureUsage.TextureBinding | TextureUsage.CopyDst, Label: name));
		// Start transparent: bilinear sampling at glyph edges reads the texels around each glyph.
		device.Queue.WriteTexture(texture, new byte[width * height * 4]);
		return new Texture2D(name, texture, (uint)width, (uint)height, 1, tracker);
	}

	public DrawingPoint GetTextureSize(object texture)
	{
		var page = (Texture2D)texture;
		return new DrawingPoint((int)page.Width, (int)page.Height);
	}

	public void SetTextureData(object texture, DrawingRectangle bounds, byte[] data)
	{
		var page = (Texture2D)texture;
		if (page.IsDisposed || bounds.Width <= 0 || bounds.Height <= 0) return;
		frame.Device.Queue.WriteTexture(page.Texture, data, (uint)bounds.Width * 4,
			new TextureRegion((uint)bounds.X, (uint)bounds.Y, (uint)bounds.Width, (uint)bounds.Height));
	}
}

/// <summary>One glyph of a laid-out string: its atlas page, its quad at scale 1 relative to the text position, and its UVs.</summary>
internal readonly record struct GlyphQuad(Texture2D Page, Vector2 Position, Vector2 Size, ulong Uv);

/// <summary>A string laid out once and drawn from the cache afterwards.</summary>
internal sealed class TextLayout(GlyphQuad[] glyphs)
{
	public GlyphQuad[] Glyphs { get; } = glyphs;

	public long LastUsedFrame;
}

/// <summary>
/// Records the glyph quads FontStashSharp emits for a string (drawn once at the origin, scale 1, no rotation).
/// </summary>
internal sealed class LayoutRecorder(GlyphAtlas atlas) : IFontStashRenderer
{
	public readonly List<GlyphQuad> Glyphs = [];

	public ITexture2DManager TextureManager => atlas;

	public void Draw(object texture, Vector2 pos, DrawingRectangle? src, FSColor color, float rotation, Vector2 scale, float depth)
	{
		var page = (Texture2D)texture;
		var source = src ?? new DrawingRectangle(0, 0, (int)page.Width, (int)page.Height);
		var u0 = source.X * page.InverseWidth;
		var v0 = source.Y * page.InverseHeight;
		var u1 = (source.X + source.Width) * page.InverseWidth;
		var v1 = (source.Y + source.Height) * page.InverseHeight;
		Glyphs.Add(new GlyphQuad(page, pos, new Vector2(source.Width, source.Height) * scale, SpriteInstance.PackUv(u0, v0, u1, v1)));
	}
}

/// <summary>
/// A FontStashSharp font set loaded by the 2D renderer: fonts of any size from the same files, sharing one glyph atlas.
/// </summary>
internal sealed class FontSet(string name, FontSystem fontSystem, GlyphAtlas atlas) : IFontSet
{
	private static long _nextId;

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; } = name;

	internal FontSystem FontSystem { get; } = fontSystem;

	internal GlyphAtlas Atlas { get; } = atlas;

	public IFont CreateStyle(float size) => new Font(this, FontSystem.GetFont(size), size);

	public void Dispose() => FontSystem.Dispose();
}

/// <summary>
/// A font of a <see cref="FontSet"/> at one size. Strings are laid out once and cached by content: drawing the same text
/// again costs no FontStashSharp call and no allocation. Entries unused for a while are evicted.
/// </summary>
internal sealed class Font(FontSet fontSet, DynamicSpriteFont spriteFont, float fontSize) : IFont
{
	/// <summary>The number of cached layouts above which unused ones are evicted.</summary>
	public const int CacheSoftLimit = 256;

	/// <summary>Layouts not drawn for this many frames are evicted once the cache is above <see cref="CacheSoftLimit"/>.</summary>
	public const int EvictAfterFrames = 120;

	private readonly Dictionary<string, TextLayout> _layouts = new(StringComparer.Ordinal);
	private readonly List<string> _evict = [];
	private LayoutRecorder? _recorder;

	public IFontSet FontSet => fontSet;

	public float FontSize { get; } = fontSize;

	public float LineHeight => spriteFont.LineHeight;

	/// <summary>The number of cached layouts.</summary>
	public int CachedLayouts => _layouts.Count;

	internal DynamicSpriteFont SpriteFont => spriteFont;

	public Vector2 MeasureString(string text)
	{
		if (string.IsNullOrEmpty(text)) return Vector2.Zero;
		return spriteFont.MeasureString(text);
	}

	/// <summary>The layout of <paramref name="text"/>, from the cache or laid out now.</summary>
	internal TextLayout GetLayout(string text, long frame)
	{
		ref var slot = ref CollectionsMarshal.GetValueRefOrAddDefault(_layouts, text, out var exists);
		if (!exists)
		{
			_recorder ??= new LayoutRecorder(fontSet.Atlas);
			_recorder.Glyphs.Clear();
			spriteFont.DrawText(_recorder, text, Vector2.Zero, FSColor.White);
			slot = new TextLayout([.. _recorder.Glyphs]);
			if (_layouts.Count > CacheSoftLimit) _evictUnused(frame, text);
		}

		var layout = slot!;
		layout.LastUsedFrame = frame;
		return layout;
	}

	private void _evictUnused(long frame, string keep)
	{
		_evict.Clear();
		foreach (var (key, layout) in _layouts)
		{
			if (frame - layout.LastUsedFrame > EvictAfterFrames && !ReferenceEquals(key, keep)) _evict.Add(key);
		}

		foreach (var key in _evict) _layouts.Remove(key);
		_evict.Clear();
	}
}

/// <summary>
/// Loads <see cref="IFontSet"/> assets (TrueType or OpenType files) for the 2D renderer.
/// </summary>
internal sealed class FontSetLoader(IPersistentStorage storage, GlyphAtlas atlas) : IFontSetLoader
{
	public Type AssetType { get; } = typeof(IFontSet);

	public IFontSet Load(string path) => Load(path, [path]);

	public IFontSet Load(string name, IReadOnlyList<string> fonts)
	{
		var fontSystem = new FontSystem(new FontSystemSettings { PremultiplyAlpha = true });
		foreach (var font in fonts)
		{
			using var stream = storage.Assets.Read(font);
			fontSystem.AddFont(stream);
		}

		return new FontSet(name, fontSystem, atlas);
	}
}
