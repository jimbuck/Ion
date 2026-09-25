using System.Numerics;

namespace Ion;

/// <summary>
/// Backend-independent keyboard, mouse button, wheel and mouse motion state. Feed it the events of one frame in order
/// after each <see cref="BeginFrame"/> call; it answers the edge (<c>Pressed</c>/<c>Released</c>), delta
/// (<see cref="WheelDelta"/>, <see cref="MouseDelta"/>) and level (<c>Down</c>, <see cref="MousePosition"/>) queries of
/// <see cref="IInputState"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every event of a frame counts, so a key that is pressed and released within one frame reports both
/// <see cref="Pressed(Key)"/> and <see cref="Released(Key)"/> for that frame and is not <see cref="Down(Key)"/> afterwards.
/// Repeat events (a key held down) never count as a press but do mark the key as down.
/// </para>
/// <para>
/// Edges and deltas have two views. The per-frame view holds what happened since the last <see cref="BeginFrame"/> and is
/// what every stage but FixedUpdate reads. The fixed-step view holds what happened since the previous fixed step started:
/// events are tagged with the fixed step that will see them (the next one to start, <see cref="ILoopContext.FixedStepCount"/>
/// plus one, as of <see cref="BeginFrame"/>), so they accumulate across frames that run no fixed step and are seen by
/// exactly one fixed step however many frames or steps there are. The view is chosen from
/// <see cref="ILoopContext.Stage"/>; without a loop context only the per-frame view is used.
/// </para>
/// This type has no dependency on Veldrid so it can be unit tested directly.
/// </remarks>
internal sealed class InputTracker(ILoopContext? loop = null)
{
	private const int KeyCount = (int)Key.LastKey + 1;
	private const int ButtonCount = (int)MouseButton.LastButton + 1;

	private readonly ILoopContext? _loop = loop;

	// Level state, shared by both views.
	private readonly bool[] _keysDown = new bool[KeyCount];
	private readonly bool[] _buttonsDown = new bool[ButtonCount];

	// Per-frame edges.
	private readonly bool[] _keysPressed = new bool[KeyCount];
	private readonly bool[] _keysReleased = new bool[KeyCount];
	private readonly ModifierKeys[] _pressedModifiers = new ModifierKeys[KeyCount];
	private readonly ModifierKeys[] _releasedModifiers = new ModifierKeys[KeyCount];
	private readonly bool[] _buttonsPressed = new bool[ButtonCount];
	private readonly bool[] _buttonsReleased = new bool[ButtonCount];
	private float _wheelDelta;
	private Vector2 _mouseDelta;

	// Fixed-step edges: the (one-based) fixed step each edge belongs to; zero is never a valid step.
	private readonly long[] _keysPressedStep = new long[KeyCount];
	private readonly long[] _keysReleasedStep = new long[KeyCount];
	private readonly ModifierKeys[] _fixedPressedModifiers = new ModifierKeys[KeyCount];
	private readonly ModifierKeys[] _fixedReleasedModifiers = new ModifierKeys[KeyCount];
	private readonly long[] _buttonsPressedStep = new long[ButtonCount];
	private readonly long[] _buttonsReleasedStep = new long[ButtonCount];
	private float _fixedWheelDelta;
	private long _fixedWheelStep;
	private Vector2 _fixedMouseDelta;
	private long _fixedMouseStep;

	// The fixed step that will see the events applied since the last BeginFrame.
	private long _targetStep = 1;

	/// <summary>
	/// The mouse position after the events applied so far.
	/// </summary>
	public Vector2 MousePosition { get; private set; }

	/// <summary>
	/// The wheel movement of this frame, or since the previous fixed step when read from a fixed step.
	/// </summary>
	public float WheelDelta => _readFixed(out var step) ? (_fixedWheelStep == step ? _fixedWheelDelta : 0f) : _wheelDelta;

	/// <summary>
	/// The mouse movement of this frame, or since the previous fixed step when read from a fixed step.
	/// </summary>
	public Vector2 MouseDelta => _readFixed(out var step) ? (_fixedMouseStep == step ? _fixedMouseDelta : Vector2.Zero) : _mouseDelta;

