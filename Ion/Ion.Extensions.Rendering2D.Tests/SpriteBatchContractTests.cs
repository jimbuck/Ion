using System.Reflection;

using Ion.Extensions.Assets;
using Ion.Extensions.Graphics.GLES;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Rhi.Tests;

namespace Ion.Extensions.Rendering2D.Tests;

/// <summary>What a <see cref="ScriptedDrawSystem"/> draws every frame.</summary>
public sealed class DrawScript
{
	public Action<IAssetManager>? Init { get; set; }
	public Action<IGraphicsDevice>? InitDevice { get; set; }
	public Action? Destroy { get; set; }
	public Action<ISpriteBatch>? Render { get; set; }
}

/// <summary>Runs a <see cref="DrawScript"/> in Init and Render.</summary>
public sealed class ScriptedDrawSystem(DrawScript script, ISpriteBatch spriteBatch, IAssetManager assets, IGraphicsFrame frame)
{
	[Init]
	public void Init(GameTime dt)
	{
		script.Init?.Invoke(assets);
		script.InitDevice?.Invoke(frame.Device);
	}

	[Render]
	public void Render(GameTime dt) => script.Render?.Invoke(spriteBatch);

	[Destroy]
	public void Destroy(GameTime dt) => script.Destroy?.Invoke();
}

/// <summary>
/// The sprite batch contract, end to end on a headless RHI backend with validation on: every test draws into a 64x64
/// offscreen frame through <see cref="IonTestHost.WithRendering"/> and checks pixels of the captured frame against pixel
/// asserts and one set of golden images shared by every backend. Each backend derives a sealed class marked with
/// <see cref="RhiBackendAttribute"/> (Vulkan on lavapipe, OpenGL ES on llvmpipe through EGL, and the ES 3.1 paths of the
/// R36S).
/// </summary>
public abstract class SpriteBatchContractTests
{
	private const int Size = 64;

	/// <summary>The backend of the concrete test class.</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	/// <summary>Configures a host to render headless on <see cref="Backend"/>.</summary>
	protected virtual IonTestHost ConfigureHost(IonTestHost host) => host.WithConfiguration("Ion:Graphics:PreferredBackend", Backend.ToString());

