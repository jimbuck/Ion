using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// An <see cref="ITexture2D"/> backed by an RHI <see cref="ITexture"/>, drawable by the 2D renderer's
/// <see cref="SpriteBatch"/>: textures loaded with <c>Load&lt;ITexture2D&gt;</c>, <see cref="RenderTarget2D"/>s and the
/// glyph atlas pages.
/// </summary>
/// <remarks>
/// The sprite batch keeps its per-texture state here (the batch slot of the current frame and the bind group of each
/// sampler preset), so looking a texture up while drawing costs a field compare, not a dictionary lookup.
/// </remarks>
public abstract class SpriteTexture : ITexture2D
{
	private static long _nextId;

	private ITexture? _texture;

	// The batch slot of the texture in the frame whose stamp is BatchStamp (see SpriteBatcher).
	internal int BatchStamp;
	internal int BatchSlot;

	// The last source rectangle drawn without flips and its packed UVs; default is the whole texture.
	internal RectangleF CachedSource;
	internal ulong CachedUv = SpriteInstance.FullUv;

	// 1 / Width and 1 / Height, for UV computation.
	internal float InverseWidth;
	internal float InverseHeight;

	// One bind group per SpriteSamplerMode, created on first use by the renderer that owns the layout.
	internal readonly IBindGroup?[] BindGroups = new IBindGroup?[4];
	internal object? BindGroupOwner;

	/// <summary>
	/// Wraps <paramref name="texture"/> (null only for CPU-side tests and benchmarks, with an explicit size).
	/// </summary>
	private protected SpriteTexture(string name, ITexture? texture, uint width, uint height, uint mipLevels, bool ownsTexture)
	{
		Name = name;
		_texture = texture;
		OwnsTexture = ownsTexture;
		Id = (nint)Interlocked.Increment(ref _nextId);
		SetSize(width, height, mipLevels);
	}

	/// <inheritdoc/>
	public nint Id { get; }

	/// <inheritdoc/>
	public string Name { get; }

	/// <inheritdoc/>
	public uint Width { get; private set; }

	/// <inheritdoc/>
	public uint Height { get; private set; }

	/// <inheritdoc/>
	public uint MipLevels { get; private set; }

	/// <summary>True once <see cref="Dispose"/> (or the renderer's teardown) released the GPU texture.</summary>
	public bool IsDisposed { get; private set; }

	/// <summary>Whether disposing this object destroys <see cref="Texture"/>.</summary>
	internal bool OwnsTexture { get; }

	/// <summary>The RHI texture.</summary>
	/// <exception cref="ObjectDisposedException">The texture was disposed.</exception>
	public ITexture Texture => _texture ?? throw new ObjectDisposedException(Name, "The texture was disposed (or the renderer was torn down).");

	internal ITexture? TextureOrNull => _texture;

	internal void SetSize(uint width, uint height, uint mipLevels)
	{
		Width = width;
		Height = height;
		MipLevels = mipLevels;
		InverseWidth = width == 0 ? 0f : 1f / width;
		InverseHeight = height == 0 ? 0f : 1f / height;
	}

	/// <summary>Drops the cached bind groups (after the texture's view changed, or before the texture goes).</summary>
	internal void ReleaseBindGroups()
	{
		for (var i = 0; i < BindGroups.Length; i++)
		{
			BindGroups[i]?.Dispose();
			BindGroups[i] = null;
		}

		BindGroupOwner = null;
	}

	/// <summary>Releases the bind groups and, when owned, the GPU texture. Safe to call more than once.</summary>
	public void Dispose()
	{
		if (IsDisposed) return;
		IsDisposed = true;
		ReleaseBindGroups();
		if (OwnsTexture) _texture?.Dispose();
		_texture = null;
		OnDisposed();
		GC.SuppressFinalize(this);
	}

	/// <summary>Called once after the GPU resources are released.</summary>
	private protected virtual void OnDisposed() { }
}

