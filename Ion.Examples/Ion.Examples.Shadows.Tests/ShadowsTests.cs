using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Shadows.Tests;

/// <summary>
/// The shadows sample, from its own <c>Program.cs</c>: four cascades up to the 150-unit shadow distance, every pillar a
/// caster, the cascade count changeable at run time; headless rendering on Vulkan and OpenGL ES shadows the ground both
/// at the camera's feet and far down the avenue.
/// </summary>
public class ShadowsTests
{
	private const uint Width = 640, Height = 360;

	private static IonTestHost Game() => new IonTestHost().UseEntryPoint<Program>();

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void FourCascadesReachTheShadowDistanceAndTheCountChangesAtRunTime()
	{
		using var host = Game();
		host.Step(2);
		var renderer = host.Get<Renderer3D>();
		Assert.Equal(4, renderer.ShadowCascades.Length);
		Assert.Equal(150f, renderer.ShadowCascadeSplits[^1]);
		// Every pillar and lintel within the shadow distance casts (the farthest pairs are about 150 units away).
		Assert.True(renderer.LastFrameStatistics.ShadowCasters > ShadowsSystem.Pairs * 2, $"{renderer.LastFrameStatistics.ShadowCasters} casters");

		renderer.Options.ShadowCascades = 2;
		host.Step();
		Assert.Equal(2, renderer.ShadowCascades.Length);
	}

	[VulkanFact, Trait(CATEGORY, E2E)]
	public void RendersOnVulkan() => Renders(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, E2E)]
	public void RendersOnGles() => Renders(GraphicsBackend.OpenGLES);

	private static void Renders(GraphicsBackend backend)
	{
		var shot = SampleRendering.Capture(Game(), Width, Height, frames: 30, backend: backend);
		// The same frame with shadows reaching 20 units only: the far ground is lit.
		var shortShot = SampleRendering.Capture(Game().WithConfiguration("Ion:Rendering3D:ShadowDistance", "20"), Width, Height, frames: 30, backend: backend);

		// Shadow on the grass (dark, greenish grey) near the camera (the bottom third) and far down the avenue (just below
		// the horizon, which is at about the middle of the picture).
		static int Shadowed(Screenshot shot, int top, int bottom)
		{
			var count = 0;
			for (var y = top; y < bottom; y++)
			{
				for (var x = 0; x < shot.Width; x++)
				{
					var p = shot.GetPixel(x, y);
					if (p.G < 95 && p.G > 40 && p.R < 90 && p.B < 90) count++;
				}
			}

			return count;
		}

		int Near(Screenshot s) => Shadowed(s, (int)(Height * 2 / 3), (int)Height);
		int Far(Screenshot s) => Shadowed(s, (int)(Height * 0.51f), (int)(Height * 0.56f));
		Assert.True(Near(shot) > 2000, $"{Near(shot)} shadowed pixels near the camera");
		Assert.True(Far(shot) > 50, $"{Far(shot)} shadowed pixels far down the avenue");
		Assert.True(Far(shot) > Far(shortShot) * 3, $"{Far(shot)} far shadowed pixels at 150 units, {Far(shortShot)} at 20");
	}
}
