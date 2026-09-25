using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Ion.Extensions.Graphics.GLES;
using Ion.Extensions.Graphics.Rhi;
using Ion.Extensions.Graphics.Vulkan;

namespace Ion.Extensions.Graphics;

/// <summary>
/// The RHI graphics service that picks its backend at the graphics Init step: Vulkan or OpenGL ES, from
/// <see cref="GraphicsConfig.PreferredBackend"/> through a <see cref="GraphicsBackendSelector"/> (an explicit backend is
/// forced; <see cref="GraphicsBackend.Auto"/> takes the first available one in platform order). Windowed or offscreen.
/// Everything else is delegated to the chosen <see cref="VulkanGraphics"/> or <see cref="GlesGraphics"/>.
/// </summary>
public sealed class RhiGraphics : IRhiGraphics
{
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly ILoggerFactory _loggers;
	private readonly IServiceProvider _services;
	private IRhiGraphics? _inner;

	/// <summary>Creates the service; the backend is chosen and its device created by <see cref="Initialize"/>.</summary>
	public RhiGraphics(IOptionsMonitor<GraphicsConfig> graphicsConfig, IOptionsMonitor<WindowConfig> windowConfig, IOptionsMonitor<GameConfig> gameConfig, ILoggerFactory loggers, GraphicsBackendSelector selector, bool offscreen, IServiceProvider services)
	{
		_graphicsConfig = graphicsConfig;
		_windowConfig = windowConfig;
		_gameConfig = gameConfig;
		_loggers = loggers;
		Selector = selector;
		IsOffscreen = offscreen;
		_services = services;
	}

	/// <summary>The selector that resolves the preferred backend.</summary>
	public GraphicsBackendSelector Selector { get; }

	/// <summary>True for the headless (offscreen) variant.</summary>
	public bool IsOffscreen { get; }

	/// <summary>The backend in use, or <see cref="GraphicsBackend.Auto"/> before <see cref="Initialize"/>.</summary>
	public GraphicsBackend Backend { get; private set; } = GraphicsBackend.Auto;

	/// <summary>The chosen backend's service (<see cref="VulkanGraphics"/> or <see cref="GlesGraphics"/>), or null before <see cref="Initialize"/>.</summary>
	public IRhiGraphics? Inner => _inner;

	/// <inheritdoc/>
	public GraphicsFrameDriver? Driver => _inner?.Driver;

	/// <inheritdoc/>
	public IGraphicsDevice Device => _inner?.Device ?? throw new InvalidOperationException("The graphics device is created by the graphics Init step (StageOrder.Graphics); use it from a later step.");

	/// <summary>Resolves the backend and creates its device and frame driver.</summary>
	public void Initialize()
	{
		if (_inner is not null) return;
		// The options were post-configured with the resolved backend; the selector remembers what was configured.
		var preferred = _graphicsConfig.CurrentValue.PreferredBackend;
		Backend = Selector.Resolve(preferred);
		_loggers.CreateLogger<RhiGraphics>().LogInformation("Graphics backend: {Backend} (configured {Preferred}; Auto order {Order}), {Mode}.",
			Backend, Selector.FirstRequested ?? preferred, string.Join(", ", Selector.Candidates), IsOffscreen ? "offscreen" : "windowed");

		_inner = Backend switch
		{
			GraphicsBackend.OpenGLES => new GlesGraphics(_graphicsConfig, _windowConfig, _loggers, IsOffscreen ? GlesGraphicsMode.Offscreen : GlesGraphicsMode.Windowed, _services),
			_ => new VulkanGraphics(_graphicsConfig, _windowConfig, _gameConfig, _loggers, IsOffscreen ? VulkanGraphicsMode.Offscreen : VulkanGraphicsMode.Windowed, _services),
		};

		try
		{
			_inner.Initialize();
		}
		catch
		{
			_inner.Dispose();
			_inner = null;
			throw;
		}
	}

	/// <inheritdoc/>
	public void BeginFrame() => _inner?.BeginFrame();

	/// <inheritdoc/>
	public void EndFrame() => _inner?.EndFrame();

	/// <inheritdoc/>
	public bool IsRendering => _inner?.IsRendering ?? false;

	/// <inheritdoc/>
	public ITexture? ColorTarget => _inner?.ColorTarget;

	/// <inheritdoc/>
	public ITexture? DepthTarget => _inner?.DepthTarget;

	/// <inheritdoc/>
	public TextureFormat ColorFormat => _inner?.ColorFormat ?? TextureFormat.Undefined;

	/// <inheritdoc/>
	public TextureFormat DepthFormat => _inner?.DepthFormat ?? TextureFormat.Undefined;

	/// <inheritdoc/>
	public uint Width => _inner?.Width ?? 0;

	/// <inheritdoc/>
	public uint Height => _inner?.Height ?? 0;

	/// <inheritdoc/>
	public Vector4 ClearColor => _inner?.ClearColor ?? _graphicsConfig.CurrentValue.ClearColor.ToVector4();

	/// <inheritdoc/>
	public RenderPassColorAttachment ColorAttachment() => _require().ColorAttachment();

	/// <inheritdoc/>
	public RenderPassDepthStencilAttachment? DepthAttachment() => _require().DepthAttachment();

