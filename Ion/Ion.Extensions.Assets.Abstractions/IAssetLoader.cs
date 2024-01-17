namespace Ion.Extensions.Assets;

public interface IAssetLoader
{
	Type AssetType { get; }

	T Load<T>(string filepath) where T : class, IAsset;
}
