﻿using System.Numerics;

namespace Ion.Extensions.Graphics;

public interface ISpriteBatch
{
	void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0);
	void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0);

	void DrawPoint(Color color, Vector2 position, float depth = 0);
	void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0);

	void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0);
	void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0);

	void DrawString(IFont font, string text, Vector2 textPosition, Color color = default, float depth = 0, Vector2 origin = default, float rotation = 0, float scale = 1, SpriteEffect options = SpriteEffect.None);

	void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
	void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None);
}
