using System.Numerics;
using WebGPU;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public abstract class BaseTexture : ITexture2D
{
	protected readonly WGPUTexture _texture;

	public nint Id => _texture.Handle;
	public string Name { get; private set; }

	public Vector2 Size { get; }

	internal BaseTexture(WGPUTexture texture, WGPUTextureDescriptor textureDescriptor)
	{
		Name = _getLabel(textureDescriptor);
		_texture = texture;
		Size = new Vector2(textureDescriptor.size.width, textureDescriptor.size.height);
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
}
