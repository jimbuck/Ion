using System.Numerics;

using FontStashSharp;

using Ion.Extensions.Assets;


namespace Ion.Extensions.Graphics;

/// <summary>
/// Represents a font that can be rendered with <see cref="TextRenderer"/>.
/// </summary>
public class FontSet : IFontSet
{
	public int Id => _fontSystem.GetHashCode();

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

	internal Font(DynamicSpriteFont spriteFont, FontSet fontSet, float fontSize)
	{
		SpriteFont = spriteFont;
		FontSet = fontSet;
		FontSize = fontSize;
	}

	/// <summary>
	/// Gets the size of a string when rendered in this font.
	/// </summary>
	/// <param name="text">The text to measure.</param>
	/// <param name="fontSize">The font size to measure.</param>
	/// <returns>The size of <paramref name="text"/> rendered with <paramref name="fontSize"/> font size.</returns>
	public Vector2 MeasureString(string text)
	{
		return SpriteFont.MeasureString(text);
	}

	Vector2 IFont.MeasureString(string text)
	{
		throw new NotImplementedException();
	}
}
