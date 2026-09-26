using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Breakout.ECS.Tests;

/// <summary>
/// The Breakout ECS game rendered headless on the Vulkan backend (lavapipe on CI) at the size the game sets its window
/// to, compared with a committed golden image after the autopilot has played for two seconds.
/// </summary>
public class BreakoutRenderingTests
{
	/// <summary>The window size the game asks for (10 columns of 192 px blocks with 10 px gaps, and the rows plus paddle area).</summary>
	public const uint Width = 2030, Height = 984;

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void MatchesTheGoldenImageAfter120Frames() => MatchesTheGoldenImage(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void MatchesTheGoldenImageAfter120FramesOnGles() => MatchesTheGoldenImage(GraphicsBackend.OpenGLES);

	private static void MatchesTheGoldenImage(GraphicsBackend backend)
	{
		var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 60)).UseGame(b => BreakoutGame.Configure(b), a => BreakoutGame.Use(a));
		var shot = SampleRendering.Capture(host, Width, Height, 120, backend: backend, inspect: h =>
		{
			Assert.IsType<SpriteBatch>(h.Get<ISpriteBatch>());
			var stats = h.Get<SpriteBatch>().LastFrameStatistics;
			Assert.True(stats.Sprites > 100, $"{stats.Sprites} sprites");
			Assert.True(stats.DrawCalls < stats.Sprites, $"{stats.DrawCalls} draw calls for {stats.Sprites} sprites");
		});

		GoldenImage.AssertMatches(shot, RenderingEnvironment.GoldenPath("breakout_ecs_120.png"), tolerance: 8, maxMismatchRatio: 0.002);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnGlesWithoutErrors()
	{
		var run = SampleWindowed.Run(b => BreakoutGame.Configure(b), a => BreakoutGame.Use(a), 120, ("Ion:Graphics:PreferredBackend", "OpenGLES"));
		Assert.Equal(120, run.Frames);
		Assert.True(run.LastFrame.Sprites >= 100, $"{run.LastFrame.Sprites} sprites");
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void RunsWindowedOnVulkanWithoutValidationErrors()
	{
		var run = SampleWindowed.Run(b => BreakoutGame.Configure(b), a => BreakoutGame.Use(a), 240);
		Assert.Equal(240, run.Frames);
		Assert.True(run.LastFrame.Sprites >= 100, $"{run.LastFrame.Sprites} sprites");
	}
}
