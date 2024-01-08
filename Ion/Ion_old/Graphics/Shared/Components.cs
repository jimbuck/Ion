using Ion.Assets;

namespace Ion.Graphics;


public record struct TextureComponent(int TextureId)
{
	public TextureComponent(Texture texture) : this(texture.Id) { }
}

public record struct ColorComponent(Color Color);