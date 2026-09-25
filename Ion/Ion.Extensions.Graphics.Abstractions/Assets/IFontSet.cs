using System.Numerics;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public interface IFontSet : IAsset { }

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
