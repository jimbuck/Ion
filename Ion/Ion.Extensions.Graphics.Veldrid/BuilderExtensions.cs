using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddVeldridGraphics(services, config.GetSection("Ion").GetSection("Graphics"), configureOptions);
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
			.AddSingleton<ISpriteBatch, SpriteBatch>()

			// Loaders
			.AddSingleton<IAssetLoader, Texture2DLoader>()

			// Implementation-specific Systems
			.AddSingleton<WindowSystem>()
			.AddSingleton<InputSystem>()
			.AddSingleton<GraphicsSystem>()
			.AddSingleton<SpriteBatchSystem>()
			.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IIonApplication UseVeldridGraphics(this IIonApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<InputSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>();
	}
}
