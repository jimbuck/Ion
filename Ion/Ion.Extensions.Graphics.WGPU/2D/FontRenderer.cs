using System.Numerics;

using FontStashSharp.Interfaces;
using FontStashSharp;

using WebGPU;

using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class FontRenderer(SpriteRenderer spriteRenderer, ITexture2DManager textureManager, ITraceTimer<FontRenderer> trace) : IFontStashRenderer
{
	public ITexture2DManager TextureManager { get; } = textureManager;

	public void Draw(object textureObj, Vector2 pos, System.Drawing.Rectangle? src, FSColor fsColor, float rotation, Vector2 scale, float depth)
	{
		var timer = trace.Start("FontRenderer::Draw");

		var texture = (Texture2D)textureObj;

		var sourceRectangle = src.HasValue ? (RectangleF)src.Value : new RectangleF(0, 0, texture.Size.X, texture.Size.Y);
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

internal unsafe class FontStashTexture2DManager(IGraphicsContext graphicsContext, ITraceTimer<FontStashTexture2DManager> trace) : ITexture2DManager
{
	private int _textureNum = 0;

	public object CreateTexture(int width, int height)
	{
		var timer = trace.Start("FontStashTexture2DManager::CreateTexture");

		ArgumentNullException.ThrowIfNull(graphicsContext.Device);

		Texture2D texture = graphicsContext.CreateTexture2D($"FontTexture{_textureNum++}", width, height, WGPUTextureUsage.TextureBinding | WGPUTextureUsage.CopyDst, WGPUTextureFormat.BGRA8Unorm);

		timer.Stop();

		return texture;
	}

	public void SetTextureData(object textureObj, System.Drawing.Rectangle bounds, byte[] data)
	{
		var timer = trace.Start("FontStashTexture2DManager::SetTextureData");

		if (textureObj is not Texture2D texture) throw new ArgumentException("textureObj is not a Texture2D", nameof(textureObj));

		graphicsContext.WriteTexture2D(texture, data, 0, new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height));

		timer.Stop();
	}

	public System.Drawing.Point GetTextureSize(object texture)
	{
		var ionTexture = (Texture2D)texture;

		return new System.Drawing.Point((int)ionTexture.Size.X, (int)ionTexture.Size.Y);
	}
}