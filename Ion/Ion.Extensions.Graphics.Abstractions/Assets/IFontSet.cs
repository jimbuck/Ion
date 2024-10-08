using System.Numerics;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

public interface IFontSet : IAsset { }

public interface IFont
{
	IFontSet FontSet { get; }
	float FontSize { get; }

	Vector2 MeasureString(string text);
}
