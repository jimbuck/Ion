using Kyber.Core.Graphics;

namespace Kyber.Core;

public record struct WindowResizeEvent(uint Width, uint Height);
public record struct WindowClosedEvent;
public record struct WindowFocusGainedEvent;
public record struct WindowFocusLostEvent;

public class Window
{
    private readonly IStartupConfig _startupConfig;
    private readonly ILogger _logger;
    private readonly InputState _inputState;
    private readonly EventSystem _events;

    private Veldrid.StartupUtilities.WindowCreateInfo _windowCreateInfo;
    internal Veldrid.Sdl2.Sdl2Window? Sdl2Window { get; private set; }

    private (int, int) _prevSize = (0, 0);

    public bool HasClosed { get; private set; }
    public bool IsActive => (Sdl2Window?.Focused ?? false);

    public Window(IStartupConfig startupConfig, InputState inputState, ILogger<Window> logger, IEventEmitter events)
    {
        _startupConfig = startupConfig;
        _logger = logger;
        _inputState = inputState;
        _events = (EventSystem)events;

        _windowCreateInfo = new()
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

        _logger.LogInformation("Creating window...");
        Sdl2Window = Veldrid.StartupUtilities.VeldridStartup.CreateWindow(ref _windowCreateInfo);
        Sdl2Window.Closed += _onClosed;
        Sdl2Window.FocusLost += _onFocusLost;
        Sdl2Window.Resized += _onResize;
        Sdl2Window.FocusGained += _onFocusGained;
    }

    private void _onResize()
    {
        if (Sdl2Window == null) return;
        if (_prevSize == (Sdl2Window.Width, Sdl2Window.Height)) return;

        _prevSize = (Sdl2Window.Width, Sdl2Window.Height);
        _events.Emit(EventCategory.Window, new WindowResizeEvent((uint)Sdl2Window.Width, (uint)Sdl2Window.Height));
    }

    private void _onFocusGained() => _events.Emit<WindowFocusGainedEvent>(EventCategory.Window);

    private void _onFocusLost() => _events.Emit<WindowFocusLostEvent>(EventCategory.Window);

    private void _onClosed()
    {
        HasClosed = true;
        _logger.LogDebug("Window closed!");
        _events.Emit<WindowClosedEvent>(EventCategory.Window);
    }

    public void Update(float dt)
    {
        if (_startupConfig.GraphicsOutput != GraphicsOutput.Window) return;
        if (Sdl2Window != null)
        {
            var snapshot = Sdl2Window.PumpEvents();
            _inputState.UpdateState(snapshot);
        }
    }

    public void Close()
    {
        if (_startupConfig.GraphicsOutput != GraphicsOutput.Window) return;
        _logger.LogDebug("Closing window...");
        Sdl2Window?.Close();
    }
}
