//using System.Runtime.CompilerServices;

//using SixLabors.ImageSharp;
//using SixLabors.ImageSharp.PixelFormats;
//using SixLabors.ImageSharp.Processing;


//using Ion.Extensions.Assets;

//namespace Ion.Extensions.Graphics;

//public static class Texture2DAssetManagerExtensions
//{
//	public static Texture2D Load<T>(this IBaseAssetManager assetManager, string path) where T : Texture2D
//	{
//		var loader = (Texture2DLoader)assetManager.GetLoader(typeof(Texture2D));
//		return loader.Load(path);
//	}
//}

//internal class Texture2DLoader(IGraphicsContext graphicsContext, IPersistentStorage storage) : IAssetLoader
//{
//	private readonly IGraphicsContext _graphicsContext = graphicsContext;
//	private readonly IPersistentStorage _storage = storage;

//	public Type AssetType { get; } = typeof(Texture2D);

//	public Texture2D Load(string assetPath)
//	{
//		return _loadTexture2D(assetPath, _storage.Assets.Read(assetPath));
//	}

//	private unsafe Texture2D _loadTexture2D(string name, Stream stream)
//	{
//		if (_graphicsContext.Device is null) throw new Exception("GraphicsDevice is not initialized yet!");

//		var image = Image.Load<Rgba32>(stream);

//		Span<Rgba32> pixels = new Rgba32[image.Width * image.Height];

//		image.CopyPixelDataTo(pixels);


//		var texture = _createDeviceTexture(
//			name,
//			Wgpu.TextureFormat.RGBA8Unorm,
//			Wgpu.TextureDimension.TwoDimensions,
//			(uint)image.Width,
//			(uint)image.Height,
//			1,
//			1,
//			1,
//			pixels,
//			_graphicsContext.Device
//		);

//		return new Texture2D(texture);
//	}

//	private static Image<T>[] _generateMipmaps<T>(Image<T> baseImage, out int totalSize) where T : unmanaged, IPixel<T>
//	{
//		int mipLevelCount = ComputeMipLevels(baseImage.Width, baseImage.Height);
//		Image<T>[] mipLevels = new Image<T>[mipLevelCount];
//		mipLevels[0] = baseImage;
//		totalSize = baseImage.Width * baseImage.Height * Unsafe.SizeOf<T>();
//		int i = 1;

//		int currentWidth = baseImage.Width;
//		int currentHeight = baseImage.Height;
//		while (currentWidth != 1 || currentHeight != 1)
//		{
//			int newWidth = Math.Max(1, currentWidth / 2);
//			int newHeight = Math.Max(1, currentHeight / 2);
//			Image<T> newImage = baseImage.Clone(context => context.Resize(newWidth, newHeight, KnownResamplers.Lanczos3));
//			System.Diagnostics.Debug.Assert(i < mipLevelCount);
//			mipLevels[i] = newImage;

//			totalSize += newWidth * newHeight * Unsafe.SizeOf<T>();
//			i++;
//			currentWidth = newWidth;
//			currentHeight = newHeight;
//		}

//		System.Diagnostics.Debug.Assert(i == mipLevelCount);

//		return mipLevels;
//	}

//	public static int ComputeMipLevels(int width, int height)
//	{
//		return 1 + (int)Math.Floor(Math.Log(Math.Max(width, height), 2));
//	}

//	private static unsafe Texture _createDeviceTexture(
//		string label,
//		Wgpu.TextureFormat format,
//		Wgpu.TextureDimension dimension,
//		uint width,
//		uint height,
//		uint depth,
//		uint mipLevels,
//		uint sampleCount,
//		Span<Rgba32> textureData,
//		Device gd)
//	{
//		var textureSize = new Wgpu.Extent3D
//		{
//			width = width,
//			height = height,
//			depthOrArrayLayers = depth
//		};
		
//		Texture texture = gd.CreateTexture(label, Wgpu.TextureUsage.TextureBinding| Wgpu.TextureUsage.CopyDst, dimension, textureSize, format, mipLevels, sampleCount);

//		ulong offset = 0;
//		for (uint level = 0; level < mipLevels; level++)
//		{
//			uint mipWidth = _getDimension(width, level);
//			uint mipHeight = _getDimension(height, level);
//			uint mipDepth = _getDimension(depth, level);
//			uint subresourceSize = mipWidth * mipHeight * mipDepth * _getFormatSize(format);

//			var mipSize = new Wgpu.Extent3D
//			{
//				width = mipWidth,
//				height = mipHeight,
//				depthOrArrayLayers = mipDepth
//			};

//			for (uint layer = 0; layer < sampleCount; layer++)
//			{
//				gd.Queue.WriteTexture<Rgba32>(
//					destination: new ImageCopyTexture
//					{
//						Aspect = Wgpu.TextureAspect.All,
//						MipLevel = level,
//						Origin = default,
//						Texture = texture
//					},
//					data: textureData,
//					dataLayout: new Wgpu.TextureDataLayout
//					{
//						bytesPerRow = (uint)(sizeof(Rgba32) * mipWidth),
//						offset = offset,
//						rowsPerImage = (uint)mipHeight
//					},
//					writeSize: mipSize
//				);

//				offset += subresourceSize;
//			}
//		}

//		return texture;
//	}

//	private static uint _getFormatSize(Wgpu.TextureFormat format)
//	{
//		return format switch
//		{
//			Wgpu.TextureFormat.RGBA8Unorm => 4,
//			Wgpu.TextureFormat.BC3RGBAUnorm => 1,
//			_ => throw new NotImplementedException(),
//		};
//	}

//	private static uint _getDimension(uint largestLevelDimension, uint mipLevel)
//	{
//		uint ret = largestLevelDimension;
//		for (uint i = 0; i < mipLevel; i++) ret /= 2;

//		return Math.Max(1, ret);
//	}
//}
