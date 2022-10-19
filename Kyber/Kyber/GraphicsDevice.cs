﻿using Kyber.Events;

namespace Kyber;

public class GraphicsDevice : IDisposable
{
    private readonly IStartupConfig _startupConfig;
    private readonly Window _window;
    private readonly IEventListener _events;
    private readonly ILogger _logger;

    private Veldrid.GraphicsDevice? _gd;

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
            PreferDepthRangeZeroToOne = true
        });
    }

    public void HandleWindowResize()
    {
        if (_gd != null && _events.OnLatest<WindowResizeEvent>(out var e)) _gd.ResizeMainWindow(e.Data.Width, e.Data.Height);
    }

    public void Dispose()
    {
        _gd?.Dispose();
    }
}
