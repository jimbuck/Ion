using System.Numerics;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Silk.NET.Core.Contexts;
using Silk.NET.Maths;
using Silk.NET.Windowing;
using Silk.NET.Windowing.Glfw;
using Silk.NET.Windowing.Sdl;

using Ion.Extensions.Graphics;

using IonWindowState = Ion.Extensions.Graphics.WindowState;
using SilkWindowState = Silk.NET.Windowing.WindowState;
using IIonWindow = Ion.Extensions.Graphics.IWindow;
using ISilkWindow = Silk.NET.Windowing.IWindow;

namespace Ion.Extensions.Windowing;

/// <summary>
/// A Silk.NET window (GLFW or SDL) driven by Ion's game loop: created by the window system's Init step
/// (<see cref="StageOrder.Window"/>), pumped with <c>DoEvents()</c> at the start of every frame (First, same order), and
/// never run with <c>IWindow.Run</c>.
/// </summary>
/// <remarks>
/// <para>
/// Events: <see cref="WindowResizeEvent"/> with the window size once at Init and whenever it changes,
/// <see cref="WindowFocusGainedEvent"/>/<see cref="WindowFocusLostEvent"/>, and <see cref="WindowClosedEvent"/> when the
/// user closes the window (the window system turns it into <see cref="ExitGameEvent"/>).
/// </para>
/// <para>
/// Sizes: <see cref="Width"/>, <see cref="Height"/> and <see cref="Size"/> are window coordinates (what mouse positions
/// are in); <see cref="FramebufferSize"/> is pixels, which differs on HiDPI displays. Graphics backends size their
/// swapchain from the framebuffer.
/// </para>
/// <para>
/// The platform (<see cref="WindowConfig.Platform"/>) is registered explicitly with Silk.NET's first-party discovery
/// turned off, so no reflection runs and the window works under NativeAOT.
/// </para>
/// <para>
/// Android and iOS: SDL is view-only there (<see cref="IsViewOnly"/>), so a Silk.NET view is created in place of a window.
/// The view fills the screen; the window-only members (title, position, size and state setters, borders) do nothing.
/// </para>
/// </remarks>
public sealed class SilkWindow : IIonWindow, IWindowSurface, IDisposable
{
	private static readonly Lock PlatformLock = new();
	private static WindowPlatform _registeredPlatform = WindowPlatform.Auto;

	private readonly IOptionsMonitor<GameConfig> _gameConfig;
	private readonly IOptionsMonitor<GraphicsConfig> _graphicsConfig;
	private readonly IOptionsMonitor<WindowConfig> _windowConfig;
	private readonly IEvents _events;
	private readonly ILogger _logger;

	// The view is the window on desktop, and the only object on view-only platforms (Android, iOS), where _window is null.
	private IView? _view;
	private ISilkWindow? _window;
	private string _title;
	private bool _isMouseGrabbed;
	private bool _cursorVisible;
	private Vector2D<int> _lastSize;

	/// <summary>Creates the window object; the native window is created by <see cref="Initialize"/>.</summary>
	public SilkWindow(IOptionsMonitor<GameConfig> gameConfig, IOptionsMonitor<GraphicsConfig> graphicsConfig, IOptionsMonitor<WindowConfig> windowConfig, IEvents events, ILogger<SilkWindow> logger)
	{
		_gameConfig = gameConfig;
		_graphicsConfig = graphicsConfig;
		_windowConfig = windowConfig;
		_events = events;
		_logger = logger;
		_title = gameConfig.CurrentValue.Title;
		_cursorVisible = windowConfig.CurrentValue.ShowCursor;
	}

	/// <summary>The Silk.NET view (the window on desktop), or null before <see cref="Initialize"/>.</summary>
	public IView? View => _view;

	/// <summary>True when the platform has views only (SDL on Android and iOS): the view fills the screen.</summary>
	public bool IsViewOnly => _view is not null && _window is null;

	/// <summary>The platform the window was created with (<see cref="WindowPlatform.Glfw"/> or <see cref="WindowPlatform.Sdl"/>).</summary>
	public WindowPlatform Platform { get; private set; }

	/// <summary>Raised on the main thread after the native window has been created, before the initial resize event.</summary>
	public event Action<IView>? Created;

	/// <inheritdoc/>
	public bool IsCreated => _view is not null;

	/// <inheritdoc/>
	public object? PlatformWindow => _view;

