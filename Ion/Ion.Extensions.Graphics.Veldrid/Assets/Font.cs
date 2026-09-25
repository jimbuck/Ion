using System.Numerics;

using FontStashSharp;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Gives the Veldrid sprite batch access to the FontStashSharp font behind an <see cref="IFont"/>.
/// </summary>
internal interface IVeldridFont
{
	DynamicSpriteFont SpriteFont { get; }
}

/// <summary>
/// A FontStashSharp font set.
/// </summary>
[Obsolete(VeldridObsolete.ConcreteAssetTypes)]
public class FontSet : IFontSet
{
	public nint Id => _fontSystem.GetHashCode();

	public string Name { get; }

	private readonly FontSystem _fontSystem;

	/// <summary>
	/// Creates a new instance of <see cref="FontSet"/>.
	/// </summary>
	internal FontSet(string name, FontSystem fontSystem)
	{
		Name = name;
		_fontSystem = fontSystem;
	}

	public Font CreateStyle(float size)
	{
		return new Font(_fontSystem.GetFont(size), this, size);
	}

	IFont IFontSet.CreateStyle(float size) => CreateStyle(size);

	public void Dispose()
	{
		_fontSystem.Dispose();
	}
}

[Obsolete(VeldridObsolete.ConcreteAssetTypes)]
public class Font : IFont, IVeldridFont
{
	internal readonly DynamicSpriteFont SpriteFont;

	public FontSet FontSet { get; }

	public float FontSize { get; }

	IFontSet IFont.FontSet => FontSet;

	float IFont.FontSize => FontSize;

	float IFont.LineHeight => LineHeight;

	DynamicSpriteFont IVeldridFont.SpriteFont => SpriteFont;

	internal Font(DynamicSpriteFont spriteFont, FontSet fontSet, float fontSize)
	{
		SpriteFont = spriteFont;
		FontSet = fontSet;
		FontSize = fontSize;
	}

	/// <summary>
	/// The distance in pixels between the baselines of two consecutive lines of text in this font.
	/// </summary>
	public float LineHeight => SpriteFont.LineHeight;

	/// <summary>
	/// Gets the size in pixels of <paramref name="text"/> when rendered in this font at scale 1.
	/// </summary>
	/// <param name="text">The text to measure.</param>
	/// <returns>The width and height of the rendered text. An empty string measures as <see cref="Vector2.Zero"/>.</returns>
	public Vector2 MeasureString(string text)
	{
		if (string.IsNullOrEmpty(text)) return Vector2.Zero;
		return SpriteFont.MeasureString(text);
	}
}
