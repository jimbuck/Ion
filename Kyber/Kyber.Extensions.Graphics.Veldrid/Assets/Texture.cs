using System.Numerics;
using VeldridLib = Veldrid;


namespace Kyber.Extensions.Graphics;

public interface IAsset : IDisposable
{
	int Id { get; }
	string Name { get; }
}

public abstract class Texture : IAsset
{
	protected readonly VeldridLib.Texture _texture;

	public int Id => _texture.GetHashCode();

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	public Vector2 Size { get; }

	public string Name => _texture.Name;

	internal Texture(string name, VeldridLib.Texture texture)
	{
		texture.Name = name;
		_texture = texture;
		Size = new Vector2(Width, Height);
	}

	public void Dispose()
	{
		_texture.Dispose();
	}

	public static implicit operator VeldridLib.Texture(Texture texture) => texture._texture;
}

public class Texture2D : Texture
{
	public Texture2D(string name, VeldridLib.Texture texture) : base(name, texture) { }

	public static implicit operator VeldridLib.Texture(Texture2D texture) => texture._texture;
}

public static class TextureFactoryExtensions
{
	public static Texture2D CreateTexture2D(this VeldridLib.ResourceFactory factory, VeldridLib.TextureDescription textureDescription, string name)
	{
		textureDescription.Type = VeldridLib.TextureType.Texture2D;
		var texture = factory.CreateTexture(textureDescription);
		return new Texture2D(name, texture);
	}
}
