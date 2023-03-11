using Microsoft.Extensions.DependencyInjection;

using Kyber.Graphics;
using Kyber.Storage;


namespace Kyber.Assets;

public interface IAssetManager
{
	T Load<T>(params string[] path);
}

public class AssetManager : IAssetManager
{
	private readonly ILogger _logger;
	private readonly IPersistentStorage _storage;
	private readonly IServiceProvider _serviceProvider;
	private readonly GraphicsDevice _graphicsDevice;
	private readonly Dictionary<Type, Type> _loaders = new();

	public AssetManager(ILogger<AssetManager> logger, IServiceProvider serviceProvider, IPersistentStorage storage, IGraphicsDevice graphicsDevice)
	{
		_logger = logger;
		_serviceProvider = serviceProvider;
		_storage = storage;
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
	}

	public void Initialize()
	{
		RegisterLoader<Texture2D, Texture2DLoader>();
	}

	public void RegisterLoader<TAsset, TSerializer>() where TSerializer : IAssetLoader<TAsset>
	{
		_loaders.Add(typeof(TAsset), typeof(TSerializer));
	}

	public T Load<T>(params string[] path)
	{
		if (!_loaders.TryGetValue(typeof(T), out Type? loaderType) || loaderType == null) throw new InvalidOperationException("No loader registered for type " + typeof(T).Name);

		var loader = (IAssetLoader<T>)_serviceProvider.GetRequiredService(loaderType);

		var name = Path.Combine(path);
		using var stream = _storage.Assets.Read(path);

		return loader.Load(stream, name, _graphicsDevice.Internal);
	}
}