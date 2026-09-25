using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using VeldridLib = Veldrid;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public static class Texture2DAssetManagerExtensions
{
	/// <summary>
	/// Loads the texture at <paramref name="path"/> and registers it with <paramref name="assetManager"/>.
	/// Loading a path whose texture is still alive returns that same texture instead of creating a second GPU texture.
	/// </summary>
	public static Texture2D Load<T>(this IBaseAssetManager assetManager, string path) where T : Texture2D
	{
		var loader = (Texture2DLoader)assetManager.GetLoader(typeof(Texture2D));

		if (loader.TryGetLoaded(path, out var existing)) return existing;

		return assetManager.Set(loader.Load(path));
	}
}

internal class Texture2DLoader(IGraphicsContext graphicsContext, IPersistentStorage storage) : IAssetLoader
{
	private readonly IGraphicsContext _graphicsContext = graphicsContext;
	private readonly IPersistentStorage _storage = storage;

	// The asset manager keys its cache by name and rejects duplicates, so remember which textures are live
	// and hand the same instance back for repeated loads of one path until it is disposed.
	private readonly Dictionary<string, Texture2D> _loaded = [];

	public Type AssetType { get; } = typeof(Texture2D);

	internal bool TryGetLoaded(string assetPath, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out Texture2D? texture)
	{
		if (_loaded.TryGetValue(assetPath, out texture) && !texture.IsDisposed) return true;

		_loaded.Remove(assetPath);
		texture = null;
		return false;
	}

	public Texture2D Load(string assetPath)
	{
		using var stream = _storage.Assets.Read(assetPath);
		var texture = _loadTexture2D(assetPath, stream);
		_loaded[assetPath] = texture;
		return texture;
	}

	private unsafe Texture2D _loadTexture2D(string name, Stream stream)
	{
		if (_graphicsContext.GraphicsDevice is null) throw new Exception("GraphicsDevice is not initialized yet!");

		using var image = Image.Load<Rgba32>(stream);
		var mipmaps = _generateMipmaps(image, out int totalSize);
		try
		{
			return _uploadTexture2D(name, image, mipmaps, totalSize);
		}
		finally
		{
			// Level 0 is `image` itself, disposed by the using above.
			for (var i = 1; i < mipmaps.Length; i++) mipmaps[i].Dispose();
		}
	}

	private unsafe Texture2D _uploadTexture2D(string name, Image<Rgba32> image, Image<Rgba32>[] mipmaps, int totalSize)
	{

		var allTexData = new byte[totalSize];
		long offset = 0;
		fixed (byte* allTexDataPtr = allTexData)
		{
			foreach (var mipmap in mipmaps)
			{
				long mipSize = mipmap.Width * mipmap.Height * sizeof(Rgba32);
				mipmap.CopyPixelDataTo(new Span<Rgba32>(allTexDataPtr + offset, mipmap.Width * mipmap.Height));

				offset += mipSize;
			}
		}

		var texture = _createDeviceTexture(
				VeldridLib.PixelFormat.R8_G8_B8_A8_UNorm, VeldridLib.TextureType.Texture2D,
				(uint)image.Width, (uint)image.Height, 1,
				(uint)mipmaps.Length, 1,
				allTexData,
				_graphicsContext.GraphicsDevice!, VeldridLib.TextureUsage.Sampled);

		return new Texture2D(name, texture);
	}

	// Taken from Veldrid.ImageSharp

	private static Image<T>[] _generateMipmaps<T>(Image<T> baseImage, out int totalSize) where T : unmanaged, IPixel<T>
	{
		int mipLevelCount = ComputeMipLevels(baseImage.Width, baseImage.Height);
		Image<T>[] mipLevels = new Image<T>[mipLevelCount];
		mipLevels[0] = baseImage;
		totalSize = baseImage.Width * baseImage.Height * Unsafe.SizeOf<T>();
		int i = 1;

		int currentWidth = baseImage.Width;
		int currentHeight = baseImage.Height;
		while (currentWidth != 1 || currentHeight != 1)
		{
			int newWidth = Math.Max(1, currentWidth / 2);
			int newHeight = Math.Max(1, currentHeight / 2);
			Image<T> newImage = baseImage.Clone(context => context.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
			System.Diagnostics.Debug.Assert(i < mipLevelCount);
			mipLevels[i] = newImage;

			totalSize += newWidth * newHeight * Unsafe.SizeOf<T>();
			i++;
			currentWidth = newWidth;
			currentHeight = newHeight;
		}

		System.Diagnostics.Debug.Assert(i == mipLevelCount);

		return mipLevels;
	}

	public static int ComputeMipLevels(int width, int height)
	{
		return 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2));
	}

	private static unsafe VeldridLib.Texture _createDeviceTexture(
		VeldridLib.PixelFormat format,
		VeldridLib.TextureType type,
		uint width,
		uint height,
		uint depth,
		uint mipLevels,
		uint arrayLayers,
		byte[] textureData,
		VeldridLib.GraphicsDevice gd, VeldridLib.TextureUsage usage)
	{
		VeldridLib.Texture texture = gd.ResourceFactory.CreateTexture(new VeldridLib.TextureDescription(width, height, depth, mipLevels, arrayLayers, format, usage, type));

		VeldridLib.Texture staging = gd.ResourceFactory.CreateTexture(new VeldridLib.TextureDescription(width, height, depth, mipLevels, arrayLayers, format, VeldridLib.TextureUsage.Staging, type));

		ulong offset = 0;
		fixed (byte* texDataPtr = &textureData[0])
		{
			for (uint level = 0; level < mipLevels; level++)
			{
				uint mipWidth = _getDimension(width, level);
				uint mipHeight = _getDimension(height, level);
				uint mipDepth = _getDimension(depth, level);
				uint subresourceSize = mipWidth * mipHeight * mipDepth * _getFormatSize(format);

				for (uint layer = 0; layer < arrayLayers; layer++)
				{
					gd.UpdateTexture(staging, (IntPtr)(texDataPtr + offset), subresourceSize, 0, 0, 0, mipWidth, mipHeight, mipDepth, level, layer);
					offset += subresourceSize;
				}
			}
		}

		using (staging)
		using (VeldridLib.CommandList cl = gd.ResourceFactory.CreateCommandList())
		{
			cl.Begin();
			cl.CopyTexture(staging, texture);
			cl.End();
			gd.SubmitCommands(cl);
			// The copy must finish before the staging texture and command list are destroyed.
			gd.WaitForIdle();
		}

		return texture;
	}

	private static uint _getFormatSize(VeldridLib.PixelFormat format)
	{
		return format switch
		{
			VeldridLib.PixelFormat.R8_G8_B8_A8_UNorm => 4,
			VeldridLib.PixelFormat.BC3_UNorm => 1,
			_ => throw new NotImplementedException(),
		};
	}

	private static uint _getDimension(uint largestLevelDimension, uint mipLevel)
	{
		uint ret = largestLevelDimension;
		for (uint i = 0; i < mipLevel; i++) ret /= 2;

		return Math.Max(1, ret);
	}
}
