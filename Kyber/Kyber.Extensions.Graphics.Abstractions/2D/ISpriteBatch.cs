using System.Numerics;

namespace Kyber.Extensions.Graphics;

public interface ISpriteBatch
{
	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
	void Draw(ITexture2D texture, Vector2 position, Vector2 scale, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
}