/// <summary>
/// A texture loaded from an image file by the 2D renderer's texture loader: RGBA8, premultiplied alpha, full mip chain.
/// </summary>
internal sealed class Texture2D : SpriteTexture
{
	private readonly GpuResourceTracker? _tracker;

	public Texture2D(string name, ITexture? texture, uint width, uint height, uint mipLevels, GpuResourceTracker? tracker)
		: base(name, texture, width, height, mipLevels, ownsTexture: true)
	{
		_tracker = tracker;
		tracker?.Add(this);
	}

	/// <summary>How many times the pixels were reloaded in place (asset hot reload).</summary>
	public int ReloadCount { get; set; }

	private protected override void OnDisposed() => _tracker?.Remove(this);
}

/// <summary>
/// A texture the sprite batch can render into (<see cref="ISpriteBatch.SetRenderTarget"/> with <see cref="SpriteTexture.Texture"/>)
/// and draw like any other <see cref="ITexture2D"/>. Its contents are premultiplied when drawn with
/// <see cref="SpriteBlendMode.AlphaBlend"/>, so draw it back with <see cref="SpriteBlendMode.AlphaBlend"/>.
/// </summary>
public sealed class RenderTarget2D : SpriteTexture
{
	/// <summary>
	/// Creates a <paramref name="width"/> by <paramref name="height"/> render target of <paramref name="format"/>
	/// (<see cref="TextureFormat.Rgba8Unorm"/> by default) on <paramref name="device"/>.
	/// </summary>
	public RenderTarget2D(IGraphicsDevice device, uint width, uint height, TextureFormat format = TextureFormat.Rgba8Unorm, string? name = null)
		: base(name ?? "RenderTarget2D", Create(device, width, height, format, name), width, height, 1, ownsTexture: true)
	{
	}

	/// <summary>Wraps an existing texture (which needs <see cref="TextureUsage.RenderAttachment"/> and <see cref="TextureUsage.TextureBinding"/>); the caller keeps owning it.</summary>
	public RenderTarget2D(ITexture texture, string? name = null)
		: base(name ?? "RenderTarget2D", texture ?? throw new ArgumentNullException(nameof(texture)), texture.Width, texture.Height, texture.MipLevelCount, ownsTexture: false)
	{
	}

	private static ITexture Create(IGraphicsDevice device, uint width, uint height, TextureFormat format, string? name)
	{
		ArgumentNullException.ThrowIfNull(device);
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);
		return device.CreateTexture(new TextureDescriptor(width, height, format,
			TextureUsage.RenderAttachment | TextureUsage.TextureBinding | TextureUsage.CopySrc, Label: name ?? "RenderTarget2D"));
	}
}

/// <summary>
/// Tracks the GPU textures created by the renderer's loaders so they can be released before the device is destroyed
/// (the asset manager disposes its assets only when the container is disposed, after the device).
/// </summary>
internal sealed class GpuResourceTracker
{
	private readonly HashSet<SpriteTexture> _textures = [];
	private readonly Lock _lock = new();

	public int Count
	{
		get { lock (_lock) return _textures.Count; }
	}

	public void Add(SpriteTexture texture)
	{
		lock (_lock) _textures.Add(texture);
	}

	public void Remove(SpriteTexture texture)
	{
		lock (_lock) _textures.Remove(texture);
	}

	/// <summary>Releases every tracked texture's GPU resources.</summary>
	public void ReleaseAll()
	{
		SpriteTexture[] textures;
		lock (_lock)
		{
			textures = [.. _textures];
			_textures.Clear();
		}

		foreach (var texture in textures) texture.Dispose();
	}
}

/// <summary>Factories for tests and benchmarks.</summary>
internal static class SpriteTextures
{
	/// <summary>A texture with a size and no GPU resource: drawable by a sprite batch that is never submitted.</summary>
	public static SpriteTexture CpuOnly(string name, uint width, uint height) => new Texture2D(name, null, width, height, 1, tracker: null);
}
