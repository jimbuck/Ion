using VeldridLib = Veldrid;


namespace Ion.Extensions.Graphics;

public abstract class BaseTexture : ITexture2D
{
	protected readonly VeldridLib.Texture _texture;

	public nint Id => _texture.GetHashCode();
	public string Name => _texture.Name;

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	public uint MipLevels => _texture.MipLevels;

	internal bool IsDisposed => _texture.IsDisposed;

	internal BaseTexture(string name, VeldridLib.Texture texture)
	{
		texture.Name = name;
		_texture = texture;
	}

	public void Dispose()
	{
		// Idempotent: the asset manager may dispose a texture that user code already disposed.
		if (!_texture.IsDisposed) _texture.Dispose();
	}

	public static implicit operator VeldridLib.Texture(BaseTexture texture) => texture._texture;
}
