using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Assets;

/// <summary>
/// Shared asset bookkeeping: lookups by id, by name and by (asset type, path), plus the set of owned assets.
/// </summary>
internal abstract class AssetStore(ILogger logger)
{
	private readonly Dictionary<nint, IAsset> _idCache = [];
	private readonly Dictionary<string, IAsset> _nameCache = [];
	private readonly Dictionary<(Type Type, string Path), IAsset> _pathCache = [];

	// Every asset ever handed to this store (including replaced ones) until it is unloaded; disposed by owners that dispose.
	private readonly List<IAsset> _owned = [];

	protected ILogger Logger { get; } = logger;

	public T Set<T>(T asset) where T : class, IAsset
	{
		if (_nameCache.TryGetValue(asset.Name, out var existing) && !ReferenceEquals(existing, asset))
		{
			Logger.LogDebug("Replacing asset '{AssetName}' ({AssetType}).", asset.Name, typeof(T).Name);
		}

		_idCache[asset.Id] = asset;
		_nameCache[asset.Name] = asset;
		_own(asset);

		return asset;
	}

	public T? Get<T>(nint id) where T : class, IAsset
	{
		return _idCache.TryGetValue(id, out var asset) ? asset as T : default;
	}

	internal bool TryGetCached<T>(string path, [NotNullWhen(true)] out T? asset) where T : class, IAsset
	{
		if (TryGetCached(typeof(T), path, out var cached) && cached is T typed)
		{
			asset = typed;
			return true;
		}

		asset = null;
		return false;
	}

	internal bool TryGetCached(Type assetType, string path, [NotNullWhen(true)] out IAsset? asset)
	{
		return _pathCache.TryGetValue((assetType, path), out asset);
	}

	protected T LoadAndCache<T>(string path, Func<string, T> load) where T : class, IAsset
	{
		return (T)LoadAndCache(typeof(T), path, load);
	}

	protected IAsset LoadAndCache(Type assetType, string path, Func<string, IAsset> load)
	{
		IAsset asset;
		try
		{
			asset = load(path);
		}
		catch (FileNotFoundException ex)
		{
			var resolved = ex.FileName is { Length: > 0 } fileName && !ex.Message.Contains(fileName, StringComparison.Ordinal)
				? $" Resolved path: '{fileName}'."
				: "";
			var hint = ex.Message.Contains("case-sensitive", StringComparison.Ordinal)
				? ""
				: " File names are case-sensitive on Linux and macOS; check the casing of the name.";
			throw new FileNotFoundException(
				$"Could not load {assetType.Name} asset '{path}'. {ex.Message}{resolved}{hint}",
				ex.FileName ?? path,
				ex);
		}

		_pathCache[(assetType, path)] = asset;
		_idCache[asset.Id] = asset;
		_nameCache[asset.Name] = asset;
		_own(asset);

		Logger.LogDebug("Loaded {AssetType} asset '{AssetPath}'.", assetType.Name, path);

		return asset;
	}

	public void Unload<T>(T asset) where T : class, IAsset
	{
		if (_idCache.TryGetValue(asset.Id, out var byId) && ReferenceEquals(byId, asset)) _idCache.Remove(asset.Id);
		if (_nameCache.TryGetValue(asset.Name, out var byName) && ReferenceEquals(byName, asset)) _nameCache.Remove(asset.Name);

		foreach (var key in _pathCache.Where(kvp => ReferenceEquals(kvp.Value, asset)).Select(kvp => kvp.Key).ToList())
		{
			_pathCache.Remove(key);
		}

		_owned.Remove(asset);
		asset.Dispose();
	}

	protected void DisposeAll()
	{
		for (var i = _owned.Count - 1; i >= 0; i--)
		{
			try
			{
				_owned[i].Dispose();
			}
			catch (Exception ex)
			{
				Logger.LogError(ex, "Failed to dispose asset '{AssetName}'.", _owned[i].Name);
			}
		}

		_owned.Clear();
		_idCache.Clear();
		_nameCache.Clear();
		_pathCache.Clear();
	}

	private void _own(IAsset asset)
	{
		for (var i = 0; i < _owned.Count; i++)
		{
			if (ReferenceEquals(_owned[i], asset)) return;
		}

		_owned.Add(asset);
	}
}

