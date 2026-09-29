using System.Numerics;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

/// <summary>
/// Touch input in <see cref="InputTracker"/> (Input v2): phases, per-frame view, taps within one frame, capacity,
/// cancellation and the default <see cref="IInputState"/> members.
/// </summary>
public class InputTrackerTouchTests
{
	private readonly InputTracker _input = new();

	[Fact]
	public void TouchGoesThroughBeganStationaryMovedEndedThenDisappears()
	{
		_input.BeginFrame();
		_input.OnTouch(3, TouchPhase.Began, new Vector2(10, 20));
		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(new TouchPoint(3, new Vector2(10, 20), Vector2.Zero, new Vector2(10, 20), TouchPhase.Began, Pressed: true), touch);
		Assert.True(touch.IsDown);
		Assert.False(touch.Released);

		_input.BeginFrame();
		touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Stationary, touch.Phase);
		Assert.False(touch.Pressed);

		_input.BeginFrame();
		_input.OnTouch(3, TouchPhase.Moved, new Vector2(15, 20));
		_input.OnTouch(3, TouchPhase.Moved, new Vector2(15, 30));
		touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Moved, touch.Phase);
		Assert.Equal(new Vector2(15, 30), touch.Position);
		Assert.Equal(new Vector2(5, 10), touch.Delta);
		Assert.Equal(new Vector2(10, 20), touch.StartPosition);

		_input.BeginFrame();
		_input.OnTouch(3, TouchPhase.Ended, new Vector2(16, 30));
		touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Ended, touch.Phase);
		Assert.True(touch.Released);
		Assert.False(touch.IsDown);
		Assert.Equal(new Vector2(1, 0), touch.Delta);

		_input.BeginFrame();
		Assert.True(_input.Touches.IsEmpty);
	}

	[Fact]
	public void TapWithinOneFrameIsPressedAndReleased()
	{
		_input.BeginFrame();
		_input.OnTouch(0, TouchPhase.Began, new Vector2(5, 5));
		_input.OnTouch(0, TouchPhase.Ended, new Vector2(5, 5));

		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.True(touch.Pressed);
		Assert.True(touch.Released);
		Assert.Equal(TouchPhase.Ended, touch.Phase);

		_input.BeginFrame();
		Assert.True(_input.Touches.IsEmpty);
	}

	[Fact]
	public void BeganAndMovedInOneFrameStaysBegan()
	{
		_input.BeginFrame();
		_input.OnTouch(1, TouchPhase.Began, new Vector2(0, 0));
		_input.OnTouch(1, TouchPhase.Moved, new Vector2(4, 3));

		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Began, touch.Phase);
		Assert.True(touch.Pressed);
		Assert.Equal(new Vector2(4, 3), touch.Position);
		Assert.Equal(new Vector2(4, 3), touch.Delta);
		Assert.Equal(Vector2.Zero, touch.StartPosition);
	}

	[Fact]
	public void MultipleTouchesKeepTheOrderTheyBeganIn()
	{
		_input.BeginFrame();
		_input.OnTouch(7, TouchPhase.Began, new Vector2(1, 1));
		_input.OnTouch(2, TouchPhase.Began, new Vector2(2, 2));
		_input.OnTouch(5, TouchPhase.Began, new Vector2(3, 3));

		_input.BeginFrame();
		_input.OnTouch(2, TouchPhase.Ended, new Vector2(2, 2));
		Assert.Equal([7, 2, 5], _input.Touches.ToArray().Select(t => t.Id));

		_input.BeginFrame();
		Assert.Equal([7, 5], _input.Touches.ToArray().Select(t => t.Id));
		Assert.True(_input.TryGetTouch(5, out var five));
		Assert.Equal(new Vector2(3, 3), five.Position);
		Assert.False(_input.TryGetTouch(2, out _));
	}

	[Fact]
	public void MoveWithoutBeganStartsATouchAndEndWithoutBeganIsIgnored()
	{
		_input.BeginFrame();
		_input.OnTouch(4, TouchPhase.Ended, new Vector2(1, 1));
		Assert.True(_input.Touches.IsEmpty);

		_input.OnTouch(4, TouchPhase.Moved, new Vector2(8, 9));
		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Began, touch.Phase);
		Assert.True(touch.Pressed);
		Assert.Equal(new Vector2(8, 9), touch.StartPosition);
	}

	[Fact]
	public void IdCanBeReusedInTheFrameItEnded()
	{
		_input.BeginFrame();
		_input.OnTouch(0, TouchPhase.Began, new Vector2(1, 1));
		_input.BeginFrame();
		_input.OnTouch(0, TouchPhase.Ended, new Vector2(1, 1));
		_input.OnTouch(0, TouchPhase.Began, new Vector2(50, 50));

		var touches = _input.Touches.ToArray();
		Assert.Equal(2, touches.Length);
		Assert.True(touches[0].Released);
		Assert.True(touches[1].Pressed);

		// The one that is down wins.
		Assert.True(_input.TryGetTouch(0, out var current));
		Assert.Equal(new Vector2(50, 50), current.Position);

		_input.BeginFrame();
		Assert.Equal(new Vector2(50, 50), Assert.Single(_input.Touches.ToArray()).Position);
	}

	[Fact]
	public void TouchesBeyondCapacityAreDropped()
	{
		_input.BeginFrame();
		for (var i = 0; i < InputTracker.MaxTouches + 3; i++) _input.OnTouch(i, TouchPhase.Began, new Vector2(i, i));

		Assert.Equal(InputTracker.MaxTouches, _input.Touches.Length);
		Assert.False(_input.TryGetTouch(InputTracker.MaxTouches, out _));

		// A released touch makes room within the same frame.
		_input.OnTouch(0, TouchPhase.Ended, Vector2.Zero);
		_input.OnTouch(42, TouchPhase.Began, Vector2.One);
		Assert.Equal(InputTracker.MaxTouches, _input.Touches.Length);
		Assert.True(_input.TryGetTouch(42, out _));
	}

	[Fact]
	public void ReleaseAllCancelsTouches()
	{
		_input.BeginFrame();
		_input.OnTouch(1, TouchPhase.Began, new Vector2(1, 1));
		_input.BeginFrame();
		_input.ReleaseAll();

		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.Equal(TouchPhase.Canceled, touch.Phase);
		Assert.True(touch.Released);

		_input.BeginFrame();
		Assert.True(_input.Touches.IsEmpty);
	}

	[Fact]
	public void TouchesDoNotAllocatePerFrame()
	{
		// Warm up.
		for (var i = 0; i < 4; i++) Frame(i);

		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) Frame(i);
		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());

		void Frame(int i)
		{
			_input.BeginFrame();
			_input.OnTouch(i % 3, TouchPhase.Began, new Vector2(i, i));
			_input.OnTouch(i % 3, TouchPhase.Moved, new Vector2(i + 1, i));
			_input.BeginFrame();
			_input.OnTouch(i % 3, TouchPhase.Ended, new Vector2(i + 1, i));
			foreach (ref readonly var t in _input.Touches) _ = t.Position;
		}
	}

	[Fact]
	public void TouchesAreTheSameListFromFixedUpdate()
	{
		var loop = new Loop();
		var input = new InputTracker(loop);
		loop.Stage = GameLoopStage.First;
		input.BeginFrame();
		input.OnTouch(1, TouchPhase.Began, new Vector2(3, 4));

		loop.Stage = GameLoopStage.FixedUpdate;
		loop.FixedStepCount = 1;
		Assert.True(Assert.Single(input.Touches.ToArray()).Pressed);
	}

	[Fact]
	public void DefaultInputStateHasNoTouches()
	{
		IInputState state = new NoTouchInput();
		Assert.True(state.Touches.IsEmpty);
		Assert.False(state.TryGetTouch(0, out _));
	}

	[Fact]
	public void TrackedInputStateExposesTheTrackersTouches()
	{
		var tracker = new InputTracker();
		IInputState state = new Tracked(tracker);
		tracker.BeginFrame();
		tracker.OnTouch(9, TouchPhase.Began, new Vector2(2, 3));

		Assert.Equal(9, Assert.Single(state.Touches.ToArray()).Id);
		Assert.True(state.TryGetTouch(9, out var touch));
		Assert.Equal(new Vector2(2, 3), touch.Position);
	}

	[Fact]
	public void TouchEventsApplyThroughInputEvent()
	{
		var e = InputEvent.ForTouch(6, TouchPhase.Moved, new Vector2(1.5f, 2.5f));
		Assert.Equal(InputEventKind.Touch, e.Kind);

		var tracker = new InputTracker();
		tracker.BeginFrame();
		InputEvent.ForTouch(6, TouchPhase.Began, Vector2.Zero).ApplyTo(tracker);
		e.ApplyTo(tracker);
		Assert.Equal(new Vector2(1.5f, 2.5f), Assert.Single(tracker.Touches.ToArray()).Position);
	}

	[Fact]
	public void SinksWrittenBeforeTouchIgnoreIt()
	{
		IInputEventSink sink = new OldSink();
		sink.OnTouch(0, TouchPhase.Began, Vector2.Zero);
	}

	private sealed class Loop : ILoopContext
	{
		public GameLoopStage Stage { get; set; }
		public uint Frame { get; set; }
		public long FixedStepCount { get; set; }
	}

	private sealed class Tracked(InputTracker tracker) : TrackedInputState(tracker)
	{
		public override void SetMousePosition(Vector2 position) { }
	}

	private sealed class OldSink : IInputEventSink
	{
		public void OnKey(Key key, bool down, bool repeat, ModifierKeys modifiers) { }
		public void OnMouseButton(MouseButton button, bool down) { }
		public void OnMouseMove(Vector2 position) { }
		public void OnWheel(float delta) { }
		public void OnText(char character) { }
		public void OnGamepadConnected(int index, bool connected) { }
		public void OnGamepadButton(int index, GamepadButton button, bool down) { }
		public void OnGamepadAxis(int index, GamepadAxis axis, float value) { }
		public void ReleaseAll() { }
	}

	/// <summary>An input state written before touch input existed: it gets the default (empty) touch members.</summary>
	private sealed class NoTouchInput : IInputState
	{
		public Vector2 MousePosition => default;
		public float WheelDelta => 0;
		public Vector2 MouseDelta => default;
		public ReadOnlySpan<char> Text => [];
		public ModifierKeys Modifiers => ModifierKeys.None;
		public IReadOnlyList<IGamepadState> Gamepads => [];
		public IGamepadState Gamepad(int index) => new InputTracker().Gamepad(index);
		public bool Down(Key key) => false;
		public bool Down(MouseButton btn) => false;
		public bool Pressed(Key key) => false;
		public bool Pressed(Key key, ModifierKeys modifiers) => false;
		public bool Pressed(MouseButton btn) => false;
		public bool Released(Key key) => false;
		public bool Released(Key key, ModifierKeys modifiers) => false;
		public bool Released(MouseButton btn) => false;
		public bool Up(Key key) => true;
		public bool Up(MouseButton btn) => true;
		public void SetMousePosition(Vector2 position) { }
		public void SetMousePosition(int x, int y) { }
	}
}
