using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using VeldridLib = Veldrid;
using Veldrid.Sdl2;

namespace Ion.Extensions.Graphics;

internal class Window : IWindow
{
	private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly ILogger _logger;
	private readonly EventEmitter _eventEmitter;
	private readonly IEventListener _events;

	public VeldridLib.InputSnapshot? InputSnapshot { get; private set; }

	private VeldridLib.StartupUtilities.WindowCreateInfo _windowCreateInfo;
	internal Sdl2Window? Sdl2Window { get; private set; }

	private (int Width, int Height) _prevSize = (0, 0);
	private bool _closeHandled = false;

	public uint Width
	{
		get => (uint)(Sdl2Window?.Width ?? 0);
		set {
			if (Sdl2Window != null)
			{
				Sdl2Window.Width = (int)value;
				_size = _size with { X = value };
			}
		}
	}

	public uint Height
	{
		get => (uint)(Sdl2Window?.Height ?? 0);
		set
		{
			if (Sdl2Window != null)
			{
				Sdl2Window.Height = (int)value;
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

	public bool IsClosing { get; private set; }
	public bool IsClosed { get; private set; }
	public bool IsActive => (Sdl2Window?.Focused ?? false);

	public bool IsVisible
	{
		get => Sdl2Window?.Visible ?? false;
		set { if (Sdl2Window != null) Sdl2Window.Visible = value; }
	}

	public bool IsMaximized
	{
		get => (Sdl2Window?.WindowState ?? VeldridLib.WindowState.Hidden) == VeldridLib.WindowState.Maximized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? VeldridLib.WindowState.Maximized : VeldridLib.WindowState.Normal; }
	}
	public bool IsMinimized
	{
		get => (Sdl2Window?.WindowState ?? VeldridLib.WindowState.Hidden) == VeldridLib.WindowState.Minimized;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? VeldridLib.WindowState.Minimized : VeldridLib.WindowState.Normal; }
	}
	public bool IsFullscreen
	{
		get => (Sdl2Window?.WindowState ?? VeldridLib.WindowState.Hidden) == VeldridLib.WindowState.FullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? VeldridLib.WindowState.FullScreen : VeldridLib.WindowState.Normal; }
	}
	public bool IsBorderless
	{
		get => (Sdl2Window?.WindowState ?? VeldridLib.WindowState.Hidden) == VeldridLib.WindowState.BorderlessFullScreen;
		set { if (Sdl2Window != null) Sdl2Window.WindowState = value ? VeldridLib.WindowState.BorderlessFullScreen : VeldridLib.WindowState.Normal; }
	}

	private bool _isMouseGrabbed = false;

	public bool IsMouseGrabbed
	{
		get => _isMouseGrabbed;
		set
		{
			if (Sdl2Window is not null)
			{
				Sdl2Native.SDL_SetWindowGrab(Sdl2Window.SdlWindowHandle, value);
				_isMouseGrabbed = value;
			}
		}
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
            WindowWidth = windowConfig.CurrentValue.Width ?? 960,
            WindowHeight = windowConfig.CurrentValue.Height ?? 540,
            WindowInitialState = (VeldridLib.WindowState)windowConfig.CurrentValue.WindowState,
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
		_prevSize = (Sdl2Window.Width, Sdl2Window.Height);

		Sdl2Window.CursorVisible = _windowConfig.CurrentValue.ShowCursor;

		//_onResize();
		_logger.LogInformation($"Window created! ({Sdl2Window.Width}x{Sdl2Window.Height})");
	}

	private static Sdl2Window _createWindow(VeldridLib.StartupUtilities.WindowCreateInfo windowCreateInfo)
	{
		SDL_WindowFlags sDL_WindowFlags = SDL_WindowFlags.OpenGL | SDL_WindowFlags.Resizable | GetWindowFlags(windowCreateInfo.WindowInitialState);
		if (windowCreateInfo.WindowInitialState != VeldridLib.WindowState.Hidden)
		{
			sDL_WindowFlags |= SDL_WindowFlags.Shown;
		}
		return new Sdl2Window(windowCreateInfo.WindowTitle, windowCreateInfo.X, windowCreateInfo.Y, windowCreateInfo.WindowWidth, windowCreateInfo.WindowHeight, sDL_WindowFlags, threadedProcessing: true);
	}

	private static SDL_WindowFlags GetWindowFlags(VeldridLib.WindowState state)
	{
		return state switch
		{
			VeldridLib.WindowState.Normal => (SDL_WindowFlags)0u,
			VeldridLib.WindowState.FullScreen => SDL_WindowFlags.Fullscreen,
			VeldridLib.WindowState.Maximized => SDL_WindowFlags.Maximized,
			VeldridLib.WindowState.Minimized => SDL_WindowFlags.Minimized,
			VeldridLib.WindowState.BorderlessFullScreen => SDL_WindowFlags.FullScreenDesktop,
			VeldridLib.WindowState.Hidden => SDL_WindowFlags.Hidden,
			_ => throw new VeldridLib.VeldridException("Invalid WindowState: " + state),
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
		if (Sdl2Window == null)
		{
			_logger.LogDebug("Null window was resized.");
			return;
		}

		if (_prevSize.Width == Sdl2Window.Width && _prevSize.Height == Sdl2Window.Height) return;

		_size = new(Sdl2Window.Width, Sdl2Window.Height);
		_prevSize = (Sdl2Window.Width, Sdl2Window.Height);
		_eventEmitter.Emit(new WindowResizeEvent((uint)Sdl2Window.Width, (uint)Sdl2Window.Height));
		_logger.LogDebug(message: "Window resized...");
	}

	private void _onFocusGained() => _eventEmitter.Emit<WindowFocusGainedEvent>();

	private void _onFocusLost() => _eventEmitter.Emit<WindowFocusLostEvent>();

	private void _onClosed()
	{
		IsClosing = true;
		_logger.LogDebug("Window closed!");
		_eventEmitter.Emit<WindowClosedEvent>();
	}
}
