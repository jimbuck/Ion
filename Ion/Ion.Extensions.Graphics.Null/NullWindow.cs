using System.Numerics;

using Microsoft.Extensions.Options;

namespace Ion.Extensions.Graphics;

/// <summary>
/// An <see cref="IWindow"/> that exists only in memory: no SDL, no display. Its size comes from <see cref="WindowConfig"/>
/// (960x540 when unset) and every property can be set. Registered by <c>AddNullGraphics</c>; resolve it as
/// <see cref="NullWindow"/> to call <see cref="Close"/> from tests.
/// </summary>
/// <remarks>
/// The window system emits one <see cref="WindowResizeEvent"/> with the initial size at Init, and one more whenever the
/// size changes afterwards. <see cref="Close"/> emits <see cref="WindowClosedEvent"/>, which the window system turns into
/// <see cref="ExitGameEvent"/> at the end of that frame's Render stage, exactly like the Silk.NET window.
/// </remarks>
public sealed class NullWindow : IWindow
{
	public const uint DefaultWidth = 960;
	public const uint DefaultHeight = 540;

	private readonly IEvents _events;
	private uint _width;
	private uint _height;

	public NullWindow(IOptionsMonitor<WindowConfig> windowConfig, IOptionsMonitor<GameConfig> gameConfig, IEvents events)
	{
		_events = events;

		var config = windowConfig.CurrentValue;
		_width = config.Width is > 0 ? (uint)config.Width.Value : DefaultWidth;
		_height = config.Height is > 0 ? (uint)config.Height.Value : DefaultHeight;

		Title = gameConfig.CurrentValue.Title;
		IsCursorVisible = config.ShowCursor;
		IsVisible = config.WindowState != WindowState.Hidden;
		IsMaximized = config.WindowState == WindowState.Maximized;
		IsMinimized = config.WindowState == WindowState.Minimized;
		IsFullscreen = config.WindowState == WindowState.FullScreen;
		IsBorderless = config.WindowState == WindowState.BorderlessFullScreen;
	}

	/// <summary>
	/// True once the window system has initialized the window (and emitted the initial <see cref="WindowResizeEvent"/>).
	/// </summary>
	public bool IsInitialized { get; private set; }

	public uint Width
	{
		get => _width;
		set => _resize(value, _height);
	}

	public uint Height
	{
		get => _height;
		set => _resize(_width, value);
	}

	public Vector2 Size
	{
		get => new(_width, _height);
		set => _resize((uint)value.X, (uint)value.Y);
	}

	public bool IsClosing { get; private set; }
	public bool IsClosed { get; private set; }

	/// <summary>
	/// Always true: a headless window never loses focus.
	/// </summary>
	public bool IsActive => !IsClosed;

	public bool IsVisible { get; set; }
	public bool IsMaximized { get; set; }
	public bool IsMinimized { get; set; }
	public bool IsFullscreen { get; set; }
	public bool IsBorderless { get; set; }
	public bool IsMouseGrabbed { get; set; }

	public string Title { get; set; }
	public bool IsResizable { get; set; } = true;
	public bool IsCursorVisible { get; set; }

	/// <summary>
	/// Simulates the user closing the window: emits <see cref="WindowClosedEvent"/> (once), which ends the game loop at the
	/// end of the next Render stage.
	/// </summary>
	public void Close()
	{
		if (IsClosing) return;

		IsClosing = true;
		_events.Emit<WindowClosedEvent>();
	}

	internal void Initialize()
	{
		if (IsInitialized) return;

		IsInitialized = true;
		_events.Emit(new WindowResizeEvent(_width, _height));
	}

	internal void MarkClosed() => IsClosed = true;

	private void _resize(uint width, uint height)
	{
		if (width == _width && height == _height) return;

		_width = width;
		_height = height;

		if (IsInitialized) _events.Emit(new WindowResizeEvent(width, height));
	}
}
