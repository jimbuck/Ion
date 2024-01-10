using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Graphics;

public interface IAssetManager
{
	T Load<T>(params string[] path) where T : class, IAsset;
	T LoadGlobal<T>(params string[] path) where T : class, IAsset;
	T? Get<T>(int id) where T : class, IAsset;
	void Unload<T>(T asset) where T : class, IAsset;
	void UnloadGlobal<T>(T asset) where T : class, IAsset;
}

public class GlobalAssetManager : IAssetManager
{
	private readonly ILogger _logger;
	private readonly IPersistentStorage _storage;
	private readonly ImmutableDictionary<Type, IAssetLoader> _loaders;
	private readonly Dictionary<int, IAsset> _assetCache = new();
	private readonly Dictionary<string, IAsset> _pathCache = new();

	public GlobalAssetManager(ILogger<GlobalAssetManager> logger, IPersistentStorage storage, IEnumerable<IAssetLoader> loaders)
	{
		_logger = logger;
		_storage = storage;
		_loaders = loaders.ToImmutableDictionary(l => l.AssetType);
	}

	public T Load<T>(params string[] path) where T : class, IAsset
	{
		var name = Path.Combine(path);

		if (_pathCache.TryGetValue(name, out var cachedAsset)) return (T)cachedAsset;

		if (!_loaders.TryGetValue(typeof(T), out IAssetLoader? loader)) throw new InvalidOperationException("No loader registered for type " + typeof(T).Name);

		using var stream = _storage.Assets.Read(name);

		var asset = loader.Load<T>(stream, name);

		_assetCache.Add(asset.Id, asset);
		_pathCache.Add(name, asset);

		return asset;
	}

	public virtual T LoadGlobal<T>(params string[] path) where T : class, IAsset
	{
		return Load<T>(path);
	}

	public virtual T? Get<T>(int id) where T : class, IAsset
	{
		return _assetCache.TryGetValue(id, out var asset) ? (T)asset : default;
	}

	public void Unload<T>(T asset) where T : class, IAsset
	{
		_assetCache.Remove(asset.Id);
		_pathCache.Remove(asset.Name);
		asset.Dispose();
	}

	public virtual void UnloadGlobal<T>(T asset) where T : class, IAsset
	{
		Unload(asset);
	}
}

public class ScopedAssetManager : GlobalAssetManager
{
	private readonly GlobalAssetManager _globalAssetManager;

	public ScopedAssetManager(ILogger<GlobalAssetManager> logger, IPersistentStorage storage, IEnumerable<IAssetLoader> loaders, GlobalAssetManager globalAssetManager) : base(logger, storage, loaders)
	{
		_globalAssetManager = globalAssetManager;
	}

	public override T LoadGlobal<T>(params string[] path)
	{
		return _globalAssetManager.Load<T>(path);
	}

	public override void UnloadGlobal<T>(T asset)
	{
		_globalAssetManager.Unload(asset);
	}

	public override T? Get<T>(int id) where T : class
	{
		return base.Get<T>(id) ?? _globalAssetManager.Get<T>(id);
	}
}