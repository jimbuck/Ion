
using Ion.Extensions.Debug;

using Microsoft.Extensions.DependencyInjection.Extensions;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ion;

public class IonApplicationBuilder : IIonApplicationBuilder
{
	private readonly HostApplicationBuilder _hostBuilder;

	public ConfigurationManager Configuration => _hostBuilder.Configuration;
	public IServiceCollection Services => _hostBuilder.Services;

	internal IonApplicationBuilder(string[] args)
	{
		_hostBuilder = Host.CreateApplicationBuilder(IonCommandLine.Normalize(args));

		Services.AddLogging(config =>
		{
			config
				.ClearProviders()
				.AddSimpleConsole(options =>
				{
					options.TimestampFormat = "[HH:mm:ss] ";
					options.SingleLine = true;
				})
				.AddDebug();
		});

		Services.Configure<GameConfig>(Configuration.GetSection("Ion"));
		Services.Configure<StorageConfig>(Configuration.GetSection("Ion:Storage"));
		Services.Configure<InputConfig>(Configuration.GetSection("Ion:Input"));

		// Metrics: a profiler without history until AddMetrics replaces it (the loop then opens a profile per frame).
		Services.TryAddSingleton(FrameProfiler.Disabled);
		Services.AddSingleton<IStepProfiler>(static sp => sp.GetRequiredService<FrameProfiler>());
		Services.AddSingleton<IFrameStatsSource, EventStatsSource>();
#pragma warning disable CS0618 // The obsolete trace timers stay registered for one release, as adapters over the profiler.
		Services.Add(ServiceDescriptor.Transient(typeof(ITraceTimer<>), typeof(TraceTimerAdapter<>)));
#pragma warning restore CS0618
		Services.AddSingleton<IClock, StopwatchClock>();
		Services.AddSingleton<GameLoopContext>();
		Services.AddSingleton<ILoopContext>(static sp => sp.GetRequiredService<GameLoopContext>());

		// The event bus: the generated bus when the application installed one (UseEventBus, called by the generated
		// CreateBuilder interceptor), otherwise the runtime bus. IEvents forwards to it and can be replaced by a fake; the
		// engine's own plumbing (the loop's fixed-step marks, EventSystem) keeps using the concrete bus.
		Services.AddSingleton<EventBus>(static sp => sp.GetService<EventBusFactory>() is { } factory
			? factory.Create(sp.GetRequiredService<ILoopContext>())
			: new EventBus(sp.GetRequiredService<ILoopContext>()));
		Services.AddSingleton<IEvents>(static sp => sp.GetRequiredService<EventBus>());
		Services.AddSingleton<EventSystem>();

#pragma warning disable CS0618 // The obsolete adapters stay registered for one release.
		Services.AddSingleton<EventEmitter>(static sp => new EventEmitter(sp.GetRequiredService<IEvents>()));
		Services.AddSingleton<IEventEmitter>(static sp => sp.GetRequiredService<EventEmitter>());
		Services.AddTransient<IEventListener>(static sp => new EventListener(sp.GetRequiredService<IEvents>()));
		Services.AddSingleton<IEventListenerFactory, EventListenerFactory>();
#pragma warning restore CS0618

		Services.AddSingleton<IPersistentStorage, PersistentStorage>();
	}

	/// <summary>
	/// Uses the event bus created by <paramref name="factory"/> (given the loop context) instead of the runtime
	/// <see cref="EventBus"/>. The Ion source generator calls this from the application's <c>CreateBuilder</c> call to
	/// install its generated bus.
	/// </summary>
	public IonApplicationBuilder UseEventBus(Func<ILoopContext?, EventBus> factory)
	{
		ArgumentNullException.ThrowIfNull(factory);
		Services.AddSingleton(new EventBusFactory(factory));
		return this;
	}

	public IonApplication Build()
	{
		// Captured last, so the root schedule can reject scoped systems and scoped step parameters (ION006).
		Services.AddSingleton(new ServiceLifetimeIndex(Services));

		var host = _hostBuilder.Build();
		var game = new IonApplication(host);

		return game;
	}
}

/// <summary>Creates the application event bus (see <see cref="IonApplicationBuilder.UseEventBus"/>).</summary>
internal sealed record EventBusFactory(Func<ILoopContext?, EventBus> Create);
