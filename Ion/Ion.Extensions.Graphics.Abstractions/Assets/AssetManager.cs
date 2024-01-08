using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Graphics;

public interface IAssetManager
{
	T Load<T>(params string[] path) where T : class, IAsset;
	T? Get<T>(int id) where T : class, IAsset;
	void Unload<T>(T asset) where T : class, IAsset;
}

public class AssetManager : IAssetManager
{
	private readonly ILogger _logger;
	private readonly IPersistentStorage _storage;
	private readonly ImmutableDictionary<Type, IAssetLoader> _loaders;
	private readonly Dictionary<int, IAsset> _assetCache = new();

	public AssetManager(ILogger<AssetManager> logger, IPersistentStorage storage, IEnumerable<IAssetLoader> loaders)
	{
		_logger = logger;
		_storage = storage;
		_loaders = loaders.ToImmutableDictionary(l => l.AssetType);
	}

	public T Load<T>(params string[] path) where T : class, IAsset
	{
		if (!_loaders.TryGetValue(typeof(T), out IAssetLoader? loader)) throw new InvalidOperationException("No loader registered for type " + typeof(T).Name);

		var name = Path.Combine(path);
		using var stream = _storage.Assets.Read(path);

		var asset = loader.Load<T>(stream, name);

		_assetCache.Add(asset.Id, asset);

		return asset;
	}

	public T? Get<T>(int id) where T : class, IAsset
	{
		return _assetCache.TryGetValue(id, out var asset) ? (T)asset : default;
	}

	public void Unload<T>(T asset) where T : class, IAsset
	{
		_assetCache.Remove(asset.Id);
		asset.Dispose();
	}
}