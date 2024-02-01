using System.Numerics;
using VeldridLib = Veldrid;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public abstract class BaseTexture : ITexture2D
{
	protected readonly VeldridLib.Texture _texture;

	public nint Id => _texture.GetHashCode();
	public string Name => _texture.Name;

	public Vector2 Size { get; }

	internal BaseTexture(string name, VeldridLib.Texture texture)
	{
		texture.Name = name;
		_texture = texture;
		Size = new Vector2(texture.Width, texture.Height);
	}

	public void Dispose()
	{
		_texture.Dispose();
	}

	public static implicit operator VeldridLib.Texture(BaseTexture texture) => texture._texture;
}
