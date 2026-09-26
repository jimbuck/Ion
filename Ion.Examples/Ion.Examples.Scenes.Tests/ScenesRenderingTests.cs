using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Scenes.Tests;

/// <summary>
/// The Scenes sample rendered headless on the Vulkan backend: the main menu scene's green square on the cornflower blue
/// clear color, then (after Tab) the gameplay scene's red square, each compared with a golden image.
/// </summary>
public class ScenesRenderingTests
{
	private const uint Width = 320, Height = 180;

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void RendersEachSceneAndMatchesTheGoldenImages() => RendersEachScene(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void RendersEachSceneAndMatchesTheGoldenImagesOnGles() => RendersEachScene(GraphicsBackend.OpenGLES);

	private static void RendersEachScene(GraphicsBackend backend)
	{
		var log = new ErrorLog();
		Screenshot menu, gameplay;
		using (var host = log.Attach(new IonTestHost().UseGame(b => ScenesApp.Configure(b), a => ScenesApp.Use((IonApplication)a))).WithRendering(Width, Height)
			.WithConfiguration("Ion:Graphics:PreferredBackend", backend.ToString()))
		{
			host.Step(3);
			menu = host.Screenshot();

			host.Input.Tap(Key.Tab);
			host.Step(3);
			gameplay = host.Screenshot();
		}

		log.AssertClean();

		Assert.Equal(Color.CornflowerBlue.ToRgba8(), menu.GetPixel(200, 150));
		Assert.Equal(Color.ForestGreen.ToRgba8(), menu.GetPixel(50, 50));
		Assert.Equal(Color.DarkRed.ToRgba8(), gameplay.GetPixel(50, 50));
		GoldenImage.AssertMatches(menu, RenderingEnvironment.GoldenPath("scenes_menu.png"), tolerance: 2);
		GoldenImage.AssertMatches(gameplay, RenderingEnvironment.GoldenPath("scenes_gameplay.png"), tolerance: 2);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnGlesWithoutErrors()
	{
		var run = SampleWindowed.Run(b => ScenesApp.Configure(b), a => ScenesApp.Use(a), 120, ("Ion:Graphics:PreferredBackend", "OpenGLES"));
		Assert.Equal(120, run.Frames);
		Assert.Equal(1, run.LastFrame.Sprites);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnVulkanWithoutValidationErrors()
	{
		var run = SampleWindowed.Run(b => ScenesApp.Configure(b), a => ScenesApp.Use(a), 240);
		Assert.Equal(240, run.Frames);
		Assert.Equal(1, run.LastFrame.Sprites);
	}
}

internal static class ColorTestExtensions
{
	public static Rgba8 ToRgba8(this Color color)
	{
		var v = color.ToVector4() * 255f + new System.Numerics.Vector4(0.5f);
		return new Rgba8((byte)v.X, (byte)v.Y, (byte)v.Z, (byte)v.W);
	}
}
