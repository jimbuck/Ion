using System.Globalization;

using Arch.Core;

using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Ecs;
using Ion.Testing;

using static Ion.Tests.TestConstants;

namespace Ion.Extensions.Networking.Tests;

/// <summary>A game step that moves every replicated position (server) and can be told to throw.</summary>
public sealed class MoverSystem(World world, INetworkSession session)
{
	private static readonly QueryDescription Positions = new QueryDescription().WithAll<Position>();

	public bool Throw { get; set; }

	[FixedUpdate]
	public void Move(GameTime dt)
	{
		if (!session.IsServer) return;
		world.Query(in Positions, (ref Position position) => position.Value += new Vector2(1, 0));
		if (Throw) throw new InvalidOperationException("step failed");
	}
}

[Trait(CATEGORY, INTEGRATION)]
public class HostTests
{
	internal static IonTestHost Host(LoopbackNetwork network, params string[] args) => new IonTestHost()
		.WithArgs(args)
		.UseGame(
			builder =>
			{
				builder.Services.AddIon(builder.Configuration);
				builder.Services.AddEcs().AddNetworking(builder.Configuration).AddLoopbackTransport(network).AddSingleton<MoverSystem>();
			},
			app => app.UseIon().UseEcs().UseNetworking().UseSystem<MoverSystem>());

	[Fact]
	public void AHeadlessServerAndClientReplicateThroughTheSchedule()
	{
		var network = new LoopbackNetwork();
		using var server = Host(network, "--Ion:Headless=true", "--Ion:Network:Mode=Server", "--Ion:Network:Port=0");
		server.Start();
		var serverSession = server.Get<INetworkSession>();
		Assert.Equal(NetworkMode.Server, serverSession.Mode);
		Assert.Equal(NetworkState.Connected, serverSession.State);
		var port = server.Get<NetworkSession>().Transport!.LocalPort;

		using var client = Host(network, "--Ion:Network:Mode=Client", "--Ion:Network:Port=" + port.ToString(CultureInfo.InvariantCulture));
		client.Start();
		var clientWorld = client.Get<INetworkWorld>();

		void Step(int frames)
		{
			for (var i = 0; i < frames; i++)
			{
				server.Step();
				client.Step();
			}
		}

		var entity = server.Get<World>().Create(new Position(Vector2.Zero), new Health(10, 10));
		Step(10);
		Assert.Equal(NetworkState.Connected, client.Get<INetworkSession>().State);
		var id = server.Get<World>().Get<NetworkId>(entity);
		Assert.True(clientWorld.TryGetEntity(id, out var mirror));
		Assert.Equal(new Health(10, 10), client.Get<World>().Get<Health>(mirror));

		// The capture runs in the FixedUpdate End scope, after the game's steps: the ring holds each tick's moved value.
		var serverWorld = server.Get<INetworkWorld>();
		Assert.Equal(serverWorld.CurrentTick, server.Get<NetworkWorld>().CapturedTick);
		var tick = serverWorld.CurrentTick;
		Assert.Equal(server.Get<World>().Get<Position>(entity), serverWorld.GetAtTick<Position>(entity, tick));
		Assert.Equal(serverWorld.GetAtTick<Position>(entity, tick - 1).Value + new Vector2(1, 0), serverWorld.GetAtTick<Position>(entity, tick).Value);

		// The counters are on the metrics module.
		var bytesOut = server.Metrics.Instruments.OfType<Ion.Extensions.Metrics.MetricsCounter>().Single(i => i.Name == "net_bytes_out");
		Assert.Equal(serverSession.Stats.BytesOut, bytesOut.Value);
		var peers = client.Metrics.Instruments.OfType<Ion.Extensions.Metrics.MetricsGauge>().Single(i => i.Name == "net_peers");
		Assert.Equal(1, peers.Value);

		// A step that throws still leaves the tick captured (the End scope runs in a finally).
		server.Get<MoverSystem>().Throw = true;
		Assert.Throws<InvalidOperationException>(() => server.Step());
		Assert.Equal(serverWorld.CurrentTick, server.Get<NetworkWorld>().CapturedTick);
		Assert.Equal(tick + 1, serverWorld.CurrentTick);
	}

	[Fact]
	public void NetworkStatusIsARemoteReadMethod()
	{
		var network = new LoopbackNetwork();
		using var server = Host(network, "--Ion:Network:Mode=Server", "--Ion:Network:Port=0");
		server.Step(2);
		var registry = new Ion.Extensions.Remote.RemoteMethodRegistry();
		// Registered by AddNetworking when the remote protocol is compiled in (not in Release builds by default).
		new NetworkRemoteMethods(server.Get<NetworkSession>()).Register(registry);
		var method = registry.Find("network.status");
		Assert.NotNull(method);
		Assert.Equal(Ion.Extensions.Remote.RemoteAccess.Read, method.Access);
		var status = method.Handler(new Ion.Extensions.Remote.RemoteRequest("network.status", null, server.Services))!;
		Assert.Equal("Server", (string?)status["mode"]);
		Assert.Equal("Connected", (string?)status["state"]);
		Assert.Equal("loopback", (string?)status["transport"]);
		Assert.Equal(2u, (uint)status["tick"]!);
	}

	[Fact]
	public void OfflineByDefault()
	{
		using var host = Host(new LoopbackNetwork());
		host.Step(3);
		var session = host.Get<INetworkSession>();
		Assert.Equal(NetworkMode.Offline, session.Mode);
		Assert.Equal(NetworkState.Offline, session.State);
		Assert.Equal(0u, host.Get<INetworkWorld>().CurrentTick);
	}
}
