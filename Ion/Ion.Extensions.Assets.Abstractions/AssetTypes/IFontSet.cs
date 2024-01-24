
using System.Numerics;

namespace Ion.Extensions.Assets;

public interface IFontSet : IAsset
{

}

public interface IFont
{
	IFontSet FontSet { get; }
	float FontSize { get; }

	Vector2 MeasureString(string text);
}
