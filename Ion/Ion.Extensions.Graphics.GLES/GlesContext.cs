using System.Runtime.InteropServices;

using Silk.NET.Core.Contexts;

namespace Ion.Extensions.Graphics.GLES;

/// <summary>
/// An OpenGL ES context the <see cref="GlesDevice"/> renders with: a window's context (presentable) or a headless EGL
/// context. All calls happen on the thread that created the device.
/// </summary>
public interface IGlesContext : IDisposable
{
	/// <summary>A description for logs (for example <c>"EGL surfaceless"</c> or <c>"window"</c>).</summary>
	string Description { get; }

	/// <summary>True when the context has a window surface to present to (<see cref="SwapBuffers"/>).</summary>
	bool IsPresentable { get; }

	/// <summary>The address of a GL entry point, or 0.</summary>
	nint GetProcAddress(string name);

	/// <summary>Makes the context current on the calling thread.</summary>
	void MakeCurrent();

	/// <summary>Presents the window's back buffer.</summary>
	void SwapBuffers();

	/// <summary>Sets the swap interval (1: vsync, 0: none).</summary>
	void SwapInterval(int interval);
}

/// <summary>
/// The GL context of a Silk.NET window (<c>IView.GLContext</c>), created by the windowing module when the window was
/// created with the OpenGL ES API (<see cref="GraphicsBackend.OpenGLES"/>). The window owns it; disposing this does not
/// destroy it.
/// </summary>
public sealed class SilkGlesContext(IGLContext context) : IGlesContext
{
	/// <summary>The Silk.NET context.</summary>
	public IGLContext Context { get; } = context ?? throw new ArgumentNullException(nameof(context));

	/// <inheritdoc/>
	public string Description => "window GL context";

	/// <inheritdoc/>
	public bool IsPresentable => true;

	/// <inheritdoc/>
	public nint GetProcAddress(string name) => Context.TryGetProcAddress(name, out var address) ? address : 0;

	/// <inheritdoc/>
	public void MakeCurrent()
	{
		if (!Context.IsCurrent) Context.MakeCurrent();
	}

	/// <inheritdoc/>
	public void SwapBuffers() => Context.SwapBuffers();

	/// <inheritdoc/>
	public void SwapInterval(int interval) => Context.SwapInterval(interval);

	/// <summary>Does nothing: the window owns its context.</summary>
	public void Dispose() { }
}

/// <summary>
/// A headless OpenGL ES context created through EGL (<c>libEGL.so.1</c>), with no window and no X server: the Mesa
/// surfaceless platform (<c>EGL_MESA_platform_surfaceless</c>) when the EGL client offers it, else the default display;
/// no surface at all when the display has <c>EGL_KHR_surfaceless_context</c>, else a 1x1 pbuffer. Rendering always goes to
/// framebuffer objects, so the surface is never drawn to.
/// </summary>
/// <remarks>
/// The EGL display is initialized once per process and never terminated: terminating it would destroy the contexts other
/// devices (tests running in parallel) still use.
/// </remarks>
public sealed unsafe class EglContext : IGlesContext
{
	private readonly nint _display;
	private readonly nint _context;
	private readonly nint _surface;
	private bool _disposed;

	private EglContext(nint display, nint context, nint surface, string description)
	{
		_display = display;
		_context = context;
		_surface = surface;
		Description = description;
	}

	/// <inheritdoc/>
	public string Description { get; }

	/// <inheritdoc/>
	public bool IsPresentable => false;

