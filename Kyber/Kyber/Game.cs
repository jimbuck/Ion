using System.Diagnostics;

using Kyber.Events;
using Kyber.Graphics;

namespace Kyber;

/// <summary>
/// Top-level class representing the runnable game.
/// </summary>
internal class Game
{
    private bool _shouldExit;

    private readonly IStartupConfig _startupConfig;
    private readonly Window _window;
    private readonly GraphicsDevice _graphicsDevice;
    private readonly EventSystem _eventSystem;
    private readonly IEventListener _eventListener;
    private readonly SystemGroup _systems;
    
    private readonly Stopwatch _updateStopwatch = new ();
    private readonly Stopwatch _renderStopwatch = new();

    public event EventHandler<EventArgs>? Exiting;

    public Game(
        IStartupConfig startupConfig,
        Window window,
        GraphicsDevice graphicsDevice,
        IEventEmitter eventSystem,
        IEventListener eventListener,
        SystemGroup systems)
    {
        _startupConfig = startupConfig;
        _window = window;
        _graphicsDevice = graphicsDevice;
        _eventSystem = (EventSystem)eventSystem;
        _systems = systems;

        _eventListener = eventListener;
    }

    public void Startup()
    {
        _window?.Initialize();
        _graphicsDevice?.Initialize();
        _systems.Startup();
    }

    public void PreUpdate(float dt)
    {
        _eventSystem.PreUpdate(dt);
        _systems.PreUpdate(dt);
    }

    public void Update(float dt)
    {
        _window?.Update(dt);
        if (_eventListener.On<WindowClosedEvent>()) Exit();
        _systems.Update(dt);
    }

    public void PostUpdate(float dt)
    {
        _systems.PostUpdate(dt);
        _graphicsDevice.HandleWindowResize(dt);
    }

    public void PreRender(float dt) => _systems.PreRender(dt);

    public void Render(float dt) => _systems.Render(dt);

    public void PostRender(float dt) => _systems.PostRender(dt);

    public void Shutdown()
    {
        _systems.Shutdown();
        _window?.Close();
    }

    public void Run()
    {
        Startup();

        var stopwatch = Stopwatch.StartNew();

        while (_shouldExit == false)
        {
            var dt = (float)stopwatch.Elapsed.TotalSeconds;
            stopwatch.Restart();
            Step(dt);
        }

        Shutdown();

        Exiting?.Invoke(this, EventArgs.Empty);
    }

    public void Step(float dt)
    {
        _updateStopwatch.Restart();
        PreUpdate(dt);
        Update(dt);
        PostUpdate(dt);
        _updateStopwatch.Stop();
        // TODO: Emit/Store Update time.

        if (_startupConfig.GraphicsOutput != GraphicsOutput.None)
        {
            _renderStopwatch.Start();
            PreRender(dt);
            Render(dt);
            PostRender(dt);
            _renderStopwatch.Stop();
            // TODO: Emit/Store Render time.
        }
    }

    public void Exit()
    {
        _shouldExit = true;
    }
}
