using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Ion.Extensions.Metrics;

/// <summary>
/// Registration of the metrics module: the frame profiler ring, <see cref="IMetrics"/>, the sprite batch counters, the
/// frame log, the <c>Ion</c> meter, trace capture and the overlay.
/// </summary>
public static class BuilderExtensions
{
	/// <summary>Registers the metrics services, binding <see cref="MetricsConfig"/> from <c>Ion:Metrics</c>.</summary>
	public static IServiceCollection AddMetrics(this IServiceCollection services, IConfiguration config, Action<MetricsConfig>? configureOptions = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		return AddMetrics(services, config.GetSection("Ion").GetSection("Metrics"), configureOptions);
	}

	/// <summary>Registers the metrics services, binding <see cref="MetricsConfig"/> from <paramref name="config"/>.</summary>
	public static IServiceCollection AddMetrics(this IServiceCollection services, IConfigurationSection config, Action<MetricsConfig>? configureOptions = null)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(config);

		services.Configure<MetricsConfig>(config);
		if (configureOptions != null) services.Configure(configureOptions);

		// The profiler the game loop, the schedules and the engine systems record into (replaces the core's disabled one).
		services.Replace(ServiceDescriptor.Singleton(static sp =>
		{
			var options = sp.GetRequiredService<IOptions<MetricsConfig>>().Value;
			return new FrameProfiler(Math.Max(1, options.HistoryFrames), Math.Max(0, options.SpansPerFrame));
		}));

		services.TryAddSingleton<MetricsOverlay>();
		services.TryAddSingleton<IMetricsOverlaySource>(static sp => sp.GetRequiredService<MetricsOverlay>());
		services.TryAddSingleton<MetricsService>();
		services.TryAddSingleton<IMetrics>(static sp => sp.GetRequiredService<MetricsService>());
		services.TryAddEnumerable(ServiceDescriptor.Singleton<IFrameStatsSource, SpriteBatchStatsSource>());
		services.TryAddSingleton<MetricsSystem>();
		services.TryAddSingleton<MetricsOverlaySystem>();

#pragma warning disable CS0618 // The 0.2 trace manager stays available for one release, as an adapter.
		services.TryAddSingleton<Ion.Extensions.Debug.ITraceManager, TraceManagerAdapter>();
#pragma warning restore CS0618

		return services;
	}

	/// <summary>
	/// Adds the metrics systems: the trace capture key (First, <see cref="StageOrder.Metrics"/>), the overlay (Render,
	/// <see cref="StageOrder.MetricsOverlay"/>) and the shutdown trace (Destroy). Resolving them starts the frame log and
	/// the meter.
	/// </summary>
	public static IIonApplication UseMetrics(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		return app
			.UseSystem<MetricsSystem>()
			.UseSystem<MetricsOverlaySystem>();
	}
}
