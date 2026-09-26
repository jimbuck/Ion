using System.Diagnostics;

using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering2D;
using Ion.Testing;

using Xunit;
using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Sprites100k.Tests;

/// <summary>
/// The 100k-sprite stress sample: correctness of the batching (one draw call per texture) under validation, and frame
/// times headless and windowed. The timings are reported, not gated (lavapipe renders on the CPU).
/// </summary>
public class StressTests(ITestOutputHelper output)
{
	private const uint Width = 1280, Height = 720;

	private static IonTestHost Host(int count) => new IonTestHost()
		.UseGame(b => SpritesApp.Configure(b), a => SpritesApp.Use(a))
		.WithConfiguration("Sprites:Count", count.ToString(System.Globalization.CultureInfo.InvariantCulture));

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void DrawsOneCallPerTextureUnderValidation()
	{
		var shot = SampleRendering.Capture(Host(100_000), Width, Height, 3, inspect: host =>
		{
			var stats = host.Get<SpriteBatch>().LastFrameStatistics;
			Assert.Equal(100_000, stats.Sprites);
			Assert.Equal(200_000, stats.Triangles);
			Assert.Equal(16, stats.DrawCalls);
		});

		// Sprites cover most of the 1280x720 target.
		var covered = 0;
		for (var y = 0; y < shot.Height; y += 8)
		{
			for (var x = 0; x < shot.Width; x += 8)
			{
				var p = shot.GetPixel(x, y);
				if (p.R + p.G + p.B > 3 * 0x30) covered++;
			}
		}

		Assert.True(covered > (shot.Width / 8) * (shot.Height / 8) / 2, $"{covered} covered samples");
	}

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void ReportsHeadlessFrameTime() => ReportHeadlessFrameTime(GraphicsBackend.Vulkan, "Vulkan on lavapipe");

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void ReportsHeadlessFrameTimeOnGles() => ReportHeadlessFrameTime(GraphicsBackend.OpenGLES, "OpenGL ES on llvmpipe");

	private void ReportHeadlessFrameTime(GraphicsBackend backend, string label)
	{
		const int frames = 60;
		using var host = Host(100_000).WithRendering(Width, Height).WithConfiguration("Ion:Graphics:PreferredBackend", backend.ToString());
		host.Step(5);
		var watch = Stopwatch.StartNew();
		host.Step(frames);
		var elapsed = watch.Elapsed.TotalMilliseconds / frames;
		var report = host.Get<StressSystem>().Report;
		var line = $"Headless (offscreen, {label}) 100k sprites: {elapsed:F2} ms/frame, {report.CpuRenderMilliseconds:F2} ms recording the sprites, {report.LastFrame.DrawCalls} draw calls.";
		output.WriteLine(line);
		Console.WriteLine(line);
		Assert.Equal(16, report.LastFrame.DrawCalls);
	}

	[WindowedVulkanFact, Trait(CATEGORY, E2E)]
	public void ReportsWindowedFrameTime()
	{
		var run = SampleWindowed.Run(b => SpritesApp.Configure(b), a => SpritesApp.Use(a), 120,
			("Ion:Graphics:Validation", "false"), ("Ion:Window:Width", "1280"), ("Ion:Window:Height", "720"));
		var line = $"Windowed (Xvfb, Vulkan on lavapipe) 100k sprites: {run.AverageFrameMilliseconds:F2} ms/frame average, {run.WorstFrameMilliseconds:F2} ms worst, {run.LastFrame.DrawCalls} draw calls, {run.LastFrame.Sprites} sprites.";
		output.WriteLine(line);
		Console.WriteLine(line);
		Assert.Equal(120, run.Frames);
		Assert.Equal(16, run.LastFrame.DrawCalls);
	}
}
