using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public interface ITexture2D : IAsset
{
	public uint Width { get; }
	public uint Height { get; }

	public uint MipLevels { get; }
}
