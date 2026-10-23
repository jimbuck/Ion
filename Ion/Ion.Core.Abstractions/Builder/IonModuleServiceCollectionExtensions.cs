using Microsoft.Extensions.DependencyInjection;

namespace Ion;

/// <summary>
/// Records which Ion modules a service collection has registered, so that a module's <c>AddX</c> method can be called
/// more than once (directly, and by every module that depends on it) and registers its services only the first time.
/// </summary>
/// <remarks>
/// A module's <c>AddX</c> applies its options delegates on every call (with <c>services.Configure</c>), then returns
/// early when <see cref="TryAddIonModule"/> says it is already registered. The record is a singleton instance in the
/// collection, so it lives exactly as long as the collection.
/// </remarks>
public static class IonModuleServiceCollectionExtensions
{
	/// <summary>
	/// Records that <paramref name="module"/> is registered in <paramref name="services"/>. Returns true the first time,
	/// false when it was already recorded.
	/// </summary>
	/// <param name="services">The service collection.</param>
	/// <param name="module">The module's name (for example <c>Ion.Rendering3D</c>).</param>
	public static bool TryAddIonModule(this IServiceCollection services, string module)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrEmpty(module);
		return Modules(services, create: true)!.Names.Add(module);
	}

	/// <summary>Whether <paramref name="module"/> was recorded with <see cref="TryAddIonModule"/>.</summary>
	/// <param name="services">The service collection.</param>
	/// <param name="module">The module's name.</param>
	public static bool HasIonModule(this IServiceCollection services, string module)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentException.ThrowIfNullOrEmpty(module);
		return Modules(services, create: false)?.Names.Contains(module) ?? false;
	}

	private static IonModules? Modules(IServiceCollection services, bool create)
	{
		for (var i = 0; i < services.Count; i++)
		{
			if (services[i].ServiceType == typeof(IonModules) && services[i].ImplementationInstance is IonModules modules) return modules;
		}

		if (!create) return null;
		var created = new IonModules();
		services.AddSingleton(created);
		return created;
	}

	/// <summary>The modules registered in a service collection.</summary>
	private sealed class IonModules
	{
		public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
	}
}
