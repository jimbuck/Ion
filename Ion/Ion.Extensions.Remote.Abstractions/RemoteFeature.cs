using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization.Metadata;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ion.Extensions.Remote;

/// <summary>
/// Whether the remote protocol is compiled in: the <c>Ion.Remote.IsSupported</c> feature switch.
/// </summary>
/// <remarks>
/// The Ion build targets set it from the <c>IonRemote</c> MSBuild property, which defaults to <c>false</c> for the
/// <c>Release</c> configuration and <c>true</c> otherwise. With <c>false</c>, a trimmed or NativeAOT publish substitutes
/// <see cref="IsSupported"/> with a constant and removes the server, its transports and every provider registered through
/// <see cref="RemoteServiceCollectionExtensions"/>; an untrimmed Release build keeps the code but <c>AddRemote</c> registers
/// nothing and logs why if <c>--remote</c> is passed. Publish with <c>-p:IonRemote=true</c> to keep it.
/// </remarks>
public static class RemoteFeature
{
	/// <summary>The feature switch name.</summary>
	public const string SwitchName = "Ion.Remote.IsSupported";

	/// <summary>Whether the remote protocol is compiled in (true when the switch is not set).</summary>
	[FeatureSwitchDefinition(SwitchName)]
	public static bool IsSupported { get; } = !AppContext.TryGetSwitch(SwitchName, out var enabled) || enabled;
}

/// <summary>Registration of remote methods, resources and events from any module.</summary>
public static class RemoteServiceCollectionExtensions
{
	/// <summary>
	/// Adds <paramref name="provider"/>'s methods to the remote protocol (when it is enabled). Registering the same provider
	/// type twice keeps the first. Does nothing when <see cref="RemoteFeature.IsSupported"/> is false.
	/// </summary>
	public static IServiceCollection AddRemoteMethods(this IServiceCollection services, Func<IServiceProvider, IRemoteMethodProvider> provider)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(provider);
		if (!RemoteFeature.IsSupported) return services;
		services.AddSingleton(provider);
		return services;
	}

	/// <summary>
	/// Adds <typeparamref name="TProvider"/>'s methods to the remote protocol (once per type). Does nothing when
	/// <see cref="RemoteFeature.IsSupported"/> is false.
	/// </summary>
	public static IServiceCollection AddRemoteMethods<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TProvider>(this IServiceCollection services)
		where TProvider : class, IRemoteMethodProvider
	{
		ArgumentNullException.ThrowIfNull(services);
		if (!RemoteFeature.IsSupported) return services;
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IRemoteMethodProvider, TProvider>());
		return services;
	}

	/// <summary>Exposes <paramref name="resource"/> through <c>resources.get</c> and <c>resources.set</c>.</summary>
	public static IServiceCollection AddRemoteResource(this IServiceCollection services, RemoteResource resource)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(resource);
		if (!RemoteFeature.IsSupported) return services;
		services.AddSingleton(resource);
		return services;
	}

	/// <summary>
	/// Exposes a resource of type <typeparamref name="T"/> (see <see cref="RemoteResource.Create{T}"/>).
	/// </summary>
	public static IServiceCollection AddRemoteResource<T>(this IServiceCollection services, string name, string description, JsonTypeInfo<T> type, Func<IServiceProvider, T> get, Action<IServiceProvider, T>? set = null)
	{
		if (!RemoteFeature.IsSupported) return services;
		return services.AddRemoteResource(RemoteResource.Create(name, description, type, get, set));
	}

	/// <summary>Makes <c>events.tail</c> return the payloads of <typeparamref name="T"/> events, serialized with <paramref name="type"/>.</summary>
	public static IServiceCollection AddRemoteEvent<T>(this IServiceCollection services, JsonTypeInfo<T> type, string? name = null) where T : unmanaged
	{
		ArgumentNullException.ThrowIfNull(services);
		if (!RemoteFeature.IsSupported) return services;
		services.AddSingleton(RemoteEventSource.Create(type, name));
		return services;
	}
}