	/// <inheritdoc/>
	public Vector2 FramebufferSize => _view is null ? Vector2.Zero : new Vector2(_view.FramebufferSize.X, _view.FramebufferSize.Y);

	/// <inheritdoc/>
	public NativeWindowHandles NativeHandles => _view?.Native is { } native ? ToHandles(native) : default;

	/// <inheritdoc/>
	public uint Width
	{
		get => (uint)Math.Max(0, _view?.Size.X ?? 0);
		set { if (_window is not null) _window.Size = new Vector2D<int>((int)value, _window.Size.Y); }
	}

	/// <inheritdoc/>
	public uint Height
	{
		get => (uint)Math.Max(0, _view?.Size.Y ?? 0);
		set { if (_window is not null) _window.Size = new Vector2D<int>(_window.Size.X, (int)value); }
	}

	/// <inheritdoc/>
	public Vector2 Size
	{
		get => _view is null ? Vector2.Zero : new Vector2(_view.Size.X, _view.Size.Y);
		set { if (_window is not null) _window.Size = new Vector2D<int>((int)value.X, (int)value.Y); }
	}

	/// <inheritdoc/>
	public bool IsClosing { get; private set; }

	/// <inheritdoc/>
	public bool IsClosed { get; private set; }

	/// <inheritdoc/>
	public bool IsActive { get; private set; }

	/// <inheritdoc/>
	public bool IsVisible
	{
		get => _window?.IsVisible ?? _view is not null;
		set { if (_window is not null) _window.IsVisible = value; }
	}

	/// <inheritdoc/>
	public bool IsMaximized
	{
		get => _window?.WindowState == SilkWindowState.Maximized;
		set { if (_window is not null) _window.WindowState = value ? SilkWindowState.Maximized : SilkWindowState.Normal; }
	}

	/// <inheritdoc/>
	public bool IsMinimized
	{
		get => _window?.WindowState == SilkWindowState.Minimized;
		set { if (_window is not null) _window.WindowState = value ? SilkWindowState.Minimized : SilkWindowState.Normal; }
	}

	/// <inheritdoc/>
	public bool IsFullscreen
	{
		get => _window is null ? _view is not null : _window.WindowState == SilkWindowState.Fullscreen;
		set { if (_window is not null) _window.WindowState = value ? SilkWindowState.Fullscreen : SilkWindowState.Normal; }
	}

	/// <inheritdoc/>
	public bool IsBorderless
	{
		get => _window?.WindowBorder == WindowBorder.Hidden;
		set { if (_window is not null) _window.WindowBorder = value ? WindowBorder.Hidden : (_windowConfig.CurrentValue.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed); }
	}

	/// <summary>
	/// Whether the mouse is captured: hidden and confined to the window, reporting unbounded movement (Silk.NET's
	/// <c>CursorMode.Disabled</c>). Applied by the input module to every mouse.
	/// </summary>
	public bool IsMouseGrabbed
	{
		get => _isMouseGrabbed;
		set
		{
			_isMouseGrabbed = value;
			CursorStateChanged?.Invoke();
		}
	}

	/// <inheritdoc/>
	public string Title
	{
		get => _title;
		set
		{
			_title = value;
			if (_window is not null) _window.Title = value;
		}
	}

	/// <inheritdoc/>
	public bool IsResizable
	{
		get => _window?.WindowBorder == WindowBorder.Resizable;
		set { if (_window is not null && _window.WindowBorder != WindowBorder.Hidden) _window.WindowBorder = value ? WindowBorder.Resizable : WindowBorder.Fixed; }
	}

	/// <inheritdoc/>
	public bool IsCursorVisible
	{
		get => _cursorVisible;
		set
		{
			_cursorVisible = value;
			CursorStateChanged?.Invoke();
		}
	}

	/// <summary>Raised when <see cref="IsMouseGrabbed"/> or <see cref="IsCursorVisible"/> changes (the input module applies them to the cursor).</summary>
	internal event Action? CursorStateChanged;

