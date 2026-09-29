using System.Numerics;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Silk.NET.Core.Contexts;

using Ion.Extensions.Graphics.Rhi;

namespace Ion.Extensions.Graphics.Vulkan;

/// <summary>
/// Where a <see cref="VulkanGraphics"/> renders.
/// </summary>
public enum VulkanGraphicsMode
{
	/// <summary>Into the swapchain of the window (<see cref="IWindowSurface"/>, from the Silk.NET windowing module).</summary>
	Windowed,
	/// <summary>Into an offscreen target sized from <see cref="WindowConfig"/> (the headless backend).</summary>
	Offscreen,
}

/// <summary>
/// The Vulkan graphics service: creates the <see cref="VulkanDevice"/> at the graphics Init step and drives every frame
/// through a <see cref="GraphicsFrameDriver"/>. Resolve it as <see cref="IGraphicsFrame"/> to render with the RHI and as
/// <see cref="IScreenshotSource"/> to capture frames.
/// </summary>
/// <remarks>
/// <see cref="Device"/> exists from the graphics Init step (<see cref="StageOrder.Graphics"/>) on; systems create their GPU
/// resources in an Init step with a higher order (the default order 0 is fine) or later.
/// </remarks>
public sealed class VulkanGraphics : IRhiGraphics
{
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly ILoggerFactory _loggers;
	private readonly IServiceProvider _services;
	private VulkanDevice? _device;
	private GraphicsFrameDriver? _driver;

	/// <summary>Creates the service; the device is created by <see cref="Initialize"/>.</summary>
	public VulkanGraphics(IOptionsMonitor<GraphicsConfig> graphicsConfig, IOptionsMonitor<WindowConfig> windowConfig, IOptionsMonitor<GameConfig> gameConfig, ILoggerFactory loggers, VulkanGraphicsMode mode, IServiceProvider services)
	{
		_graphicsConfig = graphicsConfig;
		_windowConfig = windowConfig;
		_gameConfig = gameConfig;
		_loggers = loggers;
		_services = services;
		Mode = mode;
	}

	/// <summary>Windowed or offscreen.</summary>
	public VulkanGraphicsMode Mode { get; }

	/// <summary>The device, or null before <see cref="Initialize"/>.</summary>
	public VulkanDevice? VulkanDevice => _device;

	/// <summary>The frame driver, or null before <see cref="Initialize"/>.</summary>
	public GraphicsFrameDriver? Driver => _driver;

	/// <inheritdoc/>
	public IGraphicsDevice Device => _device ?? throw new InvalidOperationException("The Vulkan device is created by the graphics Init step (StageOrder.Graphics); use it from a later step.");

	/// <summary>
	/// Creates the device (and, windowed, the surface on the window created by the window system's Init step) and the frame
	/// driver.
	/// </summary>
	public void Initialize()
	{
		if (_device is not null) return;
		var config = _graphicsConfig.CurrentValue;
		if (config.PreferredBackend is not (GraphicsBackend.Vulkan or GraphicsBackend.Auto))
		{
			_loggers.CreateLogger<VulkanGraphics>().LogWarning("PreferredBackend is {Backend}; the Vulkan backend is registered, so Vulkan is used.", config.PreferredBackend);
		}

		IWindowSurface? window = null;
		IVkSurface? surface = null;
		if (Mode == VulkanGraphicsMode.Windowed)
		{
			window = _services.GetService<IWindowSurface>() ?? throw new InvalidOperationException("The windowed Vulkan backend needs a window module (AddSilkWindowing).");
			if (!window.IsCreated) throw new InvalidOperationException("The window has not been created; its Init step (StageOrder.Window) must run before the graphics Init step.");
			surface = (window.PlatformWindow as IVkSurfaceSource)?.VkSurface ?? throw new InvalidOperationException("The window cannot create a Vulkan surface.");
		}

		_device = VulkanDevice.Create(new VulkanDeviceOptions
		{
			Surface = surface,
			FramesInFlight = config.FramesInFlight,
			Validation = config.Validation ?? DefaultValidation,
			Adapter = config.Adapter,
			ApplicationName = _gameConfig.CurrentValue.Title,
		}, _loggers.CreateLogger<VulkanDevice>());

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

	/// <summary>Starts a frame (the graphics system's Render scope).</summary>
	public void BeginFrame() => _driver?.BeginFrame();

	/// <summary>Ends and presents a frame (the graphics system's Render scope).</summary>
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

	private GraphicsFrameDriver _requireDriver() => _driver ?? throw new InvalidOperationException("The Vulkan device has not been initialized yet.");
}

/// <summary>
/// Creates the Vulkan device at Init (<see cref="StageOrder.Graphics"/>, after the window), brackets every Render stage
/// with the frame begin and end (a scope at <see cref="StageOrder.Graphics"/>), and destroys the device at Destroy
/// (<see cref="DestroyOrder"/>: after the game's Destroy steps, before the window).
/// </summary>
internal sealed class VulkanGraphicsSystem(VulkanGraphics graphics)
{
	[Init(Order = StageOrder.Graphics)]
	public void Init(GameTime dt) => graphics.Initialize();

