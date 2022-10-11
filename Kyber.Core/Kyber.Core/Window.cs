using Veldrid.Sdl2;
using Veldrid.StartupUtilities;

namespace Kyber.Core;

public class Window
{
    private readonly StartupConfig _startupConfig;
    private readonly ILogger _logger;

    private WindowCreateInfo _windowCreateInfo;
    internal Sdl2Window _sdl2Window;

    public bool IsOpen => _sdl2Window.Exists && _sdl2Window.Visible;
    public bool IsActive => _sdl2Window.Focused && _sdl2Window.Visible;

    public IntPtr Handle => _sdl2Window.SdlWindowHandle;

    public Window(StartupConfig startupConfig, ILogger<Window> logger)
    {
        _startupConfig = startupConfig;
        _logger = logger;

        _windowCreateInfo = new WindowCreateInfo()
        {
            // TODO: Pull size/position from StartupConfig.
            X = 100,
            Y = 100,
            WindowWidth = 960,
            WindowHeight = 540,
            WindowTitle = _startupConfig.WindowTitle ?? "Kyber"
        };
    }

    public void Initialize()
    {
        _sdl2Window = VeldridStartup.CreateWindow(ref _windowCreateInfo);
    }

    public void Update(float dt)
    {
        //_logger.LogInformation($"Update {dt}");
        _sdl2Window.PumpEvents();
    }

    public void Close()
    {
        _sdl2Window.Close();
    }
}
