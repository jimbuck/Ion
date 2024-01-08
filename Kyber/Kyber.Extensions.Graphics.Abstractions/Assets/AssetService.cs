namespace Kyber.Extensions.Graphics;

public interface IAssetService
{
	IAssetBatch Load(Action<IAssetBatchBuilder> batchBuilder);
	IAssetBatch LoadGlobal(Action<IAssetBatchBuilder> batchBuilder);

	T? Get<T>(string virtualPath) where T : IAsset;
}

internal class ScopedAssetService : GlobalAssetService
{
	private readonly GlobalAssetService _globalAssetService;

	public ScopedAssetService(GlobalAssetService globalAssetService)
	{
		_globalAssetService = globalAssetService;
	}

	public override IAssetBatch LoadGlobal(Action<IAssetBatchBuilder> batchBuilder)
	{
		return _globalAssetService.Load(batchBuilder);
	}
}

internal class GlobalAssetService : IAssetService, IDisposable
{
	protected readonly Dictionary<string, IAsset> _assets = new();

	public IAssetBatch Load(Action<IAssetBatchBuilder> batchBuilder)
	{
		var builder = new AssetBatchBuilder();
		batchBuilder(builder);
		return builder.Load(_assets);
	}

	public virtual IAssetBatch LoadGlobal(Action<IAssetBatchBuilder> batchBuilder)
	{
		return Load(batchBuilder);
	}

	public T? Get<T>(string virtualPath) where T : IAsset
	{
		if (_assets.TryGetValue(virtualPath, out var asset)) return (T)asset;

		return default;
	}

	public void Dispose()
	{
		foreach (var asset in _assets.Values) asset.Dispose();

		_assets.Clear();
	}
}