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
		if (_pathCache.TryGetValue((typeof(T), path), out var cached) && cached is T typed)
		{
			asset = typed;
			return true;
		}

		asset = null;
		return false;
	}

	protected T LoadAndCache<T>(string path, Func<string, T> load) where T : class, IAsset
	{
		T asset;
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
				$"Could not load {typeof(T).Name} asset '{path}'. {ex.Message}{resolved}{hint}",
				ex.FileName ?? path,
				ex);
		}

		_pathCache[(typeof(T), path)] = asset;
		_idCache[asset.Id] = asset;
		_nameCache[asset.Name] = asset;
		_own(asset);

		Logger.LogDebug("Loaded {AssetType} asset '{AssetPath}'.", typeof(T).Name, path);

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

	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		DisposeAll();
	}
}