	/// <summary>
	/// Clears the per-frame edges and deltas and starts tagging new events for the next fixed step to start. Held keys
	/// and buttons stay down, and fixed-step edges that no fixed step has seen yet are kept.
	/// </summary>
	public void BeginFrame()
	{
		Array.Clear(_keysPressed);
		Array.Clear(_keysReleased);
		Array.Clear(_pressedModifiers);
		Array.Clear(_releasedModifiers);
		Array.Clear(_buttonsPressed);
		Array.Clear(_buttonsReleased);
		_wheelDelta = 0;
		_mouseDelta = Vector2.Zero;

		_targetStep = (_loop?.FixedStepCount ?? 0) + 1;
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
				_mark(_keysPressedStep, _fixedPressedModifiers, i, modifiers);
			}

			_keysDown[i] = true;
		}
		else
		{
			_keysReleased[i] = true;
			_releasedModifiers[i] |= modifiers;
			_mark(_keysReleasedStep, _fixedReleasedModifiers, i, modifiers);
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

		if (down)
		{
			_buttonsPressed[i] = true;
			_buttonsPressedStep[i] = _targetStep;
		}
		else
		{
			_buttonsReleased[i] = true;
			_buttonsReleasedStep[i] = _targetStep;
		}

		_buttonsDown[i] = down;
	}

	/// <summary>
	/// Applies a wheel movement. Movements within one frame (and, for fixed steps, between fixed steps) add up.
	/// </summary>
	public void OnWheel(float delta)
	{
		_wheelDelta += delta;

		if (_fixedWheelStep != _targetStep)
		{
			_fixedWheelStep = _targetStep;
			_fixedWheelDelta = 0;
		}

		_fixedWheelDelta += delta;
	}

	/// <summary>
	/// Moves the mouse to <paramref name="position"/>. The movement is added to <see cref="MouseDelta"/>.
	/// </summary>
	public void OnMouseMove(Vector2 position)
	{
		var delta = position - MousePosition;
		MousePosition = position;

		if (delta == Vector2.Zero) return;

		_mouseDelta += delta;

		if (_fixedMouseStep != _targetStep)
		{
			_fixedMouseStep = _targetStep;
			_fixedMouseDelta = Vector2.Zero;
		}

		_fixedMouseDelta += delta;
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
	public bool Pressed(Key key) => _edge(_keysPressed, _keysPressedStep, (int)key);
	public bool Released(Key key) => _edge(_keysReleased, _keysReleasedStep, (int)key);

	/// <summary>
	/// True when <paramref name="key"/> was pressed (this frame, or since the previous fixed step when read from a fixed
	/// step) with at least one of <paramref name="modifiers"/> held.
	/// </summary>
	public bool Pressed(Key key, ModifierKeys modifiers) => Pressed(key) && (_modifiers(_pressedModifiers, _fixedPressedModifiers, (int)key) & modifiers) != ModifierKeys.None;

	/// <summary>
	/// True when <paramref name="key"/> was released (this frame, or since the previous fixed step when read from a fixed
	/// step) with at least one of <paramref name="modifiers"/> held.
	/// </summary>
	public bool Released(Key key, ModifierKeys modifiers) => Released(key) && (_modifiers(_releasedModifiers, _fixedReleasedModifiers, (int)key) & modifiers) != ModifierKeys.None;

	public bool Down(MouseButton button) => _get(_buttonsDown, (int)button);
	public bool Pressed(MouseButton button) => _edge(_buttonsPressed, _buttonsPressedStep, (int)button);
	public bool Released(MouseButton button) => _edge(_buttonsReleased, _buttonsReleasedStep, (int)button);

	private void _mark(long[] steps, ModifierKeys[] modifiers, int i, ModifierKeys value)
	{
		if (steps[i] != _targetStep)
		{
			steps[i] = _targetStep;
			modifiers[i] = value;
		}
		else
		{
			modifiers[i] |= value;
		}
	}

	private bool _readFixed(out long step)
	{
		if (_loop is { Stage: GameLoopStage.FixedUpdate } loop)
		{
			step = loop.FixedStepCount;
			return true;
		}

		step = 0;
		return false;
	}

	private bool _edge(bool[] frame, long[] steps, int i)
	{
		if ((uint)i >= (uint)frame.Length) return false;
		return _readFixed(out var step) ? steps[i] == step : frame[i];
	}

	private ModifierKeys _modifiers(ModifierKeys[] frame, ModifierKeys[] fixedStep, int i) => _readFixed(out _) ? fixedStep[i] : frame[i];

	private static bool _get(bool[] set, int i) => (uint)i < (uint)set.Length && set[i];
}
