#pragma warning disable CS0618 // The 0.2 API, kept as forwarders for one release.

using System.ComponentModel;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Metrics;

namespace Ion
{
	/// <summary>The 0.2 debug options (<c>Ion:Debug</c>), mapped onto <see cref="MetricsConfig"/> by <c>AddDebugUtils</c>.</summary>
	[Obsolete("Use MetricsConfig (Ion:Metrics): Profiling and TraceOutput. Removed in 0.4.")]
	public class DebugConfig
	{
		/// <summary>Maps to <see cref="MetricsConfig.Profiling"/>.</summary>
		public bool TraceEnabled { get; set; }

		/// <summary>Maps to <see cref="MetricsConfig.TraceOutput"/>.</summary>
		public string TraceOutput { get; set; } = "./trace.json";
	}
}

namespace Ion.Extensions.Debug
{
	/// <summary>The 0.2 registration of the debug utilities, forwarding to the metrics module.</summary>
	[EditorBrowsable(EditorBrowsableState.Never)]
	public static class BuilderExtensions
	{
		/// <summary>Forwards to <c>AddMetrics</c>, honouring <c>Ion:Debug:TraceEnabled</c> and <c>Ion:Debug:TraceOutput</c>.</summary>
		[Obsolete("Use AddMetrics (Ion:Metrics). Removed in 0.4.")]
		public static IServiceCollection AddDebugUtils(this IServiceCollection services, IConfiguration config, Action<DebugConfig>? configureOptions = null)
		{
			return AddDebugUtils(services, config.GetSection("Ion").GetSection("Debug"), configureOptions, config.GetSection("Ion").GetSection("Metrics"));
		}

		/// <summary>Forwards to <c>AddMetrics</c>, mapping the <see cref="DebugConfig"/> in <paramref name="config"/>.</summary>
		[Obsolete("Use AddMetrics (Ion:Metrics). Removed in 0.4.")]
		public static IServiceCollection AddDebugUtils(this IServiceCollection services, IConfigurationSection config, Action<DebugConfig>? configureOptions = null)
		{
			return AddDebugUtils(services, config, configureOptions, null);
		}

		/// <summary>Forwards to <c>UseMetrics</c>.</summary>
		[Obsolete("Use UseMetrics. Removed in 0.4.")]
		public static IIonApplication UseDebugUtils(this IIonApplication app) => app.UseMetrics();

		private static IServiceCollection AddDebugUtils(IServiceCollection services, IConfigurationSection debug, Action<DebugConfig>? configureOptions, IConfigurationSection? metrics)
		{
			var legacy = new DebugConfig();
			debug.Bind(legacy);
			configureOptions?.Invoke(legacy);

			services.AddMetrics(metrics ?? debug, options =>
			{
				if (legacy.TraceEnabled) options.Profiling = true;
				if (debug["TraceOutput"] is not null || configureOptions is not null) options.TraceOutput = legacy.TraceOutput;
			});

			return services;
		}
	}
}
