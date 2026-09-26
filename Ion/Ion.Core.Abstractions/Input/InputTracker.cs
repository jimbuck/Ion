using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ion;

/// <summary>
/// The backend-independent input state behind every <see cref="IInputState"/>: keyboard, mouse buttons, wheel, mouse
/// motion, text input and gamepads, in fixed-size storage. A backend calls <see cref="BeginFrame"/> once at the start of
/// every frame (the First stage), then feeds the frame's device events in order through the <see cref="IInputEventSink"/>
/// methods; the state is then read-only for the rest of the frame.
/// </summary>
/// <remarks>
/// <para>
/// Keys are kept in <see cref="ulong"/> bitsets indexed by <see cref="Key"/> (held, pressed and released, for both views
/// below), mouse buttons in a 32-bit mask, and each gamepad's buttons in a 32-bit mask indexed by
/// <see cref="GamepadButton"/>. Nothing is allocated per frame.
/// </para>
/// <para>
/// Every event of a frame counts, so a key that is pressed and released within one frame reports both
/// <see cref="Pressed(Key)"/> and <see cref="Released(Key)"/> for that frame and is not <see cref="Down(Key)"/> afterwards.
/// Repeat events (a key held down) never count as a press but do mark the key as down.
/// </para>
/// <para>
/// Edges, deltas and text have two views. The per-frame view holds what happened since the last <see cref="BeginFrame"/>
/// and is what every stage but FixedUpdate reads. The fixed-step view holds what happened since the previous fixed step:
/// events are tagged with the fixed step that will see them (the next one to start, <see cref="ILoopContext.FixedStepCount"/>
/// plus one, as of <see cref="BeginFrame"/>), so they accumulate across frames that run no fixed step and are seen by
/// exactly one fixed step however many frames or steps there are. The view is chosen from <see cref="ILoopContext.Stage"/>;
/// without a loop context only the per-frame view is kept.
/// </para>
/// <para>
/// Modifiers: <see cref="Pressed(Key, ModifierKeys)"/> and <see cref="Released(Key, ModifierKeys)"/> test the modifiers
/// the backend reported with the key event itself (so <c>Pressed(Key.S, ModifierKeys.Control)</c> is Ctrl+S whichever
/// of the two keys went down first), and match when at least one of the requested flags was held;
/// <see cref="ModifierKeys.None"/> never matches, so use <see cref="Pressed(Key)"/> to ignore modifiers. If a key is
/// pressed several times in one frame the modifiers of every press are combined. <see cref="Modifiers"/> is the level
/// state instead, derived from which modifier keys are held now. The modifier keys are ordinary keys too
/// (<c>Down(Key.ShiftLeft)</c>).
/// </para>
/// <para>
/// Focus loss: <see cref="ReleaseAll"/> releases every held key and mouse button without a <c>Released</c> edge, because
/// the matching key up events go to another window. Gamepads are not affected (they do not depend on window focus).
/// </para>
/// <para>
/// Recording and playback: when <see cref="Recorder"/> is set every applied event is passed on to it, frame by frame.
/// When <see cref="Playback"/> is set and playing, device events are ignored and <see cref="BeginFrame"/> applies the
/// recorded events of the frame instead.
/// </para>
/// </remarks>
public sealed class InputTracker : IInputEventSink
{
	/// <summary>The number of gamepad slots.</summary>
	public const int MaxGamepads = 8;

	/// <summary>The number of characters of text input kept per frame (and per fixed step); more are dropped.</summary>
	public const int TextCapacity = 256;

	private const int KeyCount = (int)Key.LastKey + 1;
	private const int ButtonCount = (int)MouseButton.LastButton + 1;
	private const int GamepadButtonCount = (int)GamepadButton.LastButton;
	private const int GamepadAxisCount = (int)GamepadAxis.LastAxis;

	private readonly ILoopContext? _loop;

	// Level state, shared by both views.
	private KeyBits _keysDown;
	private uint _buttonsDown;

	// Per-frame edges and deltas.
	private KeyBits _keysPressed;
	private KeyBits _keysReleased;
	private readonly ModifierKeys[] _pressedModifiers = new ModifierKeys[KeyCount];
	private readonly ModifierKeys[] _releasedModifiers = new ModifierKeys[KeyCount];
	private uint _buttonsPressed;
	private uint _buttonsReleased;
	private float _wheelDelta;
	private Vector2 _mouseDelta;
	private readonly char[] _text = new char[TextCapacity];
	private int _textLength;

