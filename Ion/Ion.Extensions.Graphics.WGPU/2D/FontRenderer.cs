//using System.Numerics;

//using FontStashSharp.Interfaces;
//using FontStashSharp;
//using Ion.Extensions.Debug;
//using SixLabors.ImageSharp.PixelFormats;

//namespace Ion.Extensions.Graphics;

//internal class FontRenderer(SpriteRenderer spriteRenderer, ITexture2DManager textureManager, ITraceTimer<FontRenderer> trace) : IFontStashRenderer
//{
//	public ITexture2DManager TextureManager { get; } = textureManager;

//	public void Draw(object textureObj, Vector2 pos, System.Drawing.Rectangle? src, FSColor fsColor, float rotation, Vector2 scale, float depth)
//	{
//		var timer = trace.Start("FontRenderer::Draw");

//		var texture = (Texture2D)textureObj;

//		var sourceRectangle = src.HasValue ? (RectangleF)src.Value : new RectangleF(0, 0, texture.Size.X, texture.Size.Y);
//		var color = new Color(fsColor.R, fsColor.G, fsColor.B, fsColor.A);

//		spriteRenderer.Draw(
//			texture: texture,
//			position: pos,
//			sourceRectangle: sourceRectangle,
//			color: color,
//			rotation: rotation,
//			origin: Vector2.Zero,
//			scale: scale,
//			options: SpriteEffect.None,
//			depth: depth);

//		timer.Stop();
//	}
//}

//internal class FontStashTexture2DManager(IGraphicsContext graphicsContext, ITraceTimer<FontStashTexture2DManager> trace) : ITexture2DManager
//{
//	private int _textureNum = 0;

//	public object CreateTexture(int width, int height)
//	{
//		var timer = trace.Start("FontStashTexture2DManager::CreateTexture");

//		ArgumentNullException.ThrowIfNull(graphicsContext.Device);

//		var texture = graphicsContext.CreateTexture2D(new Wgpu.TextureDescriptor
//		{
//			label = $"FontTexture{_textureNum++}",
//			dimension = Wgpu.TextureDimension.TwoDimensions,
//			size = new Wgpu.Extent3D() { width = (uint)width, height = (uint)height, depthOrArrayLayers = 1 },
//			format = Wgpu.TextureFormat.BGRA8Unorm,
//			usage = (uint)(Wgpu.TextureUsage.TextureBinding | Wgpu.TextureUsage.CopyDst),
//			mipLevelCount = 1,
//			sampleCount = 1
//		});

//		timer.Stop();

//		return new Texture2D(texture);
//	}

//	public void SetTextureData(object textureObj, System.Drawing.Rectangle bounds, byte[] data)
//	{
//		var timer = trace.Start("FontStashTexture2DManager::SetTextureData");

//		if (textureObj is not Texture2D texture) throw new ArgumentException("textureObj is not a Texture2D", nameof(textureObj));

//		_setTextureData(texture, bounds, data);

//		timer.Stop();
//	}

//	private unsafe void _setTextureData(Texture2D texture, System.Drawing.Rectangle bounds, byte[] data)
//	{
//		graphicsContext.Device.Queue.WriteTexture<byte>(
//			destination: new ImageCopyTexture
//			{
//				Aspect = Wgpu.TextureAspect.All,
//				MipLevel = 0,
//				Origin = default,
//				Texture = texture
//			},
//			data: data,
//			dataLayout: new Wgpu.TextureDataLayout
//			{
//				bytesPerRow = (uint)(sizeof(Bgra32) * texture.Size.X),
//				offset = 0,
//				rowsPerImage = (uint)texture.Size.Y
//			},
//			writeSize: new Wgpu.Extent3D { width = (uint)bounds.Width, height = (uint)bounds.Height, depthOrArrayLayers = 1 }
//		);
//	}

//	public System.Drawing.Point GetTextureSize(object texture)
//	{
//		var ionTexture = (Texture2D)texture;

//		return new System.Drawing.Point((int)ionTexture.Size.X, (int)ionTexture.Size.Y);
//	}
//}