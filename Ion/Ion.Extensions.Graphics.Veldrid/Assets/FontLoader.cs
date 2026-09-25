using FontStashSharp;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

#pragma warning disable CS0618 // FontSet stays public (obsolete) for one release.

public static class FontAssetManagerExtensions
{
	/// <summary>
	/// Loads a font set named <paramref name="name"/> from the font files in <paramref name="fonts"/>, cached and owned by
	/// <paramref name="assetManager"/>. Forwards to <see cref="FontSetAssetManagerExtensions.LoadFontSet"/>.
	/// </summary>
	[Obsolete("Use assets.LoadFontSet(name, fonts) (or assets.Load<IFontSet>(path) for a single file) and depend on IFontSet.")]
	public static FontSet Load<T>(this IBaseAssetManager assetManager, string name, params string[] fonts) where T : FontSet
	{
		return (FontSet)assetManager.LoadFontSet(name, fonts);
	}
}

internal class FontLoader(IPersistentStorage storage) : IFontSetLoader
{
	public Type AssetType { get; } = typeof(IFontSet);

	/// <summary>
	/// Loads a font set made of the single font file at <paramref name="path"/>, named after the path.
	/// </summary>
	public IFontSet Load(string path) => Load(path, [path]);

	public IFontSet Load(string name, IReadOnlyList<string> fonts)
	{
		var fontSystem = new FontSystem(new FontSystemSettings());

		foreach (var font in fonts)
		{
			using var stream = storage.Assets.Read(font);
			fontSystem.AddFont(stream);
		}

		return new FontSet(name, fontSystem);
	}
}
