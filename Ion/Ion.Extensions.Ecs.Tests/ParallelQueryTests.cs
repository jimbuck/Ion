using System.Collections.Concurrent;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Ecs.Tests;

/// <summary>A parallel [Query]: moves every entity, and records which threads ran it (thread-safe shared state).</summary>
public sealed partial class ParallelMover
{
	private int _visited;

	public ConcurrentDictionary<int, int> Threads { get; } = new();

	public int Visited => _visited;

	public bool Throw { get; set; }

	[Update, Query(Parallel = true), None<Frozen>]
	private void Move(ref Transform2D transform, in Velocity velocity, Entity entity, [Data] in float dt)
	{
		if (Throw && entity.Id % 1000 == 7) throw new InvalidOperationException($"entity {entity.Id}");
		transform.Position += velocity.Value * dt;
		Interlocked.Increment(ref _visited);
		Threads.AddOrUpdate(Environment.CurrentManagedThreadId, 1, static (_, n) => n + 1);
	}
}

/// <summary>A parallel query that (wrongly) changes the world's structure through a captured world.</summary>
public sealed partial class ParallelSpawner(World world)
{
	[Update, Query(Parallel = true)]
	private void Spawn(in Health health) => Create();

	private void Create() => world.Create(new Marker());
}

public class ParallelQueryTests
{
	private static Ion.Testing.IonTestHost Host<TSystem>() where TSystem : class =>
		Hosts.Ecs().Configure(s => s.AddSingleton<TSystem>()).ConfigureApp(app => app.UseSystem<TSystem>());

	private static void Populate(World world, int count)
	{
		for (var i = 0; i < count; i++)
		{
			// Three archetypes, the third excluded by None<Frozen>.
			switch (i % 3)
			{
				case 0: world.Create(new Transform2D(new Vector2(i, 0)), new Velocity(new Vector2(1, 2))); break;
				case 1: world.Create(new Transform2D(new Vector2(i, 0)), new Velocity(new Vector2(1, 2)), new Marker()); break;
				default: world.Create(new Transform2D(new Vector2(i, 0)), new Velocity(new Vector2(1, 2)), new Frozen()); break;
			}
		}
	}

	[Fact]
	public void EveryMatchingEntityRunsOnceAcrossThreads()
	{
		using var host = Host<ParallelMover>();
		var world = host.Get<World>();
		Populate(world, 300_000);
		host.Step();

		var mover = host.Get<ParallelMover>();
		Assert.Equal(200_000, mover.Visited);
		// Every moved entity moved by the same step (velocity times the frame's delta), the frozen ones not at all.
		var step = float.NaN;
		var checkedCount = 0;
		world.Query(new QueryDescription().WithAll<Transform2D, Velocity>(), (Entity e, ref Transform2D t) =>
		{
			checkedCount++;
			if (world.Has<Frozen>(e))
			{
				Assert.Equal(0f, t.Position.Y);
				return;
			}

			if (float.IsNaN(step)) step = t.Position.Y;
			Assert.Equal(step, t.Position.Y);
		});
		Assert.True(step > 0);
		Assert.Equal(300_000, checkedCount);

		// Split over the pool's workers (on a machine with more than one).
		if (Environment.ProcessorCount > 1) Assert.True(mover.Threads.Count > 1, $"{mover.Threads.Count} thread");
	}

	[Fact]
	public void AFewEntitiesRunOnTheCallingThread()
	{
		using var host = Host<ParallelMover>();
		Populate(host.Get<World>(), 300);
		var gameThread = Environment.CurrentManagedThreadId;
		host.Step();

		var mover = host.Get<ParallelMover>();
		Assert.Equal(200, mover.Visited);
		Assert.Equal([gameThread], mover.Threads.Keys);
	}

	[Fact]
	public void AnExceptionComesOutAsItself()
	{
		using var host = Host<ParallelMover>();
		Populate(host.Get<World>(), 300_000);
		host.Get<ParallelMover>().Throw = true;
		var thrown = Assert.Throws<InvalidOperationException>(() => host.Step());
		Assert.StartsWith("entity ", thrown.Message);
	}

	[Fact]
	public void AStructuralChangeIsReportedAfterTheQuery()
	{
		using var host = Host<ParallelSpawner>();
		host.Get<World>().Create(new Health(1));
		var thrown = Assert.Throws<StructuralChangeException>(() => host.Step());
		Assert.Equal("ParallelSpawner.Spawn", thrown.Step);
	}
}
