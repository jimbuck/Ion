using System.Reflection;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>[Query] methods with every kind of parameter (expanded by the generator: this project is compiled with it).</summary>
public sealed partial class MovementSystem(Journal journal)
{
	public int Moved;

	[Update, Query, None<Frozen>]
	private void Move(ref Transform2D transform, in Velocity velocity, [Data] in float dt)
	{
		transform.Position += velocity.Value * dt;
		Moved++;
	}

	[Update(Order = 10), Query, All<Marker>]
	private void Report(Entity entity, in Health health, GameTime time, World world)
	{
		journal.Add($"{entity.Id}:{health.Value}:{world.Size}:{time is not null}");
	}

	[Update(Order = 20), Query, Any<Frozen, Marker>]
	private void Heal(ref Health health, Commands commands, Entity entity)
	{
		health = new Health(health.Value + 1);
		if (health.Value > 3) commands.Destroy(entity);
	}

	[Last, Query]
	private static void Count(in Transform2D transform) => StaticCounter++;

	public static int StaticCounter;
}

/// <summary>A query that makes a structural change on the world directly, through a helper the generator cannot see.</summary>
public sealed partial class CarelessSystem(World world)
{
	[Update, Query]
	private void Spawn(in Health health) => SpawnAnother();

	private void SpawnAnother() => world.Create(new Health(0));
}

public class QueryTests
{
	private static Ion.Testing.IonTestHost Host(bool generatedRegistration)
	{
		MovementSystem.StaticCounter = 0;
		var host = Hosts.Ecs().Configure(s => s.AddJournal());
		// Generated registration: UseSystem<T>() intercepted in this assembly (pre-bound, the expansion bound directly).
		// Reflection registration: WithSystem(Type) planned by reflection, which finds the expansion through [ExpandedStep].
		return generatedRegistration
			? host.Configure(s => s.AddSingleton<MovementSystem>()).ConfigureApp(app => app.UseSystem<MovementSystem>())
			: host.WithSystem(typeof(MovementSystem));
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void BindsComponentsEntityDataTimeCommandsAndWorld(bool generatedRegistration)
	{
		using var host = Host(generatedRegistration);
		var world = host.Get<World>();
		var moving = world.Create(new Transform2D(), new Velocity(new Vector2(60, 0)));
		var frozen = world.Create(new Transform2D(), new Velocity(new Vector2(60, 0)), new Frozen());
		var marked = world.Create(new Health(1), new Marker());

		host.Step();

		var system = host.Get<MovementSystem>();
		Assert.Equal(1, system.Moved);
		Assert.Equal(1f, world.Get<Transform2D>(moving).Position.X, 3);
		Assert.Equal(0f, world.Get<Transform2D>(frozen).Position.X);
		Assert.Equal([$"{marked.Id}:1:3:True"], host.Get<Journal>().Entries);
		Assert.Equal(2, world.Get<Health>(marked).Value);
		Assert.Equal(2, MovementSystem.StaticCounter);

		host.Step(2);
		Assert.False(world.IsAlive(marked));
	}

	[Fact]
	public void TheExpansionRunsUnderTheQueryMethodsName()
	{
		using var host = Host(generatedRegistration: true);
		var printed = host.Application.PrintSchedule();

		Assert.Contains("MovementSystem.Move", printed);
		Assert.Contains("MovementSystem.Report", printed);
		Assert.DoesNotContain("__IonQuery", printed);

		var expansion = typeof(MovementSystem).GetMethod("__IonQuery_Move", BindingFlags.Public | BindingFlags.Instance);
		Assert.NotNull(expansion);
		Assert.Equal("Move", expansion!.GetCustomAttribute<ExpandedStepAttribute>()!.Method);
		Assert.NotNull(typeof(MovementSystem).GetMethod("__IonQuery_Count", BindingFlags.Public | BindingFlags.Static));
	}

	[Fact]
	public void QueryComponentsAreRegisteredWithArch()
	{
		Assert.True(EcsComponents.IsRegistered<Velocity>());
		Assert.True(EcsComponents.IsRegistered<Frozen>());
		Assert.True(EcsComponents.IsRegistered<Marker>());
	}

	[Theory]
	[InlineData(true)]
	[InlineData(false)]
	public void AStructuralChangeInsideAQueryThrowsNamingTheStepAndTheEntity(bool generatedRegistration)
	{
		var host = Hosts.Ecs();
		host = generatedRegistration
			? host.Configure(s => s.AddSingleton<CarelessSystem>()).ConfigureApp(app => app.UseSystem<CarelessSystem>())
			: host.WithSystem(typeof(CarelessSystem));

		using (host)
		{
			var world = host.Get<World>();
			var entity = world.Create(new Health(1));

			var error = Assert.Throws<StructuralChangeException>(() => host.Step());
			Assert.Equal("CarelessSystem.Spawn", error.Step);
			Assert.Equal(entity, error.Entity);
			Assert.Contains("Commands", error.Message);
		}
	}

	[Fact]
	public void TheReflectionBinderRunsAQueryWithoutItsExpansion()
	{
		// The binder the runtime uses when the generator did not expand the method (a build without Ion.Generators).
		using var world = World.Create();
		var entity = world.Create(new Transform2D(), new Velocity(new Vector2(10, 0)));
		var services = new ServiceCollection().AddSingleton(world).BuildServiceProvider();
		var method = typeof(MovementSystem).GetMethod("Move", BindingFlags.NonPublic | BindingFlags.Instance)!;
		var system = new MovementSystem(new Journal());

		var attribute = method.GetCustomAttribute<QueryAttribute>()!;
		Assert.Null(attribute.Validate(method));
		Assert.Equal([typeof(World)], attribute.GetServices(method));
		var step = attribute.Bind(system, method, services, "MovementSystem.Move");
		step(new GameTime { Delta = 0.5f });

		Assert.Equal(5f, world.Get<Transform2D>(entity).Position.X);
		Assert.Equal(1, system.Moved);
	}
}
