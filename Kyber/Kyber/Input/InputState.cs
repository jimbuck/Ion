namespace Kyber;

public class InputState
{
    private Veldrid.InputSnapshot? _inputSnapshot;

    private readonly Dictionary<Key, KeyEvent> _keyEvents = new();
    private readonly Dictionary<MouseButton, MouseEvent> _mouseEvents = new();
    private readonly HashSet<Key> _downKeys = new();

    public Vector2 MousePosition => _inputSnapshot?.MousePosition ?? Vector2.Zero;

    public float WheelDelta => _inputSnapshot?.WheelDelta ?? 0;

    internal void UpdateState(Veldrid.InputSnapshot snapshot)
	{
		_inputSnapshot = snapshot;

        _keyEvents.Clear();
        foreach(var k in _inputSnapshot.KeyEvents)
        {
            var key = (Key)k.Key;
            _keyEvents[key] = k;

            if (!k.Down) _downKeys.Remove(key);
            else if (!k.Repeat) _downKeys.Add(key);
        }

        _mouseEvents.Clear();
        foreach (var m in _inputSnapshot.MouseEvents) _mouseEvents[(MouseButton)m.MouseButton] = m;
    }

    public bool Pressed(MouseButton btn) => _mouseEvents.TryGetValue(btn, out var mouseEvent) && mouseEvent.Down;
    public bool Released(MouseButton btn) => _mouseEvents.TryGetValue(btn, out var mouseEvent) && !mouseEvent.Down;
    public bool Down(MouseButton btn) => _inputSnapshot?.IsMouseDown((Veldrid.MouseButton)btn) ?? false;
    public bool Up(MouseButton btn) => !Down(btn);


    public bool Pressed(Key key) => _keyEvents.TryGetValue(key, out var keyEvent) && keyEvent.Down && !keyEvent.Repeat;
    public bool Pressed(Key key, ModifierKeys modifiers) => _keyEvents.TryGetValue(key, out var keyEvent) && ((keyEvent.Modifiers & modifiers) != ModifierKeys.None) && keyEvent.Down && !keyEvent.Repeat;

    public bool Released(Key key) => _keyEvents.TryGetValue(key, out var keyEvent) && !keyEvent.Down;
    public bool Released(Key key, ModifierKeys modifiers) => _keyEvents.TryGetValue(key, out var keyEvent) && ((keyEvent.Modifiers & modifiers) != ModifierKeys.None) && !keyEvent.Down;

    public bool Down(Key key) => _downKeys.Contains(key);

    public bool Up(Key key) => !Down(key);
}