	/// <summary>
	/// Creates a headless ES context of at least <paramref name="major"/>.<paramref name="minor"/> (3.1, falling back to
	/// 3.0 when <paramref name="allowFallback"/>) and makes it current.
	/// </summary>
	/// <exception cref="InvalidOperationException">EGL is missing or no ES 3 context can be created.</exception>
	public static EglContext Create(int major = 3, int minor = 1, bool allowFallback = true, bool debug = false)
	{
		var egl = Egl.Instance ?? throw new InvalidOperationException($"EGL is not available ({Egl.LoadError}). On Linux install libegl1 and a Mesa driver (libegl-mesa0).");
		var (display, platform) = egl.Display();

		if (egl.BindAPI(Egl.OpenGLESApi) == 0) throw new InvalidOperationException($"eglBindAPI(OpenGL ES) failed: 0x{egl.GetError():X}.");
		var surfaceless = egl.HasDisplayExtension(display, "EGL_KHR_surfaceless_context");

		var configAttributes = stackalloc int[]
		{
			Egl.RenderableType, Egl.OpenGLES3Bit,
			Egl.SurfaceType, surfaceless ? 0 : Egl.PbufferBit,
			Egl.RedSize, 8, Egl.GreenSize, 8, Egl.BlueSize, 8, Egl.AlphaSize, 8,
			Egl.None,
		};
		nint config;
		int count;
		if (egl.ChooseConfig(display, configAttributes, &config, 1, &count) == 0 || count == 0)
		{
			throw new InvalidOperationException($"No EGL config for an OpenGL ES 3 context (eglChooseConfig: 0x{egl.GetError():X}).");
		}

		nint context = 0;
		var version = (major, minor);
		var versions = allowFallback && (major, minor) != (3, 0) ? new[] { (major, minor), (3, 0) } : new[] { (major, minor) };
		var contextAttributes = stackalloc int[7];
		foreach (var (ma, mi) in versions)
		{
			contextAttributes[0] = Egl.ContextMajorVersion;
			contextAttributes[1] = ma;
			contextAttributes[2] = Egl.ContextMinorVersion;
			contextAttributes[3] = mi;
			contextAttributes[4] = debug ? Egl.ContextOpenGLDebug : Egl.None;
			contextAttributes[5] = 1;
			contextAttributes[6] = Egl.None;
			context = egl.CreateContext(display, config, 0, contextAttributes);
			if (context != 0)
			{
				version = (ma, mi);
				break;
			}
		}

		if (context == 0) throw new InvalidOperationException($"eglCreateContext failed for OpenGL ES {major}.{minor}: 0x{egl.GetError():X}.");

		nint surface = 0;
		if (!surfaceless)
		{
			var pbufferAttributes = stackalloc int[] { Egl.Width, 1, Egl.Height, 1, Egl.None };
			surface = egl.CreatePbufferSurface(display, config, pbufferAttributes);
			if (surface == 0)
			{
				egl.DestroyContext(display, context);
				throw new InvalidOperationException($"eglCreatePbufferSurface failed: 0x{egl.GetError():X}.");
			}
		}

		var result = new EglContext(display, context, surface, $"EGL {platform}, {(surfaceless ? "no surface" : "1x1 pbuffer")}, ES {version.Item1}.{version.Item2} requested");
		result.MakeCurrent();
		return result;
	}

	/// <summary>Whether a headless ES 3 context can be created on this machine (tries once and caches the answer).</summary>
	public static bool IsAvailable() => Availability.Value;

	private static readonly Lazy<bool> Availability = new(() =>
	{
		try
		{
			using var context = Create();
			return true;
		}
		catch (Exception)
		{
			return false;
		}
	});

	/// <inheritdoc/>
	public nint GetProcAddress(string name) => Egl.Instance!.GetProcAddress(name);

	/// <inheritdoc/>
	public void MakeCurrent()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var egl = Egl.Instance!;
		if (egl.GetCurrentContext() == _context) return;
		if (egl.MakeCurrent(_display, _surface, _surface, _context) == 0) throw new InvalidOperationException($"eglMakeCurrent failed: 0x{egl.GetError():X}.");
	}

	/// <summary>Does nothing: a headless context has nothing to present.</summary>
	public void SwapBuffers() { }

	/// <summary>Does nothing: a headless context has nothing to present.</summary>
	public void SwapInterval(int interval) { }

	/// <summary>Releases the context (and pbuffer) from the calling thread and destroys them.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		var egl = Egl.Instance!;
		if (egl.GetCurrentContext() == _context) egl.MakeCurrent(_display, 0, 0, 0);
		if (_surface != 0) egl.DestroySurface(_display, _surface);
		egl.DestroyContext(_display, _context);
	}
}