	// Fixed-step edges and deltas, all seen by the fixed step _fixedStep (zero: none yet).
	private long _fixedStep;
	private KeyBits _fixedKeysPressed;
	private KeyBits _fixedKeysReleased;
	private readonly ModifierKeys[] _fixedPressedModifiers = new ModifierKeys[KeyCount];
	private readonly ModifierKeys[] _fixedReleasedModifiers = new ModifierKeys[KeyCount];
	private uint _fixedButtonsPressed;
	private uint _fixedButtonsReleased;
	private float _fixedWheelDelta;
	private Vector2 _fixedMouseDelta;
	private readonly char[] _fixedText = new char[TextCapacity];
	private int _fixedTextLength;

	private readonly GamepadState[] _gamepads = new GamepadState[MaxGamepads];
	private readonly List<IGamepadState> _connected = new(MaxGamepads);
	private readonly GamepadState _invalidGamepad;

	private uint _frameCounter;
	private bool _replaying;

	/// <summary>
	/// Creates a tracker.
	/// </summary>
	/// <param name="loop">
	/// The game loop context used to give FixedUpdate systems their own view of edges and deltas, and to number frames for
	/// recording. Without it every query uses the per-frame view and frames are counted by <see cref="BeginFrame"/>.
	/// </param>
	/// <param name="config">The input settings (gamepad dead zone). Defaults apply when omitted.</param>
	public InputTracker(ILoopContext? loop = null, InputConfig? config = null)
	{
		_loop = loop;
		DeadZone = config?.GamepadDeadZone ?? InputConfig.DefaultGamepadDeadZone;
		for (var i = 0; i < MaxGamepads; i++) _gamepads[i] = new GamepadState(this, i);
		_invalidGamepad = new GamepadState(this, -1);
	}

	/// <summary>
	/// The gamepad dead zone, from 0 (none) to just below 1. See <see cref="InputConfig.GamepadDeadZone"/>.
	/// </summary>
	public float DeadZone
	{
		get;
		set => field = float.IsFinite(value) ? Math.Clamp(value, 0f, 0.99f) : InputConfig.DefaultGamepadDeadZone;
	}

	/// <summary>
	/// Receives every event this tracker applies (from the device or from <see cref="Playback"/>), frame by frame.
	/// </summary>
	public IInputRecorder? Recorder { get; set; }

	/// <summary>
	/// Replaces device input while it is playing: <see cref="BeginFrame"/> applies its events for the frame and device
	/// events are ignored until <see cref="IInputPlayback.IsPlaying"/> turns false.
	/// </summary>
	public IInputPlayback? Playback { get; set; }

	/// <summary>
	/// Injected input (see <see cref="ScriptedInput"/>): applied at <see cref="BeginFrame"/> after <see cref="Playback"/>,
	/// in addition to device events, and recorded like device events. Ignored while a playback supplies the input.
	/// </summary>
	public IInputScript? Script { get; set; }

	/// <summary>
	/// The number of the current input frame: <see cref="ILoopContext.Frame"/> when there is a loop context, otherwise the
	/// number of <see cref="BeginFrame"/> calls before the current one.
	/// </summary>
	public uint Frame { get; private set; }

	/// <summary>
	/// The mouse position after the events applied so far.
	/// </summary>
	public Vector2 MousePosition { get; private set; }

	/// <summary>
	/// The wheel movement of this frame, or since the previous fixed step when read from a fixed step.
	/// </summary>
	public float WheelDelta => _readFixed(out var visible) ? (visible ? _fixedWheelDelta : 0f) : _wheelDelta;

	/// <summary>
	/// The mouse movement of this frame, or since the previous fixed step when read from a fixed step.
	/// </summary>
	public Vector2 MouseDelta => _readFixed(out var visible) ? (visible ? _fixedMouseDelta : Vector2.Zero) : _mouseDelta;

	/// <summary>
	/// The text typed this frame, or since the previous fixed step when read from a fixed step (at most
	/// <see cref="TextCapacity"/> characters).
	/// </summary>
	public ReadOnlySpan<char> Text => _readFixed(out var visible)
		? (visible ? _fixedText.AsSpan(0, _fixedTextLength) : [])
		: _text.AsSpan(0, _textLength);

	/// <summary>
	/// The modifiers held now, derived from the held modifier keys.
	/// </summary>
	public ModifierKeys Modifiers
	{
		get
		{
			var modifiers = ModifierKeys.None;
			if (Down(Key.ShiftLeft) || Down(Key.ShiftRight)) modifiers |= ModifierKeys.Shift;
			if (Down(Key.ControlLeft) || Down(Key.ControlRight)) modifiers |= ModifierKeys.Control;
			if (Down(Key.AltLeft) || Down(Key.AltRight)) modifiers |= ModifierKeys.Alt;
			if (Down(Key.WinLeft) || Down(Key.WinRight)) modifiers |= ModifierKeys.Gui;
			return modifiers;
		}
	}

