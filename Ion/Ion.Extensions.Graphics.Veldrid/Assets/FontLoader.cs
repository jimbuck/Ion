using FontStashSharp;

using Ion.Extensions.Assets;


namespace Ion.Extensions.Graphics;

public static class FontAssetManagerExtensions
{
	public static FontSet Load<T>(this IBaseAssetManager assetManager, string name, params string[] fonts) where T : FontSet
	{
		var fontLoader = (FontLoader)assetManager.GetLoader(typeof(FontSet));

		var font = fontLoader.Load(name, fonts);

		return assetManager.Set(font);
	}
}

public class FontLoader(IPersistentStorage storage) : IAssetLoader
{
	public Type AssetType { get; } = typeof(FontSet);

	public FontSet Load(string name, string[] fonts)
	{
		var fontSystem = new FontSystem(new FontSystemSettings()
		{
			
		});

		foreach (var font in fonts) fontSystem.AddFont(storage.Assets.Read(font));

		return new FontSet(name, fontSystem);
	}
}