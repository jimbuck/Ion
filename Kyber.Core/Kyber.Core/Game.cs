global using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

using System.Diagnostics;

namespace Kyber.Core;

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
internal class Game : IDisposable
{
    private bool _shouldExit;
    private bool _disposed;

    private readonly StartupConfig _startupConfig;
    private readonly Window _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly SystemCollection _systems;

    private IUpdateSystem[] _updateSystems = Array.Empty<IUpdateSystem>();
    private IRenderSystem[] _renderSystems = Array.Empty<IRenderSystem>();

    public event EventHandler<EventArgs>? Exiting;

    public Game(
        StartupConfig startupConfig,
        Window window,
        GraphicsDevice graphicsDevice,
        SystemCollection systems)
    {
        _startupConfig = startupConfig;
        _window = window;
        _graphicsDevice = graphicsDevice;
        _systems = systems;
    }

    public void Startup()
    {
        _window.Initialize();
        _graphicsDevice.Initialize();
        
        foreach(var service in _systems.GetStartupSystems())
        {
            service.Startup();
        }

        _updateSystems = _systems.GetUpdateSystems();
        _renderSystems = _systems.GetRenderSystems();
    }

    public void Update(float dt)
    {
        _window.Update(dt);

        foreach(var system in _updateSystems) system.Update(dt);
    }

    public void Render(float dt)
    {
        foreach (var system in _renderSystems) system.Render(dt);
    }

    public void Shutdown()
    {
        _window?.Close();
    }

    public void Run()
    {
        Startup();

        var stopwatch = Stopwatch.StartNew();
        while (!_shouldExit && _window.IsOpen)
        {
            var dt = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();

            Update(dt);
            Render(dt);
        }

        Shutdown();

        Exiting?.Invoke(this, EventArgs.Empty);
    }

    public void Exit()
    {
        _shouldExit = true;
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _graphicsDevice.Dispose();
            }

            // TODO: free unmanaged resources (unmanaged objects) and override finalizer
            // TODO: set large fields to null
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }
}
