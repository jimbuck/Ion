using System.Runtime.InteropServices;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Rendering2D;

/// <summary>
/// Loads <see cref="ITexture2D"/> assets for the 2D renderer: decodes the image with ImageSharp, premultiplies alpha,
/// builds the full mip chain on the CPU (2x2 box filter of the premultiplied texels) and uploads every level with
/// <see cref="IQueue.WriteTexture"/> (staged per frame; nothing waits for the GPU).
/// </summary>
/// <remarks>
/// Hot reload: when the image keeps its size, <see cref="TryReload"/> uploads the new pixels into the same GPU texture, so
/// every holder (and every cached bind group) sees them. A different size returns false and the asset manager swaps in a
/// newly loaded texture.
/// </remarks>
internal sealed class Texture2DLoader(IGraphicsFrame frame, IPersistentStorage storage, GpuResourceTracker tracker) : IAssetLoader<ITexture2D>, IReloadableAssetLoader
{
	public Type AssetType { get; } = typeof(ITexture2D);

	/// <summary>Decodes and uploads a new texture on every call. Use <c>IBaseAssetManager.Load&lt;ITexture2D&gt;(path)</c> for the cached one.</summary>
	public ITexture2D Load(string path)
	{
		var levels = Decode(path, out var width, out var height);
		var device = frame.Device;
		var texture = device.CreateTexture(new TextureDescriptor(width, height, TextureFormat.Rgba8Unorm,
			TextureUsage.TextureBinding | TextureUsage.CopyDst, (uint)levels.Count, Label: path));
		Upload(device.Queue, texture, levels, width, height);
		return new Texture2D(path, texture, width, height, (uint)levels.Count, tracker);
	}

	public bool TryReload(IAsset asset, string path)
	{
		if (asset is not Texture2D texture || texture.IsDisposed || texture.TextureOrNull is not { } gpu) return false;
		var levels = Decode(path, out var width, out var height);
		if (width != texture.Width || height != texture.Height || (uint)levels.Count != gpu.MipLevelCount) return false;
		Upload(frame.Device.Queue, gpu, levels, width, height);
		texture.ReloadCount++;
		return true;
	}

	private List<byte[]> Decode(string path, out uint width, out uint height)
	{
		using var stream = storage.Assets.Read(path);
		using var image = Image.Load<Rgba32>(stream);
		width = (uint)image.Width;
		height = (uint)image.Height;
		var pixels = new byte[image.Width * image.Height * 4];
		image.CopyPixelDataTo(pixels);
		return BuildMipChain(pixels, image.Width, image.Height);
	}

	private static void Upload(IQueue queue, ITexture texture, List<byte[]> levels, uint width, uint height)
	{
		for (var level = 0; level < levels.Count; level++)
		{
			var w = Math.Max(1u, width >> level);
			var h = Math.Max(1u, height >> level);
			queue.WriteTexture(texture, levels[level], w * 4, new TextureRegion(0, 0, w, h, (uint)level));
		}
	}

	/// <summary>The number of levels of a full mip chain down to 1x1.</summary>
	public static int MipLevelCount(int width, int height) => 1 + (int)Math.Floor(Math.Log2(Math.Max(1, Math.Max(width, height))));

	/// <summary>
	/// Premultiplies <paramref name="rgba"/> (straight RGBA8, rows top to bottom) in place and returns it followed by every
	/// smaller level (each texel the average of the 2x2 block above it, edge texels repeated for odd sizes).
	/// </summary>
	public static List<byte[]> BuildMipChain(byte[] rgba, int width, int height)
	{
		Premultiply(rgba);
		var levels = new List<byte[]>(MipLevelCount(width, height)) { rgba };
		var source = rgba;
		int w = width, h = height;
		while (w > 1 || h > 1)
		{
			var nw = Math.Max(1, w / 2);
			var nh = Math.Max(1, h / 2);
			var next = new byte[nw * nh * 4];
			for (var y = 0; y < nh; y++)
			{
				var y0 = Math.Min(y * 2, h - 1);
				var y1 = Math.Min(y * 2 + 1, h - 1);
				for (var x = 0; x < nw; x++)
				{
					var x0 = Math.Min(x * 2, w - 1);
					var x1 = Math.Min(x * 2 + 1, w - 1);
					var a = (y0 * w + x0) * 4;
					var b = (y0 * w + x1) * 4;
					var c = (y1 * w + x0) * 4;
					var d = (y1 * w + x1) * 4;
					var o = (y * nw + x) * 4;
					for (var ch = 0; ch < 4; ch++)
					{
						next[o + ch] = (byte)((source[a + ch] + source[b + ch] + source[c + ch] + source[d + ch] + 2) >> 2);
					}
				}
			}

			levels.Add(next);
			source = next;
			w = nw;
			h = nh;
		}

		return levels;
	}

	/// <summary>Multiplies the color of every RGBA8 texel by its alpha.</summary>
	public static void Premultiply(Span<byte> rgba)
	{
		var texels = MemoryMarshal.Cast<byte, uint>(rgba);
		for (var i = 0; i < texels.Length; i++)
		{
			var t = texels[i];
			var a = t >> 24;
			if (a == 255) continue;
			if (a == 0)
			{
				texels[i] = 0;
				continue;
			}

			var r = ((t & 0xFF) * a + 127) / 255;
			var g = (((t >> 8) & 0xFF) * a + 127) / 255;
			var b = (((t >> 16) & 0xFF) * a + 127) / 255;
			texels[i] = r | (g << 8) | (b << 16) | (a << 24);
		}
	}
}
