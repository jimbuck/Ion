using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Scenes;
using Ion.Testing;

namespace Ion.Extensions.Ecs.Tests;

public sealed class SceneProbe(World world, Commands commands, Journal journal)
{
	public World World => world;

	[Init]
	public void Spawn(GameTime dt)
	{
		journal.Add($"scene world {world.Id}");
		commands.Create(new Transform2D(new Vector2(1, 2)));
	}
}

public sealed class RootProbe(World world)
{
	public World World => world;

	[Init]
	public void Spawn(GameTime dt) => world.Create(new Transform2D(), new Marker());
}

public class WorldLifetimeTests
{
	[Fact]
	public void TheRootProviderResolvesTheRootWorldEveryTime()
	{
		using var host = Hosts.Ecs();
		var worlds = host.Get<EcsWorlds>();

		var world = host.Get<World>();
		Assert.Same(worlds.Root, world);
		Assert.Same(world, host.Get<World>());
		Assert.Same(world, host.Get<Commands>().World);
		Assert.Same(world, host.Get<NameRegistry>().World);
		Assert.Same(host.Get<Commands>(), host.Get<Commands>());
	}

	[Fact]
	public void EachScopeHasItsOwnWorldDisposedWithTheScope()
	{
		using var host = Hosts.Ecs();
		var worlds = host.Get<EcsWorlds>();
		var root = host.Get<World>();

		World scoped;
		using (var scope = host.Services.CreateScope())
		{
			scoped = scope.ServiceProvider.GetRequiredService<World>();
			Assert.NotSame(root, scoped);
			Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<World>());
			Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<Commands>().World);
			scoped.Create(new Transform2D());

			Assert.Contains(scoped, worlds.Worlds);
			Assert.Equal(1, worlds.EntityCount);
		}

		Assert.DoesNotContain(scoped, worlds.Worlds);
		Assert.Contains(root, worlds.Worlds);
		Assert.Equal(0, worlds.EntityCount);
	}

	[Fact]
	public void ScenesRunOnTheirOwnWorldAndReleaseItWhenTheyUnload()
	{
		using var host = Hosts.Ecs()
			.Configure(services => services.AddJournal().AddTransient<SceneProbe>().AddSingleton<RootProbe>())
			.ConfigureApp(app =>
			{
				app.UseSystem<RootProbe>();
				app.UseScene(1, scene => scene.UseEcs().UseSystem<SceneProbe>());
				app.UseScene(2, scene => scene.UseEcs().UseSystem<SceneProbe>());
			});

		host.Step();
		var worlds = host.Get<EcsWorlds>();
		var root = host.Get<RootProbe>().World;
		Assert.Same(worlds.Root, root);
		Assert.Equal(2, worlds.Worlds.Count);

		var first = worlds.Worlds.Single(w => w != root);
		Assert.Equal(1, first.Count<Transform2D>());
		Assert.Equal(1, root.Count<Marker>());
		Assert.Equal(0, root.Count<Transform2D>() - root.Count<Marker>());

		host.Events.EmitChangeScene(2);
		host.Step(2);

		Assert.Equal(2, worlds.Worlds.Count);
		Assert.DoesNotContain(first, worlds.Worlds);
		var second = worlds.Worlds.Single(w => w != root);
		Assert.Equal(1, second.Count<Transform2D>());
		Assert.Equal(2, host.Get<Journal>().Entries.Count);
	}

	[Fact]
	public void FrameStatsCountTheEntitiesOfEveryWorld()
	{
		using var host = Hosts.Ecs().WithSystem<RootProbe>();
		host.Step();
		var world = host.Get<World>();
		for (var i = 0; i < 4; i++) world.Create(new Transform2D());

		host.Step();

		Assert.Equal(5, host.LastFrame.Entities);
		Assert.Equal(5, host.Get<EcsWorlds>().EntityCount);
	}
}
