using Veldrid;
using Ion.Graphics;
namespace Ion;

public interface IGameConfig
{
	#region Window Options

	string Title { get; set; }
	int? WindowHeight { get; set; }
	int? WindowWidth { get; set; }
	int? WindowX { get; set; }
	int? WindowY { get; set; }
	uint? ResolutionX { get; set; }
	uint? ResolutionY { get; set; }
	WindowState WindowState { get; set; }

	#endregion

	#region Graphics Options

	GraphicsBackend PreferredBackend { get; set; }
	bool VSync { get; set; }
	uint MaxFPS { get; set; }
	GraphicsOutput Output { get; set; }
	Color ClearColor { get; set; }

	#endregion
}

internal class GameConfig : IGameConfig
{
	public string Title { get; set; } = "Ion";

	public int? WindowHeight { get; set; } = 1080;

	public int? WindowWidth { get; set; }

	public int? WindowX { get; set; } = Veldrid.Sdl2.Sdl2Native.SDL_WINDOWPOS_CENTERED;

	public int? WindowY { get; set; } = Veldrid.Sdl2.Sdl2Native.SDL_WINDOWPOS_CENTERED;

	public uint? ResolutionX { get; set; }
	public uint? ResolutionY { get; set; }

	public WindowState WindowState { get; set; } = WindowState.Normal;

	public GraphicsBackend PreferredBackend { get; set; } = Veldrid.StartupUtilities.VeldridStartup.GetPlatformDefaultBackend();

	public bool VSync { get; set; } = false;

	public uint MaxFPS { get; set; } = 300;
	public GraphicsOutput Output { get; set; } = GraphicsOutput.Window;

	public Color ClearColor { get; set; } = Color.Black;
}
