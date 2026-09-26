using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Metrics;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;
using Ion.Extensions.Rendering2D;
using Ion.Extensions.Scenes;
using Ion.Extensions.Coroutines;
using Ion.Extensions.Windowing;
using Ion.Extensions.Remote;

using Microsoft.Extensions.Logging;

namespace Ion;

public static class BuilderExtensions
{
	/// <summary>
	/// The configuration key that selects the headless backends: <c>Ion:Headless = true</c>. From the command line,
	/// <c>--Ion:Headless=true</c>.
	/// </summary>
	public const string HeadlessKey = "Ion:Headless";

	/// <summary>
	/// Registers every Ion extension: metrics, assets, graphics, audio, scenes and coroutines.
	/// </summary>
	/// <remarks>
	/// Graphics and audio are chosen from configuration. When <c>Ion:Headless</c> is <c>true</c>, or the graphics output is
	/// <see cref="GraphicsOutput.None"/> (<c>Ion:Graphics:Output = None</c>, or <paramref name="configureOptions"/> setting
	/// <see cref="GraphicsConfig.Output"/> to <see cref="GraphicsOutput.None"/>), the headless backends are registered:
	/// <c>AddNullGraphics</c> (no GPU or window, a recording sprite batch) and <c>AddNullAudio</c> (no audio device); with
	/// <c>Ion:Headless:Render = true</c> as well, headless rendering replaces the recording sprite batch
	/// (<c>AddHeadlessRendering</c>: the selected RHI backend into an offscreen target, the 2D renderer and its texture and
	/// font loaders, and <see cref="IScreenshotSource"/>; needs a Vulkan driver such as Mesa lavapipe, or EGL with OpenGL ES).
	/// Otherwise the windowed stack is registered by <see cref="AddGraphics"/> (Silk.NET window and input, the RHI backend
	/// selected from <see cref="GraphicsConfig.PreferredBackend"/>, the 2D renderer) with OpenAL audio (which falls back to the null output when no
	/// device is available). <see cref="UseIon"/> adds the systems of whichever was chosen.
	/// A game that depends only on the interfaces (<see cref="IWindow"/>, <see cref="IInputState"/>, <see cref="ISpriteBatch"/>,
	/// <see cref="IAudioManager"/>, and assets loaded with <c>Load&lt;ITexture2D&gt;</c>, <c>Load&lt;IFontSet&gt;</c> and
	/// <c>Load&lt;ISoundEffect&gt;</c>) runs unchanged on all of them.
	/// </remarks>
	/// <param name="services">The service collection.</param>
	/// <param name="config">The application configuration; <c>Ion:*</c> sections are bound from it.</param>
	/// <param name="configureOptions">
	/// Optional graphics configuration applied after binding. It is also run once on a scratch <see cref="GraphicsConfig"/>
	/// to see whether it selects <see cref="GraphicsOutput.None"/> and which backend it prefers, so it should have no side
	/// effects.
	/// </param>
	public static IServiceCollection AddIon(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		var headless = IsHeadless(config, configureOptions);

		services
			.AddMetrics(config)
			.AddAssets();

		if (headless)
		{
			services
				.AddNullGraphics(config, configureOptions)
				.AddNullAudio(config);

			if (config.IsHeadlessRender())
			{
				ThrowIfReservedBackend(config, configureOptions);
				services.AddHeadlessRendering(config);
			}

			services.AddSingleton(new IonBackendSelection(true, config.IsHeadlessRender()));
		}
		else
		{
			services
				.AddGraphics(config, configureOptions)
				.AddAudio(config);
		}

		// Agent and CI runs (ion run): a deterministic clock for headless fixed-length runs, and the screenshot and summary.
		if (IonRun.UsesFixedStep(config, headless)) services.AddSingleton<IClock>(new FixedStepClock(IonRun.FrameTime));
		if (IonRun.WantsReport(config))
		{
			var logs = new RunLogCollector();
			services.AddSingleton<ILoggerProvider>(logs);
			services.AddSingleton(sp => new RunReportSystem(sp, config, logs));
		}

		return services
			.AddScenes()
			.AddCoroutines()
			// The remote inspection protocol: registers nothing unless Ion:Remote:Enabled (--remote), and compiled out of
			// Release builds unless IonRemote=true.
			.AddRemote(config);
	}

