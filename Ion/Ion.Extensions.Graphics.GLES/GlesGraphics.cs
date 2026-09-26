using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Silk.NET.Core.Contexts;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// Where a <see cref="GlesGraphics"/> renders.
/// </summary>
public enum GlesGraphicsMode
{
	/// <summary>
	/// Into the window (<see cref="IWindowSurface"/>, from the Silk.NET windowing module), through the window's GL context.
	/// The window must be created with the OpenGL ES API: <c>Ion:Graphics:PreferredBackend = OpenGLES</c>.
	/// </summary>
	Windowed,
	/// <summary>Into an offscreen target sized from <see cref="WindowConfig"/>, on a headless EGL context (the headless backend).</summary>
	Offscreen,
}

/// <summary>
/// OpenGL ES backend options, bound from <c>Ion:Graphics:Gles</c>.
/// </summary>
public sealed class GlesConfig
{
	/// <summary>
	/// The highest feature level to use (<c>Es30</c>, <c>Es31</c> or <c>Es32</c>). Lower it to force the ES 3.0 or 3.1
	/// paths on a newer driver, for example to check on a desktop what an ES 3.0 device will run.
	/// </summary>
	public GlesFeatureLevel MaxFeatureLevel { get; set; } = GlesFeatureLevel.Es32;
}

/// <summary>
/// The OpenGL ES graphics service: creates the <see cref="GlesDevice"/> at the graphics Init step and drives every frame
/// through a <see cref="GraphicsFrameDriver"/> (the same driver as the Vulkan backend). Resolve it as
/// <see cref="IGraphicsFrame"/> to render with the RHI and as <see cref="IScreenshotSource"/> to capture frames.
/// </summary>
public sealed class GlesGraphics : IRhiGraphics
{
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly ILoggerFactory _loggers;
	private readonly IServiceProvider _services;
	private GlesDevice? _device;
	private GraphicsFrameDriver? _driver;

	/// <summary>Creates the service; the device is created by <see cref="Initialize"/>.</summary>
	public GlesGraphics(IOptionsMonitor<GraphicsConfig> graphicsConfig, IOptionsMonitor<WindowConfig> windowConfig, ILoggerFactory loggers, GlesGraphicsMode mode, IServiceProvider services)
	{
		_graphicsConfig = graphicsConfig;
		_windowConfig = windowConfig;
		_loggers = loggers;
		_services = services;
		Mode = mode;
	}

	/// <summary>Windowed or offscreen.</summary>
	public GlesGraphicsMode Mode { get; }

	/// <summary>
	/// The highest feature level the device may use (lower it to run the ES 3.0 or 3.1 paths on a newer driver). Set before
	/// <see cref="Initialize"/>; the lower of this and <see cref="GlesConfig.MaxFeatureLevel"/> is used.
	/// </summary>
	public GlesFeatureLevel MaxFeatureLevel { get; set; } = GlesFeatureLevel.Es32;

	/// <summary>The device, or null before <see cref="Initialize"/>.</summary>
	public GlesDevice? GlesDevice => _device;

	/// <inheritdoc/>
	public GraphicsFrameDriver? Driver => _driver;

	/// <inheritdoc/>
	public IGraphicsDevice Device => _device ?? throw new InvalidOperationException("The OpenGL ES device is created by the graphics Init step (StageOrder.Graphics); use it from a later step.");