	/// <inheritdoc/>
	public Screenshot Capture() => _require().Capture();

	/// <inheritdoc/>
	public void SaveScreenshot(string path) => _require().SaveScreenshot(path);

	/// <summary>Destroys the chosen backend's frame resources and device.</summary>
	public void Dispose()
	{
		_inner?.Dispose();
		_inner = null;
	}

	private IRhiGraphics _require() => _inner ?? throw new InvalidOperationException("The graphics device has not been initialized yet.");
}

/// <summary>
/// Creates the selected backend's device at Init (<see cref="StageOrder.Graphics"/>, after the window), brackets every
/// Render stage with the frame begin and end, and destroys the device at Destroy (before the window).
/// </summary>
internal sealed class RhiGraphicsSystem(RhiGraphics graphics)
{
	[Init(Order = StageOrder.Graphics)]
	public void Init(GameTime dt) => graphics.Initialize();

	[Begin(Stage.Render, Order = StageOrder.Graphics)]
	public void BeginFrame(GameTime dt) => graphics.BeginFrame();

	[End(Stage.Render, Order = StageOrder.Graphics)]
	public void EndFrame(GameTime dt) => graphics.EndFrame();

	[Destroy(Order = StageOrder.WindowClose - 50)]
	public void Destroy(GameTime dt) => graphics.Dispose();
}

/// <summary>
/// Registration of the backend-selecting RHI graphics (<see cref="RhiGraphics"/>).
/// </summary>
public static class RhiGraphicsBuilderExtensions
{
	/// <summary>
	/// Registers <see cref="RhiGraphics"/> for a window (pair it with <c>AddSilkWindowing</c>): the backend comes from
	/// <c>Ion:Graphics:PreferredBackend</c> (<c>Vulkan</c>, <c>OpenGLES</c> or <c>Auto</c>), and the resolved backend is
	/// written back into <see cref="GraphicsConfig.PreferredBackend"/> so the window is created with the matching API (no
	/// API for Vulkan, an OpenGL ES context for OpenGL ES). Call <see cref="UseRhiGraphics"/> after <c>UseSilkWindowing</c>.
	/// </summary>
	public static IServiceCollection AddRhiGraphics(this IServiceCollection services, IConfiguration config) =>
		services.AddRhiGraphics(config, offscreen: false);

	/// <summary>Registers <see cref="RhiGraphics"/>, windowed or offscreen, binding <c>Ion:Graphics</c> and <c>Ion:Window</c>.</summary>
	public static IServiceCollection AddRhiGraphics(this IServiceCollection services, IConfiguration config, bool offscreen)
	{
		var ion = config.GetSection("Ion");
		services.Configure<GraphicsConfig>(ion.GetSection("Graphics"));
		services.Configure<WindowConfig>(ion.GetSection("Window"));
		services.Configure<GlesConfig>(ion.GetSection("Graphics:Gles"));
		return services.AddRhiGraphicsServices(offscreen);
	}

	/// <summary>
	/// Registers <see cref="RhiGraphics"/> without binding configuration (for modules that bind <see cref="GraphicsConfig"/>
	/// and <see cref="WindowConfig"/> themselves), with a <see cref="GraphicsBackendSelector"/> over Vulkan and OpenGL ES.
	/// Offscreen, OpenGL ES is available when a headless EGL context can be created; windowed, it is assumed available (the
	/// window creates the context).
	/// </summary>
	public static IServiceCollection AddRhiGraphicsServices(this IServiceCollection services, bool offscreen)
	{
		services.AddOptions<GameConfig>();
		services.AddOptions<WindowConfig>();
		services.AddOptions<GlesConfig>();
		services.AddOptions<GraphicsConfig>()
			.PostConfigure<GraphicsBackendSelector>(static (config, selector) => config.PreferredBackend = selector.Resolve(config.PreferredBackend));

		services
			.AddSingleton(_ => new GraphicsBackendSelector(new Dictionary<GraphicsBackend, Func<bool>>
			{
				[GraphicsBackend.Vulkan] = VulkanDevice.IsAvailable,
				[GraphicsBackend.OpenGLES] = offscreen ? GlesDevice.IsHeadlessAvailable : static () => true,
			}))
			.AddSingleton(sp => new RhiGraphics(
				sp.GetRequiredService<IOptionsMonitor<GraphicsConfig>>(),
				sp.GetRequiredService<IOptionsMonitor<WindowConfig>>(),
				sp.GetRequiredService<IOptionsMonitor<GameConfig>>(),
				sp.GetRequiredService<ILoggerFactory>(),
				sp.GetRequiredService<GraphicsBackendSelector>(),
				offscreen,
				sp))
			.AddSingleton<IRhiGraphics>(static sp => sp.GetRequiredService<RhiGraphics>())
			.AddSingleton<IGraphicsFrame>(static sp => sp.GetRequiredService<RhiGraphics>())
			.AddSingleton<IScreenshotSource>(static sp => sp.GetRequiredService<RhiGraphics>())
			.AddSingleton<RhiGraphicsSystem>();
		return services;
	}

	/// <summary>Adds the RHI graphics system (device at Init, frame scope around Render, teardown at Destroy).</summary>
	public static IIonApplication UseRhiGraphics(this IIonApplication app) => app.UseSystem<RhiGraphicsSystem>();
}
