using Kyber.Extensions.Graphics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System.Numerics;

using Veldrid;
using Veldrid.Sdl2;

namespace Kyber;

internal class Window : IWindow
{
    private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly ILogger _logger;
    private readonly EventEmitter _eventEmitter;
	private readonly IEventListener _events;

	public InputSnapshot? InputSnapshot { get; private set; }

	private Veldrid.StartupUtilities.WindowCreateInfo _windowCreateInfo;
    internal Sdl2Window? Sdl2Window { get; private set; }

	private (int, int) _prevSize = (0, 0);
	private bool _closeHandled = false;

	public int Width
	{
		get => Sdl2Window?.Width ?? 0;
		set {
			if (Sdl2Window != null)
			{
				Sdl2Window.Width = value;
				_size = _size with { X = value };
			}
		}
	}

	public int Height
	{
		get => Sdl2Window?.Height ?? 0;
		set
		{
			if (Sdl2Window != null)
			{
				Sdl2Window.Height = value;
				_size = _size with { Y = value };
			}
		}
	}

	private Vector2 _size = default;
	public Vector2 Size
	{
		get => _size;
		set { 
			if (Sdl2Window != null)
			{
				_size = value;
				Sdl2Window.Width = (int)value.X;
				Sdl2Window.Height = (int)value.Y;
			}
				
		}
	}

	public bool HasClosed { get; private set; }
    public bool IsActive => (Sdl2Window?.Focused ?? false);

	public bool IsVisible
	{
		get => Sdl2Window?.Visible ?? false;
		set { if (Sdl2Window != null) Sdl2Window.Visible = value; }
	}

	public bool IsMaximized
	{
		get => (Sdl2Window?.WindowState ?? WindowState.Hidden) == WindowState.Maximized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? WindowState.Maximized : WindowState.Normal; }
	}
	public bool IsMinimized
	{
		get => (Sdl2Window?.WindowState ?? WindowState.Hidden) == WindowState.Minimized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? WindowState.Minimized : WindowState.Normal; }
	}
	public bool IsFullscreen
	{
		get => (Sdl2Window?.WindowState ?? WindowState.Hidden) == WindowState.FullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? WindowState.FullScreen : WindowState.Normal; }
	}
	public bool IsBorderlessFullscreen
	{
		get => (Sdl2Window?.WindowState ?? WindowState.Hidden) == WindowState.BorderlessFullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? WindowState.BorderlessFullScreen : WindowState.Normal; }
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

	public Window(IOptionsMonitor<GameConfig> gameConfig, IOptionsMonitor<GraphicsConfig> graphicsConfig, IOptionsMonitor<WindowConfig> windowConfig, ILogger<Window> logger, IEventEmitter eventEmitter, IEventListener events)
    {
		_gameConfig = gameConfig;
		_graphicsConfig = graphicsConfig;
		_windowConfig = windowConfig;

		_logger = logger;
        _eventEmitter = (EventEmitter)eventEmitter;
		_events = events;

		_windowCreateInfo = new()
        {
            X = windowConfig.CurrentValue.WindowX ?? 100,
            Y = windowConfig.CurrentValue.WindowY ?? 100,
            WindowWidth = windowConfig.CurrentValue.WindowWidth ?? 960,
            WindowHeight = windowConfig.CurrentValue.WindowHeight ?? 540,
            WindowInitialState = windowConfig.CurrentValue.WindowState,
            WindowTitle = _title = _gameConfig.CurrentValue.Title
        };
    }

    public void Initialize()
    {
        if (_graphicsConfig.CurrentValue.Output != GraphicsOutput.Window) return;

		_logger.LogInformation("Creating window...");
		Sdl2Window = _createWindow(_windowCreateInfo);
		Sdl2Window.SetCloseRequestedHandler(() => _closeHandled);
		Sdl2Window.Closed += _onClosed;
		Sdl2Window.FocusLost += _onFocusLost;
		Sdl2Window.Resized += _onResize;
		Sdl2Window.FocusGained += _onFocusGained;
		_size = new(Sdl2Window.Width, Sdl2Window.Height);

		//_onResize();
		_logger.LogInformation($"Window created! ({Sdl2Window.Width}x{Sdl2Window.Height})");
	}

	private static Sdl2Window _createWindow(Veldrid.StartupUtilities.WindowCreateInfo windowCreateInfo)
	{
		SDL_WindowFlags sDL_WindowFlags = SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable | GetWindowFlags(windowCreateInfo.WindowInitialState);
		if (windowCreateInfo.WindowInitialState != WindowState.Hidden)
		{
			sDL_WindowFlags |= SDL_WindowFlags.Shown;
		}
		return new Sdl2Window(windowCreateInfo.WindowTitle, windowCreateInfo.X, windowCreateInfo.Y, windowCreateInfo.WindowWidth, windowCreateInfo.WindowHeight, sDL_WindowFlags, threadedProcessing: true);
	}

	private static SDL_WindowFlags GetWindowFlags(WindowState state)
	{
		return state switch
		{
			WindowState.Normal => (SDL_WindowFlags)0u,
			WindowState.FullScreen => SDL_WindowFlags.Fullscreen,
			WindowState.Maximized => SDL_WindowFlags.Maximized,
			WindowState.Minimized => SDL_WindowFlags.Minimized,
			WindowState.BorderlessFullScreen => SDL_WindowFlags.FullScreenDesktop,
			WindowState.Hidden => SDL_WindowFlags.Hidden,
			_ => throw new VeldridException("Invalid WindowState: " + state),
		};
	}

	public void Step()
    {
		if (_graphicsConfig.CurrentValue.Output != GraphicsOutput.Window || Sdl2Window == null)
		{
			InputSnapshot = default;
			return;
		}

		if (_events.OnLatest<WindowClosedEvent>()) _closeHandled = true;

		InputSnapshot = Sdl2Window.PumpEvents();
	}

	private void _onResize()
	{
		if (Sdl2Window == null) return;
		if (_prevSize == (Sdl2Window.Width, Sdl2Window.Height)) return;

		_size = new(Sdl2Window.Width, Sdl2Window.Height);
		_prevSize = (Sdl2Window.Width, Sdl2Window.Height);
		_eventEmitter.Emit(new WindowResizeEvent((uint)Sdl2Window.Width, (uint)Sdl2Window.Height));
	}

	private void _onFocusGained() => _eventEmitter.Emit<WindowFocusGainedEvent>();

	private void _onFocusLost() => _eventEmitter.Emit<WindowFocusLostEvent>();

	private void _onClosed()
	{
		HasClosed = true;
		_logger.LogDebug("Window closed!");
		_eventEmitter.Emit<WindowClosedEvent>();
	}
}