	/// <summary>Creates the device (on the window's GL context, or a headless EGL context) and the frame driver.</summary>
	public void Initialize()
	{
		if (_device is not null) return;
		var config = _graphicsConfig.CurrentValue;
		var logger = _loggers.CreateLogger<GlesGraphics>();
		if (config.PreferredBackend is not (GraphicsBackend.OpenGLES or GraphicsBackend.Auto))
		{
			logger.LogWarning("PreferredBackend is {Backend}; the OpenGL ES backend is registered, so OpenGL ES is used.", config.PreferredBackend);
		}

		IWindowSurface? window = null;
		IGlesContext? context = null;
		if (Mode == GlesGraphicsMode.Windowed)
		{
			window = _services.GetService<IWindowSurface>() ?? throw new InvalidOperationException("The windowed OpenGL ES backend needs a window module (AddSilkWindowing).");
			if (!window.IsCreated) throw new InvalidOperationException("The window has not been created; its Init step (StageOrder.Window) must run before the graphics Init step.");
			var glContext = (window.PlatformWindow as IGLContextSource)?.GLContext
				?? throw new InvalidOperationException("The window has no GL context: create it with the OpenGL ES API (Ion:Graphics:PreferredBackend = OpenGLES, or AddRhiGraphics, which sets it).");
			context = new SilkGlesContext(glContext);
		}

		_device = GlesDevice.Create(new GlesDeviceOptions
		{
			Context = context,
			FramesInFlight = config.FramesInFlight,
			Validation = config.Validation ?? DefaultValidation,
			MaxFeatureLevel = (GlesFeatureLevel)Math.Min((int)MaxFeatureLevel, (int)(_services.GetService<IOptionsMonitor<GlesConfig>>()?.CurrentValue.MaxFeatureLevel ?? GlesFeatureLevel.Es32)),
		}, _loggers.CreateLogger<GlesDevice>());

		var windowConfig = _windowConfig.CurrentValue;
		var width = (uint)(windowConfig.Width is > 0 ? windowConfig.Width.Value : 960);
		var height = (uint)(windowConfig.Height is > 0 ? windowConfig.Height.Value : 540);
		_driver = new GraphicsFrameDriver(_device, config, window, width, height);
	}

	/// <summary>Validation default when <see cref="GraphicsConfig.Validation"/> is null: on in Debug builds of this assembly.</summary>
	public static bool DefaultValidation =>
#if DEBUG
		true;
#else
		false;
#endif

	/// <inheritdoc/>
	public void BeginFrame() => _driver?.BeginFrame();

	/// <inheritdoc/>
	public void EndFrame() => _driver?.EndFrame();

	/// <inheritdoc/>
	public bool IsRendering => _driver?.IsRendering ?? false;

	/// <inheritdoc/>
	public ITexture? ColorTarget => _driver?.ColorTarget;

	/// <inheritdoc/>
	public ITexture? DepthTarget => _driver?.DepthTarget;

	/// <inheritdoc/>
	public TextureFormat ColorFormat => _driver?.ColorFormat ?? TextureFormat.Undefined;

	/// <inheritdoc/>
	public TextureFormat DepthFormat => _driver?.DepthFormat ?? TextureFormat.Undefined;

	/// <inheritdoc/>
	public uint Width => _driver?.Width ?? 0;

	/// <inheritdoc/>
	public uint Height => _driver?.Height ?? 0;

	/// <inheritdoc/>
	public Vector4 ClearColor => _driver?.ClearColor ?? _graphicsConfig.CurrentValue.ClearColor.ToVector4();

	/// <inheritdoc/>
	public RenderPassColorAttachment ColorAttachment() => _requireDriver().ColorAttachment();

	/// <inheritdoc/>
	public RenderPassDepthStencilAttachment? DepthAttachment() => _requireDriver().DepthAttachment();

	/// <inheritdoc/>
	public Screenshot Capture() => _requireDriver().Capture();

	/// <inheritdoc/>
	public void SaveScreenshot(string path) => _requireDriver().SaveScreenshot(path);

	/// <summary>Destroys the frame resources and the device (waiting for the GPU).</summary>
	public void Dispose()
	{
		_driver?.Dispose();
		_driver = null;
		_device?.Dispose();
		_device = null;
	}

	private GraphicsFrameDriver _requireDriver() => _driver ?? throw new InvalidOperationException("The OpenGL ES device has not been initialized yet.");
}

/// <summary>
/// Creates the OpenGL ES device at Init (<see cref="StageOrder.Graphics"/>, after the window), brackets every Render stage
/// with the frame begin and end (a scope at <see cref="StageOrder.Graphics"/>), and destroys the device at Destroy (before
/// the window).
/// </summary>
internal sealed class GlesGraphicsSystem(GlesGraphics graphics)
{
	[Init(Order = StageOrder.Graphics)]
	public void Init(GameTime dt) => graphics.Initialize();

