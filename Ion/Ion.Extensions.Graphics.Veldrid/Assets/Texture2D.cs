
using VeldridLib = Veldrid;

namespace Ion.Extensions.Graphics;

public class Texture2D : BaseTexture
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
