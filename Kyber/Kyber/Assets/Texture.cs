using VeldridTexture = Veldrid.Texture;

namespace Kyber.Assets;


public class Texture
{
	private readonly VeldridTexture _texture;

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	internal Texture(VeldridTexture texture)
	{
		_texture = texture;
	}

	public static implicit operator VeldridTexture(Texture texture) => texture._texture;

	//public static Texture Load(string path)
	//{

	//}
}
