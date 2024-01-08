using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

using System.Reflection;

namespace Kyber.Builder;

public class GameApplicationBuilder
{
	private HostApplicationBuilder _hostBuilder;

	public ConfigurationManager Configuration => _hostBuilder.Configuration;
	public IServiceCollection Services => _hostBuilder.Services;

	public GameApplicationBuilder(string[] args)
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

		Services.AddSingleton<GameLoop>();
	}

	public GameApplication Build()
	{
		var host = _hostBuilder.Build();
		var builtGame = new GameApplication(host);
		return builtGame;
	}
}
