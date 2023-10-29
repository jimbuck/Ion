using Veldrid;

namespace Kyber.Extensions.Graphics;

public class WindowConfig
{
	public int? WindowHeight { get; set; }
	public int? WindowWidth { get; set; }
	public int? WindowX { get; set; }
	public int? WindowY { get; set; }
	public uint? ResolutionX { get; set; }
	public uint? ResolutionY { get; set; }
	public WindowState WindowState { get; set; }
}