	/// <summary>
	/// Adds the systems of every Ion extension registered by <see cref="AddIon"/>, using the headless graphics and audio
	/// systems when <see cref="AddIon"/> chose them.
	/// </summary>
	public static IIonApplication UseIon(this IIonApplication app)
	{
		var selection = app.Services.GetService<IonBackendSelection>();
		var headless = selection?.Headless ?? IsHeadless(app.Configuration);
		var render = selection?.Render ?? (headless && app.Configuration.IsHeadlessRender());

		app
			.UseMetrics()
			.UseEvents()
			.UseAssets();

		if (headless)
		{
			app
				.UseNullGraphics()
				.UseNullAudio();

			if (render) app.UseHeadlessRendering();
		}
		else
		{
			app
				.UseGraphics()
				.UseAudio();
		}

		if (app.Services.GetService<RunReportSystem>() is not null) app.UseSystem<RunReportSystem>();

		return app
			.UseCoroutines()
			.UseRemote();
	}

	/// <summary>
	/// Registers the windowed graphics stack: the Silk.NET window and input (<c>AddSilkWindowing</c>), the RHI backend
	/// selected from <see cref="GraphicsConfig.PreferredBackend"/> (<c>AddRhiGraphics</c>: <see cref="GraphicsBackend.Auto"/>
	/// takes the first available of Vulkan and OpenGL ES in platform order, <see cref="GraphicsBackend.Vulkan"/> and
	/// <see cref="GraphicsBackend.OpenGLES"/> force one) and the 2D renderer (<c>AddRendering2D</c>: <see cref="ISpriteBatch"/>
	/// and the <see cref="ITexture2D"/> and <see cref="IFontSet"/> loaders). Binds <see cref="GraphicsConfig"/> from
	/// <c>Ion:Graphics</c> and <see cref="WindowConfig"/> from <c>Ion:Window</c>, then applies
	/// <paramref name="configureOptions"/>. Add the systems with <see cref="UseGraphics"/>.
	/// </summary>
	/// <exception cref="NotSupportedException">A reserved backend is preferred (<see cref="GraphicsBackend.Direct3D12"/>, <see cref="GraphicsBackend.Metal"/>, <see cref="GraphicsBackend.WebGPU"/>).</exception>
	public static IServiceCollection AddGraphics(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		ThrowIfReservedBackend(config, configureOptions);

		services.AddSilkWindowing(config);
		services.AddRhiGraphics(config);
		services.AddRendering2D();

		if (configureOptions is not null) services.Configure(configureOptions);
		services.AddSingleton(new IonBackendSelection(false, false));
		return services;
	}

	/// <summary>
	/// Adds the systems of the windowed graphics stack registered by <see cref="AddGraphics"/>: window and input, the RHI
	/// backend's device and frame scope, and the sprite batch.
	/// </summary>
	public static IIonApplication UseGraphics(this IIonApplication app) =>
		app.UseSilkWindowing()
			.UseRhiGraphics()
			.UseRendering2D();

	/// <summary>
	/// Throws for the reserved backends (<see cref="GraphicsBackend.Direct3D12"/>, <see cref="GraphicsBackend.Metal"/> and
	/// <see cref="GraphicsBackend.WebGPU"/>, which have no implementation) set in <c>Ion:Graphics:PreferredBackend</c> or by
	/// <paramref name="configureOptions"/>.
	/// </summary>
	/// <exception cref="NotSupportedException">A reserved backend is preferred.</exception>
	public static void ThrowIfReservedBackend(IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		ArgumentNullException.ThrowIfNull(config);
		var graphics = new GraphicsConfig();
		if (Enum.TryParse<GraphicsBackend>(config["Ion:Graphics:PreferredBackend"], ignoreCase: true, out var configured)) graphics.PreferredBackend = configured;
		configureOptions?.Invoke(graphics);
		if (graphics.PreferredBackend is GraphicsBackend.Direct3D12 or GraphicsBackend.Metal or GraphicsBackend.WebGPU)
		{
			throw new NotSupportedException($"Graphics backend {graphics.PreferredBackend} is reserved and has no implementation; use Auto, Vulkan or OpenGLES.");
		}
	}

	/// <summary>
	/// True when <paramref name="config"/> asks for the headless backends: <c>Ion:Headless</c> is <c>true</c>, or
	/// <c>Ion:Graphics:Output</c> is <c>None</c>.
	/// </summary>
	public static bool IsHeadless(this IConfiguration config) => IsHeadless(config, null);

	private static bool IsHeadless(IConfiguration config, Action<GraphicsConfig>? configureOptions)
	{
		if (bool.TryParse(config[HeadlessKey], out var headless) && headless) return true;

		var graphics = new GraphicsConfig();
		if (Enum.TryParse<GraphicsOutput>(config["Ion:Graphics:Output"], ignoreCase: true, out var output)) graphics.Output = output;

		configureOptions?.Invoke(graphics);

		return graphics.Output == GraphicsOutput.None;
	}

	private sealed record IonBackendSelection(bool Headless, bool Render);
}