	[Begin(Stage.Render, Order = StageOrder.Graphics)]
	public void BeginFrame(GameTime dt) => graphics.BeginFrame();

	[End(Stage.Render, Order = StageOrder.Graphics)]
	public void EndFrame(GameTime dt) => graphics.EndFrame();

	/// <summary>The order of the device teardown in the Destroy stage: after the game's own Destroy steps, before the window.</summary>
	public const int DestroyOrder = StageOrder.WindowClose - 50;

	[Destroy(Order = DestroyOrder)]
	public void Destroy(GameTime dt) => graphics.Dispose();
}

/// <summary>
/// Registration of the OpenGL ES graphics backend.
/// </summary>
public static class GlesBuilderExtensions
{
	/// <summary>
	/// Registers the windowed OpenGL ES backend: <see cref="GlesGraphics"/> as <see cref="IGraphicsFrame"/> and
	/// <see cref="IScreenshotSource"/>, binding <see cref="GraphicsConfig"/> from <c>Ion:Graphics</c> and
	/// <see cref="WindowConfig"/> from <c>Ion:Window</c>, and making the window use the OpenGL ES API. Needs a window module
	/// (<c>AddSilkWindowing</c>); call <see cref="UseGlesGraphics"/> after <c>UseSilkWindowing</c>.
	/// </summary>
	public static IServiceCollection AddGlesGraphics(this IServiceCollection services, IConfiguration config) =>
		services.AddGlesGraphics(config, GlesGraphicsMode.Windowed);

	/// <summary>Registers the OpenGL ES backend in <paramref name="mode"/>.</summary>
	public static IServiceCollection AddGlesGraphics(this IServiceCollection services, IConfiguration config, GlesGraphicsMode mode)
	{
		var ion = config.GetSection("Ion");
		services.Configure<GraphicsConfig>(ion.GetSection("Graphics"));
		services.Configure<WindowConfig>(ion.GetSection("Window"));
		services.Configure<GlesConfig>(ion.GetSection("Graphics:Gles"));
		return services.AddGlesGraphicsServices(mode);
	}

	/// <summary>
	/// Registers the OpenGL ES backend services in <paramref name="mode"/> without binding configuration, for modules that
	/// already bind <see cref="GraphicsConfig"/> and <see cref="WindowConfig"/>. Windowed, it sets
	/// <see cref="GraphicsConfig.PreferredBackend"/> to <see cref="GraphicsBackend.OpenGLES"/> after configuration so the
	/// window is created with a GL ES context.
	/// </summary>
	public static IServiceCollection AddGlesGraphicsServices(this IServiceCollection services, GlesGraphicsMode mode)
	{
		services.AddOptions<GameConfig>();
		services.AddOptions<GraphicsConfig>();
		services.AddOptions<WindowConfig>();
		services.AddOptions<GlesConfig>();
		if (mode == GlesGraphicsMode.Windowed) services.PostConfigure<GraphicsConfig>(static c => c.PreferredBackend = GraphicsBackend.OpenGLES);

		services
			.AddSingleton(sp => new GlesGraphics(
				sp.GetRequiredService<IOptionsMonitor<GraphicsConfig>>(),
				sp.GetRequiredService<IOptionsMonitor<WindowConfig>>(),
				sp.GetRequiredService<ILoggerFactory>(),
				mode,
				sp))
			.AddSingleton<IGraphicsFrame>(static sp => sp.GetRequiredService<GlesGraphics>())
			.AddSingleton<IScreenshotSource>(static sp => sp.GetRequiredService<GlesGraphics>())
			.AddSingleton<GlesGraphicsSystem>();
		return services;
	}

	/// <summary>Adds the OpenGL ES graphics system (device at Init, frame scope around Render, teardown at Destroy).</summary>
	public static IIonApplication UseGlesGraphics(this IIonApplication app) => app.UseSystem<GlesGraphicsSystem>();
}
