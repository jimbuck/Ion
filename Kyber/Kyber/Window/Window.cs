using Kyber.Graphics;

namespace Kyber;

public record struct WindowResizeEvent(uint Width, uint Height);
public record struct WindowClosedEvent;
public record struct WindowFocusGainedEvent;
public record struct WindowFocusLostEvent;

public interface IWindow
{
	bool HasClosed { get; }
	bool IsActive { get; }

	bool IsVisible { get; set; }
	bool IsMaximized { get; set; }
	bool IsMinimized { get; set; }
	bool IsFullscreen { get; set; }
	bool IsBorderless { get; set; }

	string Title { get; set; }
	bool IsResizable { get; set; }
	bool IsCursorVisible { get; set; }
	bool IsBorderVisible { get; set; }

	void Close();
}

internal class Window : IWindow
{
    private readonly IGameConfig _config;
    private readonly ILogger _logger;
    private readonly EventEmitter _events;

    private Veldrid.StartupUtilities.WindowCreateInfo _windowCreateInfo;
    internal Veldrid.Sdl2.Sdl2Window? Sdl2Window { get; private set; }

	private (int, int) _prevSize = (0, 0);

    public bool HasClosed { get; private set; }
    public bool IsActive => (Sdl2Window?.Focused ?? false);

	public bool IsVisible
	{
		get => Sdl2Window?.Visible ?? false;
		set { if (Sdl2Window != null) Sdl2Window.Visible = value; }
	}

	public bool IsMaximized
	{
		get => (Sdl2Window?.WindowState ?? Veldrid.WindowState.Hidden) == Veldrid.WindowState.Maximized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? Veldrid.WindowState.Maximized : Veldrid.WindowState.Normal; }
	}
	public bool IsMinimized
	{
		get => (Sdl2Window?.WindowState ?? Veldrid.WindowState.Hidden) == Veldrid.WindowState.Minimized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? Veldrid.WindowState.Minimized : Veldrid.WindowState.Normal; }
	}
	public bool IsFullscreen
	{
		get => (Sdl2Window?.WindowState ?? Veldrid.WindowState.Hidden) == Veldrid.WindowState.FullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? Veldrid.WindowState.FullScreen : Veldrid.WindowState.Normal; }
	}
	public bool IsBorderless
	{
		get => (Sdl2Window?.WindowState ?? Veldrid.WindowState.Hidden) == Veldrid.WindowState.BorderlessFullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? Veldrid.WindowState.BorderlessFullScreen : Veldrid.WindowState.Normal; }
	}

	private string _title;
	public string Title
	{
		get => Sdl2Window?.Title ?? _title;
		set {
			_title = value;
			if (Sdl2Window != null) Sdl2Window.Title = _title;
		}
	}
	public bool IsResizable
	{
		get => Sdl2Window?.Resizable ?? false;
		set { if (Sdl2Window != null) Sdl2Window.Resizable = value; }
	}
	public bool IsCursorVisible
	{
		get => Sdl2Window?.CursorVisible ?? false;
		set { if (Sdl2Window != null) Sdl2Window.CursorVisible = value; }
	}
	public bool IsBorderVisible
	{
		get => Sdl2Window?.BorderVisible ?? false;
		set { if (Sdl2Window != null) Sdl2Window.BorderVisible = value; }
	}

	public Window(IGameConfig config, ILogger<Window> logger, IEventEmitter events)
    {
        _config = config;
        _logger = logger;
        _events = (EventEmitter)events;

		_windowCreateInfo = new()
        {
            X = _config.WindowX ?? 100,
            Y = _config.WindowY ?? 100,
            WindowWidth = _config.WindowWidth ?? 960,
            WindowHeight = _config.WindowHeight ?? 540,
            WindowInitialState = _config.WindowState.ToInternal(),
            WindowTitle = _title = _config.WindowTitle ?? "Kyber"
        };
    }

    public void Initialize()
    {
        if (_config.Output != GraphicsOutput.Window) return;

		_logger.LogInformation("Creating window...");
        Sdl2Window = Veldrid.StartupUtilities.VeldridStartup.CreateWindow(ref _windowCreateInfo);
        Sdl2Window.Closed += _onClosed;
        Sdl2Window.FocusLost += _onFocusLost;
        Sdl2Window.Resized += _onResize;
        Sdl2Window.FocusGained += _onFocusGained;

		_logger.LogInformation("Window created!");
	}

    public Veldrid.InputSnapshot? Step()
    {
		if (_config.Output != GraphicsOutput.Window || Sdl2Window == null) return default;

		return Sdl2Window.PumpEvents();
	}

    public void Close()
    {
        if (_config.Output != GraphicsOutput.Window) return;
        _logger.LogDebug("Closing window...");
        Sdl2Window?.Close();
    }

	private void _onResize()
	{
		if (Sdl2Window == null) return;
		if (_prevSize == (Sdl2Window.Width, Sdl2Window.Height)) return;

		_prevSize = (Sdl2Window.Width, Sdl2Window.Height);
		_events.Emit(new WindowResizeEvent((uint)Sdl2Window.Width, (uint)Sdl2Window.Height));
	}

	private void _onFocusGained() => _events.Emit<WindowFocusGainedEvent>();

	private void _onFocusLost() => _events.Emit<WindowFocusLostEvent>();

	private void _onClosed()
	{
		HasClosed = true;
		_logger.LogDebug("Window closed!");
		_events.Emit<WindowClosedEvent>();
	}
}
