namespace Ion.Extensions.Assets;

/// <summary>
/// Loads assets of one type. Loaders are registered in DI as <see cref="IAssetLoader"/> and looked up by
/// <see cref="AssetType"/>, which should be the interface games depend on (for example <c>ITexture2D</c>), not the
/// backend's concrete type.
/// </summary>
public interface IAssetLoader
{
	/// <summary>
	/// The asset type this loader is registered for. <see cref="IBaseAssetManager.Load{T}(string)"/> finds the loader whose
	/// <see cref="AssetType"/> is <c>typeof(T)</c>.
	/// </summary>
	Type AssetType { get; }
}

/// <summary>
/// An <see cref="IAssetLoader"/> that can load an asset of type <typeparamref name="T"/> from a path, so
/// <see cref="IBaseAssetManager.Load{T}(string)"/> can use it without knowing the loader's concrete type.
/// </summary>
/// <typeparam name="T">The asset type, usually an interface such as <c>ITexture2D</c> or <c>ISoundEffect</c>.</typeparam>
public interface IAssetLoader<out T> : IAssetLoader where T : class, IAsset
{
	/// <summary>
	/// Loads a new asset from <paramref name="path"/> on every call. Callers should go through
	/// <see cref="IBaseAssetManager.Load{T}(string)"/>, which caches the result and owns it.
	/// </summary>
	/// <exception cref="FileNotFoundException">The file behind <paramref name="path"/> does not exist.</exception>
	T Load(string path);
}
