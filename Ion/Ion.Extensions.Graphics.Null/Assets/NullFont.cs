using Ion.Extensions.Assets;
using System.Numerics;

namespace Ion.Extensions.Graphics;

/// <summary>
/// A font set that loads no glyphs. Its fonts measure text with a fixed glyph width, so layout code gives the same
/// result on every machine. Created by <see cref="NullFontLoader"/>.
/// </summary>
public sealed class NullFontSet : IFontSet
{
	private static long _nextId;

	public NullFontSet(string name, IReadOnlyList<string> fonts)
	{
		Name = name;
		Fonts = fonts;
	}

	public nint Id { get; } = (nint)Interlocked.Increment(ref _nextId);

	public string Name { get; }

	/// <summary>
	/// The asset paths of the font files in this set.
	/// </summary>
	public IReadOnlyList<string> Fonts { get; }

	/// <summary>
	/// True once the asset has been disposed.
	/// </summary>
	public bool IsDisposed { get; private set; }

	/// <summary>
	/// How many times the font set has been reloaded in place (asset hot reload).
	/// </summary>
	public int ReloadCount { get; private set; }

	internal void MarkReloaded() => ReloadCount++;

	public IFont CreateStyle(float size) => new NullFont(this, size);

	public void Dispose() => IsDisposed = true;
}

/// <summary>
/// A monospaced stand-in font: every character is <see cref="GlyphWidthRatio"/> times <see cref="FontSize"/> wide and every
/// line is <see cref="LineHeight"/> (equal to <see cref="FontSize"/>) tall.
/// </summary>
public sealed class NullFont(NullFontSet fontSet, float fontSize) : IFont
{
	/// <summary>
	/// Width of every glyph as a fraction of the font size.
	/// </summary>
	public const float GlyphWidthRatio = 0.5f;

	public IFontSet FontSet { get; } = fontSet;

	public float FontSize { get; } = fontSize;

	/// <summary>
	/// The width in pixels of every glyph: <see cref="FontSize"/> times <see cref="GlyphWidthRatio"/>.
	/// </summary>
	public float GlyphWidth => FontSize * GlyphWidthRatio;

	public float LineHeight => FontSize;

	/// <summary>
	/// Returns (longest line length times <see cref="GlyphWidth"/>, line count times <see cref="LineHeight"/>). Lines are
	/// separated by <c>\n</c>; <c>\r</c> is ignored. An empty string measures as <see cref="Vector2.Zero"/>.
	/// </summary>
	public Vector2 MeasureString(string text)
	{
		if (string.IsNullOrEmpty(text)) return Vector2.Zero;

		var lines = 1;
		var longest = 0;
		var current = 0;
		foreach (var c in text)
		{
			if (c == '\n')
			{
				lines++;
				current = 0;
				continue;
			}

			if (c == '\r') continue;

			current++;
			if (current > longest) longest = current;
		}

		return new Vector2(longest * GlyphWidth, lines * LineHeight);
	}
}

/// <summary>
/// Loads <see cref="IFontSet"/> assets for the headless backend. Checks that every font file exists but reads no glyphs.
/// Supports hot reload in place (the files are checked again).
/// </summary>
public sealed class NullFontLoader(IPersistentStorage storage) : IFontSetLoader, IReloadableAssetLoader
{
	public Type AssetType { get; } = typeof(IFontSet);

	/// <exception cref="FileNotFoundException">The font file does not exist.</exception>
	public IFontSet Load(string path) => Load(path, [path]);

	/// <summary>
	/// Checks the files of <paramref name="asset"/> (a <see cref="NullFontSet"/>) again and counts the reload. Returns false
	/// for any other asset type.
	/// </summary>
	/// <exception cref="FileNotFoundException">One of the font files does not exist.</exception>
	public bool TryReload(IAsset asset, string path)
	{
		if (asset is not NullFontSet fontSet) return false;

		_checkFiles(fontSet.Fonts);
		fontSet.MarkReloaded();
		return true;
	}

	/// <exception cref="FileNotFoundException">One of the font files does not exist.</exception>
	public IFontSet Load(string name, IReadOnlyList<string> fonts)
	{
		_checkFiles(fonts);
		return new NullFontSet(name, [.. fonts]);
	}

	private void _checkFiles(IReadOnlyList<string> fonts)
	{
		foreach (var font in fonts)
		{
			var filepath = storage.Assets.GetPath(font);
			if (!File.Exists(filepath))
			{
				throw new FileNotFoundException($"Font '{font}' was not found at '{filepath}'. File names are case-sensitive on Linux and macOS; check the casing of the name.", filepath);
			}
		}
	}
}
