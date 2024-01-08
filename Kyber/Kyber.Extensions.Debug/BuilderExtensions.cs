using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber.Extensions.Debug;

public static class BuilderExtensions
{
	public static IServiceCollection AddDebugUtils(this IServiceCollection services, IConfiguration config, Action<DebugConfig>? configureOptions = null)
	{
		return AddDebugUtils(services, config.GetSection("Kyber").GetSection("Debug"), configureOptions);
	}

	public static IServiceCollection AddDebugUtils(this IServiceCollection services, IConfigurationSection config, Action<DebugConfig>? configureOptions = null)
	{
		services
			.Configure<DebugConfig>(config)
			.AddSingleton<ITraceManager, TraceManager>()
			.AddSingleton<TraceTimerSystem>()
			.Add(ServiceDescriptor.Transient(typeof(ITraceTimer<>), typeof(TraceTimer<>)));

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IKyberApplication UseDebugUtils(this IKyberApplication app)
	{
		return app.UseSystem<TraceTimerSystem>();
	}
}
