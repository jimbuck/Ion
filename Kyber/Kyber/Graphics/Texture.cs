namespace Kyber.Graphics;

public class Texture
{
	private readonly Veldrid.Texture _texture;

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	public Texture(Veldrid.Texture texture)
	{
		_texture = texture;
	}
}
