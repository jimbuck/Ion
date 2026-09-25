using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

using Ion.Extensions.Assets;
using FontStashSharp.Interfaces;

namespace Ion.Extensions.Graphics;

public static class BuilderExtensions
{
	/// <summary>
	/// Registers the Veldrid graphics services, binding <see cref="GraphicsConfig"/> from <c>Ion:Graphics</c>
	/// and <see cref="WindowConfig"/> from <c>Ion:Window</c>.
	/// </summary>
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		var ionSection = config.GetSection("Ion");
		return AddVeldridGraphics(services, ionSection.GetSection("Graphics"), ionSection.GetSection("Window"), configureOptions);
	}

	/// <summary>
	/// Registers the Veldrid graphics services, binding <see cref="GraphicsConfig"/> from <paramref name="config"/>.
	/// <see cref="WindowConfig"/> keeps its defaults; use the overload taking a window section to bind it.
	/// </summary>
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddVeldridGraphics(services, config, null, configureOptions);
	}

	/// <summary>
	/// Registers the Veldrid graphics services, binding <see cref="GraphicsConfig"/> from <paramref name="graphicsConfig"/>
	/// and, when given, <see cref="WindowConfig"/> from <paramref name="windowConfig"/>.
	/// </summary>
	public static IServiceCollection AddVeldridGraphics(this IServiceCollection services, IConfigurationSection graphicsConfig, IConfigurationSection? windowConfig, Action<GraphicsConfig>? configureOptions = null)
	{
		services.AddOptions<WindowConfig>();
		if (windowConfig is not null) services.Configure<WindowConfig>(windowConfig);

		services
			// Standard
			.Configure<GraphicsConfig>(graphicsConfig)

			// Implementation-specific. The engine systems depend on the concrete types; games resolve the interfaces,
			// which forward to the same singletons.
			.AddSingleton<Window>()
			.AddSingleton<IWindow>(sp => sp.GetRequiredService<Window>())
			.AddSingleton<GraphicsContext>()
			.AddSingleton<IGraphicsContext>(sp => sp.GetRequiredService<GraphicsContext>())
			.AddSingleton<SpriteRenderer>()
			.AddSingleton<FontRenderer>()
			.AddSingleton<ITexture2DManager, FontStashTexture2DManager>()
			.AddSingleton<SpriteBatch>()
			.AddSingleton<ISpriteBatch>(sp => sp.GetRequiredService<SpriteBatch>())
			.AddSingleton(static sp => new InputState(sp.GetRequiredService<Window>(), sp.GetRequiredService<IEventListener>(), sp.GetRequiredService<InputTracker>()))
			.AddSingleton<IInputState>(sp => sp.GetRequiredService<InputState>())

			// Loaders
			.AddSingleton<IAssetLoader, Texture2DLoader>()
			.AddSingleton<IAssetLoader, FontLoader>()

			// Implementation-specific Systems
			.AddSingleton<WindowSystem>()
			.AddSingleton<InputSystem>()
			.AddSingleton<GraphicsSystem>()
			.AddSingleton<SpriteBatchSystem>();

		services.AddInputTracker();

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
