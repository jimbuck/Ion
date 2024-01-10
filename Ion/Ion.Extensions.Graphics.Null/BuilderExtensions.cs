using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddNullGraphics(services, config.GetSection("Ion").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		services
			// Standard
			.Configure<GraphicsConfig>(config)
			.AddSingleton<GlobalAssetManager>()
			.AddScoped<IAssetManager, ScopedAssetManager>()

			// Implementation-specific
			.AddSingleton<ISpriteBatch, SpriteBatch>()

			// Implementation-specific Systems
			.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IIonApplication UseNullGraphics(this IIonApplication app)
	{
		return app;
	}
}
