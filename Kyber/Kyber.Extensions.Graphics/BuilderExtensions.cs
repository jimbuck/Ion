
using Kyber.Extensions.Graphics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Builder;

public static class BuilderExtensions
{
	public static IServiceCollection AddGraphics(this IServiceCollection services, IConfiguration config)
	{
		services.Configure<GraphicsConfig>(config.GetSection("Kyber").GetSection("Graphics"));

		return services
			.AddSingleton<IWindow, Window>()
			.AddSingleton<IGraphicsContext, GraphicsContext>()
			//.AddScoped<IAssetManager, AssetManager>()
			//.AddSingleton<Texture2DLoader>()
			.AddSingleton<ISpriteBatch, SpriteBatch>();
			//.AddSingleton<IInputState, InputState>();
	}

	public static IKyberApplication UseScene(this IKyberApplication app)
	{
		return app
			.UseSystem<WindowSystem>()
			.UseSystem<GraphicsSystem>()
			.UseSystem<SpriteBatchSystem>();
	}
}
