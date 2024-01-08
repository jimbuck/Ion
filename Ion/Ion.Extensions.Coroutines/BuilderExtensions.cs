using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Coroutines;

public static class BuilderExtensions
{
	public static IServiceCollection AddCoroutines(this IServiceCollection services)
	{
		return services
			.AddTransient<ICoroutineRunner, CoroutineRunner>();
	}
}
