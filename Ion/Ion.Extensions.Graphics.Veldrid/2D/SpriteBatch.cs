using System.Numerics;

namespace Ion.Extensions.Graphics;

internal class SpriteBatch(
	SpriteRenderer spriteRenderer,
	FontRenderer fontRenderer
) : ISpriteBatch
{

	public void Initialize()
	{
		spriteRenderer.Initialize();
	}

	public void Begin(GameTime dt)
	{
		spriteRenderer.Begin(dt);
	}

	public void DrawRect(Color color, RectangleF destinationRectangle, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		spriteRenderer.DrawRect(color, destinationRectangle, origin, rotation, depth);
	}

	public void DrawRect(Color color, Vector2 position, Vector2 size, Vector2 origin = default, float rotation = 0, float depth = 0f)
	{
		spriteRenderer.DrawRect(color, position, size, origin, rotation, depth);
	}

	public void DrawPoint(Color color, Vector2 position, Vector2 size, float depth = 0)
	{
		spriteRenderer.DrawPoint(color, position, size, depth);
	}

	public void DrawPoint(Color color, Vector2 position, float depth = 0)
	{
		spriteRenderer.DrawPoint(color, position, depth);
	}

	public void DrawLine(Color color, Vector2 pointA, Vector2 pointB, float thickness = 1f, float depth = 0)
	{
		spriteRenderer.DrawLine(color, pointA, pointB, thickness, depth);
	}

	public void DrawLine(Color color, Vector2 start, float length, float angle, float thickness = 1, float depth = 0)
	{
		spriteRenderer.DrawLine(color, start, length, angle, thickness, depth);
	}

	public void DrawString(IFont font, string text, Vector2 position, Color color = default, float depth = 0f, Vector2 origin = default, float rotation = 0f, float scale = 1f, SpriteEffect options = SpriteEffect.None)
	{
		var fontstyle = (Font)font;

		fontstyle.SpriteFont.DrawText(fontRenderer,
			text: text,
			position: position, 
			color: new FontStashSharp.FSColor(color.R, color.G, color.B, color.A),
			scale: new Vector2(scale),
			rotation: rotation,
			origin: origin,
			layerDepth: depth);
	}

	public void Draw(ITexture2D texture, RectangleF destinationRectangle, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		spriteRenderer.Draw(texture, destinationRectangle, sourceRectangle, color, origin, rotation, depth, options);
	}

	public void Draw(ITexture2D texture, Vector2 position, Vector2 size, RectangleF sourceRectangle = default, Color color = default, Vector2 origin = default, float rotation = 0, float depth = 0, SpriteEffect options = SpriteEffect.None)
	{
		spriteRenderer.Draw(texture, position, size, sourceRectangle, color, origin, rotation, depth, options);
	}

	public unsafe void End()
	{
		spriteRenderer.End();
	}
}

