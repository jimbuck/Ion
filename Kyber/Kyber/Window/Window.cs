﻿using Kyber.Graphics;

using Veldrid.Sdl2;

namespace Kyber;

public record struct WindowResizeEvent(uint Width, uint Height);
public record struct WindowClosedEvent;
public record struct WindowFocusGainedEvent;
public record struct WindowFocusLostEvent;

public interface IWindow
{
	int Width { get; set; }
	int Height { get; set; }

	Vector2 Size { get; set; }
	
	bool HasClosed { get; }
	bool IsActive { get; }

	bool IsVisible { get; set; }
	bool IsMaximized { get; set; }
	bool IsMinimized { get; set; }
	bool IsFullscreen { get; set; }
	bool IsBorderlessFullscreen { get; set; }

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
    private readonly EventEmitter _eventEmitter;
	private readonly IEventListener _events;

	public Veldrid.InputSnapshot? InputSnapshot { get; private set; }

	private Veldrid.StartupUtilities.WindowCreateInfo _windowCreateInfo;
    internal Veldrid.Sdl2.Sdl2Window? Sdl2Window { get; private set; }

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
	public bool IsBorderlessFullscreen
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

	public Window(IGameConfig config, ILogger<Window> logger, IEventEmitter eventEmitter, IEventListener events)
    {
        _config = config;
        _logger = logger;
        _eventEmitter = (EventEmitter)eventEmitter;
		_events = events;

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
		Sdl2Window.SetCloseRequestedHandler(() => _closeHandled);

		Sdl2Window.Closed += _onClosed;
        Sdl2Window.FocusLost += _onFocusLost;
        Sdl2Window.Resized += _onResize;
        Sdl2Window.FocusGained += _onFocusGained;
		_size = new(Sdl2Window.Width, Sdl2Window.Height);

		//_onResize();
		_logger.LogInformation($"Window created! ({Sdl2Window.Width}x{Sdl2Window.Height})");
	}

    public void Step()
    {
		if (_config.Output != GraphicsOutput.Window || Sdl2Window == null)
		{
			InputSnapshot = default;
			return;
		}

		if (_events.OnLatest<WindowClosedEvent>()) _closeHandled = true;

		InputSnapshot = Sdl2Window.PumpEvents();
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
