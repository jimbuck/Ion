using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs.Tests;

public class TransformInterpolationTests
{
	private static void AssertClose(Vector3 expected, Vector3 actual) => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"expected {expected}, got {actual}");

	/// <summary>Two fixed steps that move <paramref name="entity"/> from x = 0 to x = 10, then the Render pass at <paramref name="alpha"/>.</summary>
	private static TransformPropagationSystem StepTwiceAndRender(World world, Entity entity, float alpha)
	{
		var system = new TransformPropagationSystem(world);
		world.Get<Transform>(entity).Position = Vector3.Zero;
		system.RecordInterpolation(new GameTime());
		world.Get<Transform>(entity).Position = new Vector3(10, 0, 0);
		system.RecordInterpolation(new GameTime());
		system.PropagateBeforeRender(new GameTime { Alpha = alpha });
		return system;
	}

	[Fact]
	public void TheRenderPassDrawsBetweenTheLastTwoFixedSteps()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(), new TransformInterpolation());

		StepTwiceAndRender(world, entity, 0.25f);

		AssertClose(new Vector3(2.5f, 0, 0), world.Get<GlobalTransform>(entity).Position);
		Assert.Equal(new Vector3(10, 0, 0), world.Get<Transform>(entity).Position);   // the simulated transform is untouched
	}

	[Fact]
	public void TheLastPassComputesTheSimulatedTransform()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(), new TransformInterpolation());

		var system = StepTwiceAndRender(world, entity, 0.25f);
		system.PropagateLast(new GameTime());

		AssertClose(new Vector3(10, 0, 0), world.Get<GlobalTransform>(entity).Position);
	}

	[Fact]
	public void ChildrenFollowTheirInterpolatedParent()
	{
		using var world = World.Create();
		var parent = world.Create(new Transform(), new TransformInterpolation());
		var child = world.Create(new Transform(new Vector3(0, 1, 0)));
		world.SetParent(child, parent);

		StepTwiceAndRender(world, parent, 0.5f);

		AssertClose(new Vector3(5, 1, 0), world.Get<GlobalTransform>(child).Position);
	}

	[Fact]
	public void RotationsAreSlerped()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(), new TransformInterpolation());
		var system = new TransformPropagationSystem(world);
		system.RecordInterpolation(new GameTime());
		world.Get<Transform>(entity).Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 2);
		system.RecordInterpolation(new GameTime());

		system.PropagateBeforeRender(new GameTime { Alpha = 0.5f });

		var expected = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI / 4);
		var matrix = world.Get<GlobalTransform>(entity).Matrix;
		AssertClose(Vector3.Transform(Vector3.UnitX, expected), Vector3.TransformNormal(Vector3.UnitX, matrix));
	}

	[Fact]
	public void ATransformChangedSinceTheLastRecordIsDrawnAsItIs()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(), new TransformInterpolation());
		var system = new TransformPropagationSystem(world);
		system.RecordInterpolation(new GameTime());
		world.Get<Transform>(entity).Position = new Vector3(10, 0, 0);
		system.RecordInterpolation(new GameTime());
		world.Get<Transform>(entity).Position = new Vector3(50, 0, 0);   // moved in Update

		system.PropagateBeforeRender(new GameTime { Alpha = 0.5f });

		AssertClose(new Vector3(50, 0, 0), world.Get<GlobalTransform>(entity).Position);
	}

	[Fact]
	public void SnapDrawsATeleportedEntityWhereItWasPut()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(), new TransformInterpolation());
		var system = new TransformPropagationSystem(world);
		system.RecordInterpolation(new GameTime());
		world.Get<Transform>(entity).Position = new Vector3(100, 0, 0);
		world.Get<TransformInterpolation>(entity).Snap();
		system.RecordInterpolation(new GameTime());

		system.PropagateBeforeRender(new GameTime { Alpha = 0.5f });

		AssertClose(new Vector3(100, 0, 0), world.Get<GlobalTransform>(entity).Position);
	}

	[Fact]
	public void BeforeAnyRecordTheTransformIsDrawnAsItIs()
	{
		using var world = World.Create();
		var entity = world.Create(new Transform(new Vector3(3, 0, 0)), new TransformInterpolation());

		new TransformPropagationSystem(world).PropagateBeforeRender(new GameTime { Alpha = 0.5f });

		AssertClose(new Vector3(3, 0, 0), world.Get<GlobalTransform>(entity).Position);
	}
}
