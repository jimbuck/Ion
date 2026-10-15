using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs.Tests;

public class HierarchyTests
{
	private static void AssertClose(Vector2 expected, Vector2 actual) => Assert.True(Vector2.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

	private static void AssertClose(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

	[Fact]
	public void PropagatesPositionRotationAndScaleDownThe2DTree()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D(new Vector2(100, 0), MathF.PI / 2, new Vector2(2)));
		var child = world.Create(new Transform2D(new Vector2(10, 0), 0.25f));
		var grandchild = world.Create(new Transform2D(new Vector2(0, 5)));
		world.SetParent(child, parent);
		world.SetParent(grandchild, child);

		var system = new TransformPropagationSystem(world);
		system.Propagate();

		var root = world.Get<GlobalTransform2D>(parent);
		Assert.Equal(new Vector2(100, 0), root.Position);
		Assert.Equal(new Transform2D(new Vector2(100, 0), MathF.PI / 2, new Vector2(2)).ToMatrix(), root.Matrix);

		// The child's local (10, 0) scaled by 2 and turned a quarter: 20 units down (y grows downwards on screen).
		var middle = world.Get<GlobalTransform2D>(child);
		AssertClose(new Vector2(100, 20), middle.Position);
		Assert.Equal(MathF.PI / 2 + 0.25f, middle.Rotation, 5);
		Assert.Equal(new Vector2(2), middle.Scale);

		var leaf = world.Get<GlobalTransform2D>(grandchild);
		AssertClose(Vector2.Transform(new Vector2(0, 5), middle.Matrix), leaf.Position);
		AssertClose(leaf.Position, leaf.TransformPoint(Vector2.Zero));
		Assert.Equal(3, system.LastUpdated);
	}

	[Fact]
	public void PropagatesThe3DTreeAsMatrixProducts()
	{
		using var world = World.Create();
		var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
		var parent = world.Create(new Transform(new Vector3(0, 10, 0), rotation));
		var child = world.Create(new Transform(new Vector3(0, 0, -5)));
		world.SetParent(child, parent);

		new TransformPropagationSystem(world).Propagate();

		var global = world.Get<GlobalTransform>(child);
		AssertClose(new Vector3(-5, 10, 0), global.Position);
		Assert.Equal(new Transform(new Vector3(0, 0, -5)).ToMatrix() * world.Get<GlobalTransform>(parent).Matrix, global.Matrix);
	}

	[Fact]
	public void OnlyRecomputesWhatChanged()
	{
		using var world = World.Create();
		var roots = new List<Entity>();
		for (var r = 0; r < 3; r++)
		{
			var root = world.Create(new Transform2D(new Vector2(r * 10, 0)));
			roots.Add(root);
			for (var c = 0; c < 4; c++) world.SetParent(world.Create(new Transform2D(new Vector2(0, c))), root);
		}

		var system = new TransformPropagationSystem(world);
		system.Propagate();
		Assert.Equal(15, system.LastUpdated);
		Assert.Equal(15, system.LastVisited);

		system.Propagate();
		Assert.Equal(0, system.LastUpdated);
		Assert.Equal(15, system.LastVisited);

		// Moving a root recomputes it and its four children; moving a leaf recomputes only the leaf.
		world.Get<Transform2D>(roots[1]).Position.Y = 7;
		system.Propagate();
		Assert.Equal(5, system.LastUpdated);

		var leaf = world.GetChildren(roots[2])[3];
		world.Get<Transform2D>(leaf).Rotation = 1;
		system.Propagate();
		Assert.Equal(1, system.LastUpdated);
		Assert.Equal(new Vector2(20, 3), world.Get<GlobalTransform2D>(leaf).Position);

		// Reparenting invalidates the moved entity.
		world.SetParent(leaf, roots[0]);
		system.Propagate();
		Assert.Equal(1, system.LastUpdated);
		Assert.Equal(new Vector2(0, 3), world.Get<GlobalTransform2D>(leaf).Position);

		world.RemoveParent(leaf);
		system.Propagate();
		Assert.Equal(1, system.LastUpdated);
		Assert.Equal(new Vector2(0, 3), world.Get<GlobalTransform2D>(leaf).Position);
		Assert.Equal(3, world.GetChildren(roots[2]).Length);
	}

