
using Kyber.Graphics;

using Microsoft.Extensions.DependencyInjection;

namespace Kyber.Builder;

public static class BuilderExtensions
{
	public static IServiceCollection AddGraphics(this IServiceCollection services)
	{
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
