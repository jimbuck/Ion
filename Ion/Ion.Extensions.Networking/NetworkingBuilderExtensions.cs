using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Metrics;

namespace Ion.Extensions.Networking;

/// <summary>Registration of the networking module.</summary>
public static class NetworkingBuilderExtensions
{
	/// <summary>
	/// Registers the networking module: <see cref="NetworkConfig"/> bound from <c>Ion:Network</c> in
	/// <paramref name="config"/> and then <paramref name="configure"/>; the <see cref="NetworkSession"/> (also as
	/// <see cref="INetworkSession"/> and <see cref="INetworkMessages"/>), its <see cref="NetworkWorld"/> (also as
	/// <see cref="INetworkWorld"/>) and <see cref="NetworkPrediction"/> (also as <see cref="INetworkPrediction"/>), for the
	/// root ECS <see cref="World"/>; and the <see cref="NetworkSystem"/>. Needs the ECS module (<c>AddEcs</c>) and a
	/// transport (<see cref="AddLoopbackTransport"/>, <c>AddLiteNetLibTransport</c>) unless the mode is
	/// <see cref="NetworkMode.Offline"/>. Add the steps with <see cref="UseNetworking"/>.
	/// </summary>
	public static IServiceCollection AddNetworking(this IServiceCollection services, IConfiguration? config = null, Action<NetworkConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		Ion.Extensions.Ecs.EcsComponents.Register<NetworkId>();
		Ion.Extensions.Ecs.EcsComponents.Register<NetworkLocal>();

		services.AddOptions<NetworkConfig>();
		if (config is not null) services.Configure<NetworkConfig>(config.GetSection(NetworkConfig.Section));
		if (configure is not null) services.Configure(configure);

		services.TryAddSingleton(static sp =>
		{
			var config = sp.GetRequiredService<IOptions<NetworkConfig>>().Value;
			var game = sp.GetService<IOptions<GameConfig>>()?.Value;
			return new NetworkSession(
				config,
				config.Mode == NetworkMode.Offline ? null : sp.GetService<INetworkTransport>(),
				sp.GetRequiredService<World>(),
				sp.GetRequiredService<IEvents>(),
				sp.GetRequiredService<IClock>(),
				game?.FixedUpdateRate ?? GameConfig.DefaultFixedUpdateRate,
				game?.Title ?? "Ion",
				sp.GetService<ILoggerFactory>()?.CreateLogger("Ion.Networking"),
				sp.GetService<IMetrics>());
		});
		services.TryAddSingleton<INetworkSession>(static sp => sp.GetRequiredService<NetworkSession>());
		services.TryAddSingleton<INetworkMessages>(static sp => sp.GetRequiredService<NetworkSession>());
		services.TryAddSingleton(static sp => sp.GetRequiredService<NetworkSession>().World);
		services.TryAddSingleton<INetworkWorld>(static sp => sp.GetRequiredService<NetworkSession>().World);
		services.TryAddSingleton(static sp => sp.GetRequiredService<NetworkSession>().Prediction);
		services.TryAddSingleton<INetworkPrediction>(static sp => sp.GetRequiredService<NetworkSession>().Prediction);
		services.TryAddSingleton(static sp => new NetworkSystem(sp.GetRequiredService<NetworkSession>()));

		// network.status on the remote protocol (only used when the remote server runs; removed from builds that compile
		// the remote module out, see RemoteFeature).
		if (Ion.Extensions.Remote.RemoteFeature.IsSupported)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<Ion.Extensions.Remote.IRemoteMethodProvider, NetworkRemoteMethods>(static sp => new NetworkRemoteMethods(sp.GetRequiredService<NetworkSession>())));
		}

		return services;
	}

	/// <summary>
	/// Registers the <see cref="LoopbackTransport"/> on <paramref name="network"/> (<see cref="LoopbackNetwork.Shared"/> when
	/// null), timed by the game's <see cref="IClock"/> and simulating <see cref="NetworkConfig.Simulate"/> on what it sends.
	/// Give every host of a test the same network.
	/// </summary>
	public static IServiceCollection AddLoopbackTransport(this IServiceCollection services, LoopbackNetwork? network = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.TryAddSingleton<INetworkTransport>(sp => new LoopbackTransport(
			network ?? LoopbackNetwork.Shared,
			sp.GetRequiredService<IClock>(),
			sp.GetRequiredService<IOptions<NetworkConfig>>().Value.Simulate));
		return services;
	}

	/// <summary>
	/// Adds the networking steps to the root schedule (see <see cref="NetworkSystem"/>): start (Init), poll and
	/// reconciliation (First), the tick scope with the snapshot capture and the prediction step (FixedUpdate), the
	/// interpolation (Render), the send (Last) and the stop (Destroy). They do nothing in <see cref="NetworkMode.Offline"/>.
	/// </summary>
	public static IIonApplication UseNetworking(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app.UseSystem<NetworkSystem>();
	}
}

/// <summary>
/// The networking steps, all in the engine order bands (<see cref="StageOrder.Network"/> and
/// <see cref="StageOrder.NetworkSend"/>), so game steps at the default order run between them whatever the registration
/// order.
/// </summary>
public sealed class NetworkSystem(NetworkSession session)
{
	/// <summary>The session driven.</summary>
	public NetworkSession Session => session;

	/// <summary>Starts the transport in the configured role.</summary>
	[Init(Order = StageOrder.Network)]
	public void Start(GameTime dt) => session.Start();

	/// <summary>Drains the transport, decodes messages and snapshots, applies the newest snapshot (client).</summary>
	[First(Order = StageOrder.Network)]
	public void Poll(GameTime dt) => session.Poll();

	/// <summary>Compares the newest snapshot with the client's predictions and replays on a mismatch.</summary>
	[First(Order = StageOrder.Network + 10)]
	public void Reconcile(GameTime dt) => session.Prediction.Reconcile();

	/// <summary>Opens the tick: increments <see cref="INetworkWorld.CurrentTick"/>.</summary>
	[Begin(Stage.FixedUpdate, Order = StageOrder.Network)]
	public void BeginTick(GameTime dt) => session.World.BeginTick();

	/// <summary>Closes the tick (after every other fixed step, even one that threw): captures the snapshot (server).</summary>
	[End(Stage.FixedUpdate, Order = StageOrder.Network)]
	public void EndTick(GameTime dt) => session.World.Capture();

	/// <summary>Applies the inputs of the tick: sampled and predicted (client), received (server).</summary>
	[FixedUpdate(Order = StageOrder.Network + 10)]
	public void Predict(GameTime dt) => session.Prediction.FixedStep(dt.Delta);

	/// <summary>Writes the interpolated state of the remote entities (client).</summary>
	[Render(Order = StageOrder.Network)]
	public void Interpolate(GameTime dt) => session.World.Interpolate();

	/// <summary>Sends snapshots, messages, owner updates and pings, and flushes the transport.</summary>
	[Last(Order = StageOrder.NetworkSend)]
	public void Send(GameTime dt) => session.Send();

	/// <summary>Disconnects the peers and stops the transport.</summary>
	[Destroy(Order = StageOrder.NetworkSend)]
	public void Stop(GameTime dt) => session.Stop();
}
