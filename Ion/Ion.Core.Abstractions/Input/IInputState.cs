using System.Numerics;

namespace Ion;

public interface IInputState
{
	Vector2 MousePosition { get; }
	float WheelDelta { get; }

	bool Down(Key key);
	bool Down(MouseButton btn);
	bool Pressed(Key key);
	bool Pressed(Key key, ModifierKeys modifiers);
	bool Pressed(MouseButton btn);
	bool Released(Key key);
	bool Released(Key key, ModifierKeys modifiers);
	bool Released(MouseButton btn);
	bool Up(Key key);
	bool Up(MouseButton btn);

	void SetMousePosition(Vector2 position);
	void SetMousePosition(int x, int y);
}