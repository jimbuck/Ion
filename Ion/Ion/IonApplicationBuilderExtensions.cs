using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;
using Ion.Extensions.Rendering3D;

namespace Ion;

/// <summary>
/// The engine's modules on the application builder and the application: <c>builder.AddX()</c> registers a module with the
/// application's configuration, <c>app.UseX()</c> adds its systems. Each one registers (or adds) what it depends on, and
/// calling one again, directly or through another module, only applies its options: list the modules a game uses in any
/// order.
/// </summary>
/// <remarks>
/// The dependencies: every module needs the engine core (<see cref="AddIon"/>/<see cref="BuilderExtensions.UseIon"/>:
/// metrics, assets, graphics and input, audio, scenes, coroutines, the remote protocol). The 3D renderer
/// (<see cref="AddRendering3D"/>) draws with the core graphics. The modules of other packages (ECS, ECS rendering, UI,
/// physics, networking, web) document theirs.
/// </remarks>
public static class IonApplicationBuilderExtensions
{
	/// <summary>
	/// Registers the engine core with the application's configuration (see
	/// <see cref="BuilderExtensions.AddIon(IServiceCollection, IConfiguration, Action{GraphicsConfig})"/>): metrics, assets,
	/// graphics and input (windowed, or headless with <c>--headless</c>), audio, scenes, coroutines and the remote
	/// protocol. Add the systems with <see cref="BuilderExtensions.UseIon"/>.
	/// </summary>
	/// <param name="builder">The application builder.</param>
	/// <param name="configure">Graphics options (for example the clear color), applied after <c>Ion:Graphics</c> is bound.</param>
	public static IonApplicationBuilder AddIon(this IonApplicationBuilder builder, Action<GraphicsConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.Services.AddIon(builder.Configuration, configure);
		return builder;
	}

	/// <summary>
	/// Registers the 3D renderer (<see cref="Rendering3DBuilderExtensions.AddRendering3D"/>, options bound from
	/// <c>Ion:Rendering3D</c>) and the engine core it draws with. Add the systems with <see cref="UseRendering3D"/>.
	/// </summary>
	/// <param name="builder">The application builder.</param>
	/// <param name="configure">Renderer options, applied after <c>Ion:Rendering3D</c> is bound.</param>
	public static IonApplicationBuilder AddRendering3D(this IonApplicationBuilder builder, Action<Rendering3DOptions>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddIon();
		builder.Services.AddRendering3D(builder.Configuration);
		if (configure is not null) builder.Services.Configure(configure);
		return builder;
	}

	/// <summary>
	/// Adds the 3D renderer's system (see <see cref="Rendering3DBuilderExtensions.UseRendering3D"/>) and the engine's
	/// systems (<see cref="BuilderExtensions.UseIon"/>).
	/// </summary>
	public static IIonApplication UseRendering3D(this IIonApplication app)
	{
		ArgumentNullException.ThrowIfNull(app);
		app.UseIon();
		return Rendering3DBuilderExtensions.UseRendering3D(app);
	}

	/// <summary>
	/// Registers the engine core (the audio is part of it: OpenAL, or the null output when headless) and applies
	/// <paramref name="configure"/> to <see cref="AudioConfig"/> after <c>Ion:Audio</c> is bound.
	/// </summary>
	public static IonApplicationBuilder AddAudio(this IonApplicationBuilder builder, Action<AudioConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddIon();
		if (configure is not null) builder.Services.Configure(configure);
		return builder;
	}

	/// <summary>
	/// Registers the engine core (the metrics module is part of it) and applies <paramref name="configure"/> to
	/// <see cref="MetricsConfig"/> after <c>Ion:Metrics</c> is bound.
	/// </summary>
	public static IonApplicationBuilder AddMetrics(this IonApplicationBuilder builder, Action<MetricsConfig>? configure = null)
	{
		ArgumentNullException.ThrowIfNull(builder);
		builder.AddIon();
		if (configure is not null) builder.Services.Configure(configure);
		return builder;
	}

	/// <summary>Registers the engine core, which includes the scenes module (<c>UseScene</c> adds scenes to the schedule).</summary>
	public static IonApplicationBuilder AddScenes(this IonApplicationBuilder builder) => builder.AddIon();

	/// <summary>Registers the engine core, which includes the coroutine runner (<see cref="BuilderExtensions.UseIon"/> adds its system).</summary>
	public static IonApplicationBuilder AddCoroutines(this IonApplicationBuilder builder) => builder.AddIon();

	/// <summary>Registers the engine core, which includes the asset managers and asset hot reload (<c>Ion:Assets:HotReload</c>).</summary>
	public static IonApplicationBuilder AddAssets(this IonApplicationBuilder builder) => builder.AddIon();

	/// <summary>
	/// Registers the engine core, which includes the remote inspection protocol (it runs when <c>Ion:Remote:Enabled</c> is
	/// true: <c>--remote</c>).
	/// </summary>
	public static IonApplicationBuilder AddRemote(this IonApplicationBuilder builder) => builder.AddIon();
}
