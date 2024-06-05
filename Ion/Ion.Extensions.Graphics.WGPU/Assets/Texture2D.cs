using WebGPU;

using static WebGPU.WebGPU;

namespace Ion.Extensions.Graphics;

public class Texture2D : BaseTexture
{
	public static implicit operator WGPUTexture(Texture2D texture) => texture._texture;

	public Texture2D(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor) : base(texture, textureDescriptor) { }
	public Texture2D(string label, WGPUTexture texture, WGPUTextureDescriptor textureDescriptor) : base(label, texture, textureDescriptor) { }
}

public unsafe static class TextureFactoryExtensions
{
	public static Texture2D CreateTexture2D(this IGraphicsContext graphicsContext, string label, int width, int height, WGPUTextureUsage usage, WGPUTextureFormat format, int mipLevels = 1, int samples = 1)
	{
		return CreateTexture2D(graphicsContext, label, (uint)width, (uint)height, usage, format, (uint)mipLevels, (uint)samples);
	}

	public static Texture2D CreateTexture2D(this IGraphicsContext graphicsContext, string label, uint width, uint height, WGPUTextureUsage usage, WGPUTextureFormat format, uint mipLevels = 1, uint samples = 1)
	{
		return CreateTexture2D(graphicsContext, label, new WGPUTextureDescriptor
		{
			dimension = WGPUTextureDimension._2D,
			size = new WGPUExtent3D(width, height, 1),
			mipLevelCount = mipLevels,
			sampleCount = samples,
			usage = usage,
			format = format,
		});
	}

	public static Texture2D CreateTexture2D(this IGraphicsContext graphicsContext, string label, WGPUTextureDescriptor textureDescriptor)
	{
		Texture2D texture;

		fixed (sbyte* labelPtr = label.GetUtf8Span())
		{
			textureDescriptor.label = labelPtr;

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
		if (target.IsEmpty) target = new Rectangle(0, 0, (int)texture.Width, (int)texture.Height);

		var dataItemSize = sizeof(T);

		var destination = new WGPUImageCopyTexture
		{
			texture = texture,
			aspect = WGPUTextureAspect.All,
			origin = new WGPUOrigin3D(target.X, target.Y, 0),
			mipLevel = mipLevel
		};

		var dataLayout = new WGPUTextureDataLayout
		{
			bytesPerRow = (uint)(texture.PixelSize * target.Width),
			rowsPerImage = (uint)target.Height
		};
		var writeSize = new WGPUExtent3D(target.Width, target.Height);

		//Console.WriteLine($"[{texture.Name}]\tTexture: {texture.Size}, Target: {target.Size}, Data: ({data.Length}) {dataLayout.bytesPerRow}x{dataLayout.rowsPerImage}");

		wgpuQueueWriteTexture(graphicsContext.Queue, &destination, data.GetPointerUnsafe(), (nuint)(data.Length * dataItemSize), &dataLayout, &writeSize);
	}
}
