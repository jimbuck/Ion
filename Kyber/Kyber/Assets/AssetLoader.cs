using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Assets;

public interface IAssetLoader
{
	T Load<T>(string name);
}

public class AssetLoader : IAssetLoader
{
	private readonly ILogger _logger;
	private readonly IServiceProvider _serviceProvider;
	private readonly Dictionary<Type, BinaryAssetSerializer> _serializers = new();

	public AssetLoader(ILogger<AssetLoader> logger, IServiceProvider	serviceProvider)
	{
		_logger = logger;
		_serviceProvider = serviceProvider;
	}

	public void Initialize()
	{
		_setupFolderStructure();

		RegisterSerializer<ProcessedTexture, ProcessedTextureDataSerializer>();
		RegisterSerializer<ProcessedModel, ProcessedModelSerializer>();
		RegisterSerializer<byte[], ByteArraySerializer>();
	}

	public void RegisterSerializer<TAsset, TSerializer>() where TSerializer : BinaryAssetSerializer
	{
		_serializers.Add(typeof(TAsset), _serviceProvider.GetRequiredService<TSerializer>());
	}

	public T Load<T>(string name)
	{
		if (!_serializers.TryGetValue(typeof(T), out BinaryAssetSerializer? serializer) || serializer == null) throw new InvalidOperationException("No serializer registered for type " + typeof(T).Name);

		using Stream? stream = GetType().Assembly.GetManifestResourceStream(name);
		
		if (stream == null) throw new InvalidOperationException("No embedded asset with the name " + name);

		using BinaryReader reader = new(stream);
		return (T)serializer.Read(reader);
	}

	private void _setupFolderStructure()
	{
		var cwd = Environment.CurrentDirectory;

		_logger.LogInformation("Setting up folder structure...");
		Directory.CreateDirectory("Assets");
		Directory.CreateDirectory("Mods");
		Directory.CreateDirectory("Cache");
	}
}

public class AssetSystem : IInitializeSystem
{
	private readonly AssetLoader _assets;

	public bool IsEnabled { get; set; } = true;

	public AssetSystem(IAssetLoader assets)
	{
		_assets = (AssetLoader)assets;
	}

	public void Initialize()
	{
		_assets.Initialize();
	}
}