using System.Runtime.InteropServices;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Silk.NET.OpenGLES;

using Ion.Core;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Rhi.Tests;
using Ion.Extensions.Windowing;
using Ion.Testing;

namespace Ion.Extensions.Graphics.GLES.Tests;

/// <summary>
/// The backend selection seam: <see cref="GraphicsBackendSelector"/>'s platform order and forcing, and the selecting
/// <see cref="RhiGraphics"/> headless (<c>Ion:Headless:Render</c>) and windowed.
/// </summary>
public class BackendSelectionTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void AutoPrefersOpenGlesOnLinuxArm64AndVulkanElsewhere()
	{
		Assert.Equal([GraphicsBackend.OpenGLES, GraphicsBackend.Vulkan], GraphicsBackendSelector.AutoOrder(isLinux: true, isAndroid: false, Architecture.Arm64));
		Assert.Equal([GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES], GraphicsBackendSelector.AutoOrder(isLinux: true, isAndroid: false, Architecture.X64));
		Assert.Equal([GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES], GraphicsBackendSelector.AutoOrder(isLinux: false, isAndroid: false, Architecture.X64));
		Assert.Equal([GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES], GraphicsBackendSelector.AutoOrder(isLinux: true, isAndroid: true, Architecture.Arm64));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AnExplicitBackendIsForcedAndAutoTakesTheFirstAvailable()
	{
		var probes = 0;
		var selector = new GraphicsBackendSelector(new Dictionary<GraphicsBackend, Func<bool>>
		{
			[GraphicsBackend.Vulkan] = () => { probes++; return false; },
			[GraphicsBackend.OpenGLES] = () => true,
		}, [GraphicsBackend.Vulkan, GraphicsBackend.OpenGLES]);

		Assert.Equal(GraphicsBackend.Vulkan, selector.Resolve(GraphicsBackend.Vulkan));
		Assert.Equal(GraphicsBackend.OpenGLES, selector.Resolve(GraphicsBackend.OpenGLES));
		Assert.Equal(0, probes);
		Assert.Equal(GraphicsBackend.OpenGLES, selector.Resolve(GraphicsBackend.Auto));
		// Unregistered backends resolve like Auto; the probes ran once.
		Assert.Equal(GraphicsBackend.OpenGLES, selector.Resolve(GraphicsBackend.Direct3D12));
		Assert.Equal(1, probes);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void WithNothingAvailableAutoTakesTheFirstCandidate()
	{
		var selector = new GraphicsBackendSelector(new Dictionary<GraphicsBackend, Func<bool>>
		{
			[GraphicsBackend.OpenGLES] = () => false,
			[GraphicsBackend.Vulkan] = () => throw new DllNotFoundException("no loader"),
		}, [GraphicsBackend.OpenGLES, GraphicsBackend.Vulkan]);
		Assert.Equal(GraphicsBackend.OpenGLES, selector.Resolve(GraphicsBackend.Auto));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheR36SProfileBinds()
	{
		// docs/platforms/r36s.md: SDL, fullscreen 640x480, OpenGL ES.
		var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Window:Platform"] = "Sdl",
			["Ion:Window:Fullscreen"] = "true",
			["Ion:Window:Width"] = "640",
			["Ion:Window:Height"] = "480",
			["Ion:Graphics:PreferredBackend"] = "OpenGLES",
		}).Build();
		var window = config.GetSection("Ion:Window").Get<WindowConfig>()!;
		var graphics = config.GetSection("Ion:Graphics").Get<GraphicsConfig>()!;

		Assert.Equal(WindowPlatform.Sdl, window.Platform);
		Assert.True(window.Fullscreen);
		Assert.Equal(WindowState.FullScreen, window.WindowState);
		Assert.Equal((640, 480), (window.Width, window.Height));
		Assert.Equal(GraphicsBackend.OpenGLES, graphics.PreferredBackend);
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void HeadlessRenderingFallsBackToOpenGlesWhenVulkanIsMissing()
	{
		// Ion:Headless:Render through AddIon, PreferredBackend Auto, on a machine whose Vulkan probe fails.
		var log = new ValidationLog();
		Screenshot shot;
		using (var host = log.Attach(new IonTestHost())
			.WithRendering(64, 64)
			.WithConfiguration("Ion:Graphics:PreferredBackend", "Auto")
			.WithConfiguration("Ion:Graphics:ClearColorHex", "#202020")
			.Configure(services => services.AddSingleton(new GraphicsBackendSelector(new Dictionary<GraphicsBackend, Func<bool>>
			{
				[GraphicsBackend.Vulkan] = () => false,
				[GraphicsBackend.OpenGLES] = GlesDevice.IsHeadlessAvailable,
			})))
			.WithSystem<QuadTestSystem>())
		{
			host.Step(2);
			var graphics = host.Get<RhiGraphics>();
			Assert.Equal(GraphicsBackend.OpenGLES, graphics.Backend);
			Assert.IsType<GlesGraphics>(graphics.Inner);
			shot = host.Screenshot();
		}

		log.AssertClean();
		GoldenImage.AssertMatches(shot, TestEnvironment.GoldenPath("test_host_quad_64.png"), tolerance: 2);
	}

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void TheMaxFeatureLevelIsReadFromConfiguration()
	{
		using var host = new IonTestHost()
			.WithRendering(8, 8)
			.WithConfiguration("Ion:Graphics:PreferredBackend", "OpenGLES")
			.WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", "Es30");
		host.Start();
		var device = Assert.IsType<GlesDevice>(host.Get<IGraphicsFrame>().Device);
		Assert.Equal(GlesFeatureLevel.Es30, device.FeatureLevel);
	}
}

/// <summary>
/// GLES in a window: the backend-selecting registration creates the window with an OpenGL ES context, and the present
/// blit flips the offscreen target into GL's bottom-up default framebuffer.
/// </summary>
[RhiBackend(GraphicsBackend.OpenGLES)]
[Collection(WindowedContractTests.Collection)]
public class GlesWindowTests
{
	private static (IonApplication App, GameLoop Loop) Build(ValidationLog log, Action<IServiceCollection> configure)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Window:Platform"] = "Glfw",
			["Ion:Window:Width"] = "64",
			["Ion:Window:Height"] = "64",
			["Ion:Graphics:PreferredBackend"] = "Auto",
			["Ion:Graphics:ClearColorHex"] = "#102030",
			["Ion:Graphics:RetainLastFrame"] = "true",
			["Ion:Graphics:Validation"] = "true",
		});
		builder.Services.AddLogging(logging => logging.ClearProviders().AddProvider(log));
		builder.Services.AddSilkWindowing(builder.Configuration);
		builder.Services.AddRhiGraphics(builder.Configuration);
		configure(builder.Services);
		builder.Services.AddSingleton<QuadTestSystem>();

		var app = builder.Build();
		app.UseEvents();
		app.UseSilkWindowing();
		app.UseRhiGraphics();
		app.UseSystem<QuadTestSystem>();
		var loop = app.Build();
		loop.Initialize();
		return (app, loop);
	}

	[RhiFact(Windowed = true), Trait(CATEGORY, E2E)]
	public void AutoWithoutVulkanCreatesAGlesWindowAndPresentsRowZeroAtTheTop()
	{
		var log = new ValidationLog();
		var (app, loop) = Build(log, services => services.AddSingleton(new GraphicsBackendSelector(new Dictionary<GraphicsBackend, Func<bool>>
		{
			[GraphicsBackend.Vulkan] = () => false,
			[GraphicsBackend.OpenGLES] = () => true,
		})));
		try
		{
			var graphics = app.Services.GetRequiredService<RhiGraphics>();
			Assert.Equal(GraphicsBackend.OpenGLES, graphics.Backend);
			var device = Assert.IsType<GlesDevice>(graphics.Device);
			Assert.IsType<SilkGlesContext>(device.Context);

			// Read the default framebuffer right after the present blit (before the swap), bottom row first.
			Screenshot? presented = null;
			device.SurfaceImpl!.Presenting = (width, height) =>
			{
				var pixels = new byte[width * height * 4];
				device.Gl.BindFramebuffer(GLEnum.ReadFramebuffer, 0);
				device.Gl.ReadPixels<byte>(0, 0, width, height, GLEnum.Rgba, GLEnum.UnsignedByte, pixels);
				var flipped = new byte[pixels.Length];
				var row = (int)width * 4;
				for (var y = 0; y < height; y++) Array.Copy(pixels, y * row, flipped, ((int)height - 1 - y) * row, row);
				presented = new Screenshot((int)width, (int)height, flipped);
			};

			for (var i = 0; i < 3; i++) loop.Step();
			device.SurfaceImpl.Presenting = null;

			Assert.NotNull(presented);
			QuadAssert.Checkerboard(presented, presented.Width, new Rgba8(0x10, 0x20, 0x30, 255));
			var retained = app.Services.GetRequiredService<IScreenshotSource>().Capture();
			Assert.Equal(retained.Rgba, presented.Rgba);
		}
		finally
		{
			loop.Shutdown();
			app.Dispose();
			log.AssertClean();
		}
	}
}
