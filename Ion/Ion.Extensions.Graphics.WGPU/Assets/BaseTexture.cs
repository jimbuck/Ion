using WebGPU;
using SixLabors.ImageSharp.PixelFormats;

namespace Ion.Extensions.Graphics;

public abstract class BaseTexture : ITexture2D
{
	protected readonly WGPUTexture _texture;

	public nint Id => _texture.Handle;
	public string Name { get; }

	public uint Width { get; }
	public uint Height { get; }

	public uint MipLevels { get; }

	public uint PixelSize { get; }

	internal BaseTexture(string label, WGPUTexture texture, WGPUTextureDescriptor textureDescriptor)
	{
		Name = label;
		_texture = texture;
		Width = textureDescriptor.size.width;
		Height = textureDescriptor.size.height;
		MipLevels = textureDescriptor.mipLevelCount;
		PixelSize = _getPixelSize(textureDescriptor.format);
	}

	internal BaseTexture(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor) : this(_getLabel(textureDescriptor), texture, textureDescriptor) { }

	public WGPUTextureView CreateView()
	{
		return _texture.CreateView();
	}

	public WGPUTextureView CreateView(string label, WGPUTextureViewDescriptor descriptor)
	{
		return _texture.CreateView(label, descriptor);
	}
	

	public void Dispose()
	{
		_texture.Dispose();
	}

	public static implicit operator WGPUTexture(BaseTexture texture) => texture._texture;

	private unsafe static uint _getPixelSize(WGPUTextureFormat format)
	{
		var size = format switch {
			WGPUTextureFormat.BGRA8Unorm or WGPUTextureFormat.BGRA8UnormSrgb => sizeof(Bgra32),
			WGPUTextureFormat.RGBA8Unorm or WGPUTextureFormat.RGBA8UnormSrgb => sizeof(Rgba32),
			WGPUTextureFormat.Depth24Plus or WGPUTextureFormat.Depth24PlusStencil8 or WGPUTextureFormat.Depth32Float => 4,
			_ => throw new NotImplementedException("Pixel size conversion not yet specified!")
		};

		return (uint)size;
	}

	private static unsafe string _getLabel(WGPUTextureDescriptor textureDescriptor)
	{
		return textureDescriptor.label == null ? string.Empty : new string(textureDescriptor.label);
	}
}
