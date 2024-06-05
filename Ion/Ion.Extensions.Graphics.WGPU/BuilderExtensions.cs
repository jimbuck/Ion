using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Ion.Extensions.Assets;
using FontStashSharp.Interfaces;

namespace Ion.Extensions.Graphics;

public static class BuilderExtensions
{
	public static IServiceCollection AddWGPUGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddWGPUGraphics(services, config.GetSection("Ion").GetSection("Graphics"), configureOptions);
	}

	public static IServiceCollection AddWGPUGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		services
			// Standard
			.Configure<GraphicsConfig>(config)
			
			// Implementation-specific
			.AddSingleton<IWindow, Window>()
			.AddSingleton<IGraphicsContext, GraphicsContext>()
			.AddSingleton<SpriteRenderer>()
			.AddSingleton<TriangleRenderer>()
			.AddSingleton<QuadRenderer>()
			.AddSingleton<FontRenderer>()
			.AddSingleton<ITexture2DManager, FontStashTexture2DManager>()
			.AddSingleton<ISpriteBatch, SpriteBatch>()

			// Loaders
			.AddSingleton<IAssetLoader, Texture2DLoader>()
			.AddSingleton<IAssetLoader, FontLoader>()

			// Implementation-specific Systems
			.AddSingleton<WindowSystem>()
			.AddSingleton<InputSystem>()
			.AddSingleton<GraphicsSystem>()
			.AddSingleton<SpriteBatchSystem>()
			.AddSingleton<IInputState, InputState>();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	public static IIonApplication UseWGPUGraphics(this IIonApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<InputSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>()
			.UseSystem<TriangleRenderer>()
			.UseSystem<QuadRenderer>();
	}
}
