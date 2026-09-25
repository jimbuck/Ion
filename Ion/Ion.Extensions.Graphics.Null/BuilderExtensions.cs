using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Assets;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Registration of the headless graphics backend: no GPU, no window, no SDL. It provides everything the Veldrid backend
/// does (<see cref="IWindow"/>, <see cref="IInputState"/>, <see cref="ISpriteBatch"/>, and loaders for
/// <see cref="ITexture2D"/> and <see cref="IFontSet"/>) so a game that depends only on those interfaces runs unchanged.
/// The concrete <see cref="NullWindow"/>, <see cref="NullInputState"/> and <see cref="NullSpriteBatch"/> are registered
/// too, for scripting input and asserting on what was drawn.
/// </summary>
public static class BuilderExtensions
{
	/// <summary>
	/// Registers the null graphics services, binding <see cref="GraphicsConfig"/> from <c>Ion:Graphics</c>
	/// and <see cref="WindowConfig"/> from <c>Ion:Window</c>.
	/// </summary>
	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		var ionSection = config.GetSection("Ion");
		return AddNullGraphics(services, ionSection.GetSection("Graphics"), ionSection.GetSection("Window"), configureOptions);
	}

	/// <summary>
	/// Registers the null graphics services, binding <see cref="GraphicsConfig"/> from <paramref name="config"/>.
	/// <see cref="WindowConfig"/> keeps its defaults; use the overload taking a window section to bind it.
	/// </summary>
	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfigurationSection config, Action<GraphicsConfig>? configureOptions = null)
	{
		return AddNullGraphics(services, config, null, configureOptions);
	}

	/// <summary>
	/// Registers the null graphics services, binding <see cref="GraphicsConfig"/> from <paramref name="graphicsConfig"/>
	/// and, when given, <see cref="WindowConfig"/> from <paramref name="windowConfig"/>.
	/// </summary>
	public static IServiceCollection AddNullGraphics(this IServiceCollection services, IConfigurationSection graphicsConfig, IConfigurationSection? windowConfig, Action<GraphicsConfig>? configureOptions = null)
	{
		services.AddOptions<WindowConfig>();
		services.AddOptions<GameConfig>();
		if (windowConfig is not null) services.Configure<WindowConfig>(windowConfig);

		services
			// Standard
			.Configure<GraphicsConfig>(graphicsConfig)

			// Implementation-specific, resolvable both as the concrete type (for tests) and as the interface (for games).
			.AddSingleton<NullWindow>()
			.AddSingleton<IWindow>(sp => sp.GetRequiredService<NullWindow>())
			.AddSingleton(static sp => new NullInputState(sp.GetRequiredService<InputTracker>()))
			.AddSingleton<IInputState>(sp => sp.GetRequiredService<NullInputState>())
			.AddSingleton<NullSpriteBatch>()
			.AddSingleton<ISpriteBatch>(sp => sp.GetRequiredService<NullSpriteBatch>())

			// Loaders
			.AddSingleton<IAssetLoader, NullTexture2DLoader>()
			.AddSingleton<IAssetLoader, NullFontLoader>()

			// Systems
			.AddSingleton<NullWindowSystem>()
			.AddSingleton<NullInputSystem>()
			.AddSingleton<NullSpriteBatchSystem>();

		services.AddInputTracker();

		if (configureOptions != null) services.Configure(configureOptions);

		return services;
	}

	/// <summary>
	/// Adds the null graphics systems: the window system (initial <see cref="WindowResizeEvent"/>, and
	/// <see cref="WindowClosedEvent"/> to <see cref="ExitGameEvent"/>), the input system (applies scripted
	/// <see cref="NullInputState"/> input in the First stage) and the sprite batch system (frames the Render stage).
	/// </summary>
	public static IIonApplication UseNullGraphics(this IIonApplication app)
	{
		return app
			.UseSystem<NullWindowSystem>()
			.UseSystem<NullInputSystem>()
			.UseSystem<NullSpriteBatchSystem>();
	}
}
