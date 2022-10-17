using Veldrid;
using Veldrid.StartupUtilities;

namespace Kyber.Core;

public class GraphicsDevice : IDisposable
{
    private readonly IStartupConfig _startupConfig;
    private readonly Window _window;

    private Veldrid.GraphicsDevice? _gd;

    public GraphicsDevice(IStartupConfig startupConfig, Window window)
    {
        _startupConfig = startupConfig;
        _window = window;
    }

    public void Initialize()
    {
        if (_startupConfig.GraphicsOutput == Graphics.GraphicsOutput.None) return;

        _gd = VeldridStartup.CreateGraphicsDevice(_window._sdl2Window, new GraphicsDeviceOptions()
        {
            PreferStandardClipSpaceYDirection = true,
            PreferDepthRangeZeroToOne = true
        });
    }

    public void Dispose()
    {
        _gd?.Dispose();
    }
}
