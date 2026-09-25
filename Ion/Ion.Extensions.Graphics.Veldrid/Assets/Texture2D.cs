using VeldridLib = Veldrid;

namespace Ion.Extensions.Graphics;

#pragma warning disable CS0618 // BaseTexture and Texture2D stay public (obsolete) for one release; the backend still uses them internally.

[Obsolete(VeldridObsolete.ConcreteAssetTypes)]
public class Texture2D : BaseTexture
{
	public Texture2D(VeldridLib.Texture texture) : base(texture.Name, texture) { }
	public Texture2D(string name, VeldridLib.Texture texture) : base(name, texture) { }

	public static implicit operator VeldridLib.Texture(Texture2D texture) => texture._texture;
}

internal static class TextureFactoryExtensions
{
	public static Texture2D CreateTexture2D(this VeldridLib.ResourceFactory factory, VeldridLib.TextureDescription textureDescription, string name)
	{
		textureDescription.Type = VeldridLib.TextureType.Texture2D;
		var texture = factory.CreateTexture(textureDescription);
		return new Texture2D(name, texture);
	}
}