/// <summary>
/// The few EGL 1.4/1.5 entry points the headless context needs, loaded from <c>libEGL</c> with <see cref="NativeLibrary"/>
/// into unmanaged function pointers (no reflection, NativeAOT-clean). Silk.NET 2.x has no EGL bindings.
/// </summary>
internal sealed unsafe class Egl
{
	public const int None = 0x3038;
	public const int SurfaceType = 0x3033;
	public const int PbufferBit = 0x0001;
	public const int RenderableType = 0x3040;
	public const int OpenGLES3Bit = 0x0040;
	public const int RedSize = 0x3024;
	public const int GreenSize = 0x3023;
	public const int BlueSize = 0x3022;
	public const int AlphaSize = 0x3021;
	public const int Width = 0x3057;
	public const int Height = 0x3056;
	public const int Extensions = 0x3055;
	public const int ContextMajorVersion = 0x3098;
	public const int ContextMinorVersion = 0x30FB;
	public const int ContextOpenGLDebug = 0x31B0;
	public const uint OpenGLESApi = 0x30A0;
	public const int PlatformSurfacelessMesa = 0x31DD;

	private static readonly Lazy<Egl?> Loaded = new(_load);
	private static string? _loadError;
	private static readonly Lock DisplayLock = new();
	private static (nint Display, string Platform)? _display;

	private readonly delegate* unmanaged<byte*, nint> _getProcAddress;
	private readonly delegate* unmanaged<nint, nint> _getDisplay;
	private readonly delegate* unmanaged<nint, int*, int*, uint> _initialize;
	private readonly delegate* unmanaged<uint, uint> _bindApi;
	private readonly delegate* unmanaged<nint, int*, nint*, int, int*, uint> _chooseConfig;
	private readonly delegate* unmanaged<nint, nint, nint, int*, nint> _createContext;
	private readonly delegate* unmanaged<nint, nint, int*, nint> _createPbufferSurface;
	private readonly delegate* unmanaged<nint, nint, nint, nint, uint> _makeCurrent;
	private readonly delegate* unmanaged<nint, nint, uint> _destroySurface;
	private readonly delegate* unmanaged<nint, nint, uint> _destroyContext;
	private readonly delegate* unmanaged<int> _getError;
	private readonly delegate* unmanaged<nint, int, byte*> _queryString;
	private readonly delegate* unmanaged<nint> _getCurrentContext;

	private Egl(nint library)
	{
		_getProcAddress = (delegate* unmanaged<byte*, nint>)NativeLibrary.GetExport(library, "eglGetProcAddress");
		_getDisplay = (delegate* unmanaged<nint, nint>)NativeLibrary.GetExport(library, "eglGetDisplay");
		_initialize = (delegate* unmanaged<nint, int*, int*, uint>)NativeLibrary.GetExport(library, "eglInitialize");
		_bindApi = (delegate* unmanaged<uint, uint>)NativeLibrary.GetExport(library, "eglBindAPI");
		_chooseConfig = (delegate* unmanaged<nint, int*, nint*, int, int*, uint>)NativeLibrary.GetExport(library, "eglChooseConfig");
		_createContext = (delegate* unmanaged<nint, nint, nint, int*, nint>)NativeLibrary.GetExport(library, "eglCreateContext");
		_createPbufferSurface = (delegate* unmanaged<nint, nint, int*, nint>)NativeLibrary.GetExport(library, "eglCreatePbufferSurface");
		_makeCurrent = (delegate* unmanaged<nint, nint, nint, nint, uint>)NativeLibrary.GetExport(library, "eglMakeCurrent");
		_destroySurface = (delegate* unmanaged<nint, nint, uint>)NativeLibrary.GetExport(library, "eglDestroySurface");
		_destroyContext = (delegate* unmanaged<nint, nint, uint>)NativeLibrary.GetExport(library, "eglDestroyContext");
		_getError = (delegate* unmanaged<int>)NativeLibrary.GetExport(library, "eglGetError");
		_queryString = (delegate* unmanaged<nint, int, byte*>)NativeLibrary.GetExport(library, "eglQueryString");
		_getCurrentContext = (delegate* unmanaged<nint>)NativeLibrary.GetExport(library, "eglGetCurrentContext");
	}

