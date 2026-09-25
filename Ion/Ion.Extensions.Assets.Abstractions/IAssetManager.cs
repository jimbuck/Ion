
namespace Ion.Extensions.Assets;

public interface IBaseAssetManager
{
	IAssetLoader GetLoader(Type assetType);

	/// <summary>
	/// Adds an asset to this manager. An existing asset with the same name or id is replaced in the lookups
	/// (it stays owned by the manager and is disposed with it).
	/// </summary>
	T Set<T>(T asset) where T : class, IAsset;

	T? Get<T>(nint id) where T : class, IAsset;

	/// <summary>
	/// Returns the asset of type <typeparamref name="T"/> previously loaded from <paramref name="path"/>, or calls
	/// <paramref name="load"/> once, caches and returns the result. Loader extension methods should go through this so
	/// that loading the same path twice returns the same instance instead of creating a new one.
	/// </summary>
	/// <exception cref="FileNotFoundException">The file behind <paramref name="path"/> does not exist.</exception>
	T GetOrLoad<T>(string path, Func<string, T> load) where T : class, IAsset;

	void Unload<T>(T asset) where T : class, IAsset;
}

public interface IAssetManager : IBaseAssetManager
{
	IBaseAssetManager Global { get; }
}
