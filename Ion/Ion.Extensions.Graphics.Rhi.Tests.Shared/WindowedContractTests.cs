using System.Reflection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Ion.Core;
using Ion.Extensions.Windowing;

namespace Ion.Extensions.Graphics.Rhi.Tests;

/// <summary>
/// The windowed stack end to end, run on every backend: a Silk.NET window (GLFW or SDL) created and pumped by Ion's loop,
/// the backend's surface on it, the quad rendered and the presented frame read back. Needs a display: run under
/// <c>xvfb-run</c> on Linux. Concrete classes need <c>[Collection(WindowedCollection.Name)]</c> (the Silk.NET platform
/// registration is process-wide) and <see cref="RhiBackendAttribute"/>.
/// </summary>
public abstract class WindowedContractTests
{
	/// <summary>The xunit collection name concrete classes use; each test assembly defines it with parallelization off.</summary>
	public const string Collection = "Windowed";

	/// <summary>The backend of the concrete test class.</summary>
	protected GraphicsBackend Backend => GetType().GetCustomAttribute<RhiBackendAttribute>()?.Backend
		?? throw new InvalidOperationException($"{GetType().Name} needs an [RhiBackend] attribute.");

	private (IonApplication App, GameLoop Loop) Build(WindowPlatform platform, int width, int height, ValidationLog log)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Window:Platform"] = platform.ToString(),
			["Ion:Window:Width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
			["Ion:Window:Height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
			["Ion:Graphics:PreferredBackend"] = Backend.ToString(),
			["Ion:Graphics:RetainLastFrame"] = "true",
			["Ion:Graphics:ClearColorHex"] = "#102030",
			["Ion:Graphics:VSync"] = "false",
			["Ion:Graphics:Validation"] = "true",
		});
		builder.Services.AddLogging(logging => logging.ClearProviders().AddProvider(log));
		builder.Services.AddSilkWindowing(builder.Configuration);
		RhiBackends.AddWindowed(Backend, builder.Services, builder.Configuration);
		builder.Services.AddSingleton<QuadTestSystem>();

		var app = builder.Build();
		app.UseEvents();
		app.UseSilkWindowing();
		RhiBackends.UseWindowed(Backend, app);
		app.UseSystem<QuadTestSystem>();

		var loop = app.Build();
		loop.Initialize();
		return (app, loop);
	}

	[RhiFact(Windowed = true), Trait(CATEGORY, E2E)]
	public void RendersTheQuadIntoAGlfwWindowAndReadsBackThePresentedFrame() => _rendersTheQuad(WindowPlatform.Glfw);

	[RhiFact(Windowed = true), Trait(CATEGORY, E2E)]
	public void RendersTheQuadIntoAnSdlWindowAndReadsBackThePresentedFrame() => _rendersTheQuad(WindowPlatform.Sdl);

	private void _rendersTheQuad(WindowPlatform platform)
	{
		var log = new ValidationLog();
		var (app, loop) = Build(platform, 64, 64, log);
		try
		{
			var window = app.Services.GetRequiredService<SilkWindow>();
			Assert.True(window.IsCreated);
			Assert.Equal(platform, window.Platform);
			Assert.NotEqual(NativeWindowKind.None, window.NativeHandles.Kind);

			for (var i = 0; i < 5; i++) loop.Step();

			var frame = app.Services.GetRequiredService<IGraphicsFrame>();
			Assert.Equal(Backend, frame.Device.Backend);
			Assert.NotNull(frame.Device.Surface);
			Assert.True(frame.ColorFormat is TextureFormat.Bgra8Unorm or TextureFormat.Rgba8Unorm);

			var shot = app.Services.GetRequiredService<IScreenshotSource>().Capture();
			var size = (int)window.FramebufferSize.X;
			QuadAssert.Checkerboard(shot, size, new Rgba8(0x10, 0x20, 0x30, 255));
		}
		finally
		{
			loop.Shutdown();
			app.Dispose();
			log.AssertClean();
		}
	}

	[RhiFact(Windowed = true), Trait(CATEGORY, E2E)]
	public void TheQuadSampleMatchesItsGoldenInAWindow()
	{
		var log = new ValidationLog();
		var shot = QuadSample.Run(Backend, headless: false, log);
		log.AssertClean();
		Ion.Testing.GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("quad_sample_320x240.png"), tolerance: 2);
	}

	[RhiFact(Windowed = true), Trait(CATEGORY, E2E)]
	public void RecreatesTheSurfaceWhenTheWindowIsResized()
	{
		var log = new ValidationLog();
		var (app, loop) = Build(WindowPlatform.Glfw, 64, 64, log);
		try
		{
			var window = app.Services.GetRequiredService<SilkWindow>();
			loop.Step();

			window.Size = new Vector2(96, 96);
			for (var i = 0; i < 10 && window.FramebufferSize != new Vector2(96, 96); i++) loop.Step();
			for (var i = 0; i < 3; i++) loop.Step();

			Assert.Equal(new Vector2(96, 96), window.FramebufferSize);
			var surface = app.Services.GetRequiredService<IGraphicsFrame>().Device.Surface!;
			Assert.Equal((96u, 96u), (surface.Width, surface.Height));

			var shot = app.Services.GetRequiredService<IScreenshotSource>().Capture();
			QuadAssert.Checkerboard(shot, 96, new Rgba8(0x10, 0x20, 0x30, 255));
		}
		finally
		{
			loop.Shutdown();
			app.Dispose();
			log.AssertClean();
		}
	}
}
