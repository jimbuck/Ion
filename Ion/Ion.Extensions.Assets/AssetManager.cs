using System.Collections.Immutable;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Assets;

internal class GlobalAssetManager(ILogger<GlobalAssetManager> logger, IEnumerable<IAssetLoader> loaders) : IBaseAssetManager
{
	private readonly ILogger _logger = logger;
	private readonly ImmutableDictionary<Type, IAssetLoader> _loaders = loaders.ToImmutableDictionary(l => l.AssetType);
	private readonly Dictionary<nint, IAsset> _idCache = [];
	private readonly Dictionary<string, IAsset> _nameCache = [];

	public virtual IAssetLoader GetLoader(Type assetType)
	{
		if (!_loaders.TryGetValue(assetType, out IAssetLoader? loader)) throw new InvalidOperationException("No loader registered for type " + assetType.Name);

		return loader;
	}

	public T Set<T>(T asset) where T : class, IAsset
	{
		_idCache.Add(asset.Id, asset);
		_nameCache.Add(asset.Name, asset);

		return asset;
	}

	public T? Get<T>(nint id) where T : class, IAsset
	{
		return _idCache.TryGetValue(id, out var asset) ? (T)asset : default;
	}

	public void Unload<T>(T asset) where T : class, IAsset
	{
		_idCache.Remove(asset.Id);
		_nameCache.Remove(asset.Name);
		asset.Dispose();
	}
}

internal class ScopedAssetManager(ILogger<GlobalAssetManager> logger, GlobalAssetManager globalAssetManager) : GlobalAssetManager(logger, Enumerable.Empty<IAssetLoader>()), IAssetManager
{
	public IBaseAssetManager Global { get; } = globalAssetManager;

	public override IAssetLoader GetLoader(Type assetType)
	{
		return Global.GetLoader(assetType);
	}
}