using Microsoft.Extensions.DependencyInjection;

using Kyber.Extensions.Graphics;
using Microsoft.Extensions.Configuration;

namespace Kyber.Builder;

public static class BuilderExtensions
{
	public static IServiceCollection AddGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig> configureOptions = null)
	{
		return AddGraphics(services, config.GetSection("Kyber").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig> configureOptions = null)
	{
		services
			.Configure<GraphicsConfig>(config)
			.AddSingleton<IWindow, Window>()
			.AddSingleton<IGraphicsContext, GraphicsContext>()
			.AddSingleton<ISpriteBatch, SpriteBatch>()
			//.AddScoped<IAssetManager, AssetManager>()
			//.AddSingleton<Texture2DLoader>()

			.AddSingleton<WindowSystem>()
			.AddSingleton<GraphicsSystem>()
			.AddSingleton<SpriteBatchSystem>();
		//.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IKyberApplication UseGraphics(this IKyberApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>();
	}
}
