using System.Numerics;

namespace Ion.Extensions.Graphics;

public interface IWindow
{
	uint Width { get; set; }
	uint Height { get; set; }

	Vector2 Size { get; set; }
	bool IsClosing { get; }
	bool IsClosed { get; }
	bool IsActive { get; }

	bool IsVisible { get; set; }
	bool IsMaximized { get; set; }
	bool IsMinimized { get; set; }
	bool IsFullscreen { get; set; }
	bool IsBorderless { get; set; }
	bool IsMouseGrabbed { get; set; }

	string Title { get; set; }
	bool IsResizable { get; set; }
	bool IsCursorVisible { get; set; }
}