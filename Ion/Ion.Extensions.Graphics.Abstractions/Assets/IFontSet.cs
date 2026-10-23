using System.Numerics;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

/// <summary>
/// A set of font files (for example a regular face plus fallbacks) from which fonts of any size are created.
/// Load one with <c>assets.Load&lt;IFontSet&gt;("MyFont.ttf")</c>, or several files as one set with
/// <see cref="FontSetAssetManagerExtensions.LoadFontSet(IBaseAssetManager, string, string[])"/>.
/// </summary>
public interface IFontSet : IAsset
{
	/// <summary>
	/// Creates a font of this set at <paramref name="size"/> pixels, for use with <see cref="ISpriteBatch.DrawString"/>.
	/// </summary>
	IFont CreateStyle(float size);
}

public interface IFont
{
	IFontSet FontSet { get; }
	float FontSize { get; }

	/// <summary>
	/// The distance in pixels between the baselines of two consecutive lines of text in this font.
	/// </summary>
	float LineHeight { get; }

	/// <summary>
	/// Gets the size in pixels of <paramref name="text"/> when rendered in this font at scale 1.
	/// </summary>
	/// <param name="text">The text to measure.</param>
	/// <returns>The width and height of the rendered text.</returns>
	Vector2 MeasureString(string text);
}

/// <summary>
/// An <see cref="IAssetLoader{T}"/> for <see cref="IFontSet"/> that can also combine several font files into one set.
/// <see cref="IAssetLoader{T}.Load(string)"/> loads a set made of the single font file at the given path.
/// </summary>
public interface IFontSetLoader : IAssetLoader<IFontSet>
{
	/// <summary>
	/// Loads a new font set named <paramref name="name"/> from the font files in <paramref name="fonts"/> (asset paths).
	/// </summary>
	IFontSet Load(string name, IReadOnlyList<string> fonts);
}

public static class FontSetAssetManagerExtensions
{
	/// <summary>
	/// Loads a font set named <paramref name="name"/> from the font files in <paramref name="fonts"/>, cached and owned by
	/// <paramref name="assetManager"/>. The cache key is <paramref name="name"/>: loading the same name again returns the
	/// cached set, as does <c>Load&lt;IFontSet&gt;(name)</c>.
	/// </summary>
	/// <exception cref="InvalidOperationException">The registered <see cref="IFontSet"/> loader is not an <see cref="IFontSetLoader"/>.</exception>
	public static IFontSet LoadFontSet(this IBaseAssetManager assetManager, string name, params string[] fonts)
	{
		if (fonts.Length == 0) return assetManager.Load<IFontSet>(name);

		if (assetManager.GetLoader(typeof(IFontSet)) is not IFontSetLoader loader)
		{
			throw new InvalidOperationException($"The {nameof(IFontSet)} loader does not implement {nameof(IFontSetLoader)}, so it cannot combine several font files.");
		}

		return assetManager.GetOrLoad(name, fontName => loader.Load(fontName, fonts));
	}
}
