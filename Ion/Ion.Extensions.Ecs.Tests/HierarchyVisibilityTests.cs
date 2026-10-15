using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Graphics;
using Ion.Extensions.Rendering3D;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>Hiding an entity hides everything under it, in 2D and 3D.</summary>
public class HierarchyVisibilityTests
{
	[Fact]
	public void HidingASpritesParentHidesItsSubtree()
	{
		using var host = Hosts.Ecs(rendering: true);
		var world = host.Get<World>();
		var texture = Hosts.Texture();
		var root = world.Create(new Transform2D(new Vector2(100, 100)), new Sprite(texture));
		var middle = world.Create(new Transform2D(new Vector2(10, 0)), new Sprite(texture));
		var leaf = world.Create(new Transform2D(new Vector2(10, 0)), new Sprite(texture));
		world.SetParent(middle, root);
		world.SetParent(leaf, middle);
		int Drawn()
		{
			host.Step();
			return host.SpriteBatch.LastFrame.Sprites;
		}

		Assert.Equal(3, Drawn());

		// Hiding the root hides all three; showing it shows them again.
		root.Add(new Hidden());
		Assert.Equal(0, Drawn());
		Assert.True(world.Get<GlobalTransform2D>(leaf).IsHidden);
		root.Remove<Hidden>();
		Assert.Equal(3, Drawn());
		Assert.False(world.Get<GlobalTransform2D>(leaf).IsHidden);

		// Hiding the middle hides it and the leaf, not the root.
		middle.Add(new Hidden());
		Assert.Equal(1, Drawn());

		// A leaf hidden by itself stays hidden when its ancestors show.
		leaf.Add(new Hidden());
		middle.Remove<Hidden>();
		Assert.Equal(2, Drawn());

		// Moved out of a hidden subtree, an entity shows (it is hidden by where it is, not by a tag).
		leaf.Remove<Hidden>();
		root.Add(new Hidden());
		world.RemoveParent(middle);
		Assert.Equal(2, Drawn());
		Assert.False(world.Get<GlobalTransform2D>(middle).IsHidden);
	}

	[Fact]
	public void HidingAModelsRootHidesItsMeshesAndLights()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		var (mesh, material) = Hosts3D.CubeAssets(renderer);
		Hosts3D.Camera(world);
		var root = world.Create(new Transform(Vector3.Zero), new MeshRenderer(mesh, material));
		var child = world.Create(new Transform(new Vector3(1, 0, 0)), new MeshRenderer(mesh, material));
		var lamp = world.Create(new Transform(new Vector3(0, 2, 0)), new PointLight());
		world.SetParent(child, root);
		world.SetParent(lamp, child);

		host.Step();
		Assert.Equal(2, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, renderer.LastFrameStatistics.Lights);

		root.Add(new Hidden());
		host.Step();
		Assert.Equal(0, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(0, renderer.LastFrameStatistics.Lights);

		root.Remove<Hidden>();
		child.Add(new Hidden());
		host.Step();
		Assert.Equal(1, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(0, renderer.LastFrameStatistics.Lights);

		child.Remove<Hidden>();
		host.Step();
		Assert.Equal(2, renderer.LastFrameStatistics.Submitted);
		Assert.Equal(1, renderer.LastFrameStatistics.Lights);
	}

	[Fact]
	public void AHiddenCamerasSubtreeDoesNotRender()
	{
		using var host = Hosts3D.Ecs3D();
		var world = host.Get<World>();
		var renderer = host.Get<Renderer3D>();
		var rig = world.Create(new Transform(new Vector3(0, 0, 10)));
		var camera = world.Create(new Transform(), new Camera());
		world.SetParent(camera, rig);
		host.Step();
		Assert.Equal(1, renderer.LastFrameStatistics.Views);

		rig.Add(new Hidden());
		host.Step();
		Assert.Equal(0, renderer.LastFrameStatistics.Views);
	}
}
