using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics.Rhi;
using Ion.Core;
using Ion.Extensions.Windowing;

namespace Ion.Extensions.Graphics.Vulkan.Tests;

/// <summary>
/// The windowed stack end to end: a Silk.NET window (GLFW or SDL) created and pumped by Ion's loop, a Vulkan swapchain on
/// it, the quad rendered and the presented frame read back. Needs a display: run under <c>xvfb-run</c> on Linux.
/// </summary>
[Collection(WindowedCollection.Name)]
public class WindowedRenderingTests
{
	private static (IonApplication App, GameLoop Loop) Build(WindowPlatform platform, int width, int height, ValidationLog log)
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
		{
			["Ion:Window:Platform"] = platform.ToString(),
			["Ion:Window:Width"] = width.ToString(System.Globalization.CultureInfo.InvariantCulture),
			["Ion:Window:Height"] = height.ToString(System.Globalization.CultureInfo.InvariantCulture),
			["Ion:Graphics:RetainLastFrame"] = "true",
			["Ion:Graphics:ClearColorHex"] = "#102030",
			["Ion:Graphics:VSync"] = "false",
			["Ion:Graphics:Validation"] = "true",
		});
		builder.Services.AddLogging(logging => logging.ClearProviders().AddProvider(log));
		builder.Services.AddSilkWindowing(builder.Configuration);
		builder.Services.AddVulkanGraphics(builder.Configuration);
		builder.Services.AddSingleton<QuadTestSystem>();

		var app = builder.Build();
		app.UseEvents();
		app.UseSilkWindowing();
		app.UseVulkanGraphics();
		app.UseSystem<QuadTestSystem>();

		var loop = app.Build();
		loop.Initialize();
		return (app, loop);
	}

	[WindowedVulkanTheory, Trait(CATEGORY, E2E)]
	[InlineData(WindowPlatform.Glfw)]
	[InlineData(WindowPlatform.Sdl)]
	public void RendersTheQuadIntoAWindowAndReadsBackThePresentedFrame(WindowPlatform platform)
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

			var graphics = app.Services.GetRequiredService<VulkanGraphics>();
			Assert.NotNull(graphics.Device.Surface);
			Assert.True(graphics.ColorFormat is TextureFormat.Bgra8Unorm or TextureFormat.Rgba8Unorm);

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

	[WindowedVulkanTheory, Trait(CATEGORY, E2E)]
	[InlineData(WindowPlatform.Glfw)]
	public void RecreatesTheSwapchainWhenTheWindowIsResized(WindowPlatform platform)
	{
		var log = new ValidationLog();
		var (app, loop) = Build(platform, 64, 64, log);
		try
		{
			var window = app.Services.GetRequiredService<SilkWindow>();
			loop.Step();

			window.Size = new Vector2(96, 96);
			for (var i = 0; i < 10 && window.FramebufferSize != new Vector2(96, 96); i++) loop.Step();
			for (var i = 0; i < 3; i++) loop.Step();

			Assert.Equal(new Vector2(96, 96), window.FramebufferSize);
			var surface = app.Services.GetRequiredService<VulkanGraphics>().Device.Surface!;
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

/// <summary>Windowed tests share the process-wide Silk.NET platform registration, so they never run in parallel.</summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WindowedCollection
{
	public const string Name = "Windowed";
}