	private Screenshot Render(DrawScript script, int frames = 1, Action<IonTestHost>? inspect = null)
	{
		var log = new ErrorLog();
		Screenshot shot;
		using (var host = ConfigureHost(log.Attach(new IonTestHost().WithRendering(Size, Size)))
			.Configure(services => services.AddSingleton(script))
			.WithSystem<ScriptedDrawSystem>())
		{
			host.Step(frames);
			Assert.IsType<SpriteBatch>(host.Get<ISpriteBatch>());
			Assert.Equal(Backend, host.Get<IGraphicsFrame>().Device.Backend);
			shot = host.Screenshot();
			inspect?.Invoke(host);
		}

		log.AssertClean();
		return shot;
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void DrawsRectanglesInSubmissionOrder()
	{
		var shot = Render(new DrawScript
		{
			Render = batch =>
			{
				batch.DrawRect(Color.Red, new RectangleF(8, 8, 16, 16));
				batch.DrawRect(Color.Blue, new RectangleF(16, 16, 16, 16));
			},
		});

		TestImages.AssertPixel(shot, 2, 2, 0, 0, 0);
		TestImages.AssertPixel(shot, 10, 10, 255, 0, 0);
		TestImages.AssertPixel(shot, 20, 20, 0, 0, 255);
		TestImages.AssertPixel(shot, 30, 30, 0, 0, 255);
		TestImages.AssertPixel(shot, 40, 40, 0, 0, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void DrawsTexturesWithPremultipliedAlpha()
	{
		ITexture2D texture = null!;
		var path = TestImages.Quadrants($"{GetType().Name}_quadrants_draw.png");
		var shot = Render(new DrawScript
		{
			Init = assets => texture = assets.Load<ITexture2D>(path),
			Render = batch =>
			{
				batch.Begin(new SpriteBatchOptions { SamplerMode = SpriteSamplerMode.PointClamp });
				batch.Draw(texture, new RectangleF(0, 0, 32, 32));
				// Flipped both ways, tinted green.
				batch.Draw(texture, new RectangleF(32, 32, 32, 32), color: Color.Lime, options: SpriteEffect.FlipHorizontally | SpriteEffect.FlipVertically);
				batch.End();
			},
		});

		Assert.Equal(2u, texture.Width);
		Assert.Equal(2u, texture.MipLevels);
		TestImages.AssertPixel(shot, 8, 8, 255, 0, 0);
		TestImages.AssertPixel(shot, 24, 8, 0, 255, 0);
		TestImages.AssertPixel(shot, 8, 24, 0, 0, 255);
		TestImages.AssertPixel(shot, 24, 24, 128, 128, 128); // half-transparent white over black
		TestImages.AssertPixel(shot, 40, 40, 0, 128, 0); // flipped: the half-white texel, tinted green
		TestImages.AssertPixel(shot, 56, 40, 0, 0, 0); // flipped: the blue texel times green
		TestImages.AssertPixel(shot, 40, 56, 0, 255, 0); // flipped: the green texel
		TestImages.AssertPixel(shot, 56, 56, 0, 0, 0); // flipped: the red texel times green
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void BlendScissorCameraAndSortModesAreSegmentState()
	{
		var shot = Render(new DrawScript
		{
			Render = batch =>
			{
				// Additive: red + green = yellow.
				batch.DrawRect(Color.Red, new RectangleF(0, 0, 16, 16));
				batch.Begin(new SpriteBatchOptions { BlendMode = SpriteBlendMode.Additive });
				batch.DrawRect(new Color(0, 255, 0), new RectangleF(0, 0, 16, 16));
				batch.End();

				// Scissor: only the left half of the rectangle is drawn.
				batch.Begin(new SpriteBatchOptions { Scissor = new Rectangle(16, 0, 8, 16) });
				batch.DrawRect(Color.White, new RectangleF(16, 0, 16, 16));
				batch.End();

				// Camera: world (0, 0) is drawn at pixel (40, 0).
				batch.Begin(new SpriteBatchOptions { Transform = System.Numerics.Matrix3x2.CreateTranslation(40, 0) });
				batch.DrawRect(Color.Blue, new RectangleF(0, 0, 8, 8));
				batch.End();

				// BackToFront: the smaller depth is drawn last, on top, although it was submitted first.
				batch.Begin(new SpriteBatchOptions { SortMode = SpriteSortMode.BackToFront });
				batch.DrawRect(Color.Red, new RectangleF(0, 32, 16, 16), depth: 0.1f);
				batch.DrawRect(Color.Blue, new RectangleF(0, 32, 16, 16), depth: 0.9f);
				batch.End();
			},
		}, inspect: host =>
		{
			var stats = host.Get<SpriteBatch>().LastFrameStatistics;
			Assert.Equal(6, stats.Sprites);
			Assert.Equal(12, stats.Triangles);
			Assert.Equal(5, stats.DrawCalls); // one per segment: the sorted pair shares the white texture
		});

		TestImages.AssertPixel(shot, 8, 8, 255, 255, 0);
		TestImages.AssertPixel(shot, 20, 8, 255, 255, 255);
		TestImages.AssertPixel(shot, 28, 8, 0, 0, 0);
		TestImages.AssertPixel(shot, 44, 4, 0, 0, 255);
		TestImages.AssertPixel(shot, 8, 40, 255, 0, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void RendersIntoATextureAndDrawsItBack()
	{
		RenderTarget2D? target = null;
		var shot = Render(new DrawScript
		{
			InitDevice = device => target = new RenderTarget2D(device, 16, 16),
			Destroy = () => target?.Dispose(),
			Render = batch =>
			{
				batch.SetRenderTarget(target!.Texture, Color.Blue);
				batch.DrawRect(Color.Red, new RectangleF(0, 0, 8, 16));
				batch.SetRenderTarget(null);
				batch.Draw(target!, new RectangleF(32, 32, 32, 32));
			},
		}, frames: 2);

		TestImages.AssertPixel(shot, 40, 48, 255, 0, 0);
		TestImages.AssertPixel(shot, 56, 48, 0, 0, 255);
		TestImages.AssertPixel(shot, 8, 8, 0, 0, 0);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void DrawsTextWithCachedLayouts()
	{
		IFont font = null!;
		var shot = Render(new DrawScript
		{
			Init = assets => font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(24),
			Render = batch => batch.DrawString(font, "Ion", new Vector2(4, 4), Color.Yellow),
		}, frames: 3, inspect: host =>
		{
			var sprites = host.Get<SpriteBatch>();
			Assert.Equal(3, sprites.LastFrameStatistics.Sprites);
			Assert.Equal(1, ((Font)font).CachedLayouts);
			var size = font.MeasureString("Ion");
			Assert.True(size.X > 20 && size.Y > 10, $"measured {size}");
			Assert.True(font.LineHeight > 20);
		});

		var yellow = 0;
		for (var y = 0; y < 40; y++)
		{
			for (var x = 0; x < 64; x++)
			{
				var p = shot.GetPixel(x, y);
				if (p.R > 200 && p.G > 200 && p.B < 60) yellow++;
			}
		}

		Assert.True(yellow > 40, $"{yellow} yellow pixels");
		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("text_ion_64.png"), tolerance: 8, maxMismatchRatio: 0.01);
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void ReloadsATextureInPlaceWhenItKeepsItsSize()
	{
		ITexture2D texture = null!;
		var path = TestImages.Write($"{GetType().Name}_reload.png", 2, 2, (_, _) => TestImages.Red);
		var log = new ErrorLog();
		using (var host = ConfigureHost(log.Attach(new IonTestHost().WithRendering(Size, Size)))
			.Configure(services => services.AddSingleton(new DrawScript
			{
				Init = assets => texture = assets.Load<ITexture2D>(path),
				Render = batch => batch.Draw(texture, new RectangleF(0, 0, 64, 64)),
			}))
			.WithSystem<ScriptedDrawSystem>())
		{
			host.Step();
			TestImages.AssertPixel(host.Screenshot(), 32, 32, 255, 0, 0);

			TestImages.Write($"{GetType().Name}_reload.png", 2, 2, (_, _) => TestImages.Green);
			var loader = (IReloadableAssetLoader)host.Get<IAssetManager>().GetLoader(typeof(ITexture2D));
			Assert.True(loader.TryReload(texture, path));
			host.Step();
			TestImages.AssertPixel(host.Screenshot(), 32, 32, 0, 255, 0);

			TestImages.Write($"{GetType().Name}_reload.png", 4, 4, (_, _) => TestImages.Blue);
			Assert.False(loader.TryReload(texture, path));
		}

		log.AssertClean();
	}

	[RhiFact, Trait(CATEGORY, INTEGRATION)]
	public void DebugShapesAndAGoldenScene()
	{
		ITexture2D texture = null!;
		var path = TestImages.Quadrants($"{GetType().Name}_quadrants_scene.png");
		var shot = Render(new DrawScript
		{
			Init = assets => texture = assets.Load<ITexture2D>(path),
			Render = batch =>
			{
				batch.Draw(texture, new RectangleF(32, 32, 20, 20), origin: new Vector2(0.5f), rotation: 0.4f);
				batch.DrawCircle(Color.Orange, new Vector2(16, 16), 12, 2);
				batch.DrawLine(Color.White, new Vector2(2, 60), new Vector2(62, 40), 3);
				batch.DrawPoint(Color.Cyan, new Vector2(60, 4), new Vector2(4, 4));
				batch.DrawRectOutline(Color.Magenta, new RectangleF(40, 2, 14, 14), 2);
			},
		});

		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("shapes_64.png"), tolerance: 8, maxMismatchRatio: 0.01);
	}
}

/// <summary>The sprite batch contract on Vulkan.</summary>
[RhiBackend(GraphicsBackend.Vulkan)]
public sealed class VulkanSpriteBatchTests : SpriteBatchContractTests;

/// <summary>The sprite batch contract on OpenGL ES at the driver's feature level.</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesSpriteBatchTests : SpriteBatchContractTests;

/// <summary>The sprite batch contract on the OpenGL ES 3.1 paths (the R36S's Mali-G31 level).</summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
public sealed class GlesEs31SpriteBatchTests : SpriteBatchContractTests
{
	protected override IonTestHost ConfigureHost(IonTestHost host) => base.ConfigureHost(host).WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es31));
}