	/// <summary>
	/// Creates the native window. Called by the window system's Init step; does nothing when graphics output is not a
	/// window or the window already exists.
	/// </summary>
	public void Initialize()
	{
		if (_window is not null) return;
		if (_graphicsConfig.CurrentValue.Output != GraphicsOutput.Window)
		{
			_logger.LogInformation("Graphics output is {Output}; no window is created.", _graphicsConfig.CurrentValue.Output);
			return;
		}

		var config = _windowConfig.CurrentValue;
		var requested = config.Platform;
		var platform = ResolvePlatform(requested);

		IView view;
		try
		{
			view = Create(platform, config);
		}
		catch (Exception ex) when (requested == WindowPlatform.Auto && platform == WindowPlatform.Glfw)
		{
			_logger.LogWarning(ex, "GLFW window creation failed; falling back to SDL.");
			platform = WindowPlatform.Sdl;
			view = Create(platform, config);
		}

		Platform = platform;
		_view = view;
		_window = view as ISilkWindow;
		view.Closing += _onClosing;
		view.FocusChanged += _onFocusChanged;
		view.Resize += _onResize;
		IsActive = true;

		if (_window is not null && config.WindowState == IonWindowState.BorderlessFullScreen) _makeBorderlessFullscreen(_window);

		_lastSize = view.Size;
		_logger.LogInformation("{Kind} created with {Platform}: {Width}x{Height} (framebuffer {FbWidth}x{FbHeight}, {Native}).",
			_window is null ? "View" : "Window", platform, view.Size.X, view.Size.Y, view.FramebufferSize.X, view.FramebufferSize.Y, view.Native?.Kind);

		Created?.Invoke(view);
		_events.Emit(new WindowResizeEvent(Width, Height));
	}

	/// <summary>
	/// Pumps the window's events (input callbacks, resize, focus, close). Called by the window system at the start of every
	/// frame.
	/// </summary>
	public void Step()
	{
		if (_view is null || IsClosed) return;
		_view.DoEvents();
	}

	/// <summary>Destroys the native window.</summary>
	public void Dispose()
	{
		if (_view is null) return;
		var window = _view;
		_view = null;
		_window = null;
		IsClosed = true;
		window.Closing -= _onClosing;
		window.FocusChanged -= _onFocusChanged;
		window.Resize -= _onResize;
		try
		{
			window.Reset();
		}
		finally
		{
			window.Dispose();
		}
	}

	/// <summary>Marks the window closed after the exit request (the native window lives until disposal).</summary>
	internal void MarkClosed() => IsClosed = true;

	private IView Create(WindowPlatform platform, WindowConfig config)
	{
		RegisterPlatform(platform);

		var backend = _graphicsConfig.CurrentValue.PreferredBackend;
		var options = backend switch
		{
			// The GLES backend renders into offscreen targets and blits to the default framebuffer: no depth or stencil.
			GraphicsBackend.OpenGLES => WindowOptions.Default with { API = GlesApi(3, 1), PreferredDepthBufferBits = 0, PreferredStencilBufferBits = 0 },
			_ => WindowOptions.DefaultVulkan,
		};
		options = options with
		{
			Title = _title,
			Size = new Vector2D<int>(config.Width is > 0 ? config.Width.Value : 960, config.Height is > 0 ? config.Height.Value : 540),
			Position = config.WindowX is { } x && config.WindowY is { } y ? new Vector2D<int>(x, y) : new Vector2D<int>(-1, -1),
			IsVisible = config.WindowState != IonWindowState.Hidden,
			WindowState = config.WindowState switch
			{
				IonWindowState.FullScreen => SilkWindowState.Fullscreen,
				IonWindowState.Maximized => SilkWindowState.Maximized,
				IonWindowState.Minimized => SilkWindowState.Minimized,
				_ => SilkWindowState.Normal,
			},
			WindowBorder = config.WindowState == IonWindowState.BorderlessFullScreen ? WindowBorder.Hidden : config.Resizable ? WindowBorder.Resizable : WindowBorder.Fixed,
			VSync = _graphicsConfig.CurrentValue.VSync,
			// Ion's loop paces frames; Silk's own timing is unused because Run is never called.
			FramesPerSecond = 0,
			UpdatesPerSecond = 0,
			ShouldSwapAutomatically = false,
		};

		// SDL on Android and iOS is view-only: the view is the whole screen (size, position and state do not apply).
		var viewOnly = Window.IsViewOnly;
		IView window = viewOnly ? Window.GetView(new ViewOptions(options)) : Window.Create(options);
		try
		{
			window.Initialize();
		}
		catch (Exception ex) when (backend == GraphicsBackend.OpenGLES && options.API.Version.MinorVersion > 0)
		{
			// No OpenGL ES 3.1 context: fall back to 3.0 (the GLES backend has ES 3.0 paths).
			_logger.LogWarning(ex, "No OpenGL ES 3.1 context; retrying with OpenGL ES 3.0.");
			window.Dispose();
			var fallback = options with { API = GlesApi(3, 0) };
			window = viewOnly ? Window.GetView(new ViewOptions(fallback)) : Window.Create(fallback);
			window.Initialize();
		}

		return window;
	}

