using VeldridLib = Veldrid;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Gives the Veldrid renderer access to the device texture behind an <see cref="ITexture2D"/> without depending on the
/// public concrete texture types.
/// </summary>
internal interface IVeldridTexture
{
	VeldridLib.Texture DeviceTexture { get; }
}

internal static class VeldridTextureExtensions
{
	/// <summary>
	/// Returns the device texture behind <paramref name="texture"/>.
	/// </summary>
	/// <exception cref="ArgumentException"><paramref name="texture"/> was not created by the Veldrid graphics backend.</exception>
	public static VeldridLib.Texture GetDeviceTexture(this ITexture2D texture)
	{
		return texture is IVeldridTexture veldridTexture
			? veldridTexture.DeviceTexture
			: throw new ArgumentException($"Texture '{texture.Name}' ({texture.GetType().Name}) was not created by the Veldrid graphics backend.", nameof(texture));
	}
}

[Obsolete(VeldridObsolete.ConcreteAssetTypes)]
public abstract class BaseTexture : ITexture2D, IVeldridTexture
{
	protected readonly VeldridLib.Texture _texture;

	public nint Id => _texture.GetHashCode();
	public string Name => _texture.Name;

	public uint Width => _texture.Width;
	public uint Height => _texture.Height;

	public uint MipLevels => _texture.MipLevels;

	internal bool IsDisposed => _texture.IsDisposed;

	VeldridLib.Texture IVeldridTexture.DeviceTexture => _texture;

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

internal static class VeldridObsolete
{
	public const string ConcreteAssetTypes =
		"Depend on the asset interfaces (ITexture2D, IFontSet, IFont) and load them with IAssetManager.Load<ITexture2D>(path) " +
		"or LoadFontSet(name, fonts) so the game also runs on the headless backend. The Veldrid asset types become internal in the next release.";
}
