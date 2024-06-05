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

		Services.Add(ServiceDescriptor.Transient(typeof(ITraceTimer<>), typeof(NullTraceTimer<>)));
		Services.AddSingleton<IEventEmitter, EventEmitter>();
		Services.AddTransient<IEventListener, EventListener>();
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
