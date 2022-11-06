using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Assets;

public interface IAssetManager
{
	T Load<T>(string name);
}

public class AssetManager : IAssetManager
{
	private readonly ILogger _logger;
	private readonly IServiceProvider _serviceProvider;
	private readonly Dictionary<Type, Type> _serializers = new();

	public AssetManager(ILogger<AssetManager> logger, IServiceProvider	serviceProvider)
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
		_serializers.Add(typeof(TAsset), typeof(TSerializer));
	}

	public T Load<T>(string name)
	{
		if (!_serializers.TryGetValue(typeof(T), out Type? serializerType) || serializerType == null) throw new InvalidOperationException("No serializer registered for type " + typeof(T).Name);

		var serializer = (BinaryAssetSerializer)_serviceProvider.GetRequiredService(serializerType);

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