
namespace Ion.Extensions.Graphics;

/// <summary>
/// Window settings, bound from <c>Ion:Window</c>.
/// </summary>
public class WindowConfig
{
	public bool ShowCursor { get; set; } = true;
	public int? Height { get; set; }
	public int? Width { get; set; }
	public int? WindowX { get; set; }
	public int? WindowY { get; set; }
	public uint? ResolutionX { get; set; }
	public uint? ResolutionY { get; set; }

	/// <summary>
	/// The initial state: normal, maximized, minimized, hidden, <see cref="WindowState.FullScreen"/> (exclusive, at the
	/// window size) or <see cref="WindowState.BorderlessFullScreen"/> (borderless at the monitor size).
	/// </summary>
	public WindowState WindowState { get; set; }

	/// <summary>Whether the user can resize the window.</summary>
	public bool Resizable { get; set; } = true;

	/// <summary>
	/// The windowing platform of the Silk.NET windowing module (<c>Ion:Window:Platform</c>): <see cref="WindowPlatform.Glfw"/>,
	/// <see cref="WindowPlatform.Sdl"/> or <see cref="WindowPlatform.Auto"/> (GLFW on desktop, SDL on Android and iOS).
	/// Registered explicitly, without reflection-based discovery.
	/// </summary>
	public WindowPlatform Platform { get; set; } = WindowPlatform.Auto;
}

/// <summary>
/// The windowing library used by the Silk.NET windowing module.
/// </summary>
public enum WindowPlatform
{
	/// <summary>GLFW on desktop (Windows, macOS, Linux), SDL on Android and iOS.</summary>
	Auto,
	/// <summary>GLFW (desktop only).</summary>
	Glfw,
	/// <summary>SDL2 (desktop, Android, iOS, and handhelds such as the R36S where no GLFW build exists).</summary>
	Sdl,
}
