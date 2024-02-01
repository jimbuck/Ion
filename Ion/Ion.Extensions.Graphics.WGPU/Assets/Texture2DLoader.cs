using System.Runtime.CompilerServices;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

using WebGPU;
using static WebGPU.WebGPU;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public static class Texture2DAssetManagerExtensions
{
	public static Texture2D Load<T>(this IBaseAssetManager assetManager, string path) where T : Texture2D
	{
		var loader = (Texture2DLoader)assetManager.GetLoader(typeof(Texture2D));
		return loader.Load(path);
	}
}

internal class Texture2DLoader(IGraphicsContext graphicsContext, IPersistentStorage storage) : IAssetLoader
{
	public Type AssetType { get; } = typeof(Texture2D);

	public Texture2D Load(string assetPath)
	{
		return _loadTexture2D(assetPath, storage.Assets.Read(assetPath));
	}

	private unsafe Texture2D _loadTexture2D(string name, Stream stream)
	{
		if (graphicsContext.Device.IsNull) throw new Exception("GraphicsDevice is not initialized yet!");

		var image = Image.Load<Rgba32>(stream);

		Span<Rgba32> pixels = new Rgba32[image.Width * image.Height];

		image.CopyPixelDataTo(pixels);

		return _createTexture2D(name, WGPUTextureFormat.RGBA8Unorm, (uint)image.Width, (uint)image.Height, 1, 1, 1, pixels);
	}

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

	private unsafe Texture2D _createTexture2D(
		string label,
		WGPUTextureFormat format,
		uint width,
		uint height,
		uint depth,
		uint mipLevels,
		uint sampleCount,
		Span<Rgba32> textureData)
	{
		Texture2D texture = graphicsContext.CreateTexture2D(label, width, height, WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopyDst, format);

		graphicsContext.WriteTexture2D(texture, textureData);

		//ulong offset = 0;
		//for (uint level = 0; level < mipLevels; level++)
		//{
		//	uint mipWidth = _getDimension(width, level);
		//	uint mipHeight = _getDimension(height, level);
		//	uint mipDepth = _getDimension(depth, level);
		//	uint subresourceSize = mipWidth * mipHeight * mipDepth * _getFormatSize(format);

		//	for (uint layer = 0; layer < sampleCount; layer++)
		//	{
		//		graphicsContext.WriteTexture2D(texture, textureData, level, new Rectangle(0, 0, (int)mipWidth, (int)mipHeight));

		//		offset += subresourceSize;
		//	}
		//}

		return texture;
	}

	private static uint _getFormatSize(WGPUTextureFormat format)
	{
		return format switch
		{
			WGPUTextureFormat.RGBA8Unorm => 4,
			WGPUTextureFormat.BC3RGBAUnorm => 1,
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
