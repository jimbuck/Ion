using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Cubes.Tests;

/// <summary>
/// The cubes sample: headless without rendering (the CPU pipeline's statistics), headless rendering compared with one
/// golden image on Vulkan and OpenGL ES (both backends must match it), and windowed runs under validation.
/// </summary>
public class CubesTests
{
	private const uint Width = 640, Height = 360;

	private static IonTestHost Game() => new IonTestHost().UseGame(b => CubesApp.Configure(b), a => CubesApp.Use(a));

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RunsHeadlessWithoutAGpuAndBatchesTheCubes()
	{
		using var host = Game();
		host.Step(3);
		var renderer = host.Get<Renderer3D>();
		Assert.False(renderer.HasDevice);
		var stats = renderer.LastFrameStatistics;
		Assert.Equal(1001, stats.Submitted);
		Assert.Equal(1, stats.Views);
		Assert.InRange(stats.Visible, 900, 1001);
		// Opaque: ground plus one instanced batch per cube material; shadow: one batch per mesh.
		Assert.Equal(5, stats.Batches);
		Assert.Equal(1001, stats.ShadowCasters);
	}

	[VulkanFact, Trait(CATEGORY, E2E)]
	public void RendersTheGoldenImageOnVulkan() => RendersTheGoldenImage(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, E2E)]
	public void RendersTheGoldenImageOnGles() => RendersTheGoldenImage(GraphicsBackend.OpenGLES);

	private static void RendersTheGoldenImage(GraphicsBackend backend)
	{
		var shot = SampleRendering.Capture(Game(), Width, Height, frames: 30, inspect: host =>
		{
			var stats = host.Get<IRenderer3D>().LastFrameStatistics;
			Assert.Equal(5, stats.Batches);
			Assert.Equal(5, stats.DrawCalls);
			// The metrics frame stats include the 3D draw calls and the HUD's sprite draw calls.
			Assert.True(host.LastFrame.DrawCalls > stats.DrawCalls);
		}, backend: backend);

		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("cubes_640x360.png"), tolerance: 4, maxMismatchRatio: 0.001);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnVulkanWithoutValidationErrors()
	{
		var run = SampleWindowed.Run(b => CubesApp.Configure(b), a => CubesApp.Use(a), 120);
		Assert.Equal(120, run.Frames);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnGlesWithoutErrors()
	{
		var run = SampleWindowed.Run(b => CubesApp.Configure(b), a => CubesApp.Use(a), 120, ("Ion:Graphics:PreferredBackend", "OpenGLES"));
		Assert.Equal(120, run.Frames);
	}
}
