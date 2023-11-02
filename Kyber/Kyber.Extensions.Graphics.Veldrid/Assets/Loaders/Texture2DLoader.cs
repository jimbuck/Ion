using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using VeldridLib = Veldrid;

namespace Kyber.Extensions.Graphics;

internal class Texture2DLoader : IAssetLoader
{
	private readonly IVeldridGraphicsContext _graphicsContext;

	public Type AssetType { get; } = typeof(Texture2D);

	public Texture2DLoader(IVeldridGraphicsContext graphicsContext)
	{
		_graphicsContext = graphicsContext;
	}

	public T Load<T>(Stream stream, string name) where T : class, IAsset
	{
		if (typeof(T) == typeof(Texture2D)) return (T)_loadTexture2D(stream, name);

		throw new ArgumentException("Incorrect type specified for loading!", nameof(T));
	}

	private unsafe IAsset _loadTexture2D(Stream stream, string name)
	{
		if (_graphicsContext.GraphicsDevice is null) throw new Exception("GraphicsDevice is not initialized yet!");

		var image = Image.Load<Rgba32>(stream);
		var mipmaps = _generateMipmaps(image, out int totalSize);

		var allTexData = new byte[totalSize];
		long offset = 0;
		fixed (byte* allTexDataPtr = allTexData)
		{
			foreach (var mipmap in mipmaps)
			{
				long mipSize = mipmap.Width * mipmap.Height * sizeof(Rgba32);
				if (!mipmap.TryGetSinglePixelSpan(out Span<Rgba32> pixelSpan)) throw new Exception("Unable to get image pixelspan.");
				fixed (void* pixelPtr = &MemoryMarshal.GetReference(pixelSpan))
				{
					Buffer.MemoryCopy(pixelPtr, allTexDataPtr + offset, mipSize, mipSize);
				}

				offset += mipSize;
			}
		}

		var texture = _createDeviceTexture(
				VeldridLib.PixelFormat.R8_G8_B8_A8_UNorm, VeldridLib.TextureType.Texture2D,
				(uint)image.Width, (uint)image.Height, 1,
				(uint)mipmaps.Length, 1,
				allTexData,
				_graphicsContext.GraphicsDevice, VeldridLib.TextureUsage.Sampled);

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

	private unsafe VeldridLib.Texture _createDeviceTexture(
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

		VeldridLib.CommandList cl = gd.ResourceFactory.CreateCommandList();
		cl.Begin();
		cl.CopyTexture(staging, texture);
		cl.End();
		gd.SubmitCommands(cl);

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
