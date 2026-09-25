namespace Ion;

/// <summary>
/// Backend-independent keyboard and mouse button state. Feed it the events of one frame in order between
/// <see cref="BeginFrame"/> calls; it answers the edge (<c>Pressed</c>/<c>Released</c>) and level (<c>Down</c>) queries
/// of <see cref="IInputState"/>.
/// </summary>
/// <remarks>
/// Every event of a frame counts, so a key that is pressed and released within one frame reports both
/// <see cref="Pressed(Key)"/> and <see cref="Released(Key)"/> for that frame and is not <see cref="Down(Key)"/> afterwards.
/// Repeat events (a key held down) never count as a press but do mark the key as down.
/// This type has no dependency on Veldrid so it can be unit tested directly.
/// </remarks>
internal sealed class InputTracker
{
	private const int KeyCount = (int)Key.LastKey + 1;
	private const int ButtonCount = (int)MouseButton.LastButton + 1;

	private readonly bool[] _keysDown = new bool[KeyCount];
	private readonly bool[] _keysPressed = new bool[KeyCount];
	private readonly bool[] _keysReleased = new bool[KeyCount];
	private readonly ModifierKeys[] _pressedModifiers = new ModifierKeys[KeyCount];
	private readonly ModifierKeys[] _releasedModifiers = new ModifierKeys[KeyCount];

	private readonly bool[] _buttonsDown = new bool[ButtonCount];
	private readonly bool[] _buttonsPressed = new bool[ButtonCount];
	private readonly bool[] _buttonsReleased = new bool[ButtonCount];

	/// <summary>
	/// Clears the per-frame pressed and released state. Held keys and buttons stay down.
	/// </summary>
	public void BeginFrame()
	{
		Array.Clear(_keysPressed);
		Array.Clear(_keysReleased);
		Array.Clear(_pressedModifiers);
		Array.Clear(_releasedModifiers);
		Array.Clear(_buttonsPressed);
		Array.Clear(_buttonsReleased);
	}

	/// <summary>
	/// Applies one key event. Events must be applied in the order they happened.
	/// </summary>
	public void OnKey(Key key, bool down, bool repeat, ModifierKeys modifiers)
	{
		var i = (int)key;
		if ((uint)i >= KeyCount) return;

		if (down)
		{
			if (!repeat)
			{
				_keysPressed[i] = true;
				_pressedModifiers[i] |= modifiers;
			}

			_keysDown[i] = true;
		}
		else
		{
			_keysReleased[i] = true;
			_releasedModifiers[i] |= modifiers;
			_keysDown[i] = false;
		}
	}

	/// <summary>
	/// Applies one mouse button event. Events must be applied in the order they happened.
	/// </summary>
	public void OnMouseButton(MouseButton button, bool down)
	{
		var i = (int)button;
		if ((uint)i >= ButtonCount) return;

		if (down) _buttonsPressed[i] = true;
		else _buttonsReleased[i] = true;

		_buttonsDown[i] = down;
	}

	/// <summary>
	/// Releases every held key and button without reporting a <c>Released</c> edge, for example when the window
	/// loses focus and will not receive the matching key up events.
	/// </summary>
	public void ReleaseAll()
	{
		Array.Clear(_keysDown);
		Array.Clear(_buttonsDown);
	}

	public bool Down(Key key) => _get(_keysDown, (int)key);
	public bool Pressed(Key key) => _get(_keysPressed, (int)key);
	public bool Released(Key key) => _get(_keysReleased, (int)key);

	/// <summary>
	/// True when <paramref name="key"/> was pressed this frame with at least one of <paramref name="modifiers"/> held.
	/// </summary>
	public bool Pressed(Key key, ModifierKeys modifiers) => Pressed(key) && (_pressedModifiers[(int)key] & modifiers) != ModifierKeys.None;

	/// <summary>
	/// True when <paramref name="key"/> was released this frame with at least one of <paramref name="modifiers"/> held.
	/// </summary>
	public bool Released(Key key, ModifierKeys modifiers) => Released(key) && (_releasedModifiers[(int)key] & modifiers) != ModifierKeys.None;

	public bool Down(MouseButton button) => _get(_buttonsDown, (int)button);
	public bool Pressed(MouseButton button) => _get(_buttonsPressed, (int)button);
	public bool Released(MouseButton button) => _get(_buttonsReleased, (int)button);

	private static bool _get(bool[] set, int i) => (uint)i < (uint)set.Length && set[i];
}
