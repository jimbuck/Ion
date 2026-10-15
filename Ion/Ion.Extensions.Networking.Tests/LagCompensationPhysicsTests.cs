using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;
using Ion.Extensions.Networking;
using Ion.Extensions.Physics2D;
using Ion.Extensions.Physics3D;
using Ion.Testing;

using static Ion.Tests.TestConstants;

// The engine transforms are replicated in these tests, as a game opts them in.
[assembly: ReplicateComponent(typeof(Transform2D))]
[assembly: ReplicateComponent(typeof(Transform))]

namespace Ion.Extensions.Networking.Tests;

/// <summary>A target that slides right by one unit a tick (2D and 3D), on the server.</summary>
public sealed class SlidingTargetSystem(World world)
{
	private static readonly QueryDescription Targets2D = new QueryDescription().WithAll<Transform2D, Collider2D>();
	private static readonly QueryDescription Targets3D = new QueryDescription().WithAll<Transform, Collider3D>();

	[FixedUpdate]
	public void Slide(GameTime dt)
	{
		world.Query(in Targets2D, (ref Transform2D transform) => transform.Position += new Vector2(1, 0));
		world.Query(in Targets3D, (ref Transform transform) => transform.Position += new Vector3(1, 0, 0));
	}
}

/// <summary>
/// Lag compensation with physics: inside <see cref="INetworkWorld.WithWorldAtTick"/> the physics bodies are where the
/// entities were at that tick, so a ray cast hits the target where the shooter saw it, not where it is now.
/// </summary>
[Trait(CATEGORY, INTEGRATION)]
public class LagCompensationPhysicsTests
{
	private static IonTestHost Server(bool threeD) => new IonTestHost()
		.WithArgs("--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0", "--Ion:Network:MaxRewindTicks=30")
		.UseGame(
			builder =>
			{
				builder.Services.AddIon(builder.Configuration);
				builder.Services.AddEcs().AddNetworking(builder.Configuration).AddLoopbackTransport(new LoopbackNetwork()).AddSingleton<SlidingTargetSystem>();
				if (threeD) builder.Services.AddPhysics3D(configure: c => c.GravityY = 0);
				else builder.Services.AddPhysics2D(configure: c => c.GravityY = 0);
			},
			app =>
			{
				app.UseIon().UseEcs().UseNetworking().UseSystem<SlidingTargetSystem>();
				if (threeD) app.UsePhysics3D();
				else app.UsePhysics2D();
			});

	[Fact]
	public void A2DRayCastInsideTheRewindHitsWhereTheTargetWas()
	{
		using var host = Server(threeD: false);
		host.Start();
		var world = host.Get<World>();
		var network = host.Get<INetworkWorld>();
		var physics = host.Get<IPhysicsWorld2D>();
		var target = world.Create(new Transform2D(Vector2.Zero), Collider2D.Circle(2f), network.Allocate(NetworkPeer.Server));
		host.Step(40);

		var now = world.Get<Transform2D>(target).Position.X;
		var then = network.CurrentTick - 20;
		var x = 0f;
		network.WithWorldAtTick(then, w => x = w.Get<Transform2D>(target).Position.X);
		Assert.True(now - x >= 19, $"the target was at {x}, it is at {now}");

		// A vertical ray through where the target was: it hits inside the rewind, and misses outside it.
		bool Hits(float at) => physics.RayCast(new Vector2(at, -50), new Vector2(0, 100), out var hit) && hit.Entity == target;
		var hitThen = false;
		network.WithWorldAtTick(then, _ => hitThen = Hits(x));
		Assert.True(hitThen, "no hit where the target was");
		Assert.False(Hits(x), "the body stayed rewound");
		Assert.True(Hits(now), "the body is not back where the target is");

		// And the physics world steps on from the present.
		host.Step(1);
		Assert.Equal(now + 1, world.Get<Transform2D>(target).Position.X);
		Assert.True(Hits(now + 1));
	}

	[Fact]
	public void A3DRayCastInsideTheRewindHitsWhereTheTargetWas()
	{
		using var host = Server(threeD: true);
		host.Start();
		var world = host.Get<World>();
		var network = host.Get<INetworkWorld>();
		var physics = host.Get<IPhysicsWorld3D>();
		var target = world.Create(new Transform(Vector3.Zero), Collider3D.Sphere(2f), network.Allocate(NetworkPeer.Server));
		host.Step(40);

		var now = world.Get<Transform>(target).Position.X;
		var then = network.CurrentTick - 20;
		var x = 0f;
		network.WithWorldAtTick(then, w => x = w.Get<Transform>(target).Position.X);
		Assert.True(now - x >= 19, $"the target was at {x}, it is at {now}");

		bool Hits(float at) => physics.RayCast(new Vector3(at, 50, 0), -Vector3.UnitY, 100, out var hit) && hit.Entity == target;
		var hitThen = false;
		network.WithWorldAtTick(then, _ => hitThen = Hits(x));
		Assert.True(hitThen, "no hit where the target was");
		Assert.False(Hits(x), "the body stayed rewound");
		Assert.True(Hits(now), "the body is not back where the target is");
	}
}
