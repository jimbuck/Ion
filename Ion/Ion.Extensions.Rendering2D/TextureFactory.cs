using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// Creates sprite textures from pixels in memory (procedural textures, generated atlases). Textures created here are
/// released with the renderer's other GPU resources before the device is destroyed, so they need no explicit teardown
/// (disposing one earlier is fine).
/// </summary>
public sealed class TextureFactory
{
	private readonly IGraphicsFrame _frame;
	private readonly GpuResourceTracker _tracker;

	internal TextureFactory(IGraphicsFrame frame, GpuResourceTracker tracker)
	{
		_frame = frame;
		_tracker = tracker;
	}

	/// <summary>
	/// Creates a <paramref name="width"/> by <paramref name="height"/> texture from straight-alpha RGBA8 pixels (rows top
	/// to bottom), premultiplied and with a full mip chain like loaded textures. The device must exist (Init steps with
	/// the default order or later).
	/// </summary>
	public ITexture2D Create(string name, uint width, uint height, ReadOnlySpan<byte> rgba) => Create(name, width, height, rgba, premultiply: true);

	/// <summary>
	/// Creates a texture like <see cref="Create(string, uint, uint, ReadOnlySpan{byte})"/>, optionally keeping straight
	/// alpha (<paramref name="premultiply"/> false): for data textures and for materials that treat alpha themselves (the
	/// 3D renderer's glTF textures: base color, normal, metallic-roughness maps).
	/// </summary>
	public ITexture2D Create(string name, uint width, uint height, ReadOnlySpan<byte> rgba, bool premultiply)
	{
		ArgumentOutOfRangeException.ThrowIfZero(width);
		ArgumentOutOfRangeException.ThrowIfZero(height);
		if (rgba.Length != width * height * 4) throw new ArgumentException($"Expected {width * height * 4} bytes for {width}x{height} RGBA8, got {rgba.Length}.", nameof(rgba));

		var levels = Texture2DLoader.BuildMipChain(rgba.ToArray(), (int)width, (int)height, premultiply);
		var device = _frame.Device;
		var texture = device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.Rgba8Unorm,
			TextureUsage.TextureBinding | TextureUsage.CopyDst, (uint)levels.Count, Label: name));
		for (var level = 0; level < levels.Count; level++)
		{
			var w = Math.Max(1u, width >> level);
			var h = Math.Max(1u, height >> level);
			device.Queue.WriteTexture(texture, levels[level], w * 4, new TextureRegion(0, 0, w, h, (uint)level));
		}

		return new Texture2D(name, texture, width, height, (uint)levels.Count, _tracker);
	}
}
