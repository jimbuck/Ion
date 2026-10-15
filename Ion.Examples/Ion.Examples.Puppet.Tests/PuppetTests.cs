using System.Numerics;

using Arch.Core;

using Ion.Examples.Tests;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Puppet.Tests;

/// <summary>
/// The puppet sample, from its own <c>Program.cs</c>: three skinned puppets spawned from the glTF, each with an animator
/// playing its clip, the middle one cross-fading on schedule and the right one walking its circle; headless rendering on
/// Vulkan and OpenGL ES draws them.
/// </summary>
public class PuppetTests
{
	private const uint Width = 640, Height = 360;

	private static IonTestHost Game() => new IonTestHost().UseEntryPoint<Program>();

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ThePuppetsPlayTheirClipsAndTheMiddleOneCrossFades()
	{
		using var host = Game();
		host.Step(2);
		var puppets = host.Get<PuppetSystem>();
		var world = host.Get<World>();
		Animator Of(Entity e) => world.Get<Animator>(e);

		Assert.Equal(12, puppets.Model!.Skins[0].JointCount);
		Assert.Equal("Idle", Of(puppets.Idler).Player.Clip?.Name);
		Assert.Equal("Idle", Of(puppets.Performer).Player.Clip?.Name);
		Assert.Equal("Walk", Of(puppets.Walker).Player.Clip?.Name);

		// After the first clip the middle puppet fades into the second, then plays it alone.
		var frames = (int)(PuppetSystem.ClipSeconds * 60);
		host.Step(frames);
		var performer = Of(puppets.Performer).Player;
		Assert.Equal(1, puppets.Fades);
		Assert.Equal("Walk", performer.Next?.Name ?? performer.Clip?.Name);
		host.Step((int)(PuppetSystem.FadeSeconds * 60) + 2);
		Assert.Equal("Walk", performer.Clip?.Name);
		Assert.Null(performer.Next);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheWalkerMovesItsLegsAndGoesRoundItsCircle()
	{
		using var host = Game();
		host.Step(1);
		var puppets = host.Get<PuppetSystem>();
		var world = host.Get<World>();
		var player = world.Get<Animator>(puppets.Walker).Player;
		var knee = player.Skeleton.IndexOf("LowerLeg.L");
		var before = player.JointMatrices[knee].Translation;
		var start = world.Get<Transform>(puppets.Walker).Position;

		host.Step(15);
		var after = player.JointMatrices[knee].Translation;
		Assert.True(Vector3.Distance(before, after) > 0.05f, $"the knee stayed at {before}");
		var position = world.Get<Transform>(puppets.Walker).Position;
		Assert.True(Vector3.Distance(start, position) > 0.2f, $"the walker stayed at {start}");
		Assert.Equal(PuppetSystem.WalkRadius, Vector2.Distance(new Vector2(position.X, position.Z), new Vector2(3.4f, -2.4f)), 3);
	}

	[VulkanFact, Trait(CATEGORY, E2E)]
	public void RendersOnVulkan() => Renders(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, E2E)]
	public void RendersOnGles() => Renders(GraphicsBackend.OpenGLES);

	private static void Renders(GraphicsBackend backend)
	{
		var shot = SampleRendering.Capture(Game(), Width, Height, frames: 120, inspect: host =>
		{
			var stats = host.Get<IRenderer3D>().LastFrameStatistics;
			// The ground and the three puppets, and the puppets cast shadows.
			Assert.Equal(4, stats.Visible);
			Assert.True(stats.ShadowCasters >= 3, $"{stats.ShadowCasters} shadow casters");
		}, backend: backend);

		// The shirts in the picture: a saturated blue that neither the sky nor the ground has.
		var shirt = 0;
		for (var y = 0; y < shot.Height; y++)
		{
			for (var x = 0; x < shot.Width; x++)
			{
				var p = shot.GetPixel(x, y);
				if (p.B > 140 && p.B - p.R > 70) shirt++;
			}
		}

		Assert.True(shirt > 1500, $"{shirt} shirt pixels");
	}
}
