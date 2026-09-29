
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
	/// Returns the asset of type <typeparamref name="T"/> loaded from <paramref name="path"/>, loading and caching it on
	/// first use with the <see cref="IAssetLoader{T}"/> registered for <c>typeof(T)</c>. Loading the same path again
	/// returns the same instance until it is unloaded.
	/// </summary>
	/// <remarks>
	/// <typeparamref name="T"/> should be the asset interface (for example <c>assets.Load&lt;ITexture2D&gt;("tiles.png")</c>),
	/// so the game runs unchanged on any backend, including the headless one. When no loader is registered for
	/// <typeparamref name="T"/> itself, the loader registered for an interface that <typeparamref name="T"/> implements is
	/// used and its result is cast to <typeparamref name="T"/>; this keeps older calls such as <c>Load&lt;Texture2D&gt;</c>
	/// working but ties the game to one backend.
	/// </remarks>
	/// <exception cref="InvalidOperationException">
	/// No loader is registered for <typeparamref name="T"/>, the loader does not implement <see cref="IAssetLoader{T}"/>,
	/// or the loaded asset is not a <typeparamref name="T"/>.
	/// </exception>
	/// <exception cref="FileNotFoundException">The file behind <paramref name="path"/> does not exist.</exception>
	T Load<T>(string path) where T : class, IAsset;

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
