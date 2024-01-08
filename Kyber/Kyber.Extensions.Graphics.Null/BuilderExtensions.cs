using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddNullGraphics(services, config.GetSection("Kyber").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		services
			// Standard
			.Configure<GraphicsConfig>(config)
			.AddScoped<IAssetManager, AssetManager>()

			// Implementation-specific
			.AddSingleton<ISpriteBatch, SpriteBatch>()

			// Loaders
			//.AddSingleton<IAssetLoader, Texture2DLoader>()

			// Implementation-specific Systems
			.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IKyberApplication UseNullGraphics(this IKyberApplication app)
	{
		return app;
	}
}
