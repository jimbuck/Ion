using Kyber.Assets;

namespace Kyber.Graphics;


public record struct TextureComponent(int TextureId)
{
	public TextureComponent(Texture texture) : this(texture.Id) { }
}

public record struct ColorComponent(Color Color);