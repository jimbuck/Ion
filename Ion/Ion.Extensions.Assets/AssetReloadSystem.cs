namespace Ion.Extensions.Assets;

/// <summary>
/// Hot reload: at the start of every frame (First, order <see cref="StageOrder.AssetReload"/>, in the engine setup band)
/// takes the changed paths from <see cref="IAssetWatcher"/>, reloads every cached asset loaded from them (global and scene
/// caches) through its loader, and emits an <see cref="AssetReloadedEvent"/> for each. Added with
/// <see cref="BuilderExtensions.UseAssets"/>.
/// </summary>
/// <remarks>
/// Loaders that implement <see cref="IReloadableAssetLoader"/> update the asset in place, so every holder sees the new
/// content. Other assets are loaded again and the new instance replaces the old one in the cache.
/// </remarks>
public sealed class AssetReloadSystem
{
	private readonly IAssetWatcher _watcher;
	private readonly GlobalAssetManager _assets;
	private readonly IEventEmitter _events;
	private readonly List<string> _changed = [];
	private readonly List<AssetReloadedEvent> _reloaded = [];

	internal AssetReloadSystem(IAssetWatcher watcher, GlobalAssetManager assets, IEventEmitter events)
	{
		_watcher = watcher;
		_assets = assets;
		_events = events;
	}

	/// <summary>
	/// The frame step: <see cref="ReloadPending"/>.
	/// </summary>
	[First(Order = StageOrder.AssetReload)]
	public void First(GameTime dt) => ReloadPending();

	/// <summary>
	/// Reloads the assets behind every path the watcher has ready now and emits their events. Returns the number of assets
	/// reloaded. Called every frame by the First step; call it directly to reload outside the game loop.
	/// </summary>
	public int ReloadPending()
	{
		if (_watcher.Drain(_changed) == 0) return 0;

		_reloaded.Clear();
		foreach (var path in _changed) _assets.Reload(path, _reloaded);
		_changed.Clear();

		foreach (var e in _reloaded) _events.Emit(e);
		return _reloaded.Count;
	}
}
