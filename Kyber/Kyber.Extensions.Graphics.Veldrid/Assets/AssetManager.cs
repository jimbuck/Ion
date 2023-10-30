using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Extensions.Graphics;

public interface IAssetManager
{
	T Load<T>(params string[] path) where T : IAsset;
	T? Get<T>(int id) where T : IAsset;
	void Unload<T>(T asset) where T : IAsset;
}

public class AssetManager : IAssetManager
{
	private readonly ILogger _logger;
	private readonly IPersistentStorage _storage;
	private readonly IServiceProvider _serviceProvider;
	private readonly GraphicsContext _graphicsContext;
	private readonly Dictionary<Type, Type> _loaders = new();
	private readonly Dictionary<int, IAsset> _assetCache = new();

	public AssetManager(ILogger<AssetManager> logger, IServiceProvider serviceProvider, IPersistentStorage storage, IGraphicsContext graphicsContext)
	{
		_logger = logger;
		_serviceProvider = serviceProvider;
		_storage = storage;
		_graphicsContext = (GraphicsContext)graphicsContext;
	}

	public void Initialize()
	{
		RegisterLoader<Texture2D, Texture2DLoader>();
	}

	public void RegisterLoader<TAsset, TSerializer>() where TSerializer : IAssetLoader<TAsset>
	{
		_loaders.Add(typeof(TAsset), typeof(TSerializer));
	}

	public T Load<T>(params string[] path) where T : IAsset
	{
		if (!_loaders.TryGetValue(typeof(T), out Type? loaderType) || loaderType == null) throw new InvalidOperationException("No loader registered for type " + typeof(T).Name);

		var loader = (IAssetLoader<T>)_serviceProvider.GetRequiredService(loaderType);

		var name = Path.Combine(path);
		using var stream = _storage.Assets.Read(path);

		var asset = loader.Load(stream, name, _graphicsContext.GraphicsDevice!);

		_assetCache.Add(asset.Id, asset);

		return asset;
	}

	public T? Get<T>(int id) where T : IAsset
	{
		return _assetCache.TryGetValue(id, out var asset) ? (T)asset : default;
	}

	public void Unload<T>(T asset) where T : IAsset
	{
		_assetCache.Remove(asset.Id);
		asset.Dispose();
	}
}