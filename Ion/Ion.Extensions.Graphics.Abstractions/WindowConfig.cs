
namespace Ion.Extensions.Graphics;

public class WindowConfig
{
	public bool ShowCursor { get; set; } = true;
	public int? Height { get; set; }
	public int? Width { get; set; }
	public int? WindowX { get; set; }
	public int? WindowY { get; set; }
	public uint? ResolutionX { get; set; }
	public uint? ResolutionY { get; set; }
	public WindowState WindowState { get; set; }
}
