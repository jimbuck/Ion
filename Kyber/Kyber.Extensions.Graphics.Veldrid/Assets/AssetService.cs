using System.Collections.Immutable;

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
		if (_assets.TryGetValue(virtualPath, out var texture)) return (T)texture;

		return default;
	}

	public void Dispose()
	{
		foreach (var asset in _assets.Values) asset.Dispose();

		_assets.Clear();
	}
}

public interface IAssetBatchBuilder
{
	IAssetBatchBuilder Text(string virtualPath);
	IAssetBatchBuilder Binary(string virtualPath);
	IAssetBatchBuilder Texture(string virtualPath);
}

public class AssetBatchBuilder : IAssetBatchBuilder
{
	private readonly AssetBatch _batch;

	public AssetBatchBuilder()
	{
		_batch = new();
	}

	public IAssetBatchBuilder Binary(string virtualPath)
	{
		_batch.Update(virtualPath, AssetStatus.Unloaded);
		return this;
	}

	public IAssetBatchBuilder Text(string virtualPath)
	{
		_batch.Update(virtualPath, AssetStatus.Unloaded);
		return this;
	}

	public IAssetBatchBuilder Texture(string virtualPath)
	{
		_batch.Update(virtualPath, AssetStatus.Unloaded);
		return this;
	}

	internal IAssetBatch Load(Dictionary<string, IAsset> assets)
	{
		// TODO: Load files and add them to assets as they are available...
		return _batch;
	}
}

public enum AssetStatus
{
	Unknown = 0,
	Unloaded,
	Loading,
	Loaded,
	Failed
}

public interface IAssetBatch
{
	float Progress { get; }
	ImmutableDictionary<string, AssetStatus> Assets { get; }
	bool IsComplete { get; }
	int FailedCount { get; }
}

public class AssetBatch : IAssetBatch
{
	public float Progress { get; internal set; }

	public ImmutableDictionary<string, AssetStatus> Assets { get; private set; } = ImmutableDictionary<string, AssetStatus>.Empty;

	public bool IsComplete { get; private set; }

	public int FailedCount { get; private set; }

	internal void Update(string virtualPath, AssetStatus status)
	{
		Assets = Assets.SetItem(virtualPath, status);
		IsComplete = Assets.All(kvp => kvp.Value == AssetStatus.Loaded || kvp.Value == AssetStatus.Failed);
		if (status == AssetStatus.Failed) FailedCount++;
		
		Progress = Assets.Count == 0 ? 0 : (float)Assets.Count(x => x.Value == AssetStatus.Loaded || x.Value == AssetStatus.Failed) / Assets.Count;
	}
}