	[Fact]
	public void VisitsChildrenInTheOrderTheyWereAttached()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D());
		var a = world.Create(new Transform2D());
		var b = world.Create(new Transform2D());
		var c = world.Create(new Transform2D());
		world.SetParent(c, parent);
		world.SetParent(a, parent);
		world.SetParent(b, parent);

		Assert.Equal([c, a, b], world.GetChildren(parent).ToArray());

		world.SetParent(a, c);
		Assert.Equal([c, b], world.GetChildren(parent).ToArray());
		Assert.Equal([a], world.GetChildren(c).ToArray());
	}

	[Fact]
	public void RejectsCycles()
	{
		using var world = World.Create();
		var a = world.Create(new Transform2D());
		var b = world.Create(new Transform2D());
		world.SetParent(b, a);

		Assert.Throws<InvalidOperationException>(() => world.SetParent(a, b));
		Assert.Throws<InvalidOperationException>(() => world.SetParent(a, a));
	}

	[Fact]
	public void DestroyRecursiveRemovesTheSubtreeAndDetachesIt()
	{
		using var world = World.Create();
		var root = world.Create(new Transform2D());
		var middle = world.Create(new Transform2D());
		var leaf = world.Create(new Transform2D());
		var sibling = world.Create(new Transform2D());
		world.SetParent(middle, root);
		world.SetParent(leaf, middle);
		world.SetParent(sibling, root);

		world.DestroyRecursive(middle);

		Assert.False(world.IsAlive(middle));
		Assert.False(world.IsAlive(leaf));
		Assert.Equal([sibling], world.GetChildren(root).ToArray());
	}

	[Fact]
	public void AddsGlobalTransformsAndSpriteBoundsWhenMissing()
	{
		using var world = World.Create();
		var sprite = world.Create(new Transform2D(new Vector2(50, 50), 0, new Vector2(2)), new Sprite(Hosts.Texture(10, 20)));

		var system = new TransformPropagationSystem(world);
		system.Propagate();
		system.UpdateBounds();

		Assert.True(world.Get<GlobalTransform2D>(sprite).IsComputed);
		Assert.Equal(new Aabb2D(new Vector2(40, 30), new Vector2(60, 70)), world.Get<Aabb2D>(sprite));
	}

	[Fact]
	public void SetParentInsertsAtAnIndex()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D());
		var a = world.Create(new Transform2D());
		var b = world.Create(new Transform2D());
		var c = world.Create(new Transform2D());
		var d = world.Create(new Transform2D());
		world.SetParent(a, parent);
		world.SetParent(b, parent);
		world.SetParent(c, parent, 0);
		world.SetParent(d, parent, 99); // clamped: appended

		Assert.Equal([c, a, b, d], world.GetChildren(parent).ToArray());
		Assert.Equal(0, world.GetSiblingIndex(c));
		Assert.Equal(3, world.GetSiblingIndex(d));
		Assert.Equal(-1, world.GetSiblingIndex(parent));
	}

	[Fact]
	public void SetSiblingIndexMovesAChildAmongItsSiblings()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D());
		var a = world.Create(new Transform2D());
		var b = world.Create(new Transform2D());
		var c = world.Create(new Transform2D());
		foreach (var e in new[] { a, b, c }) world.SetParent(e, parent);

		world.SetSiblingIndex(c, 0);
		Assert.Equal([c, a, b], world.GetChildren(parent).ToArray());
		world.SetSiblingIndex(c, 2);
		Assert.Equal([a, b, c], world.GetChildren(parent).ToArray());
		world.SetSiblingIndex(a, -5); // clamped to the front
		Assert.Equal([a, b, c], world.GetChildren(parent).ToArray());
		world.SetSiblingIndex(a, 1);
		Assert.Equal([b, a, c], world.GetChildren(parent).ToArray());

		// SetParent to the same parent with an index moves; without one it keeps the place.
		world.SetParent(c, parent, 0);
		Assert.Equal([c, b, a], world.GetChildren(parent).ToArray());
		world.SetParent(c, parent);
		Assert.Equal([c, b, a], world.GetChildren(parent).ToArray());
		Assert.True(world.TryGetParent(c, out var found));
		Assert.Equal(parent, found);

		// A root has no siblings: nothing happens.
		world.SetSiblingIndex(parent, 3);
		Assert.Equal(-1, world.GetSiblingIndex(parent));
	}

	[Fact]
	public void PropagationFollowsTheReorderedChildren()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D(new Vector2(10, 0)));
		var a = world.Create(new Transform2D(new Vector2(1, 0)));
		var b = world.Create(new Transform2D(new Vector2(2, 0)));
		world.SetParent(a, parent);
		world.SetParent(b, parent);
		world.SetSiblingIndex(b, 0);

		new TransformPropagationSystem(world).Propagate();
		AssertClose(new Vector2(11, 0), world.Get<GlobalTransform2D>(a).Position);
		AssertClose(new Vector2(12, 0), world.Get<GlobalTransform2D>(b).Position);
	}

	[Fact]
	public void APlainDestroyOfAParentTakesItsSubtreeAtTheNextPropagation()
	{
		using var world = World.Create();
		var root = world.Create(new Transform2D());
		var parent = world.Create(new Transform2D());
		var child = world.Create(new Transform2D());
		var grandchild = world.Create(new Transform2D());
		var sibling = world.Create(new Transform2D());
		world.SetParent(parent, root);
		world.SetParent(sibling, root);
		world.SetParent(child, parent);
		world.SetParent(grandchild, child);
		var system = new TransformPropagationSystem(world);
		system.Propagate();
		Assert.Equal(0, system.LastRepaired);

		world.Destroy(parent);
		system.Propagate();

		Assert.Equal(2, system.LastRepaired);
		Assert.False(world.IsAlive(child));
		Assert.False(world.IsAlive(grandchild));
		Assert.Equal([sibling], world.GetChildren(root).ToArray());
		Assert.Equal(2, world.Size);

		// Nothing left to repair.
		system.Propagate();
		Assert.Equal(0, system.LastRepaired);
	}

	[Fact]
	public void APlainDestroyOfAChildLeavesItsParentsChildren()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform());
		var a = world.Create(new Transform());
		var b = world.Create(new Transform());
		world.SetParent(a, parent);
		world.SetParent(b, parent);
		var system = new TransformPropagationSystem(world);
		system.Propagate();

		world.Destroy(a);
		system.Propagate();

		Assert.Equal([b], world.GetChildren(parent).ToArray());
		Assert.Equal(0, world.GetSiblingIndex(b));
		Assert.True(world.IsAlive(parent));
	}

	[Fact]
	public void RepairHierarchyRepairsWithoutThePropagation()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform2D());
		var child = world.Create(new Transform2D());
		var other = world.Create(new Transform2D());
		var leaf = world.Create(new Transform2D());
		world.SetParent(child, parent);
		world.SetParent(leaf, other);

		world.Destroy(parent);
		world.Destroy(leaf);
		Assert.Equal(1, world.RepairHierarchy());

		Assert.False(world.IsAlive(child));
		Assert.Equal(0, world.GetChildren(other).Length);
		Assert.Equal(0, world.RepairHierarchy());
	}
}