	/// <summary>
	/// The connected gamepads, by ascending slot index.
	/// </summary>
	public IReadOnlyList<IGamepadState> Gamepads => _connected;

	/// <summary>
	/// The gamepad in slot <paramref name="index"/>. Every slot (and any out-of-range index) returns a state; a
	/// disconnected one reports nothing held.
	/// </summary>
	public IGamepadState Gamepad(int index) => (uint)index < MaxGamepads ? _gamepads[index] : _invalidGamepad;

	/// <summary>
	/// Starts a new input frame: clears the per-frame edges, deltas and text, starts tagging new events for the next
	/// fixed step to start (clearing the fixed-step view once its step has run), and applies <see cref="Playback"/>. Held
	/// keys and buttons stay down.
	/// </summary>
	public void BeginFrame()
	{
		_keysPressed.Reset();
		_keysReleased.Reset();
		Array.Clear(_pressedModifiers);
		Array.Clear(_releasedModifiers);
		_buttonsPressed = _buttonsReleased = 0;
		_wheelDelta = 0;
		_mouseDelta = Vector2.Zero;
		_textLength = 0;

		var clearFixed = false;
		if (_loop is not null)
		{
			// Events applied from now on are seen by the next fixed step to start. When that is a new step, the previous
			// one has run (steps only move forward), so its view is done with.
			var target = _loop.FixedStepCount + 1;
			if (target != _fixedStep)
			{
				clearFixed = true;
				_fixedStep = target;
				_fixedKeysPressed.Reset();
				_fixedKeysReleased.Reset();
				Array.Clear(_fixedPressedModifiers);
				Array.Clear(_fixedReleasedModifiers);
				_fixedButtonsPressed = _fixedButtonsReleased = 0;
				_fixedWheelDelta = 0;
				_fixedMouseDelta = Vector2.Zero;
				_fixedTextLength = 0;
			}
		}

		for (var i = 0; i < MaxGamepads; i++) _gamepads[i].BeginFrame(clearFixed);

		Frame = _loop?.Frame ?? _frameCounter;
		_frameCounter++;

		Recorder?.BeginFrame(Frame);

		if (Playback is { } playback)
		{
			_replaying = true;
			try
			{
				playback.Play(Frame, this);
			}
			finally
			{
				_replaying = false;
			}
		}

		Script?.Apply(Frame, this);
	}

	/// <summary>
	/// Applies one key event. Events must be applied in the order they happened.
	/// </summary>
	public void OnKey(Key key, bool down, bool repeat, ModifierKeys modifiers)
	{
		if (_ignored) return;

		var i = (int)key;
		if ((uint)i >= KeyCount) return;

		if (down)
		{
			if (!repeat)
			{
				_keysPressed.Set(i);
				_pressedModifiers[i] |= modifiers;
				if (_trackFixed)
				{
					_fixedKeysPressed.Set(i);
					_fixedPressedModifiers[i] |= modifiers;
				}
			}

			_keysDown.Set(i);
		}
		else
		{
			_keysReleased.Set(i);
			_releasedModifiers[i] |= modifiers;
			if (_trackFixed)
			{
				_fixedKeysReleased.Set(i);
				_fixedReleasedModifiers[i] |= modifiers;
			}

			_keysDown.Unset(i);
		}

		Recorder?.OnKey(key, down, repeat, modifiers);
	}

	/// <summary>
	/// Applies one mouse button event. Events must be applied in the order they happened.
	/// </summary>
	public void OnMouseButton(MouseButton button, bool down)
	{
		if (_ignored) return;

		var i = (int)button;
		if ((uint)i >= ButtonCount) return;

		var bit = 1u << i;
		if (down)
		{
			_buttonsPressed |= bit;
			if (_trackFixed) _fixedButtonsPressed |= bit;
			_buttonsDown |= bit;
		}
		else
		{
			_buttonsReleased |= bit;
			if (_trackFixed) _fixedButtonsReleased |= bit;
			_buttonsDown &= ~bit;
		}

		Recorder?.OnMouseButton(button, down);
	}

	/// <summary>
	/// Applies a wheel movement. Movements within one frame (and, for fixed steps, between fixed steps) add up.
	/// </summary>
	public void OnWheel(float delta)
	{
		if (_ignored || delta == 0) return;

		_wheelDelta += delta;
		if (_trackFixed) _fixedWheelDelta += delta;

		Recorder?.OnWheel(delta);
	}

