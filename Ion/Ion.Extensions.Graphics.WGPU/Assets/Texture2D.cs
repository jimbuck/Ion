using WebGPU;

using static WebGPU.WebGPU;

namespace Ion.Extensions.Graphics;

public class Texture2D(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor) : BaseTexture(texture, textureDescriptor)
{
	public static implicit operator WGPUTexture(Texture2D texture) => texture._texture;
}

public unsafe static class TextureFactoryExtensions
{
	public static Texture2D CreateTexture2D(this IGraphicsContext graphicsContext, string label, int width, int height, WGPUTextureUsage usage, WGPUTextureFormat format, int mipLevels = 1, int samples = 1)
	{
		return CreateTexture2D(graphicsContext, label, (uint)width, (uint)height, usage, format, (uint)mipLevels, (uint)samples);
	}


	public static Texture2D CreateTexture2D(this IGraphicsContext graphicsContext, string label, uint width, uint height, WGPUTextureUsage usage, WGPUTextureFormat format, uint mipLevels = 1, uint samples = 1)
	{
		Texture2D texture;

		fixed (sbyte* labelPtr = label.GetUtf8Span())
		{
			var textureDescriptor = new WGPUTextureDescriptor
			{
				label = labelPtr,
				dimension = WGPUTextureDimension._2D,
				size = new WGPUExtent3D(width, height, 1),
				mipLevelCount = mipLevels,
				sampleCount = samples,
				usage = usage,
				format = format,
			};

			var wgpuTexture = wgpuDeviceCreateTexture(graphicsContext.Device, &textureDescriptor);
			texture = new Texture2D(wgpuTexture, textureDescriptor);
		}

		return texture;
	}

	public static void WriteTexture2D<T>(this IGraphicsContext graphicsContext, Texture2D texture, T[] data, int mipLevel = 0, Rectangle target = default) where T : unmanaged
	{
		WriteTexture2D(graphicsContext, texture, data.AsSpan(), (uint)mipLevel, target);
	}

	public static void WriteTexture2D<T>(this IGraphicsContext graphicsContext, Texture2D texture, Span<T> data, int mipLevel = 0, Rectangle target = default) where T : unmanaged
	{
		WriteTexture2D(graphicsContext, texture, data, (uint)mipLevel, target);
	}

	public static void WriteTexture2D<T>(this IGraphicsContext graphicsContext, Texture2D texture, Span<T> data, uint mipLevel, Rectangle target) where T : unmanaged
	{
		if (target.IsEmpty) target = new Rectangle(0, 0, (int)texture.Size.X, (int)texture.Size.Y);

		var destination = new WGPUImageCopyTexture
		{
			aspect = WGPUTextureAspect.All,
			mipLevel = mipLevel,
			origin = new WGPUOrigin3D(target.X, target.Y, 0),
			texture = texture,
		};

		var dataLayout = new WGPUTextureDataLayout
		{
			bytesPerRow = (uint)(texture.PixelSize * texture.Size.X),
			offset = 0,
			rowsPerImage = (uint)texture.Size.Y
		};
		var writeSize = new WGPUExtent3D(target.Width, target.Height);

		wgpuQueueWriteTexture(graphicsContext.Queue, &destination, data.GetPointerUnsafe(), (nuint)data.Length, &dataLayout, &writeSize);
	}
}
