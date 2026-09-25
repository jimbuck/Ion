using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Coroutines;

public static class BuilderExtensions
{
	/// <summary>
	/// Registers the coroutine services.
	/// </summary>
	/// <remarks>
	/// <see cref="ICoroutineRunner"/> is a singleton: every consumer (including scene-scoped systems) shares one runner
	/// per application, so coroutines started anywhere are stepped by <see cref="CoroutineSystem"/>. Before 0.3 it was
	/// transient and each consumer got its own runner that it had to step itself.
	/// </remarks>
	public static IServiceCollection AddCoroutines(this IServiceCollection services)
	{
		return services
			.AddSingleton<ICoroutineRunner, CoroutineRunner>()
			.AddSingleton<CoroutineSystem>();
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
