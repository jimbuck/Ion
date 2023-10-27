using Kyber.Core;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Kyber.Builder;

public class KyberApplicationBuilder
{
	private HostApplicationBuilder _hostBuilder;

	public ConfigurationManager Configuration => _hostBuilder.Configuration;
	public IServiceCollection Services => _hostBuilder.Services;

	public KyberApplicationBuilder(string[] args)
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
	}

	public KyberApplication Build()
	{
		var host = _hostBuilder.Build();
		var builtGame = new KyberApplication(host);
		return builtGame;
	}
}
