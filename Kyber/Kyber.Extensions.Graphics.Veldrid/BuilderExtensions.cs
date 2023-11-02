using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddVeldridGraphics(services, config.GetSection("Kyber").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		services
			// Standard
			.Configure<GraphicsConfig>(config)
			.AddScoped<IAssetManager, AssetManager>()

			// Implementation-specific
			.AddSingleton<IWindow, Window>()
			.AddSingleton<IGraphicsContext, GraphicsContext>()
			.AddSingleton<IGraphicsContext>(svc => svc.GetRequiredService<IGraphicsContext>())
			.AddSingleton<ISpriteBatch, SpriteBatch>()
			
			// Loaders
			.AddSingleton<IAssetLoader, Texture2DLoader>()

			// Implementation-specific Systems
			.AddSingleton<WindowSystem>()
			.AddSingleton<GraphicsSystem>()
			.AddSingleton<SpriteBatchSystem>();
		//.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IKyberApplication UseVeldridGraphics(this IKyberApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>();
	}
}