/// <summary>
/// Application-wide assets. Not disposed by the container: global assets live as long as the process, and graphics
/// resources may outlive the device at shutdown otherwise.
/// </summary>
internal class GlobalAssetManager(ILogger<GlobalAssetManager> logger, IEnumerable<IAssetLoader> loaders) : AssetStore(logger), IBaseAssetManager
{
	private readonly ImmutableDictionary<Type, IAssetLoader> _loaders = loaders.ToImmutableDictionary(l => l.AssetType);

	public IAssetLoader GetLoader(Type assetType)
	{
		if (!_loaders.TryGetValue(assetType, out IAssetLoader? loader)) throw new InvalidOperationException("No loader registered for type " + assetType.Name);

		return loader;
	}

	public T GetOrLoad<T>(string path, Func<string, T> load) where T : class, IAsset
	{
		return TryGetCached<T>(path, out var cached) ? cached : LoadAndCache(path, load);
	}

	public T Load<T>(string path) where T : class, IAsset
	{
		var (cacheType, loader) = ResolveLoader(typeof(T));
		var asset = TryGetCached(cacheType, path, out var cached) ? cached : LoadAndCache(cacheType, path, loader.Load);

		return CastAsset<T>(asset, path);
	}

	/// <summary>
	/// Finds the loader for <paramref name="requested"/>: the one registered for exactly that type, or else the single
	/// loader registered for an interface or base type of it. Returns the type the loader is registered under, which is
	/// also the cache key, so <c>Load&lt;ITexture2D&gt;</c> and a legacy <c>Load&lt;Texture2D&gt;</c> share one instance.
	/// </summary>
	internal (Type CacheType, IAssetLoader<IAsset> Loader) ResolveLoader(Type requested)
	{
		if (_loaders.TryGetValue(requested, out var exact)) return (requested, AsGeneric(exact, requested));

		KeyValuePair<Type, IAssetLoader>? match = null;
		foreach (var entry in _loaders)
		{
			if (!entry.Key.IsAssignableFrom(requested)) continue;
			if (match is not null)
			{
				throw new InvalidOperationException(
					$"Loading {requested.Name} is ambiguous: loaders are registered for both {match.Value.Key.Name} and {entry.Key.Name}. Load one of those types instead.");
			}

			match = entry;
		}

		if (match is null) throw new InvalidOperationException("No loader registered for type " + requested.Name);

		return (match.Value.Key, AsGeneric(match.Value.Value, match.Value.Key));
	}

	private static IAssetLoader<IAsset> AsGeneric(IAssetLoader loader, Type assetType)
	{
		// IAssetLoader<T> is covariant, so any IAssetLoader<ISomeAsset> is an IAssetLoader<IAsset>.
		return loader as IAssetLoader<IAsset> ?? throw new InvalidOperationException(
			$"The loader registered for {assetType.Name} ({loader.GetType().Name}) does not implement IAssetLoader<{assetType.Name}>, so IAssetManager.Load<{assetType.Name}> cannot use it.");
	}

	internal static T CastAsset<T>(IAsset asset, string path) where T : class, IAsset
	{
		return asset as T ?? throw new InvalidOperationException(
			$"Asset '{path}' was loaded as {asset.GetType().Name}, which is not a {typeof(T).Name}. Load it through its interface type instead (for example Load<ITexture2D>), which works on every backend.");
	}
}

/// <summary>
/// Scene (DI scope) assets. Loads that are not already cached globally are owned by the scope and disposed with it.
/// </summary>
internal sealed class ScopedAssetManager(ILogger<ScopedAssetManager> logger, GlobalAssetManager globalAssetManager) : AssetStore(logger), IAssetManager, IDisposable
{
	private bool _disposed;

	public IBaseAssetManager Global => globalAssetManager;

	public IAssetLoader GetLoader(Type assetType)
	{
		return globalAssetManager.GetLoader(assetType);
	}

	public T GetOrLoad<T>(string path, Func<string, T> load) where T : class, IAsset
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (TryGetCached<T>(path, out var cached)) return cached;
		if (globalAssetManager.TryGetCached(path, out cached)) return cached;

		return LoadAndCache(path, load);
	}

	public T Load<T>(string path) where T : class, IAsset
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		var (cacheType, loader) = globalAssetManager.ResolveLoader(typeof(T));

		if (!TryGetCached(cacheType, path, out var asset) && !globalAssetManager.TryGetCached(cacheType, path, out asset))
		{
			asset = LoadAndCache(cacheType, path, loader.Load);
		}

		return GlobalAssetManager.CastAsset<T>(asset, path);
	}

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		DisposeAll();
	}
}
