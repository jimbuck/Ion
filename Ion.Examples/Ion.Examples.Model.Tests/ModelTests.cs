using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Model.Tests;

/// <summary>
/// The glTF model sample: headless without rendering (the model and skybox load, the scene is submitted), headless
/// rendering compared with one golden image on Vulkan and OpenGL ES, and windowed runs under validation.
/// </summary>
public class ModelTests
{
	private const uint Width = 640, Height = 360;

	private static IonTestHost Game() => new IonTestHost().UseGame(b => ModelApp.Configure(b), a => ModelApp.Use(a));

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void LoadsTheModelAndSubmitsTheSceneHeadless()
	{
		using var host = Game();
		host.Step(2);
		var model = host.Get<ModelSystem>().Model;
		Assert.NotNull(model);
		Assert.Single(model.Meshes);
		Assert.Equal(3, model.Textures.Count);
		var stats = host.Get<IRenderer3D>().LastFrameStatistics;
		// The pedestal, the avocado and five spheres; three lights (the sun and two point lights).
		Assert.Equal(7, stats.Submitted);
		Assert.Equal(7, stats.Visible);
		Assert.Equal(3, stats.Lights);
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
			Assert.True(host.Get<Renderer3D>().ShadowActive);
			// Opaque: pedestal, avocado and one batch per sphere material; skybox; shadow: one batch per mesh.
			Assert.Equal(7, stats.Visible);
		}, backend: backend);

		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("model_640x360.png"), tolerance: 4, maxMismatchRatio: 0.001);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnVulkanWithoutValidationErrors()
	{
		var run = SampleWindowed.Run(b => ModelApp.Configure(b), a => ModelApp.Use(a), 120);
		Assert.Equal(120, run.Frames);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnGlesWithoutErrors()
	{
		var run = SampleWindowed.Run(b => ModelApp.Configure(b), a => ModelApp.Use(a), 120, ("Ion:Graphics:PreferredBackend", "OpenGLES"));
		Assert.Equal(120, run.Frames);
	}
}
