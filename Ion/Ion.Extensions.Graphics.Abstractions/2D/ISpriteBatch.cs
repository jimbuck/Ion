using System.Numerics;

namespace Ion.Extensions.Graphics;

public interface ISpriteBatch
{
	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	/// <summary>
	/// Draws <paramref name="text"/> with <paramref name="font"/> at <paramref name="textPosition"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None);

	/// <summary>
	/// Draws <paramref name="texture"/> (or the <paramref name="sourceRectangle"/> part of it; <c>default</c> means the whole texture) into <paramref name="destinationRectangle"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
	/// <summary>
	/// Draws <paramref name="texture"/> (or the <paramref name="sourceRectangle"/> part of it; <c>default</c> means the whole texture) at <paramref name="position"/> with <paramref name="size"/>.
	/// </summary>
	/// <remarks>
	/// <see cref="Color"/> has no separate "unset" value: <c>default(Color)</c> is bitwise identical to <see cref="Color.Transparent"/>.
	/// An omitted or <c>default</c> <c>color</c> therefore means "no tint" and is drawn as <see cref="Color.White"/>,
	/// so passing <see cref="Color.Transparent"/> also draws untinted. To draw fully transparent, use a color whose alpha is 0
	/// and whose RGB is not all zero (for example <c>new Color(Color.White, 0f)</c>), or skip the draw call.
	/// </remarks>
	void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
}
