using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Breakout.Tests;

/// <summary>
/// The Breakout sample rendered headless on the Vulkan backend (lavapipe on CI) at the size the game sets its window to,
/// compared with a committed golden image: blocks, paddle, the captured ball and the score.
/// </summary>
public class BreakoutRenderingTests
{
	/// <summary>The window size the game asks for.</summary>
	public const uint Width = 2030, Height = 984;

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void MatchesTheGoldenImage() => AssertGolden(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void MatchesTheGoldenImageOnGles() => AssertGolden(GraphicsBackend.OpenGLES);

	private static void AssertGolden(GraphicsBackend backend)
	{
		var host = new IonTestHost().UseGame(b => BreakoutApp.Configure(b), a => BreakoutApp.Use(a));
		var shot = SampleRendering.Capture(host, Width, Height, 30, backend: backend, inspect: h =>
		{
			var stats = h.Get<SpriteBatch>().LastFrameStatistics;
			// 100 blocks, the paddle, the ball and the glyphs of "Score:  0".
			Assert.Equal(102 + 7, stats.Sprites);
		});

		// The clear color is #333 and the first block row starts 10 px in.
		var background = shot.GetPixel(5, (int)Height - 5);
		Assert.True(background.MaxChannelDifference(new Rgba8(0x33, 0x33, 0x33, 255)) <= 1, $"background {background}");
		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("breakout_30.png"), tolerance: 8, maxMismatchRatio: 0.002);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnGlesWithoutErrors()
	{
		var run = SampleWindowed.Run(b => BreakoutApp.Configure(b), a => BreakoutApp.Use(a), 120, ("Ion:Graphics:PreferredBackend", "OpenGLES"));
		Assert.Equal(120, run.Frames);
		Assert.Equal(102 + 7, run.LastFrame.Sprites);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnVulkanWithoutValidationErrors()
	{
		var run = SampleWindowed.Run(b => BreakoutApp.Configure(b), a => BreakoutApp.Use(a), 240);
		Assert.Equal(240, run.Frames);
		Assert.Equal(102 + 7, run.LastFrame.Sprites);
	}
}
