
namespace Ion.Extensions.Assets;

public interface IBaseAssetManager
{
	IAssetLoader GetLoader(Type assetType);
	T Set<T>(T asset) where T : class, IAsset;
	T? Get<T>(int id) where T : class, IAsset;
	void Unload<T>(T asset) where T : class, IAsset;
}

public interface IAssetManager : IBaseAssetManager
{
	IBaseAssetManager Global { get; }
}