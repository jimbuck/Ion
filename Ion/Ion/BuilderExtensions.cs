using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Debug;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;
using Ion.Extensions.Scenes;
using Ion.Extensions.Coroutines;

namespace Ion;

public static class BuilderExtensions
{
	public static IServiceCollection AddIon(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return services
			.AddDebugUtils(config)
			.AddAssets()
			.AddVeldridGraphics(config, configureOptions)
			.AddAudio()
			.AddScenes()
			.AddCoroutines();
	}

	public static IIonApplication UseIon(this IIonApplication app)
	{
		return app
			.UseDebugUtils()
			.UseEvents()
			.UseVeldridGraphics();
	}
}
