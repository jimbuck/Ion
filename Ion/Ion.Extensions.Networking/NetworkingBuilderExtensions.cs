using Arch.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Ecs;
using Ion.Extensions.Metrics;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Networking;

/// <summary>Registration of the networking module.</summary>
public static class NetworkingBuilderExtensions
{
	/// <summary>
	/// Registers the networking module: <see cref="NetworkConfig"/> bound from <c>Ion:Network</c> in
	/// <paramref name="config"/> and then <paramref name="configure"/>; the <see cref="NetworkSession"/> (also as
	/// <see cref="INetworkSession"/> and <see cref="INetworkMessages"/>), its <see cref="NetworkWorlds"/> (also as
	/// <see cref="INetworkWorlds"/>), <see cref="NetworkWorld"/> (also as <see cref="INetworkWorld"/>: the root world's from
	/// the root services, a networked scene's from its scope, see <see cref="UseNetworking(ISceneBuilder, string)"/>) and
	/// <see cref="NetworkPrediction"/> (also as <see cref="INetworkPrediction"/>); and the <see cref="NetworkSystem"/>.
	/// Registers the ECS module (<c>AddEcs</c>) too. Needs a
	/// transport (<see cref="AddLoopbackTransport(IServiceCollection, LoopbackNetwork)"/>, <c>AddLiteNetLibTransport</c>) unless the mode is
	/// <see cref="NetworkMode.Offline"/>. Add the steps with <see cref="UseNetworking(IIonApplication)"/>.
	/// </summary>
	public static IServiceCollection AddNetworking(this IServiceCollection services, IConfiguration? config = null, Action<NetworkConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		services.AddEcs();
		Ion.Extensions.Ecs.EcsComponents.Register<NetworkId>();
		Ion.Extensions.Ecs.EcsComponents.Register<NetworkLocal>();

		services.AddOptions<NetworkConfig>();
		if (config is not null) services.Configure<NetworkConfig>(config.GetSection(NetworkConfig.Section));
		if (configure is not null) services.Configure(configure);

		services.TryAddSingleton(static sp =>
		{
			var config = sp.GetRequiredService<IOptions<NetworkConfig>>().Value;
			var game = sp.GetService<IOptions<GameConfig>>()?.Value;
			var session = new NetworkSession(
				config,
				config.Mode == NetworkMode.Offline ? null : sp.GetService<INetworkTransport>(),
				sp.GetRequiredService<World>(),
				sp.GetRequiredService<IEvents>(),
				sp.GetRequiredService<IClock>(),
				game?.FixedUpdateRate ?? GameConfig.DefaultFixedUpdateRate,
				game?.Title ?? "Ion",
				sp.GetService<ILoggerFactory>()?.CreateLogger("Ion.Networking"),
				sp.GetService<IMetrics>());
			// The whole fixed schedule for RollbackMode.FixedSchedule: the game loop's, once it exists.
			if (sp.GetService<ILoopContext>() is GameLoopContext loop) session.Prediction.Resimulator = () => loop.Loop?.ResimulateFixedStep();
			return session;
		});
		services.TryAddSingleton<INetworkSession>(static sp => sp.GetRequiredService<NetworkSession>());
		services.TryAddSingleton<INetworkMessages>(static sp => sp.GetRequiredService<NetworkSession>());
		services.TryAddSingleton(static sp => sp.GetRequiredService<NetworkSession>().Worlds);
		services.TryAddSingleton<INetworkWorlds>(static sp => sp.GetRequiredService<NetworkSession>().Worlds);

		// Like World, resolved per scope: a transient whose factory returns the scope's (see NetworkScope).
		services.TryAddSingleton(static sp => new NetworkRootServices(sp));
		services.TryAddScoped(static sp => new NetworkScope(sp.GetRequiredService<NetworkSession>(), sp));
		services.TryAddTransient(static sp => WorldFor(sp));
		services.TryAddTransient<INetworkWorld>(static sp => WorldFor(sp));
		services.TryAddTransient(static sp => new SceneNetworkSystem(sp.GetRequiredService<NetworkScope>()));

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

	/// <summary>The networked world of <paramref name="services"/>' scope: the root world's, or a networked scene's.</summary>
	private static NetworkWorld WorldFor(IServiceProvider services)
	{
		var session = services.GetRequiredService<NetworkSession>();
		if (ReferenceEquals(services, services.GetRequiredService<NetworkRootServices>().Services)) return session.World;
		return services.GetRequiredService<NetworkScope>().World ?? session.World;
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
	/// Registers the networking module (see <see cref="AddNetworking(IServiceCollection, IConfiguration, Action{NetworkConfig})"/>,
	/// with the application's configuration) and the ECS module it replicates (<c>AddEcs</c>). Register a transport too
	/// (<see cref="AddLoopbackTransport(IonApplicationBuilder, LoopbackNetwork)"/>, <c>AddLiteNetLibTransport</c>) unless
	/// the mode is <see cref="NetworkMode.Offline"/>. Add the steps with <see cref="UseNetworking(IIonApplication)"/>.
	/// </summary>
	public static IonApplicationBuilder AddNetworking(this IonApplicationBuilder builder, Action<NetworkConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddEcs();
		builder.Services.AddNetworking(builder.Configuration, configure);
		return builder;
	}

	/// <summary>
	/// Registers the <see cref="LoopbackTransport"/> (see <see cref="AddLoopbackTransport(IServiceCollection, LoopbackNetwork)"/>)
	/// and the networking module.
	/// </summary>
	public static IonApplicationBuilder AddLoopbackTransport(this IonApplicationBuilder builder, LoopbackNetwork? network = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddNetworking();
		builder.Services.AddLoopbackTransport(network);
		return builder;
	}

	/// <summary>
	/// Adds the networking steps to the root schedule (see <see cref="NetworkSystem"/>): start (Init), poll and
	/// reconciliation (First), the tick scope with the snapshot capture and the prediction step (FixedUpdate), the
	/// interpolation (Render), the send (Last) and the stop (Destroy), with the ECS systems (<c>UseEcs</c>). They do
	/// nothing in <see cref="NetworkMode.Offline"/>.
	/// </summary>
	public static IIonApplication UseNetworking(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app.UseEcs().UseSystem<NetworkSystem>();
	}

	/// <summary>
	/// Networks the scene's world as the named world <paramref name="name"/> (see <see cref="INetworkWorlds"/>), with the
	/// scene's ECS systems (<c>UseEcs</c>). The world is replicated while the scene is loaded: <see cref="INetworkWorld"/>
	/// resolved in the scene is this world's; on the server its entities get ids and are sent to the clients that have the
	/// world open; a client asks the server for it when the scene loads and stops when it unloads. The root schedule must
	/// call <see cref="UseNetworking(IIonApplication)"/>. Both sides must network the same set of names (the set is part of
	/// the handshake), so call this unconditionally: the name is registered when the application's schedule is built.
	/// </summary>
	public static ISceneBuilder UseNetworking(this ISceneBuilder scene, string name)
	{
		ArgumentNullException.ThrowIfNull(scene);
		ArgumentException.ThrowIfNullOrEmpty(name);
		scene.Services.GetRequiredService<NetworkScope>().Use(name);
		return scene.UseEcs().UseSystem<SceneNetworkSystem>();
	}
}

/// <summary>The application's root services, to tell them apart from a scene's scope.</summary>
internal sealed record NetworkRootServices(IServiceProvider Services);

/// <summary>
/// The networked world of one service scope: set by a scene's <see cref="NetworkingBuilderExtensions.UseNetworking(ISceneBuilder, string)"/>,
/// attached on first use (its scene's schedule is built) and detached when the scope is disposed (the scene unloads).
/// </summary>
public sealed class NetworkScope : IDisposable
{
	private readonly NetworkSession _session;
	private readonly IServiceProvider _services;
	private NetworkWorld? _world;
	private bool _disposed;

	internal NetworkScope(NetworkSession session, IServiceProvider services)
	{
		_session = session;
		_services = services;
	}

	/// <summary>The name the scope's world is networked under, or null when its scene is not networked.</summary>
	public string? Name { get; private set; }

	/// <summary>The scope's networked world (attached on first use), or null when its scene is not networked.</summary>
	public NetworkWorld? World
	{
		get
		{
			if (Name is null || _disposed) return _world;
			return _world ??= _session.Worlds.Attach(Name, _services.GetRequiredService<World>());
		}
	}

	internal void Use(string name)
	{
		if (Name is not null && Name != name) throw new InvalidOperationException($"The scene's world is already networked as '{Name}'; a world has one name.");
		_session.Worlds.Register(name);
		Name = name;
	}

	/// <summary>Detaches the world.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		if (_world is not null) _session.Worlds.Detach(_world);
	}
}

/// <summary>A networked scene's step: attaches the scene's world before the scene's other Init steps (see <see cref="NetworkScope"/>).</summary>
public sealed class SceneNetworkSystem(NetworkScope scope)
{
	/// <summary>The scene's networked world.</summary>
	public NetworkWorld? World => scope.World;

