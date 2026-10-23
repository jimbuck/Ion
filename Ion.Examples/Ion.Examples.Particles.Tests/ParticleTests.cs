using System.Numerics;

using Ion.Examples.Tests;
using Ion.Extensions.Graphics;
using Ion.Extensions.Graphics.GLES;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Particles.Tests;

/// <summary>
/// The GPU particles sample, run from its own <c>Program.cs</c>: the compute pass steps the particles on the GPU (read back
/// and checked), the draw shows them, on Vulkan and OpenGL ES under validation; without compute or a GPU it runs and
/// draws nothing.
/// </summary>
public class ParticleTests
{
	private const uint Width = 320, Height = 180;
	private const int Count = 4096;

	private static IonTestHost Host() => new IonTestHost()
		.UseEntryPoint<Program>()
		.WithConfiguration("Particles:Count", Count.ToString(System.Globalization.CultureInfo.InvariantCulture))
		.WithConfiguration("Particles:Size", "0.03");

	[VulkanFact, Trait(CATEGORY, INTEGRATION)]
	public void SimulatesAndDrawsOnVulkan() => SimulatesAndDraws(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void SimulatesAndDrawsOnGles() => SimulatesAndDraws(GraphicsBackend.OpenGLES);

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void SimulatesAndDrawsOnGlesEs31() => SimulatesAndDraws(GraphicsBackend.OpenGLES, ("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es31)));

	[GlesFact, Trait(CATEGORY, INTEGRATION)]
	public void WithoutComputeTheSampleRunsAndDrawsNothing()
	{
		var host = Host().WithConfiguration("Ion:Graphics:Gles:MaxFeatureLevel", nameof(GlesFeatureLevel.Es30));
		var shot = SampleRendering.Capture(host, Width, Height, 5, inspect: h => Assert.Null(h.Get<ParticleSystem>().Particles), backend: GraphicsBackend.OpenGLES);
		Assert.Equal(0, Lit(shot));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void WithoutAGpuTheSampleRuns()
	{
		using var host = Host();
		host.Step(5);
		Assert.Null(host.Get<ParticleSystem>().Particles);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheSeedSpreadsLivesAndStartsAboveTheFloor()
	{
		var particles = ParticleSystem.Seed(Count, 1, 16f / 9f);
		Assert.All(particles, p => Assert.InRange(p.Life, 0.001f, 1f));
		Assert.All(particles, p => Assert.True(p.Position.Y >= -1f, $"{p.Position}"));
		// Lives are spread, so respawns are steady rather than all at once.
		Assert.InRange(particles.Average(p => p.Life), 0.4f, 0.6f);
		Assert.Equal(particles, ParticleSystem.Seed(Count, 1, 16f / 9f));
	}

	private static void SimulatesAndDraws(GraphicsBackend backend, params (string Key, string Value)[] settings)
	{
		var host = Host();
		foreach (var (key, value) in settings) host.WithConfiguration(key, value);
		Particle[] before = [], after = [];
		var shot = SampleRendering.Capture(host, Width, Height, 1, inspect: h =>
		{
			var gpu = h.Get<ParticleSystem>().Particles;
			Assert.NotNull(gpu);
			before = gpu.Read();
			h.Step(30);
			after = gpu.Read();
		}, backend: backend);

		// The compute pass ran: every particle is alive, above the floor and finite, and the particles moved and aged
		// (or respawned) between the reads.
		Assert.Equal(Count, after.Length);
		Assert.All(after, p =>
		{
			Assert.InRange(p.Life, 0f, 1f);
			Assert.True(float.IsFinite(p.State.X) && float.IsFinite(p.State.Y), $"{p.State}");
			Assert.True(p.Position.Y >= -1.0001f, $"{p.Position}");
		});
		var moved = 0;
		for (var i = 0; i < Count; i++)
		{
			if (Vector2.Distance(before[i].Position, after[i].Position) > 1e-3f) moved++;
		}

		Assert.True(moved > Count * 9 / 10, $"{moved} of {Count} particles moved");
		// Some respawned: their life went up (they were seeded with lives down to 0.001 and 30 frames take a sixth of a life).
		Assert.Contains(Enumerable.Range(0, Count), i => after[i].Life > before[i].Life);

		// The draw ran: a good part of the image is lit by the additive dots (the screenshot is of the frame before the
		// second read; the first capture covers the rest).
		Assert.True(Lit(shot) > (int)(Width * Height / 50), $"{Lit(shot)} lit pixels");
	}

	/// <summary>Pixels clearly brighter than the clear color (0x0a0a12).</summary>
	private static int Lit(Screenshot shot)
	{
		var lit = 0;
		for (var y = 0; y < shot.Height; y++)
		{
			for (var x = 0; x < shot.Width; x++)
			{
				var p = shot.GetPixel(x, y);
				if (p.R + p.G + p.B > 0x60) lit++;
			}
		}

		return lit;
	}
}
