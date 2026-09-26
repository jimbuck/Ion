using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Ion.Extensions.Rendering2D;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Registration of the headless rendering backend: an RHI backend without a surface (Vulkan or OpenGL ES, chosen from
/// <see cref="GraphicsConfig.PreferredBackend"/>), rendering every frame into an offscreen color (and depth) target sized
/// from <see cref="WindowConfig"/> (960x540 by default), the 2D renderer (<see cref="SpriteBatch"/> as
/// <see cref="ISpriteBatch"/>, with its texture and font loaders), and <see cref="IScreenshotSource"/> returning the last
/// frame as RGBA8 and saving it as PNG.
/// </summary>
/// <remarks>
/// <para>
/// With the <c>Ion</c> package this is what <c>AddIon</c> registers when <c>Ion:Headless=true</c> and
/// <c>Ion:Headless:Render=true</c>, on top of the null window and input (the null sprite batch stays registered as
/// <see cref="NullSpriteBatch"/> but no longer receives the game's draws).
/// </para>
/// <para>
/// Backends: <c>Ion:Graphics:PreferredBackend=Vulkan</c> needs a Vulkan driver (on Linux CI, Mesa lavapipe:
/// <c>mesa-vulkan-drivers</c>); <c>OpenGLES</c> needs EGL with an OpenGL ES 3 driver (on Linux CI, Mesa llvmpipe:
/// <c>libegl-mesa0</c>, no X server needed); <c>Auto</c> (the default) takes the first available in platform order (Vulkan
/// then OpenGL ES on desktop, OpenGL ES first on Linux arm64), so it runs on machines without Vulkan.
/// </para>
/// </remarks>
public static class HeadlessBuilderExtensions
{
	/// <summary>The configuration key that turns headless rendering on: <c>Ion:Headless:Render = true</c>.</summary>
	public const string RenderKey = "Ion:Headless:Render";

	/// <summary>True when <paramref name="config"/> asks for headless rendering (<see cref="RenderKey"/>).</summary>
	public static bool IsHeadlessRender(this IConfiguration config) => bool.TryParse(config[RenderKey], out var render) && render;

	/// <summary>
	/// Registers the null window and input (<c>AddNullGraphics</c>) and headless rendering (<see cref="AddHeadlessRendering"/>).
	/// </summary>
	public static IServiceCollection AddHeadlessGraphics(this IServiceCollection services, IConfiguration config) =>
		services.AddNullGraphics(config).AddHeadlessRendering(config);

	/// <summary>
	/// Registers the offscreen RHI device and frame driver (<see cref="RhiGraphics"/>, offscreen, as
	/// <see cref="IGraphicsFrame"/> and <see cref="IScreenshotSource"/>) and the 2D renderer (<c>AddRendering2D</c>, which
	/// replaces the null sprite batch mapping and the null texture and font loaders), on top of <c>AddNullGraphics</c>, which
	/// binds <see cref="GraphicsConfig"/> and <see cref="WindowConfig"/> (so options set in code there are kept).
	/// </summary>
	public static IServiceCollection AddHeadlessRendering(this IServiceCollection services, IConfiguration config)
	{
		ArgumentNullException.ThrowIfNull(config);
		services.Configure<GLES.GlesConfig>(config.GetSection("Ion:Graphics:Gles"));
		return services.AddRhiGraphicsServices(offscreen: true).AddRendering2D();
	}

	/// <summary>Adds the null graphics systems and the headless rendering systems.</summary>
	public static IIonApplication UseHeadlessGraphics(this IIonApplication app) => app.UseNullGraphics().UseHeadlessRendering();

	/// <summary>Adds the headless rendering systems: the device (Init, frame scope around Render, teardown) and the sprite batch.</summary>
	public static IIonApplication UseHeadlessRendering(this IIonApplication app) => app.UseRhiGraphics().UseRendering2D();
}
