using Veldrid;
using Veldrid.StartupUtilities;

namespace Kyber.Core;

public class GraphicsDevice : IDisposable
{
    private readonly StartupConfig _startupConfig;
    private readonly Window _window;

    private Veldrid.GraphicsDevice _gd;

    public GraphicsDevice(StartupConfig startupConfig, Window window)
    {
        _startupConfig = startupConfig;
        _window = window;
    }

    public void Initialize()
    {
        _gd = VeldridStartup.CreateGraphicsDevice(_window._sdl2Window, new GraphicsDeviceOptions()
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true
        });
    }

    public void Dispose()
    {
        _gd.Dispose();
    }
}
