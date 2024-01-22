using System.Numerics;

using FontStashSharp.Interfaces;
using FontStashSharp;
using Veldrid;

namespace Ion.Extensions.Graphics;

internal class FontStashRenderer : IFontStashRenderer
{
	private readonly ISpriteBatch _spriteBatch;
	private readonly IGraphicsContext _graphicsContext;

	public ITexture2DManager TextureManager { get; }

	public FontStashRenderer(ISpriteBatch spriteBatch, IGraphicsContext graphicsContext)
	{
		_spriteBatch = spriteBatch;
		_graphicsContext = graphicsContext;
		TextureManager = new FontStashTexture2DManager(_graphicsContext);
	}

	public void Draw(object textureObj, Vector2 pos, System.Drawing.Rectangle? src, FSColor color, float rotation, Vector2 scale, float depth)
	{
		var texture = (Texture2D)textureObj;

		_spriteBatch.Draw(
			texture: texture,
			position: pos,
			sourceRectangle: src.HasValue ? new RectangleF(src.Value.X, src.Value.Y, src.Value.Width, src.Value.Height) : new RectangleF(0, 0, texture.Size.X, texture.Size.Y),
			color: new Color(color.R, color.G, color.B, color.A),
			rotation: rotation,
			origin: Vector2.Zero,
			scale: scale,
			options: SpriteEffect.None,
			depth: depth);
	}
}

internal class FontStashTexture2DManager : ITexture2DManager
{
	private readonly IGraphicsContext _graphicsContext;

	private int _textureNum = 0;

	public FontStashTexture2DManager(IGraphicsContext graphicsContext)
	{
		ArgumentNullException.ThrowIfNull(graphicsContext);

		_graphicsContext = graphicsContext;
	}

	public object CreateTexture(int width, int height)
	{
		ArgumentNullException.ThrowIfNull(_graphicsContext.GraphicsDevice);

		TextureDescription desc = new((uint)width, (uint)height, 1, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D);
		var texture = _graphicsContext.Factory.CreateTexture2D(desc, $"FontTexture{_textureNum++}");

		return new Texture2D(texture);
	}

	public void SetTextureData(object textureObj, System.Drawing.Rectangle bounds, byte[] data)
	{
		ArgumentNullException.ThrowIfNull(_graphicsContext.GraphicsDevice);

		var texture = (Texture2D)textureObj;

		_graphicsContext.GraphicsDevice.UpdateTexture(texture, data, (uint)bounds.X, (uint)bounds.Y, 0, (uint)bounds.Width, (uint)bounds.Height, 1, 0, 0);
	}

	public System.Drawing.Point GetTextureSize(object texture)
	{
		var ionTexture = (Texture2D)texture;

		return new System.Drawing.Point((int)ionTexture.Size.X, (int)ionTexture.Size.Y);
	}
}