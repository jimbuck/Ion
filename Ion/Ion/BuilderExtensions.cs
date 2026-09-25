using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Debug;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.Audio;
using Ion.Extensions.Scenes;
using Ion.Extensions.Coroutines;

namespace Ion;

public static class BuilderExtensions
{
	/// <summary>
	/// The configuration key that selects the headless backends: <c>Ion:Headless = true</c>. From the command line,
	/// <c>--Ion:Headless=true</c>.
	/// </summary>
	public const string HeadlessKey = "Ion:Headless";

	/// <summary>
	/// Registers every Ion extension: debug utilities, assets, graphics, audio, scenes and coroutines.
	/// </summary>
	/// <remarks>
	/// Graphics and audio are chosen from configuration. When <c>Ion:Headless</c> is <c>true</c>, or the graphics output is
	/// <see cref="GraphicsOutput.None"/> (<c>Ion:Graphics:Output = None</c>, or <paramref name="configureOptions"/> setting
	/// <see cref="GraphicsConfig.Output"/> to <see cref="GraphicsOutput.None"/>), the headless backends are registered:
	/// <c>AddNullGraphics</c> (no GPU, window or SDL) and <c>AddNullAudio</c> (no audio device). Otherwise the Veldrid graphics
	/// and OpenAL audio backends are registered (audio falls back to the null output when no device is available). <see cref="UseIon"/> adds the systems of whichever was chosen.
	/// A game that depends only on the interfaces (<see cref="IWindow"/>, <see cref="IInputState"/>, <see cref="ISpriteBatch"/>,
	/// <see cref="IAudioManager"/>, and assets loaded with <c>Load&lt;ITexture2D&gt;</c>, <c>Load&lt;IFontSet&gt;</c> and
	/// <c>Load&lt;ISoundEffect&gt;</c>) runs unchanged on both.
	/// </remarks>
	/// <param name="services">The service collection.</param>
	/// <param name="config">The application configuration; <c>Ion:*</c> sections are bound from it.</param>
	/// <param name="configureOptions">
	/// Optional graphics configuration applied after binding. It is also run once on a scratch <see cref="GraphicsConfig"/>
	/// to see whether it selects <see cref="GraphicsOutput.None"/>, so it should have no side effects.
	/// </param>
	public static IServiceCollection AddIon(this IServiceCollection services, IConfiguration config, Action<GraphicsConfig>? configureOptions = null)
	{
		var headless = IsHeadless(config, configureOptions);

		services
			.AddDebugUtils(config)
			.AddAssets();

		if (headless)
		{
			services
				.AddNullGraphics(config, configureOptions)
				.AddNullAudio(config);
		}
		else
		{
			services
				.AddVeldridGraphics(config, configureOptions)
				.AddAudio(config);
		}

		services.AddSingleton(new IonBackendSelection(headless));

		return services
			.AddScenes()
			.AddCoroutines();
	}

	/// <summary>
	/// Adds the systems of every Ion extension registered by <see cref="AddIon"/>, using the headless graphics and audio
	/// systems when <see cref="AddIon"/> chose them.
	/// </summary>
	public static IIonApplication UseIon(this IIonApplication app)
	{
		var headless = app.Services.GetService<IonBackendSelection>()?.Headless ?? IsHeadless(app.Configuration);

		app
			.UseDebugUtils()
			.UseEvents()
			.UseAssets();

		if (headless)
		{
			app
				.UseNullGraphics()
				.UseNullAudio();
		}
		else
		{
			app
				.UseVeldridGraphics()
				.UseAudio();
		}

		return app.UseCoroutines();
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

	private sealed record IonBackendSelection(bool Headless);
}
