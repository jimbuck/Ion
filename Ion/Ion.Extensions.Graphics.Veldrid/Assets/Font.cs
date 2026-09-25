using System.Numerics;

using FontStashSharp;

using Ion.Extensions.Assets;


namespace Ion.Extensions.Graphics;

/// <summary>
/// Represents a font that can be rendered with <see cref="TextRenderer"/>.
/// </summary>
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

	public void Dispose()
	{
		_fontSystem.Dispose();
	}
}

public class Font : IFont
{
	internal readonly DynamicSpriteFont SpriteFont;

	public FontSet FontSet { get; }

	public float FontSize { get; }

	IFontSet IFont.FontSet => FontSet;

	float IFont.FontSize => FontSize;

	float IFont.LineHeight => LineHeight;

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
