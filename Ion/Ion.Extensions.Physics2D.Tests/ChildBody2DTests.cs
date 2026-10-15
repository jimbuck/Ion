using Arch.Core;
using Arch.Core.Extensions;

using Ion.Extensions.Ecs;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Physics2D.Tests;

/// <summary>Rigid bodies on child entities: simulated at their world pose, written back relative to their parent.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class ChildBody2DTests
{
	private static Vector2 WorldPosition(World world, Entity entity) => world.Get<GlobalTransform2D>(entity).Position;

	[Fact]
	public void ADynamicChildFallsInWorldSpaceAndIsWrittenBackRelativeToItsParent()
	{
		using var host = Hosts.Physics();
		var world = host.Get<World>();
		world.Floor(y: 10f);

		// A turned, scaled group (not a body) holding a ball.
		var group = world.Create(new Transform2D(new Vector2(5, 0), MathF.PI / 2, new Vector2(2, 2)));
		var ball = world.Create(new Transform2D(new Vector2(1, 0)), Collider2D.Circle(0.5f), RigidBody2D.Dynamic());
		world.SetParent(ball, group);
		host.Step();

		// (1, 0) under the group is (5, 2) in the world: the ball starts there, not at (1, 0).
		Assert.InRange(WorldPosition(world, ball).X, 4.99f, 5.01f);
		Assert.InRange(WorldPosition(world, ball).Y, 1.9f, 2.2f);

		// It rests on the floor (top at 9.5) in world space; its local transform says so relative to the group.
		host.Step(300);
		var resting = WorldPosition(world, ball);
		Assert.InRange(resting.Y, 8.9f, 9.05f);
		Assert.InRange(resting.X, 4.95f, 5.05f);
		var local = ball.Get<Transform2D>().Position;
		// World y = 0 + 2 * local x (turned a quarter, scaled by 2): local x is about 4.5.
		Assert.InRange(local.X, 4.45f, 4.53f);
		Assert.Equal(Vector2.One, ball.Get<Transform2D>().Scale);
	}

	[Fact]
	public void ADynamicChildAtRestStaysPutAndFollowsAParentTheGameMoves()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var group = world.Create(new Transform2D(new Vector2(10, 10)));
		var box = world.Create(new Transform2D(new Vector2(2, 0)), Collider2D.Box(Vector2.One), RigidBody2D.Dynamic());
		world.SetParent(box, group);
		host.Step(10);

		// Not teleported by rounding every step: still asleep-able, still where it was.
		Assert.Equal(new Vector2(2, 0), box.Get<Transform2D>().Position);
		Assert.Equal(new Vector2(12, 10), WorldPosition(world, box));

		// The game moves the group: the body moves with it.
		group.Get<Transform2D>().Position = new Vector2(30, 10);
		host.Step();
		Assert.InRange(WorldPosition(world, box).X, 31.99f, 32.01f);
		Span<Entity> found = stackalloc Entity[2];
		Assert.Equal(1, host.Get<IPhysicsWorld2D>().OverlapPoint(new Vector2(32, 10), found));
		Assert.Equal(box, found[0]);
	}

	[Fact]
	public void AKinematicChildIsCarriedByItsMovingParentAndPushesBodies()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();

		// A moving platform (a dynamic body sliding right) carrying a kinematic paddle on its front.
		var platform = world.Create(new Transform2D(new Vector2(0, 0)), Collider2D.Box(new Vector2(2, 2)), RigidBody2D.Dynamic(new Vector2(5, 0)) with { GravityScale = 0 });
		var paddle = world.Create(new Transform2D(new Vector2(3, 0)), Collider2D.Box(new Vector2(0.5f, 2)), RigidBody2D.Kinematic());
		world.SetParent(paddle, platform);
		var ball = world.Ball(new Vector2(8, 0));
		host.Step(60);

		// The platform went about 5 units right; the paddle went with it, two units further on.
		var platformX = platform.Get<Transform2D>().Position.X;
		Assert.InRange(platformX, 4f, 5.5f);
		Assert.InRange(WorldPosition(world, paddle).X - platformX, 2.9f, 3.1f);
		Assert.InRange(paddle.Get<Transform2D>().Position.X, 2.9f, 3.1f);

		// And it pushed the ball ahead of it.
		Assert.True(ball.Get<Transform2D>().Position.X > 8.5f, $"{ball.Get<Transform2D>().Position}");
	}

	[Fact]
	public void PartsUnderAScaledBodyAreWhereTheyAreDrawn()
	{
		using var host = Hosts.Physics(c => c.GravityY = 0);
		var world = host.Get<World>();
		var body = world.Create(new Transform2D(new Vector2(0, 0), 0, new Vector2(3, 3)), RigidBody2D.Static());
		var part = world.Create(new Transform2D(new Vector2(2, 0)), Collider2D.Box(Vector2.One));
		world.SetParent(part, body);
		host.Step(2);

		// Drawn at (6, 0): the body's scale scales its children's positions (the collider itself is not scaled).
		Assert.Equal(new Vector2(6, 0), WorldPosition(world, part));
		Span<Entity> found = stackalloc Entity[2];
		Assert.Equal(1, host.Get<IPhysicsWorld2D>().OverlapPoint(new Vector2(6, 0), found));
		Assert.Equal(0, host.Get<IPhysicsWorld2D>().OverlapPoint(new Vector2(2, 0), found));
	}
}
