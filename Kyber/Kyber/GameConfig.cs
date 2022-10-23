using Kyber.Graphics;

namespace Kyber;

public interface IGameConfig
{
	#region Window Options

	string WindowTitle { get; set; }
	int? WindowHeight { get; }
	int? WindowWidth { get; }
	int? WindowX { get; }
	int? WindowY { get; }
	uint? ResolutionX { get; set; }
	uint? ResolutionY { get; set; }
	WindowState WindowState { get; }

	#endregion

	#region Graphics Options

	GraphicsBackend PreferredBackend { get; }
	bool VSync { get; set; }
	uint MaxFPS { get; set; }
	GraphicsOutput Output { get; }

	#endregion
}

internal class GameConfig : IGameConfig
{
	public string WindowTitle { get; set; } = "Kyber";

	public int? WindowHeight { get; set; } = 1080;

	public int? WindowWidth { get; set; }

	public int? WindowX { get; set; } = 100;

	public int? WindowY { get; set; } = 100;

	public uint? ResolutionX { get; set; }
	public uint? ResolutionY { get; set; }

	public WindowState WindowState { get; set; } = WindowState.Normal;

	public GraphicsBackend PreferredBackend { get; set; } = GraphicsBackend.Unspecified;

	public bool VSync { get; set; } = false;

	public uint MaxFPS { get; set; } = 0;
	public GraphicsOutput Output { get; set; } = GraphicsOutput.Window;
}
