using FontStashSharp;

using Ion.Extensions.Assets;


namespace Ion.Extensions.Graphics;

public static class FontAssetManagerExtensions
{
	/// <summary>
	/// Loads a font set named <paramref name="name"/> from the font files in <paramref name="fonts"/> through
	/// <see cref="IBaseAssetManager.GetOrLoad{T}"/>, so the set is cached and owned by <paramref name="assetManager"/>.
	/// The cache key is <paramref name="name"/>: loading the same name again returns the cached set.
	/// </summary>
	public static FontSet Load<T>(this IBaseAssetManager assetManager, string name, params string[] fonts) where T : FontSet
	{
		var fontLoader = (FontLoader)assetManager.GetLoader(typeof(FontSet));

		return assetManager.GetOrLoad(name, fontName => fontLoader.Load(fontName, fonts));
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