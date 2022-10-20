using Kyber.Events;

namespace Kyber;

public class GraphicsDevice : IInitializeSystem, IDisposable
{
    private readonly IStartupConfig _startupConfig;
    private readonly Window _window;
    private readonly IEventListener _events;
    private readonly ILogger _logger;

    private Veldrid.GraphicsDevice? _gd;

	public bool IsEnabled { get; set; } = true;

    // TODO: Remove this once the graphics API is implemented.
    public Veldrid.GraphicsDevice? Internal => _gd;

    public GraphicsDevice(IStartupConfig startupConfig, Window window, IEventListener events, ILogger<GraphicsDevice> logger)
    {
        _startupConfig = startupConfig;
        _window = window;
        _events = events;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_startupConfig.GraphicsOutput == Graphics.GraphicsOutput.None) return;

		_gd = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new Veldrid.GraphicsDeviceOptions()
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true,
            SyncToVerticalBlank = _startupConfig.VSync,
        });
    }

    public void Dispose()
    {
        _gd?.Dispose();
    }
}