	/// <summary>
	/// Moves the mouse to <paramref name="position"/>. The movement is added to <see cref="MouseDelta"/>.
	/// </summary>
	public void OnMouseMove(Vector2 position)
	{
		if (_ignored) return;

		var delta = position - MousePosition;
		if (delta == Vector2.Zero) return;

		MousePosition = position;
		_mouseDelta += delta;
		if (_trackFixed) _fixedMouseDelta += delta;

		Recorder?.OnMouseMove(position);
	}

	/// <summary>
	/// Appends a character of text input to <see cref="Text"/>. Characters beyond <see cref="TextCapacity"/> in one frame
	/// are dropped.
	/// </summary>
	public void OnText(char character)
	{
		if (_ignored) return;

		if (_textLength < TextCapacity) _text[_textLength++] = character;
		if (_trackFixed && _fixedTextLength < TextCapacity) _fixedText[_fixedTextLength++] = character;

		Recorder?.OnText(character);
	}

	/// <summary>
	/// Appends every character of <paramref name="text"/>, as <see cref="OnText(char)"/>.
	/// </summary>
	public void OnText(ReadOnlySpan<char> text)
	{
		foreach (var c in text) OnText(c);
	}

	/// <summary>
	/// Connects or disconnects the gamepad in slot <paramref name="index"/>. Disconnecting releases its buttons (without
	/// edges) and zeroes its axes.
	/// </summary>
	public void OnGamepadConnected(int index, bool connected)
	{
		if (_ignored || (uint)index >= MaxGamepads) return;

		var pad = _gamepads[index];
		if (pad.IsConnected == connected) return;

		pad.SetConnected(connected);

		_connected.Clear();
		foreach (var p in _gamepads)
		{
			if (p.IsConnected) _connected.Add(p);
		}

		Recorder?.OnGamepadConnected(index, connected);
	}

	/// <summary>
	/// Applies a gamepad button event. A gamepad that was not connected is connected first.
	/// </summary>
	public void OnGamepadButton(int index, GamepadButton button, bool down)
	{
		if (_ignored || (uint)index >= MaxGamepads || (uint)button >= GamepadButtonCount) return;

		OnGamepadConnected(index, true);
		_gamepads[index].OnButton((int)button, down);

		Recorder?.OnGamepadButton(index, button, down);
	}

	/// <summary>
	/// Sets a gamepad axis (raw, clamped to its range: -1 to 1 for sticks, 0 to 1 for triggers). A gamepad that was not
	/// connected is connected first.
	/// </summary>
	public void OnGamepadAxis(int index, GamepadAxis axis, float value)
	{
		if (_ignored || (uint)index >= MaxGamepads || (uint)axis >= GamepadAxisCount) return;

		OnGamepadConnected(index, true);
		_gamepads[index].OnAxis((int)axis, value);

		Recorder?.OnGamepadAxis(index, axis, value);
	}

	/// <summary>
	/// Releases every held key and mouse button without reporting a <c>Released</c> edge, for example when the window
	/// loses focus and will not receive the matching key up events. Gamepads are not affected.
	/// </summary>
	public void ReleaseAll()
	{
		if (_ignored) return;

		_keysDown.Reset();
		_buttonsDown = 0;

		Recorder?.ReleaseAll();
	}

	/// <summary>True while <paramref name="key"/> is held.</summary>
	public bool Down(Key key) => (uint)key < KeyCount && _keysDown.Get((int)key);

	/// <summary>True when <paramref name="key"/> went down this frame (from FixedUpdate: since the previous fixed step).</summary>
	public bool Pressed(Key key) => (uint)key < KeyCount && (_readFixed(out var visible) ? visible && _fixedKeysPressed.Get((int)key) : _keysPressed.Get((int)key));

	/// <summary>True when <paramref name="key"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	public bool Released(Key key) => (uint)key < KeyCount && (_readFixed(out var visible) ? visible && _fixedKeysReleased.Get((int)key) : _keysReleased.Get((int)key));

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

	/// <summary>True while <paramref name="button"/> is held.</summary>
	public bool Down(MouseButton button) => (uint)button < ButtonCount && (_buttonsDown & (1u << (int)button)) != 0;

	/// <summary>True when <paramref name="button"/> went down this frame (from FixedUpdate: since the previous fixed step).</summary>
	public bool Pressed(MouseButton button) => _mask(_buttonsPressed, _fixedButtonsPressed, (int)button, ButtonCount);

	/// <summary>True when <paramref name="button"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	public bool Released(MouseButton button) => _mask(_buttonsReleased, _fixedButtonsReleased, (int)button, ButtonCount);

