using System.Numerics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SDL;
using static SDL.SDL;
using static SDL.SDL_bool;
using WebGPU;
using static WebGPU.WebGPU;

using Ion.Extensions.Graphics.MacOS;

namespace Ion.Extensions.Graphics;

internal class Window(
	IOptionsMonitor<GameConfig> gameConfig,
	IOptionsMonitor<GraphicsConfig> graphicsConfig,
	IOptionsMonitor<WindowConfig> windowConfig,
	ILogger<Window> logger,
	IEventEmitter eventEmitter,
	IEventListener events) : IWindow
{
	private readonly EventEmitter _eventEmitter = (EventEmitter)eventEmitter;
	private SDL_Window _window = SDL_Window.Null;
	private SDL_WindowID _windowId = new(0);

	internal object? InputSnapshot { get; private set; }

	private Rectangle _initialPosition = new Rectangle(windowConfig.CurrentValue.WindowX ?? SDL_WINDOWPOS_CENTERED, windowConfig.CurrentValue.WindowY ?? SDL_WINDOWPOS_CENTERED, windowConfig.CurrentValue.Width ?? 960, windowConfig.CurrentValue.Height ?? 540);
	private SDL_WindowFlags _initialWindowState = SDL_WindowFlags.None;


	private (uint Width, uint Height) _prevSize = (0, 0);
	private bool _isRunning = false;

	public uint Width
	{
		get => (uint)_size.X;
		set
		{
			if (_window.IsNotNull)
			{
				_ = SDL_SetWindowSize(_window, (int)value, (int)_size.Y);
				_size = _size with { X = value };
			}
		}
	}

	public uint Height
	{
		get => (uint)_size.Y;
		set
		{
			if (_window.IsNotNull)
			{
				_ = SDL_SetWindowSize(_window, (int)_size.X, (int)value);
				_size = _size with { Y = value };
			}
		}
	}

	private Vector2 _size = default;
	public Vector2 Size
	{
		get => _size;
		set
		{
			if (_window.IsNotNull)
			{
				_size = value;
				_ = SDL_SetWindowSize(_window, (int)_size.X, (int)_size.Y);
			}
		}
	}

	public bool IsClosing { get; private set; }
	public bool IsClosed { get; private set; }
	public bool IsActive { get; private set; }

	private bool _isVisible = true;
	public bool IsVisible
	{
		get => _isVisible;
		set {
			if (_window.IsNotNull)
			{
				_ = value ? SDL_ShowWindow(_window) : SDL_HideWindow(_window);
				_isVisible = value;
			}
		}
	}

	public bool IsMaximized
	{
		get => ((SDL_WindowFlags)SDL_GetWindowFlags(_window) & SDL_WindowFlags.Maximized) == SDL_WindowFlags.Maximized;
		set { if (_window.IsNotNull) _ = value ? SDL_MaximizeWindow(_window) : SDL_RestoreWindow(_window); }
	}
	public bool IsMinimized
	{
		get => ((SDL_WindowFlags)SDL_GetWindowFlags(_window) & SDL_WindowFlags.Minimized) == SDL_WindowFlags.Minimized;
		set { if (_window.IsNotNull) _ = value ? SDL_MinimizeWindow(_window) : SDL_RestoreWindow(_window); }
	}
	public bool IsFullscreen
	{
		get => ((SDL_WindowFlags)SDL_GetWindowFlags(_window) & SDL_WindowFlags.Fullscreen) == SDL_WindowFlags.Fullscreen;
		set { if (_window.IsNotNull) _ = SDL_SetWindowFullscreen(_window, value); }
	}
	public bool IsBorderless
	{
		get => ((SDL_WindowFlags)SDL_GetWindowFlags(_window) & SDL_WindowFlags.Borderless) == SDL_WindowFlags.Borderless;
		set { if (_window.IsNotNull) _ = SDL_SetWindowBordered(_window, value ? SDL_TRUE : SDL_FALSE); }
	}

	private bool _isMouseGrabbed = false;

	public bool IsMouseGrabbed
	{
		get => _isMouseGrabbed;
		set
		{
			if (_window.IsNotNull)
			{
				SDL_SetWindowMouseGrab(_window, value ? SDL_TRUE : SDL_FALSE);
				_isMouseGrabbed = value;
			}
		}
	}

	public string Title
	{
		get => SDL_GetWindowTitleString(_window);
		set
		{
			if (_window.IsNotNull) _ = SDL_SetWindowTitle(_window, value);
		}
	}
	public bool IsResizable
	{
		get => ((SDL_WindowFlags)SDL_GetWindowFlags(_window) & SDL_WindowFlags.Resizable) == SDL_WindowFlags.Resizable;
		set { if (_window.IsNotNull) _ = SDL_SetWindowResizable(_window, value ? SDL_TRUE : SDL_FALSE); }
	}
	public bool IsCursorVisible
	{
		get => SDL_CursorVisible() == SDL_TRUE;
		set { if (_window.IsNotNull) _ = value ? SDL_ShowCursor() : SDL_HideCursor(); }
	}

	public void Initialize()
	{
		if (graphicsConfig.CurrentValue.Output != GraphicsOutput.Window) return;

		logger.LogInformation("Creating window...");


		_initWindow(gameConfig.CurrentValue.Title, _initialPosition, _initialWindowState);	

		logger.LogInformation($"Window created! ({Width}x{Height})");
	}

	private void _initWindow(string windowTitle, Rectangle position, SDL_WindowFlags flags)
	{
#if DEBUG
		SDL_LogSetAllPriority(SDL_LogPriority.Verbose);
#endif

		SDL_SetLogOutputFunction(_onSdlLog);

		flags |= SDL_WindowFlags.HighPixelDensity;

		if (SDL_Init(SDL_InitFlags.Timer | SDL_InitFlags.Video | SDL_InitFlags.Gamepad | SDL_InitFlags.Haptic) != 0)
		{
			var error = SDL_GetErrorString();
			throw new Exception($"Failed to start SDL2: {error}");
		}

		_size = new Vector2(position.Width, position.Height);
		_window = SDL_CreateWindow(windowTitle, position.Width, position.Height, flags);
		_ = SDL_SetWindowPosition(_window, position.X, position.Y);

		_windowId = SDL_GetWindowID(_window);

		IsCursorVisible = windowConfig.CurrentValue.ShowCursor;
		_isRunning = true;
	}

	internal unsafe WGPUSurface CreateSurface(WGPUInstance instance, bool useWayland = false)
	{
		if (OperatingSystem.IsWindows())
		{
			WGPUSurfaceDescriptorFromWindowsHWND chain = new()
			{
				hwnd = SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.win32.hwnd"),
				hinstance = WindowHandleHelpers.GetModuleHandleW(null),
				chain = new WGPUChainedStruct()
				{
					sType = WGPUSType.SurfaceDescriptorFromWindowsHWND
				}
			};
			WGPUSurfaceDescriptor descriptor = new()
			{
				nextInChain = (WGPUChainedStruct*)&chain
			};
			return wgpuInstanceCreateSurface(instance, &descriptor);
		}
		else if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
		{
			NSWindow ns_window = new(SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.cocoa.window"));
			CAMetalLayer metal_layer = CAMetalLayer.New();
			ns_window.contentView.wantsLayer = true;
			ns_window.contentView.layer = metal_layer.Handle;

			WGPUSurfaceDescriptorFromMetalLayer chain = new()
			{
				layer = metal_layer.Handle,
				chain = new WGPUChainedStruct()
				{
					sType = WGPUSType.SurfaceDescriptorFromMetalLayer
				}
			};
			WGPUSurfaceDescriptor descriptor = new()
			{
				nextInChain = (WGPUChainedStruct*)&chain
			};
			return wgpuInstanceCreateSurface(instance, &descriptor);
		}
		else if (OperatingSystem.IsLinux())
		{
			if (useWayland)
			{
				WGPUSurfaceDescriptorFromWaylandSurface chain = new()
				{
					display = SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.wayland.display"),
					surface = SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.wayland.surface"),
					chain = new WGPUChainedStruct()
					{
						sType = WGPUSType.SurfaceDescriptorFromWaylandSurface
					}
				};
				WGPUSurfaceDescriptor descriptor = new()
				{
					nextInChain = (WGPUChainedStruct*)&chain
				};
				return wgpuInstanceCreateSurface(instance, &descriptor);
			}
			else
			{
				WGPUSurfaceDescriptorFromXlibWindow chain = new()
				{
					display = SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.x11.display"),
					window = (ulong)SDL_GetProperty(SDL_GetWindowProperties(_window), "SDL.window.x11.window"),
					chain = new WGPUChainedStruct()
					{
						sType = WGPUSType.SurfaceDescriptorFromXlibWindow
					}
				};
				WGPUSurfaceDescriptor descriptor = new()
				{
					nextInChain = (WGPUChainedStruct*)&chain
				};
				return wgpuInstanceCreateSurface(instance, &descriptor);
			}
		}
		else if (OperatingSystem.IsBrowser())
		{
			var config = graphicsConfig.CurrentValue;
			if (string.IsNullOrWhiteSpace(config.CanvasSelector)) throw new ArgumentNullException(nameof(config.CanvasSelector), "No Canvas HTML selector was provided!");

			fixed (sbyte* selector = Interop.GetUtf8Span(config.CanvasSelector))
			{
				WGPUSurfaceDescriptorFromCanvasHTMLSelector chain = new()
				{
					selector = selector,
					chain = new WGPUChainedStruct()
					{
						sType = WGPUSType.SurfaceDescriptorFromCanvasHTMLSelector
					}
				};
				WGPUSurfaceDescriptor descriptor = new()
				{
					nextInChain = (WGPUChainedStruct*)&chain
				};
				return wgpuInstanceCreateSurface(instance, &descriptor);
			}
		}

		return WGPUSurface.Null;
	}

	public unsafe void Step()
	{
		if (!_isRunning || graphicsConfig.CurrentValue.Output != GraphicsOutput.Window || _window.IsNull)
		{
			InputSnapshot = default;
			return;
		}

		if (IsClosing && events.OnLatest<WindowClosedEvent>())
		{
			InputSnapshot = default;
			IsClosed = true;
			_isRunning = false;
			return;
		}

		SDL_Event evt;
		while (SDL_PollEvent(&evt))
		{
			if (evt.type == SDL_EventType.Quit)
			{
				logger.LogInformation("Window closed!");
				_isRunning = false;
				break;
			}

			if (evt.type >= SDL_EventType.WindowFirst && evt.type <= SDL_EventType.WindowLast)
			{
				_handleWindowEvent(evt);
			}
		}
	}

	private void _handleWindowEvent(in SDL_Event evt)
	{
		switch (evt.window.type)
		{
			case SDL_EventType.WindowCloseRequested:
				if (evt.window.windowID == _windowId) _onClosedRequested();
				break;
			case SDL_EventType.WindowResized:
				_onResize(evt);
				break;
			case SDL_EventType.WindowExposed:
				_onExposed();
				break;
			case SDL_EventType.WindowRestored:
				_onRestored();
				break;
			case SDL_EventType.WindowMinimized:
				_onMinimized();
				break;
			case SDL_EventType.WindowFocusGained:
				_onFocusGained();
				break;
			case SDL_EventType.WindowFocusLost:
				_onFocusLost();
				break;
			default:
				logger.LogDebug("SDL Event: {eventType}", evt.window.type.ToString("G"));
				break;
		}
	}

	private void _onResize(in SDL_Event evt)
	{
		if (_window.IsNull)
		{
			logger.LogDebug("Null window was resized.");
			return;
		}

		var newWidth = evt.window.data1;
		var newHeight = evt.window.data2;

		if (_prevSize.Width == newWidth && _prevSize.Height == newHeight) return;

		_prevSize = new((uint)_size.X, (uint)_size.Y);
		_size = new(newWidth, newHeight);

		_eventEmitter.Emit(new WindowResizeEvent((uint)_size.X, (uint)_size.Y));
		logger.LogDebug(message: "Window resized: {oldWidth}x{oldHeight} -> {newWidth}x{newHeight}", _prevSize.Width, _prevSize.Height, _size.X, _size.Y);
	}

	private void _onExposed()
	{
		_eventEmitter.Emit(new WindowResizeEvent((uint)_size.X, (uint)_size.Y));

		logger.LogDebug(message: "Window exposed: {newWidth}x{newHeight}", _size.X, _size.Y);
	}

	private void _onMinimized()
	{
		_eventEmitter.Emit(new WindowResizeEvent((uint)_size.X, (uint)_size.Y));

		logger.LogDebug(message: "Window minimized: {newWidth}x{newHeight}", _size.X, _size.Y);
	}

	private void _onRestored()
	{
		_size = new(_prevSize.Width, _prevSize.Height);
		_eventEmitter.Emit(new WindowResizeEvent((uint)_size.X, (uint)_size.Y));

		logger.LogDebug(message: "Window restored: {newWidth}x{newHeight}", _size.X, _size.Y);
	}

	private void _onFocusGained() => _eventEmitter.Emit<WindowFocusGainedEvent>();

	private void _onFocusLost() => _eventEmitter.Emit<WindowFocusLostEvent>();

	private void _onClosedRequested()
	{
		IsClosing = true;
		logger.LogDebug("Window closing!");
		_eventEmitter.Emit<WindowClosedEvent>();
	}

	private void _onSdlLog(SDL_LogCategory category, SDL_LogPriority priority, string description)
	{
		var logLevel = priority switch {
			SDL_LogPriority.Critical => LogLevel.Critical,
			SDL_LogPriority.Verbose => LogLevel.Trace,
			SDL_LogPriority.Debug => LogLevel.Debug,
			SDL_LogPriority.Info => LogLevel.Information,
			SDL_LogPriority.Warn => LogLevel.Warning,
			SDL_LogPriority.Error => LogLevel.Error,
			_ => throw new NotImplementedException(),
		};

		logger.Log(logLevel, "[{category}] SDL: {description}", category, description);
	}
}

internal sealed unsafe partial class WindowHandleHelpers
{
	[LibraryImport("kernel32")]
	public static partial nint GetModuleHandleW(ushort* lpModuleName);
}
