using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kyber.Builder;

public class KyberApplicationBuilder : IKyberApplicationBuilder
{
	private HostApplicationBuilder _hostBuilder;

	public ConfigurationManager Configuration => _hostBuilder.Configuration;
	public IServiceCollection Services => _hostBuilder.Services;

	internal KyberApplicationBuilder(string[] args)
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
		
		Services.Configure<GameConfig>(Configuration.GetSection("Kyber"));

		Services.AddSingleton<IEventEmitter, EventEmitter>();
		Services.AddTransient<IEventListener, EventListener>();
		Services.AddSingleton<EventSystem>();
	}

	public KyberApplication Build()
	{
		var host = _hostBuilder.Build();
		var game = new KyberApplication(host);

		game.UseSystem<EventSystem>();

		return game;
	}
}
