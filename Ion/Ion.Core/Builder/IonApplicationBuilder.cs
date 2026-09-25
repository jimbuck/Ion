using Ion.Debug;
using Ion.Extensions.Debug;

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
		_hostBuilder = Host.CreateApplicationBuilder(args);

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

		Services.Add(ServiceDescriptor.Transient(typeof(ITraceTimer<>), typeof(NullTraceTimer<>)));
		Services.AddSingleton<IClock, StopwatchClock>();

		// The concrete emitter is registered for the engine's own event plumbing (listeners, EventSystem); the interface
		// forwards to it by default and can be replaced by a fake without breaking that plumbing.
		Services.AddSingleton<EventEmitter>();
		Services.AddSingleton<IEventEmitter>(static sp => sp.GetRequiredService<EventEmitter>());
		Services.AddTransient<IEventListener>(static sp => new EventListener(sp.GetRequiredService<EventEmitter>()));
		Services.AddSingleton<IEventListenerFactory, EventListenerFactory>();
		Services.AddSingleton<EventSystem>();

		Services.AddSingleton<IPersistentStorage, PersistentStorage>();
	}

	public IonApplication Build()
	{
		var host = _hostBuilder.Build();
		var game = new IonApplication(host);

		return game;
	}
}
