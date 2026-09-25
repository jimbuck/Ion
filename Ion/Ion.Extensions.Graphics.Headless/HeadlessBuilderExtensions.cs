using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Registration of the headless rendering backend: an RHI backend without a surface (Vulkan or OpenGL ES, chosen from
/// <see cref="GraphicsConfig.PreferredBackend"/>), rendering every frame into an offscreen color (and depth) target sized
/// from <see cref="WindowConfig"/> (960x540 by default), with <see cref="IScreenshotSource"/> returning the last frame as
/// RGBA8 and saving it as PNG.
/// </summary>
/// <remarks>
/// <para>
/// With the <c>Ion</c> package this is what <c>AddIon</c> registers when <c>Ion:Headless=true</c> and
/// <c>Ion:Headless:Render=true</c>, on top of the null window, input and sprite batch (which stay the default headless mode
/// because they need no GPU).
/// </para>
/// <para>
/// Backends: <c>Ion:Graphics:PreferredBackend=Vulkan</c> (the default) needs a Vulkan driver (on Linux CI, Mesa lavapipe:
/// <c>mesa-vulkan-drivers</c>); <c>OpenGLES</c> needs EGL with an OpenGL ES 3 driver (on Linux CI, Mesa llvmpipe:
/// <c>libegl-mesa0</c>, no X server needed); <c>Auto</c> takes the first available in platform order (Vulkan then OpenGL ES
/// on desktop, OpenGL ES first on Linux arm64), so it runs on machines without Vulkan.
/// </para>
/// <para>
/// The 2D sprite batch is not ported to the RHI yet, so in this mode sprites are still recorded by the null sprite batch
/// rather than rasterized; systems that render with the RHI through <see cref="IGraphicsFrame"/> are.
/// </para>
/// </remarks>
public static class HeadlessBuilderExtensions
{
	/// <summary>The configuration key that turns headless rendering on: <c>Ion:Headless:Render = true</c>.</summary>
	public const string RenderKey = "Ion:Headless:Render";

	/// <summary>True when <paramref name="config"/> asks for headless rendering (<see cref="RenderKey"/>).</summary>
	public static bool IsHeadlessRender(this IConfiguration config) => bool.TryParse(config[RenderKey], out var render) && render;

	/// <summary>
	/// Registers the null window, input and sprite batch (<c>AddNullGraphics</c>) and headless rendering
	/// (<see cref="AddHeadlessRendering"/>).
	/// </summary>
	public static IServiceCollection AddHeadlessGraphics(this IServiceCollection services, IConfiguration config) =>
		services.AddNullGraphics(config).AddHeadlessRendering(config);

	/// <summary>
	/// Registers only the offscreen RHI device and frame driver (<see cref="RhiGraphics"/>, offscreen, as
	/// <see cref="IGraphicsFrame"/> and <see cref="IScreenshotSource"/>), on top of <c>AddNullGraphics</c>, which binds
	/// <see cref="GraphicsConfig"/> and <see cref="WindowConfig"/> (so options set in code there are kept).
	/// </summary>
	public static IServiceCollection AddHeadlessRendering(this IServiceCollection services, IConfiguration config)
	{
		ArgumentNullException.ThrowIfNull(config);
		services.Configure<GLES.GlesConfig>(config.GetSection("Ion:Graphics:Gles"));
		return services.AddRhiGraphicsServices(offscreen: true);
	}

	/// <summary>Adds the null graphics systems and the headless rendering system.</summary>
	public static IIonApplication UseHeadlessGraphics(this IIonApplication app) => app.UseNullGraphics().UseHeadlessRendering();

	/// <summary>Adds the headless rendering system (device at Init, frame scope around Render, teardown at Destroy).</summary>
	public static IIonApplication UseHeadlessRendering(this IIonApplication app) => app.UseRhiGraphics();
}