	[Begin(Stage.Render, Order = StageOrder.Graphics)]
	public void BeginFrame(GameTime dt) => graphics.BeginFrame();

	[End(Stage.Render, Order = StageOrder.Graphics)]
	public void EndFrame(GameTime dt) => graphics.EndFrame();

	/// <summary>
	/// The order of the device teardown in the Destroy stage: after the game's own Destroy steps (default order 0), which
	/// release their GPU resources, and before the window is destroyed (<see cref="StageOrder.WindowClose"/>).
	/// </summary>
	public const int DestroyOrder = StageOrder.WindowClose - 50;

	[Destroy(Order = DestroyOrder)]
	public void Destroy(GameTime dt) => graphics.Dispose();
}

/// <summary>
/// Registration of the Vulkan graphics backend.
/// </summary>
public static class VulkanBuilderExtensions
{
	/// <summary>
	/// Registers the windowed Vulkan backend: <see cref="VulkanGraphics"/> as <see cref="IGraphicsFrame"/> and
	/// <see cref="IScreenshotSource"/>, binding <see cref="GraphicsConfig"/> from <c>Ion:Graphics</c> and
	/// <see cref="WindowConfig"/> from <c>Ion:Window</c>. Needs a window module (<c>AddSilkWindowing</c>); call
	/// <see cref="UseVulkanGraphics"/> after <c>UseSilkWindowing</c>.
	/// </summary>
	public static IServiceCollection AddVulkanGraphics(this IServiceCollection services, IConfiguration config) =>
		services.AddVulkanGraphics(config, VulkanGraphicsMode.Windowed);

	/// <summary>
	/// Registers the Vulkan backend in <paramref name="mode"/> (the headless backend uses
	/// <see cref="VulkanGraphicsMode.Offscreen"/>).
	/// </summary>
	public static IServiceCollection AddVulkanGraphics(this IServiceCollection services, IConfiguration config, VulkanGraphicsMode mode)
	{
		var ion = config.GetSection("Ion");
		services.Configure<GraphicsConfig>(ion.GetSection("Graphics"));
		services.Configure<WindowConfig>(ion.GetSection("Window"));
		return services.AddVulkanGraphicsServices(mode);
	}

	/// <summary>
	/// Registers the Vulkan backend services in <paramref name="mode"/> without binding configuration, for modules that
	/// already bind <see cref="GraphicsConfig"/> and <see cref="WindowConfig"/> (such as the null graphics backend).
	/// </summary>
	public static IServiceCollection AddVulkanGraphicsServices(this IServiceCollection services, VulkanGraphicsMode mode)
	{
		services.AddOptions<GameConfig>();
		services.AddOptions<GraphicsConfig>();
		services.AddOptions<WindowConfig>();

		services
			.AddSingleton(sp => new VulkanGraphics(
				sp.GetRequiredService<IOptionsMonitor<GraphicsConfig>>(),
				sp.GetRequiredService<IOptionsMonitor<WindowConfig>>(),
				sp.GetRequiredService<IOptionsMonitor<GameConfig>>(),
				sp.GetRequiredService<ILoggerFactory>(),
				mode,
				sp))
			.AddSingleton<IGraphicsFrame>(static sp => sp.GetRequiredService<VulkanGraphics>())
			.AddSingleton<IScreenshotSource>(static sp => sp.GetRequiredService<VulkanGraphics>())
			.AddSingleton<VulkanGraphicsSystem>();
		return services;
	}

	/// <summary>Adds the Vulkan graphics system (device at Init, frame scope around Render, teardown at Destroy).</summary>
	public static IIonApplication UseVulkanGraphics(this IIonApplication app) => app.UseSystem<VulkanGraphicsSystem>();
}
