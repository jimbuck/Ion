using System.Globalization;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Extensions.Scenes;
using Ion.Testing;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

/// <summary>The level scene's game step: on the server, fills the scene's world (networked as "level") with blocks.</summary>
public sealed class LevelSpawner(World world, INetworkWorld network, INetworkSession session)
{
	public const int Blocks = 3;

	/// <summary>The networked world the scene resolved.</summary>
	public INetworkWorld Network => network;

	[Init]
	public void Spawn(GameTime dt)
	{
		if (!session.IsServer) return;
		for (var i = 0; i < Blocks; i++) world.Create(new Health(i, 10));
	}
}

/// <summary>Scene worlds networked with <c>scene.UseNetworking("level")</c>, through the real schedule and scene changes.</summary>
[Trait(CATEGORY, INTEGRATION)]
public class SceneWorldTests
{
	private static readonly QueryDescription Healths = new QueryDescription().WithAll<NetworkId, Health>();

	private const int Menu = 0, Level = 1;

	private static IonTestHost Host(LoopbackNetwork network, bool levelFirst, params string[] args) => new IonTestHost()
		.WithArgs(args)
		.UseGame(
			builder =>
			{
				builder.Services.AddIon(builder.Configuration);
				builder.Services.AddEcs().AddNetworking(builder.Configuration).AddLoopbackTransport(network).AddTransient<LevelSpawner>();
			},
			app =>
			{
				app.UseIon().UseEcs().UseNetworking();
				// The first registered scene loads first: the server starts in the level, the client in the menu.
				if (levelFirst) UseLevel(app);
				app.UseScene(Menu, scene => scene.UseEcs());
				if (!levelFirst) UseLevel(app);
			});

	private static void UseLevel(IIonApplication app) => app.UseScene(Level, scene => scene.UseNetworking("level").UseSystem<LevelSpawner>());

	[Fact]
	public void AClientGetsTheLevelWhileItsLevelSceneIsLoaded()
	{
		var network = new LoopbackNetwork();
		using var server = Host(network, levelFirst: true, "--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0");
		server.Start();
		var port = server.Get<NetworkSession>().Transport!.LocalPort;
		using var client = Host(network, levelFirst: false, "--Ion:Network:Mode=Client", "--Ion:Network:Port=" + port.ToString(CultureInfo.InvariantCulture));
		client.Start();

		void Step(int frames)
		{
			for (var i = 0; i < frames; i++)
			{
				server.Step();
				client.Step();
			}
		}

		Step(10);
		var serverWorlds = server.Get<INetworkWorlds>();
		var clientWorlds = client.Get<INetworkWorlds>();
		Assert.Equal(NetworkState.Connected, client.Get<INetworkSession>().State);
		Assert.Equal(["level"], clientWorlds.Names);
		Assert.True(serverWorlds.TryGet("level", out var serverLevel));
		Assert.Equal(LevelSpawner.Blocks, serverLevel.World.CountEntities(in Healths));

		// The client is in the menu: no level world, and the server sends it none.
		Assert.Single(clientWorlds.Live);
		Assert.False(((NetworkWorld)serverLevel).HasPeer(client.Get<INetworkSession>().LocalPeer));

		for (var visit = 0; visit < 2; visit++)
		{
			// Into the level: the scene's INetworkWorld is the level's, and its world fills with the server's blocks.
			client.Get<IEvents>().Emit(new ChangeSceneEvent(Level));
			Step(15);
			Assert.True(clientWorlds.TryGet("level", out var level));
			Assert.Equal(LevelSpawner.Blocks, level.World.CountEntities(in Healths));
			Assert.Equal(0, client.Get<World>().CountEntities(in Healths));
			Assert.Equal(level.World, client.Get<SceneSystem>().ActiveScene!.Services.GetRequiredService<World>());

			// Back to the menu: the level's world goes with its scene, and the server stops sending it.
			client.Get<IEvents>().Emit(new ChangeSceneEvent(Menu));
			Step(10);
			Assert.False(clientWorlds.TryGet("level", out _));
			Assert.False(((NetworkWorld)serverLevel).HasPeer(client.Get<INetworkSession>().LocalPeer));
		}

		Assert.Equal(0, server.Get<INetworkSession>().Stats.Malformed);
		Assert.Equal(0, server.Get<INetworkSession>().Stats.Rejected);
	}
}
