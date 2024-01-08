using System.Numerics;

namespace Ion.Extensions.Graphics;

public interface IWindow
{
	int Width { get; set; }
	int Height { get; set; }

	Vector2 Size { get; set; }
	bool HasClosed { get; }
	bool IsActive { get; }

	bool IsVisible { get; set; }
	bool IsMaximized { get; set; }
	bool IsMinimized { get; set; }
	bool IsFullscreen { get; set; }
	bool IsBorderlessFullscreen { get; set; }

	string Title { get; set; }
	bool IsResizable { get; set; }
	bool IsCursorVisible { get; set; }
	bool IsBorderVisible { get; set; }
}