	/// <summary>Attaches the scene's world, if no step resolved it yet.</summary>
	[Init(Order = StageOrder.Network)]
	public void Attach(GameTime dt) => _ = scope.World;
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
	public void BeginTick(GameTime dt) => session.BeginTick();

	/// <summary>Closes the tick (after every other fixed step, even one that threw): captures the snapshot (server).</summary>
	[End(Stage.FixedUpdate, Order = StageOrder.Network)]
	public void EndTick(GameTime dt) => session.Capture();

	/// <summary>Applies the inputs of the tick: sampled and predicted (client), received (server).</summary>
	[FixedUpdate(Order = StageOrder.Network + 10)]
	public void Predict(GameTime dt) => session.Prediction.FixedStep(dt.Delta);

	/// <summary>Writes the interpolated state of the remote entities (client).</summary>
	[Render(Order = StageOrder.Network)]
	public void Interpolate(GameTime dt) => session.Interpolate();

	/// <summary>Sends snapshots, messages, owner updates and pings, and flushes the transport.</summary>
	[Last(Order = StageOrder.NetworkSend)]
	public void Send(GameTime dt) => session.Send();

	/// <summary>Disconnects the peers and stops the transport.</summary>
	[Destroy(Order = StageOrder.NetworkSend)]
	public void Stop(GameTime dt) => session.Stop();
}
