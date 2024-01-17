using System.Collections.Immutable;

namespace Ion.Extensions.Assets.Abstractions;

//public enum AssetStatus
//{
//	Unknown = 0,
//	Unloaded,
//	Loading,
//	Loaded,
//	Failed
//}

//public interface IAssetBatchBuilder
//{
//	IAssetBatchBuilder Text(string virtualPath);
//	IAssetBatchBuilder Binary(string virtualPath);
//	IAssetBatchBuilder Texture(string virtualPath);
//}

//public class AssetBatchBuilder : IAssetBatchBuilder
//{
//	private readonly AssetBatch _batch;

//	public AssetBatchBuilder()
//	{
//		_batch = new();
//	}

//	public IAssetBatchBuilder Binary(string virtualPath)
//	{
//		_batch.Update(virtualPath, AssetStatus.Unloaded);
//		return this;
//	}

//	public IAssetBatchBuilder Text(string virtualPath)
//	{
//		_batch.Update(virtualPath, AssetStatus.Unloaded);
//		return this;
//	}

//	public IAssetBatchBuilder Texture(string virtualPath)
//	{
//		_batch.Update(virtualPath, AssetStatus.Unloaded);
//		return this;
//	}

//	internal IAssetBatch Load(Dictionary<string, IAsset> assets)
//	{
//		// TODO: Load files and add them to assets as they are available...
//		return _batch;
//	}
//}

//public interface IAssetBatch
//{
//	float Progress { get; }
//	ImmutableDictionary<string, AssetStatus> Assets { get; }
//	bool IsComplete { get; }
//	int FailedCount { get; }
//}

//public class AssetBatch : IAssetBatch
//{
//	public float Progress { get; internal set; }

//	public ImmutableDictionary<string, AssetStatus> Assets { get; private set; } = ImmutableDictionary<string, AssetStatus>.Empty;

//	public bool IsComplete { get; private set; }

//	public int FailedCount { get; private set; }

//	internal void Update(string virtualPath, AssetStatus status)
//	{
//		Assets = Assets.SetItem(virtualPath, status);
//		IsComplete = Assets.All(kvp => kvp.Value == AssetStatus.Loaded || kvp.Value == AssetStatus.Failed);
//		if (status == AssetStatus.Failed) FailedCount++;

//		Progress = Assets.Count == 0 ? 0 : (float)Assets.Count(x => x.Value == AssetStatus.Loaded || x.Value == AssetStatus.Failed) / Assets.Count;
//	}
//}
