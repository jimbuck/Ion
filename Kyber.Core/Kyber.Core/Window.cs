using Kyber.Core.Graphics;

using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Kyber.Core;

public class Window
{
    private readonly IStartupConfig _startupConfig;
    private readonly ILogger _logger;

    private WindowCreateInfo _windowCreateInfo;
    internal Sdl2Window? _sdl2Window;

    public bool HasClosed { get; private set; }
    public bool IsActive => (_sdl2Window?.Focused ?? false);

    public IntPtr? Handle => _sdl2Window?.SdlWindowHandle;

    public Window(IStartupConfig startupConfig, ILogger<Window> logger)
    {
        _startupConfig = startupConfig;
        _logger = logger;

        _windowCreateInfo = new WindowCreateInfo()
        {
            X = _startupConfig.WindowX ?? 100,
            Y = _startupConfig.WindowY ?? 100,
            WindowWidth = _startupConfig.WindowWidth ?? 960,
            WindowHeight = _startupConfig.WindowHeight ?? 540,
            WindowInitialState = _startupConfig.WindowState.ToInternal(),
            WindowTitle = _startupConfig.WindowTitle ?? "Kyber"
        };
    }

    public void Initialize()
    {
        if (_startupConfig.GraphicsOutput != GraphicsOutput.Window) return;

        _logger.LogDebug("Creating window...");
        _sdl2Window = VeldridStartup.CreateWindow(ref _windowCreateInfo);
        _sdl2Window.Closed += _onClosed;
    }

    private void _onClosed()
    {
        HasClosed = true;
        _logger.LogDebug("Window closed!");
    }

    public void Update(float dt)
    {
        if (_startupConfig.GraphicsOutput != GraphicsOutput.Window) return;
        var snapshot = _sdl2Window?.PumpEvents();
    }

    public void Close()
    {
        if (_startupConfig.GraphicsOutput != GraphicsOutput.Window) return;
        _logger.LogDebug("Closing window...");
        _sdl2Window?.Close();
    }
}