	/// <summary>The loaded EGL, or null when no EGL library could be loaded (<see cref="LoadError"/> says why).</summary>
	public static Egl? Instance => Loaded.Value;

	/// <summary>Why EGL could not be loaded.</summary>
	public static string LoadError => _loadError ?? "not loaded";

	private static Egl? _load()
	{
		ReadOnlySpan<string> names = OperatingSystem.IsWindows() ? ["libEGL.dll"] : OperatingSystem.IsMacOS() ? ["libEGL.dylib"] : ["libEGL.so.1", "libEGL.so"];
		foreach (var name in names)
		{
			if (!NativeLibrary.TryLoad(name, out var library)) continue;
			try
			{
				return new Egl(library);
			}
			catch (EntryPointNotFoundException ex)
			{
				_loadError = $"{name}: {ex.Message}";
				return null;
			}
		}

		_loadError = $"none of {string.Join(", ", names.ToArray())} could be loaded";
		return null;
	}

	/// <summary>The process-wide display, initialized on first use.</summary>
	public (nint Display, string Platform) Display()
	{
		lock (DisplayLock)
		{
			if (_display is { } existing) return existing;

			nint display = 0;
			var platform = "default display";
			// Client extensions (EGL_NO_DISPLAY): the Mesa surfaceless platform needs no X server, GBM device or window.
			var clientExtensions = QueryString(0, Extensions) ?? string.Empty;
			if (clientExtensions.Contains("EGL_MESA_platform_surfaceless", StringComparison.Ordinal))
			{
				var getPlatformDisplay = GetProcAddress("eglGetPlatformDisplayEXT");
				if (getPlatformDisplay != 0)
				{
					display = ((delegate* unmanaged<int, nint, int*, nint>)getPlatformDisplay)(PlatformSurfacelessMesa, 0, null);
					platform = "surfaceless platform";
				}
			}

			if (display == 0)
			{
				display = _getDisplay(0);
				platform = "default display";
			}

			if (display == 0) throw new InvalidOperationException($"eglGetDisplay failed: 0x{GetError():X}.");
			int major, minor;
			if (_initialize(display, &major, &minor) == 0) throw new InvalidOperationException($"eglInitialize failed ({platform}): 0x{GetError():X}.");
			_display = (display, $"{major}.{minor} {platform}");
			return _display.Value;
		}
	}

	public bool HasDisplayExtension(nint display, string extension) =>
		(QueryString(display, Extensions) ?? string.Empty).Split(' ').Contains(extension);

	public string? QueryString(nint display, int name)
	{
		var text = _queryString(display, name);
		return text is null ? null : Marshal.PtrToStringUTF8((nint)text);
	}

	public nint GetProcAddress(string name)
	{
		var length = System.Text.Encoding.UTF8.GetByteCount(name);
		var bytes = stackalloc byte[length + 1];
		System.Text.Encoding.UTF8.GetBytes(name, new Span<byte>(bytes, length));
		bytes[length] = 0;
		return _getProcAddress(bytes);
	}

	public uint BindAPI(uint api) => _bindApi(api);

	public uint ChooseConfig(nint display, int* attributes, nint* configs, int size, int* count) => _chooseConfig(display, attributes, configs, size, count);

	public nint CreateContext(nint display, nint config, nint share, int* attributes) => _createContext(display, config, share, attributes);

	public nint CreatePbufferSurface(nint display, nint config, int* attributes) => _createPbufferSurface(display, config, attributes);

	public uint MakeCurrent(nint display, nint draw, nint read, nint context) => _makeCurrent(display, draw, read, context);

	public uint DestroySurface(nint display, nint surface) => _destroySurface(display, surface);

	public uint DestroyContext(nint display, nint context) => _destroyContext(display, context);

	public nint GetCurrentContext() => _getCurrentContext();

	public int GetError() => _getError();
}
