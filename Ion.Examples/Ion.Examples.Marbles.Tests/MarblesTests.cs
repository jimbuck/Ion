using System.Numerics;

using Arch.Core;

using Ion.Examples.Tests;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Physics3D;
using Ion.Extensions.Rendering3D;
using Ion.Testing;

using Xunit;

using static Ion.Tests.TestConstants;

namespace Ion.Examples.Marbles.Tests;

/// <summary>
/// The marbles sample, from its own <c>Program.cs</c>: the marbles land on the terrain's triangle-mesh collider (made from
/// the mesh the renderer draws), none falls through, and they come to rest on its surface; headless rendering on Vulkan
/// and OpenGL ES draws them.
/// </summary>
public class MarblesTests
{
	private const uint Width = 640, Height = 360;
	private static readonly QueryDescription Marbles = new QueryDescription().WithAll<Marble, Transform, RigidBody3D>();

	private static IonTestHost Game() => new IonTestHost().UseEntryPoint<Program>();

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheMarblesLandOnTheMeshAndRestOnItsSurface()
	{
		using var host = Game();
		host.Step(600);
		var world = host.Get<World>();
		var physics = host.Get<IPhysicsWorld3D>();
		var marbles = host.Get<MarblesSystem>();

		// None fell through the terrain (they would have been put back).
		Assert.Equal(0, marbles.Respawns);
		Assert.Equal(80, world.CountEntities(in Marbles));

		var onSurface = 0;
		var count = 0;
		foreach (ref var chunk in world.Query(in Marbles))
		{
			var transforms = chunk.GetSpan<Transform>();
			for (var i = 0; i < chunk.Count; i++)
			{
				count++;
				var position = transforms[i].Position;

				// Straight down from its center, only the terrain (its layer) is hit, and from above: a marble below the surface
				// would see its back side, which does not collide.
				Assert.True(physics.RayCast(position, -Vector3.UnitY, 50, out var hit, mask: Terrain.Layer), $"nothing under the marble at {position}");
				Assert.Equal(marbles.TerrainEntity, hit.Entity);
				Assert.True(hit.Distance > MarblesSystem.Radius * 0.85f, $"a marble at {position} is {hit.Distance} above the terrain");

				// The ones on the ground sit on the surface the renderer draws.
				if (MathF.Abs(position.Y - (Terrain.Height(position.X, position.Z) + MarblesSystem.Radius)) < 0.2f) onSurface++;
			}
		}

		Assert.True(onSurface > count / 2, $"{onSurface} of {count} marbles on the surface");
		Assert.True(marbles.Resting > count / 2, $"{marbles.Resting} of {count} marbles resting");
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheTerrainMeshIsTheHeightFunction()
	{
		var mesh = Terrain.CreateMesh();
		Assert.Equal(Terrain.Subdivisions * Terrain.Subdivisions * 2, mesh.TriangleCount);
		foreach (var p in mesh.Positions) Assert.Equal(Terrain.Height(p.X, p.Z), p.Y);

		// Front faces up, like the renderer's plane: the normal of every triangle points up.
		for (var t = 0; t < mesh.TriangleCount; t++)
		{
			var a = mesh.Positions[mesh.Indices[t * 3]];
			var b = mesh.Positions[mesh.Indices[t * 3 + 1]];
			var c = mesh.Positions[mesh.Indices[t * 3 + 2]];
			Assert.True(Vector3.Cross(b - a, c - a).Y > 0);
		}
	}

	[VulkanFact, Trait(CATEGORY, E2E)]
	public void RendersOnVulkan() => Renders(GraphicsBackend.Vulkan);

	[GlesFact, Trait(CATEGORY, E2E)]
	public void RendersOnGles() => Renders(GraphicsBackend.OpenGLES);

	private static void Renders(GraphicsBackend backend)
	{
		var shot = SampleRendering.Capture(Game(), Width, Height, frames: 240, inspect: host =>
		{
			var stats = host.Get<IRenderer3D>().LastFrameStatistics;
			// The terrain and the marbles (a few may be off screen).
			Assert.InRange(stats.Visible, 60, 81);
			Assert.Equal(0, host.Get<MarblesSystem>().Respawns);
		}, backend: backend);

		// Marbles in the picture: pixels of the palette's red and blue, which neither the terrain nor the sky has.
		var red = 0;
		var blue = 0;
		for (var y = 0; y < shot.Height; y++)
		{
			for (var x = 0; x < shot.Width; x++)
			{
				var p = shot.GetPixel(x, y);
				if (p.R > 150 && p.G < 110 && p.B < 100) red++;
				if (p.B > 150 && p.R < 90 && p.G < 170 && p.B > p.G + 30) blue++;
			}
		}

		Assert.True(red > 200, $"{red} red pixels");
		Assert.True(blue > 200, $"{blue} blue pixels");
	}
}