	// Device events are dropped while a playback supplies the input.
	private bool _ignored => !_replaying && Playback is { IsPlaying: true };

	private bool _trackFixed => _loop is not null;

	/// <summary>True when the caller reads the fixed-step view; <paramref name="visible"/> tells whether that view belongs to the running step.</summary>
	private bool _readFixed(out bool visible)
	{
		if (_loop is { Stage: GameLoopStage.FixedUpdate } loop)
		{
			visible = loop.FixedStepCount == _fixedStep;
			return true;
		}

		visible = false;
		return false;
	}

	private bool _mask(uint frame, uint fixedStep, int i, int count)
	{
		if ((uint)i >= (uint)count) return false;
		var bit = 1u << i;
		return _readFixed(out var visible) ? visible && (fixedStep & bit) != 0 : (frame & bit) != 0;
	}

	private ModifierKeys _modifiers(ModifierKeys[] frame, ModifierKeys[] fixedStep, int i) => _readFixed(out _) ? fixedStep[i] : frame[i];

	private sealed class GamepadState(InputTracker owner, int index) : IGamepadState
	{
		private uint _down;
		private uint _pressed;
		private uint _released;
		private uint _fixedPressed;
		private uint _fixedReleased;
		private readonly float[] _axes = new float[GamepadAxisCount];

		public int Index { get; } = index;

		public bool IsConnected { get; private set; }

		public Vector2 LeftStick => _stick(GamepadAxis.LeftX);

		public Vector2 RightStick => _stick(GamepadAxis.RightX);

		public void BeginFrame(bool clearFixed)
		{
			_pressed = _released = 0;
			if (clearFixed) _fixedPressed = _fixedReleased = 0;
		}

		public void SetConnected(bool connected)
		{
			IsConnected = connected;
			if (!connected)
			{
				_down = 0;
				Array.Clear(_axes);
			}
		}

		public void OnButton(int i, bool down)
		{
			var bit = 1u << i;
			if (down)
			{
				_pressed |= bit;
				if (owner._trackFixed) _fixedPressed |= bit;
				_down |= bit;
			}
			else
			{
				_released |= bit;
				if (owner._trackFixed) _fixedReleased |= bit;
				_down &= ~bit;
			}
		}

		public void OnAxis(int i, float value)
		{
			if (!float.IsFinite(value)) value = 0;
			_axes[i] = i >= (int)GamepadAxis.LeftTrigger ? Math.Clamp(value, 0f, 1f) : Math.Clamp(value, -1f, 1f);
		}

		public bool Down(GamepadButton button) => (uint)button < GamepadButtonCount && (_down & (1u << (int)button)) != 0;

		public bool Up(GamepadButton button) => !Down(button);

		public bool Pressed(GamepadButton button) => owner._mask(_pressed, _fixedPressed, (int)button, GamepadButtonCount);

		public bool Released(GamepadButton button) => owner._mask(_released, _fixedReleased, (int)button, GamepadButtonCount);

		public float Axis(GamepadAxis axis)
		{
			switch (axis)
			{
				case GamepadAxis.LeftX: return LeftStick.X;
				case GamepadAxis.LeftY: return LeftStick.Y;
				case GamepadAxis.RightX: return RightStick.X;
				case GamepadAxis.RightY: return RightStick.Y;
				case GamepadAxis.LeftTrigger:
				case GamepadAxis.RightTrigger:
					var value = _axes[(int)axis];
					var deadZone = owner.DeadZone;
					return value <= deadZone ? 0f : (value - deadZone) / (1f - deadZone);
				default:
					return 0f;
			}
		}

		// Radial dead zone: the stick reads zero inside the circle, and its magnitude is rescaled outside it.
		private Vector2 _stick(GamepadAxis x)
		{
			var raw = new Vector2(_axes[(int)x], _axes[(int)x + 1]);
			var length = raw.Length();
			var deadZone = owner.DeadZone;
			if (length <= deadZone) return Vector2.Zero;

			var scaled = Math.Min(1f, (length - deadZone) / (1f - deadZone));
			return raw / length * scaled;
		}
	}
}

/// <summary>
/// A 256-bit set of <see cref="Key"/> values: four <see cref="ulong"/> words.
/// </summary>
[InlineArray(Words)]
internal struct KeyBits
{
	public const int Words = 4;

	private ulong _word0;

	public readonly bool Get(int i) => (this[i >> 6] & (1UL << (i & 63))) != 0;

	public void Set(int i) => this[i >> 6] |= 1UL << (i & 63);

	public void Unset(int i) => this[i >> 6] &= ~(1UL << (i & 63));

	public void Reset() => ((Span<ulong>)this).Clear();
}
