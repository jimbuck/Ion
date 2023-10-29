using System.Numerics;
using VeldridTexture = Veldrid.Texture;


namespace Kyber.Assets;

public interface IAsset : IDisposable
{
	int Id { get; }
	string Name { get; }
}

public abstract class Texture : IAsset
{
	protected readonly VeldridTexture _texture;

	public int Id => _texture.GetHashCode();

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	public Vector2 Size { get; }

	public string Name => _texture.Name;

	internal Texture(string name, VeldridTexture texture)
	{
		texture.Name = name;
		_texture = texture;
		Size = new Vector2(Width, Height);
	}

	public void Dispose()
	{
		_texture.Dispose();
	}

	public static implicit operator VeldridTexture(Texture texture) => texture._texture;
}

public class Texture2D : Texture
{
	public Texture2D(string name, VeldridTexture texture) : base(name, texture) { }

	public static implicit operator VeldridTexture(Texture2D texture) => texture._texture;
}

public static class TextureFactoryExtensions
{
	public static Texture2D CreateTexture2D(this Veldrid.ResourceFactory factory, Veldrid.TextureDescription textureDescription, string name)
	{
		textureDescription.Type = Veldrid.TextureType.Texture2D;
		var texture = factory.CreateTexture(textureDescription);
		return new Texture2D(name, texture);
	}
}
