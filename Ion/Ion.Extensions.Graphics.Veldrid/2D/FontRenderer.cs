using System.Numerics;

using FontStashSharp.Interfaces;
using FontStashSharp;
using Veldrid;
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class FontRenderer(SpriteRenderer spriteRenderer, ITexture2DManager textureManager, ITraceTimer<FontRenderer> trace) : IFontStashRenderer
{
	public ITexture2DManager TextureManager { get; } = textureManager;

	public void Draw(object textureObj, Vector2 pos, System.Drawing.Rectangle? src, FSColor fsColor, float rotation, Vector2 scale, float depth)
	{
		var timer = trace.Start("FontRenderer::Draw");

		var texture = (Texture2D)textureObj;

		var sourceRectangle = src.HasValue ? (RectangleF)src.Value : new RectangleF(0, 0, texture.Width, texture.Height);
		var color = new Color(fsColor.R, fsColor.G, fsColor.B, fsColor.A);

		spriteRenderer.Draw(
			texture: texture,
			position: pos,
			sourceRectangle: sourceRectangle,
			color: color,
			rotation: rotation,
			origin: Vector2.Zero,
			scale: scale,
			options: SpriteEffect.None,
			depth: depth);

		timer.Stop();
	}
}

internal class FontStashTexture2DManager(IGraphicsContext graphicsContext, ITraceTimer<FontStashTexture2DManager> trace) : ITexture2DManager
{
	private int _textureNum = 0;

	public object CreateTexture(int width, int height)
	{
		var timer = trace.Start("FontStashTexture2DManager::CreateTexture");

		ArgumentNullException.ThrowIfNull(graphicsContext.GraphicsDevice);

		TextureDescription desc = new((uint)width, (uint)height, 1, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D);
		var texture = graphicsContext.Factory.CreateTexture2D(desc, $"FontTexture{_textureNum++}");

		timer.Stop();

		return new Texture2D(texture);
	}

	public void SetTextureData(object textureObj, System.Drawing.Rectangle bounds, byte[] data)
	{
		var timer = trace.Start("FontStashTexture2DManager::SetTextureData");

		ArgumentNullException.ThrowIfNull(graphicsContext.GraphicsDevice);

		var texture = (Texture2D)textureObj;

		graphicsContext.GraphicsDevice.UpdateTexture(texture, data, (uint)bounds.X, (uint)bounds.Y, 0, (uint)bounds.Width, (uint)bounds.Height, 1, 0, 0);

		timer.Stop();
	}

	public System.Drawing.Point GetTextureSize(object texture)
	{
		var ionTexture = (Texture2D)texture;

		return new System.Drawing.Point((int)ionTexture.Width, (int)ionTexture.Height);
	}
}