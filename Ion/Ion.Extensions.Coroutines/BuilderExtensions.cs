using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Ion.Extensions.Coroutines;

public static class BuilderExtensions
{
	/// <summary>
	/// Registers the coroutine services.
	/// </summary>
	/// <remarks>
	/// <see cref="CoroutineRunner"/> is a singleton: one runner per application, stepped by <see cref="CoroutineSystem"/>.
	/// <see cref="ICoroutineRunner"/> depends on where it is resolved: from the root provider (root systems, singletons,
	/// the application's function steps) it is that runner, so its coroutines live as long as the application; from a
	/// service scope (a scene's systems and steps) it is the scope's <see cref="ScopedCoroutineRunner"/>, whose
	/// coroutines run on the same runner and stop when the scope is disposed, that is when the scene unloads. Inject
	/// <see cref="CoroutineRunner"/> in a scene to start a coroutine that outlives it. Before 0.3 the runner was
	/// transient and each consumer got its own runner that it had to step itself.
	/// </remarks>
	public static IServiceCollection AddCoroutines(this IServiceCollection services)
	{
		ArgumentNullException.ThrowIfNull(services);

		services.TryAddSingleton(static sp => new CoroutineRunner(sp.GetRequiredService<IEvents>()));
		services.TryAddSingleton(static sp => new CoroutineRunnerResolver(sp));
		services.TryAddScoped(static sp => new ScopedCoroutineRunner(sp.GetRequiredService<CoroutineRunner>()));
		// A transient whose factory picks the runner of the provider's scope (a scoped service cannot be resolved from the
		// root provider, and the root schedule needs a runner too).
		services.TryAddTransient<ICoroutineRunner>(static sp => sp.GetRequiredService<CoroutineRunnerResolver>().For(sp));
		services.TryAddSingleton<CoroutineSystem>();
		return services;
	}

	/// <summary>
	/// Adds <see cref="CoroutineSystem"/> to the Update stage so that coroutines are stepped automatically.
	/// Calling <see cref="ICoroutineRunner.Update"/> manually as well is harmless: the runner steps at most once per frame
	/// between the two.
	/// </summary>
	public static IIonApplication UseCoroutines(this IIonApplication app)
	{
		return app.UseSystem<CoroutineSystem>();
	}
}
