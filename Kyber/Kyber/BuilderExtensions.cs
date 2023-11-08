using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Kyber.Extensions.Debug;
using Kyber.Extensions.Graphics;
using Kyber.Extensions.Scenes;
using Kyber.Extensions.Coroutines;

namespace Kyber;

public static class BuilderExtensions
{
	public static IServiceCollection AddKyber(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return services
			.AddDebugUtils(config)
			.AddVeldridGraphics(config, configureOptions)
			.AddScenes()
			.AddCoroutines();
	}

	public static IKyberApplication UseKyber(this IKyberApplication app)
	{
		return app
			.UseDebugUtils()
			.UseEvents()
			.UseVeldridGraphics();
	}
}
