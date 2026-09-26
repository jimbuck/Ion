using Arch.Core;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Ion.Extensions.Remote;
using Ion.Extensions.Scenes;

namespace Ion.Extensions.Ecs;

/// <summary>Registration of the ECS module.</summary>
public static class EcsBuilderExtensions
{
	/// <summary>
	/// Registers the ECS module: <see cref="EcsWorlds"/>, and <see cref="World"/>, <see cref="Commands"/> and
	/// <see cref="NameRegistry"/> resolved per scope (the root provider gets the root world; each scene scope its own,
	/// disposed with the scene), the built-in systems (<see cref="EcsCommandsSystem"/>, <see cref="TransformPropagationSystem"/>,
	/// <see cref="SpriteAnimationSystem"/>, registered as transients so the root schedule and each scene get instances
	/// bound to their own world) and <see cref="FrameStats.Entities"/>. Add the systems with
	/// <see cref="UseEcs(IIonApplication)"/> (and <see cref="UseEcs(ISceneBuilder)"/> in scenes that use ECS), and the world
	/// serializers with <see cref="AddEcsSerialization"/>.
	/// </summary>
	public static IServiceCollection AddEcs(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);
		EcsComponents.RegisterBuiltIns();

		services.TryAddSingleton(static sp => new EcsWorlds(sp));
		services.TryAddScoped(static sp => new EcsScope(sp.GetRequiredService<EcsWorlds>()));
		services.TryAddTransient(static sp => sp.GetRequiredService<EcsWorlds>().WorldFor(sp));
		services.TryAddTransient(static sp => sp.GetRequiredService<EcsWorlds>().CommandsFor(sp));
		services.TryAddTransient(static sp => sp.GetRequiredService<EcsWorlds>().NamesFor(sp));

		services.TryAddTransient(static sp => new EcsCommandsSystem(sp.GetRequiredService<Commands>()));
		services.TryAddTransient(static sp => new TransformPropagationSystem(sp.GetRequiredService<World>()));
		services.TryAddTransient(static _ => new SpriteAnimationSystem());

		services.TryAddEnumerable(ServiceDescriptor.Singleton<IFrameStatsSource, EcsStatsSource>(static sp => new EcsStatsSource(sp.GetRequiredService<EcsWorlds>())));

		// The remote protocol's world.* and registry.schema methods (only used when the remote server runs; removed from
		// builds that compile the remote module out, see RemoteFeature).
		if (RemoteFeature.IsSupported)
		{
			services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteMethodProvider, EcsRemoteMethods>(static sp => new EcsRemoteMethods(sp)));
		}

		return services;
	}

	/// <summary>
	/// Registers the world serializers: the <see cref="ComponentSerializerRegistry"/> (the built-in components, then
	/// <paramref name="configure"/> adds the game's), <see cref="JsonWorldSerializer"/>, <see cref="BinaryWorldSerializer"/>,
	/// and <see cref="IWorldSerializer"/> as the binary one. Separate from <see cref="AddEcs"/> so that a game that does not
	/// save worlds does not carry System.Text.Json's serializer into a NativeAOT image.
	/// </summary>
	public static IServiceCollection AddEcsSerialization(this IServiceCollection services, Action<ComponentSerializerRegistry>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		var registry = ComponentSerializerRegistry.CreateDefault();
		configure?.Invoke(registry);

		services.TryAddSingleton(registry);
		services.TryAddSingleton(static sp => new JsonWorldSerializer(sp.GetRequiredService<ComponentSerializerRegistry>()));
		services.TryAddSingleton(static sp => new BinaryWorldSerializer(sp.GetRequiredService<ComponentSerializerRegistry>()));
		services.TryAddSingleton<IWorldSerializer>(static sp => sp.GetRequiredService<BinaryWorldSerializer>());
		return services;
	}

	/// <summary>
	/// Adds the ECS systems to the root schedule (for the root world): commands played back at the end of every stage
	/// (<see cref="StageOrder.Ecs"/>), transform propagation (<see cref="StageOrder.TransformPropagation"/> in Last and
	/// Render) and sprite animation (<see cref="StageOrder.SpriteAnimation"/> in Update).
	/// </summary>
	public static IIonApplication UseEcs(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app
			.UseSystem<EcsCommandsSystem>()
			.UseSystem<TransformPropagationSystem>()
			.UseSystem<SpriteAnimationSystem>();
	}

	/// <summary>Adds the ECS systems to a scene's schedule (for the scene's own world); see <see cref="UseEcs(IIonApplication)"/>.</summary>
	public static ISceneBuilder UseEcs(this ISceneBuilder scene)
	{
		ArgumentNullException.ThrowIfNull(scene);
		return scene
			.UseSystem<EcsCommandsSystem>()
			.UseSystem<TransformPropagationSystem>()
			.UseSystem<SpriteAnimationSystem>();
	}
}
