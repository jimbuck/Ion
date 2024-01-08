using System.Numerics;

namespace Ion;

internal class InputState : IInputState
{
	public Vector2 MousePosition { get; } = Vector2.Zero;

	public float WheelDelta { get;  } = 0;

	public InputState() { }


	public bool Pressed(MouseButton btn) => false;
	public bool Released(MouseButton btn) => false;
	public bool Down(MouseButton btn) => false;
	public bool Up(MouseButton btn) => !Down(btn);

	public bool Pressed(Key key) => false;
	public bool Pressed(Key key, ModifierKeys modifiers) => false;

	public bool Released(Key key) => false;
	public bool Released(Key key, ModifierKeys modifiers) => false;

	public bool Down(Key key) => false;
	public bool Up(Key key) => !Down(key);

	public void SetMousePosition(Vector2 position) { }

	public void SetMousePosition(int x, int y) { }
}
