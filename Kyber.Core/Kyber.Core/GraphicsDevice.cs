using Veldrid;
using Veldrid.StartupUtilities;

namespace Kyber.Core;

public class GraphicsDevice : IDisposable
{
    private readonly IStartupConfig _startupConfig;
    private readonly Window _window;
    private readonly IEventListener _events;

    private Veldrid.GraphicsDevice? _gd;

    public GraphicsDevice(IStartupConfig startupConfig, Window window, IEventListener events)
    {
        _startupConfig = startupConfig;
        _window = window;
        _events = events;
    }

    public void Initialize()
    {
        if (_startupConfig.GraphicsOutput == Graphics.GraphicsOutput.None) return;

        _gd = VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new GraphicsDeviceOptions()
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true
        });
    }

    public void HandleWindowResize(float dt)
    {
        //IEvent<WindowResizeEvent>? e;
        //while (_events.On(out e)) { }
        //if (e != null) _gd?.ResizeMainWindow(e.Data.Width, e.Data.Height);
    }

    public void Dispose()
    {
        _gd?.Dispose();
    }
}
