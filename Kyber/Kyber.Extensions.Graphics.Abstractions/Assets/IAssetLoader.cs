namespace Kyber.Extensions.Graphics;

public interface IAssetLoader
{
	Type AssetType { get; }

	T Load<T>(Stream stream, string name) where T : class, IAsset;
}
