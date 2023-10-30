using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Kyber.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig> configureOptions = null)
	{
		return AddVeldridGraphics(services, config.GetSection("Kyber").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig> configureOptions = null)
	{
		services
			.Configure<GraphicsConfig>(config)
			.AddSingleton<IWindow, Window>()
			.AddSingleton<IVeldridGraphicsContext, GraphicsContext>()
			.AddSingleton<IGraphicsContext>(svc => svc.GetRequiredService<IVeldridGraphicsContext>())
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

	public static IKyberApplication UseVeldridGraphics(this IKyberApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>();
	}
}
