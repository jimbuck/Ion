using System.Numerics;
using WebGPU;

using Ion.Extensions.Assets;
using SixLabors.ImageSharp.PixelFormats;

namespace Ion.Extensions.Graphics;

public abstract class BaseTexture : ITexture2D
{
	protected readonly WGPUTexture _texture;

	public nint Id => _texture.Handle;
	public string Name { get; private set; }

	public Vector2 Size { get; }

	public uint PixelSize { get; }

	internal BaseTexture(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor)
	{
		Name = _getLabel(textureDescriptor);
		_texture = texture;
		Size = new Vector2(textureDescriptor.size.width, textureDescriptor.size.height);
		PixelSize = _getPixelSize(textureDescriptor.format);
	}

	private unsafe string _getLabel(WGPUTextureDescriptor textureDescriptor)
	{
		return textureDescriptor.label == null ? string.Empty : new string(textureDescriptor.label);
	}

	public void Dispose()
	{
		//_texture.Dispose();
	}

	public static implicit operator WGPUTexture(BaseTexture texture) => texture._texture;

	private unsafe static uint _getPixelSize(WGPUTextureFormat format)
	{
		var size = format switch {
			WGPUTextureFormat.BGRA8Unorm or WGPUTextureFormat.BGRA8UnormSrgb => sizeof(Bgra32),
			_ => throw new NotImplementedException("Pixel size conversion not yet specified!")
		};

		return (uint)size;
	}
}