	/// <summary>The window API for an OpenGL ES <paramref name="major"/>.<paramref name="minor"/> context.</summary>
	private static GraphicsAPI GlesApi(int major, int minor) =>
		new(ContextAPI.OpenGLES, ContextProfile.Core, ContextFlags.Default, new APIVersion(major, minor));

	/// <summary>
	/// The platform <paramref name="requested"/> resolves to on this OS: <see cref="WindowPlatform.Auto"/> is GLFW on
	/// desktop and SDL on Android and iOS.
	/// </summary>
	public static WindowPlatform ResolvePlatform(WindowPlatform requested) => requested switch
	{
		WindowPlatform.Auto when OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() => WindowPlatform.Sdl,
		WindowPlatform.Auto => WindowPlatform.Glfw,
		_ => requested,
	};

	/// <summary>
	/// Registers <paramref name="platform"/> (windowing and input) with Silk.NET, with the reflection-based discovery of
	/// first-party platforms turned off. Registration is process-wide; switching platforms removes the previous one.
	/// </summary>
	internal static void RegisterPlatform(WindowPlatform platform)
	{
		lock (PlatformLock)
		{
			if (_registeredPlatform == platform) return;

			// Before Silk.NET loads any native library: its own probing misses the NuGet runtimes/ folder on unlisted
			// Linux distributions (Ubuntu among them), see SilkNativeLibraries.
			SilkNativeLibraries.EnsureResolver();

			// Silk.NET allows this only before its platform list is first used, so only on the first registration.
			if (_registeredPlatform == WindowPlatform.Auto)
			{
				Window.ShouldLoadFirstPartyPlatforms(false);
				Silk.NET.Input.InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false);
			}

			foreach (var existing in Window.Platforms.ToArray()) Window.Remove(existing);
			foreach (var existing in Silk.NET.Input.InputWindowExtensions.Platforms.ToArray()) Silk.NET.Input.InputWindowExtensions.Remove(existing);

			switch (platform)
			{
				case WindowPlatform.Glfw:
					GlfwWindowing.RegisterPlatform();
					Silk.NET.Input.Glfw.GlfwInput.RegisterPlatform();
					break;
				case WindowPlatform.Sdl:
					SdlWindowing.RegisterPlatform();
					Silk.NET.Input.Sdl.SdlInput.RegisterPlatform();
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(platform), platform, "Resolve Auto with ResolvePlatform first.");
			}

			_registeredPlatform = platform;
		}
	}

	private void _makeBorderlessFullscreen(ISilkWindow window)
	{
		if (window.Monitor is not { } monitor) return;
		var bounds = monitor.Bounds;
		window.Position = bounds.Origin;
		window.Size = bounds.Size;
	}

	private void _onClosing()
	{
		if (IsClosing) return;
		IsClosing = true;
		_logger.LogDebug("Window close requested.");
		_events.Emit<WindowClosedEvent>();
	}

	private void _onFocusChanged(bool focused)
	{
		IsActive = focused;
		if (focused) _events.Emit<WindowFocusGainedEvent>();
		else _events.Emit<WindowFocusLostEvent>();
	}

	private void _onResize(Vector2D<int> size)
	{
		if (size == _lastSize) return;
		_lastSize = size;
		_events.Emit(new WindowResizeEvent((uint)Math.Max(0, size.X), (uint)Math.Max(0, size.Y)));
	}

	private static NativeWindowHandles ToHandles(INativeWindow native)
	{
		if (native.Win32 is { } win32) return new(NativeWindowKind.Win32, win32.HInstance, win32.Hwnd);
		if (native.Wayland is { } wayland) return new(NativeWindowKind.Wayland, wayland.Display, wayland.Surface);
		if (native.X11 is { } x11) return new(NativeWindowKind.X11, x11.Display, (nint)x11.Window);
		if (native.Cocoa is { } cocoa) return new(NativeWindowKind.Cocoa, 0, cocoa);
		if (native.Android is { } android) return new(NativeWindowKind.Android, android.Surface, android.Window);
		if (native.UIKit is { } uikit) return new(NativeWindowKind.UIKit, 0, uikit.Window);
		return default;
	}
}
