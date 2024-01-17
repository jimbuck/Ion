
namespace Ion.Extensions.Assets;

public interface IAssetManager
{
	T Load<T>(params string[] path) where T : class, IAsset;
	T LoadGlobal<T>(params string[] path) where T : class, IAsset;
	T? Get<T>(int id) where T : class, IAsset;
	void Unload<T>(T asset) where T : class, IAsset;
	void UnloadGlobal<T>(T asset) where T : class, IAsset;
}