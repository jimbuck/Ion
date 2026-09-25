namespace Ion.Extensions.Assets;

/// <summary>
/// A loader that can update an asset it loaded in place when its file changes (hot reload). Holders of the asset keep
/// their reference and see the new content. Loaders that do not implement it are reloaded by loading a new instance and
/// swapping it into the cache (see <see cref="AssetReloadedEvent"/>).
/// </summary>
public interface IReloadableAssetLoader : IAssetLoader
{
	/// <summary>
	/// Reloads <paramref name="asset"/> (created by this loader from <paramref name="path"/>) from its file, in place.
	/// </summary>
	/// <returns>
	/// True when the asset was updated in place; false when it cannot be (for example a texture whose size changed), in
	/// which case the caller loads a new instance instead.
	/// </returns>
	/// <exception cref="FileNotFoundException">The file no longer exists.</exception>
	bool TryReload(IAsset asset, string path);
}

/// <summary>
/// Watches the assets folder for changed files and queues their paths for <c>AssetReloadSystem</c>, which reloads the
/// cached assets loaded from them at the start of the next frame.
/// </summary>
/// <remarks>
/// Enabled by <c>Ion:Assets:HotReload = true</c> (the default in Debug builds of the assets package). When disabled no file
/// system watcher runs, but <see cref="Enqueue"/> still queues reloads by hand.
/// </remarks>
public interface IAssetWatcher
{
	/// <summary>True when the watcher follows file changes under <see cref="Root"/>.</summary>
	bool IsEnabled { get; }

	/// <summary>The watched folder (the assets root).</summary>
	string Root { get; }

	/// <summary>
	/// Queues a reload of the assets loaded from <paramref name="path"/> (relative to <see cref="Root"/>, either separator),
	/// ready at the next <see cref="Drain"/>.
	/// </summary>
	void Enqueue(string path);

	/// <summary>
	/// Moves the queued paths that are ready (a changed file must stop changing for a short settle time first, so that a
	/// half-written file is not loaded) into <paramref name="paths"/>, each once, normalized to forward slashes. Returns how
	/// many were added.
	/// </summary>
	int Drain(ICollection<string> paths);
}

/// <summary>
/// Emitted when a cached asset has been reloaded after its file changed.
/// </summary>
/// <remarks>
/// Events carry only unmanaged data, so the asset is identified by id: resolve its name with
/// <c>IBaseAssetManager.Get&lt;IAsset&gt;(AssetId)?.Name</c>. When the loader reloaded it in place
/// (<see cref="InPlace"/>), <see cref="AssetId"/> and <see cref="PreviousAssetId"/> are the same instance and every holder
/// sees the new content. Otherwise a new instance with <see cref="AssetId"/> replaced <see cref="PreviousAssetId"/> in the
/// cache: later loads return the new one, and code holding the old one should load it again.
/// </remarks>
/// <param name="AssetId">The id of the reloaded (or replacing) asset.</param>
/// <param name="PreviousAssetId">The id of the asset before the reload.</param>
public readonly record struct AssetReloadedEvent(nint AssetId, nint PreviousAssetId)
{
	/// <summary>True when the asset was updated in place.</summary>
	public bool InPlace => AssetId == PreviousAssetId;
}
