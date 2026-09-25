using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;

namespace Ion.Extensions.Graphics;

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
		if (windowConfig is not null) services.Configure<WindowConfig>(windowConfig);

		services
			// Standard
			.Configure<GraphicsConfig>(graphicsConfig)

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
