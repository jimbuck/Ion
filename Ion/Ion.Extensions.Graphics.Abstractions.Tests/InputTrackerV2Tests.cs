using System.Numerics;

namespace Ion.Extensions.Graphics.Abstractions.Tests;

public class InputTrackerV2Tests
{
	private sealed class Loop : ILoopContext
	{
		public GameLoopStage Stage { get; set; }
		public uint Frame { get; set; }
		public long FixedStepCount { get; set; }
	}

	private readonly InputTracker _input = new();

	private static IEnumerable<Key> AllKeys() => Enumerable.Range(0, (int)Key.LastKey + 1).Select(i => (Key)i);

	public static TheoryData<int> KeyValues()
	{
		var data = new TheoryData<int>();
		foreach (var key in AllKeys()) data.Add((int)key);
		return data;
	}

	[Theory]
	[MemberData(nameof(KeyValues))]
	public void EveryKeyHasItsOwnBit(int value)
	{
		var key = (Key)value;

		_input.BeginFrame();
		_input.OnKey(key, down: true, repeat: false, ModifierKeys.None);
		foreach (var other in AllKeys())
		{
			Assert.Equal(other == key, _input.Down(other));
			Assert.Equal(other == key, _input.Pressed(other));
			Assert.False(_input.Released(other));
		}

		_input.BeginFrame();
		Assert.True(_input.Down(key));
		Assert.False(_input.Pressed(key));

		_input.BeginFrame();
		_input.OnKey(key, down: false, repeat: false, ModifierKeys.None);
		foreach (var other in AllKeys())
		{
			Assert.False(_input.Down(other));
			Assert.False(_input.Pressed(other));
			Assert.Equal(other == key, _input.Released(other));
		}
	}

	[Fact]
	public void AllKeysHeldAtOnceAreIndependent()
	{
		_input.BeginFrame();
		foreach (var key in AllKeys()) _input.OnKey(key, down: true, repeat: false, ModifierKeys.None);
		Assert.All(AllKeys(), key => Assert.True(_input.Down(key)));

		_input.BeginFrame();
		foreach (var key in AllKeys().Where(k => (int)k % 2 == 0)) _input.OnKey(key, down: false, repeat: false, ModifierKeys.None);
		Assert.All(AllKeys(), key =>
		{
			var even = (int)key % 2 == 0;
			Assert.Equal(!even, _input.Down(key));
			Assert.Equal(even, _input.Released(key));
			Assert.False(_input.Pressed(key));
		});
	}

	[Fact]
	public void OutOfRangeKeysAndButtonsAreIgnored()
	{
		_input.BeginFrame();
		_input.OnKey((Key)500, down: true, repeat: false, ModifierKeys.None);
		_input.OnKey((Key)(-1), down: true, repeat: false, ModifierKeys.None);
		_input.OnMouseButton((MouseButton)40, down: true);

		Assert.False(_input.Down((Key)500));
		Assert.False(_input.Pressed((Key)(-1)));
		Assert.False(_input.Down((MouseButton)40));
		Assert.All(AllKeys(), key => Assert.False(_input.Down(key)));
	}

	[Fact]
	public void EveryMouseButtonHasItsOwnBit()
	{
		for (var b = MouseButton.Left; b < MouseButton.LastButton; b++)
		{
			_input.BeginFrame();
			_input.OnMouseButton(b, down: true);
			for (var other = MouseButton.Left; other <= MouseButton.LastButton; other++)
			{
				Assert.Equal(other == b, _input.Down(other));
				Assert.Equal(other == b, _input.Pressed(other));
			}

			_input.BeginFrame();
			_input.OnMouseButton(b, down: false);
			Assert.True(_input.Released(b));
			Assert.False(_input.Down(b));
		}
	}

	[Fact]
	public void ModifiersAreDerivedFromHeldKeys()
	{
		_input.BeginFrame();
		Assert.Equal(ModifierKeys.None, _input.Modifiers);

		_input.OnKey(Key.ShiftRight, true, false, ModifierKeys.None);
		_input.OnKey(Key.ControlLeft, true, false, ModifierKeys.Shift);
		Assert.Equal(ModifierKeys.Shift | ModifierKeys.Control, _input.Modifiers);

		_input.OnKey(Key.S, true, false, ModifierKeys.Shift | ModifierKeys.Control);
		Assert.True(_input.Pressed(Key.S, ModifierKeys.Control));
		Assert.True(_input.Pressed(Key.S, ModifierKeys.Alt | ModifierKeys.Shift));
		Assert.False(_input.Pressed(Key.S, ModifierKeys.Alt));
		Assert.False(_input.Pressed(Key.S, ModifierKeys.None));

		_input.ReleaseAll();
		Assert.Equal(ModifierKeys.None, _input.Modifiers);
	}

	[Fact]
	public void ReleaseAllClearsHeldKeysAndButtonsWithoutEdges()
	{
		_input.BeginFrame();
		_input.OnKey(Key.W, true, false, ModifierKeys.None);
		_input.OnMouseButton(MouseButton.Right, true);

		_input.BeginFrame();
		_input.ReleaseAll();

		Assert.False(_input.Down(Key.W));
		Assert.False(_input.Down(MouseButton.Right));
		Assert.False(_input.Released(Key.W));
		Assert.False(_input.Released(MouseButton.Right));
	}

	[Fact]
	public void TextAccumulatesWithinAFrameAndClearsOnTheNext()
	{
		_input.BeginFrame();
		Assert.True(_input.Text.IsEmpty);

		_input.OnText('h');
		_input.OnText("ello");
		_input.OnText('!');
		Assert.Equal("hello!", _input.Text.ToString());

		_input.BeginFrame();
		Assert.True(_input.Text.IsEmpty);

		_input.OnText("é中");
		Assert.Equal("é中", _input.Text.ToString());
	}

	[Fact]
	public void TextBeyondCapacityIsDropped()
	{
		_input.BeginFrame();
		_input.OnText(new string('x', InputTracker.TextCapacity + 10));
		Assert.Equal(InputTracker.TextCapacity, _input.Text.Length);
	}

	[Fact]
	public void FixedStepSeesTextTypedOnFramesWithoutAFixedStep()
	{
		var loop = new Loop();
		var input = new InputTracker(loop);

		loop.Stage = GameLoopStage.First;
		input.BeginFrame();
		input.OnText("ab");
		loop.Stage = GameLoopStage.Update;
		Assert.Equal("ab", input.Text.ToString());

		loop.Frame++;
		loop.Stage = GameLoopStage.First;
		input.BeginFrame();
		input.OnText("c");
		loop.Stage = GameLoopStage.Update;
		Assert.Equal("c", input.Text.ToString());

		loop.FixedStepCount++;
		loop.Stage = GameLoopStage.FixedUpdate;
		Assert.Equal("abc", input.Text.ToString());
		loop.FixedStepCount++;
		Assert.True(input.Text.IsEmpty);
	}

	[Fact]
	public void GamepadSlotsStartDisconnected()
	{
		_input.BeginFrame();
		Assert.Empty(_input.Gamepads);
		for (var i = 0; i < InputTracker.MaxGamepads; i++)
		{
			var pad = _input.Gamepad(i);
			Assert.Equal(i, pad.Index);
			Assert.False(pad.IsConnected);
			Assert.False(pad.Down(GamepadButton.A));
			Assert.Equal(0f, pad.Axis(GamepadAxis.LeftX));
		}

		Assert.False(_input.Gamepad(-1).IsConnected);
		Assert.False(_input.Gamepad(InputTracker.MaxGamepads).IsConnected);
	}

	[Fact]
	public void GamepadButtonsHaveEdgesAndLevels()
	{
		_input.BeginFrame();
		_input.OnGamepadConnected(1, true);
		_input.OnGamepadButton(1, GamepadButton.Start, true);

		var pad = _input.Gamepad(1);
		Assert.Single(_input.Gamepads);
		Assert.Same(pad, _input.Gamepads[0]);
		for (var b = GamepadButton.A; b < GamepadButton.LastButton; b++)
		{
			Assert.Equal(b == GamepadButton.Start, pad.Down(b));
			Assert.Equal(b == GamepadButton.Start, pad.Pressed(b));
		}
		Assert.False(_input.Gamepad(0).Down(GamepadButton.Start));

		_input.BeginFrame();
		Assert.True(pad.Down(GamepadButton.Start));
		Assert.False(pad.Pressed(GamepadButton.Start));

		_input.OnGamepadButton(1, GamepadButton.Start, false);
		_input.OnGamepadButton(1, GamepadButton.A, true);
		_input.OnGamepadButton(1, GamepadButton.A, false);
		Assert.True(pad.Released(GamepadButton.Start));
		Assert.True(pad.Pressed(GamepadButton.A));
		Assert.True(pad.Released(GamepadButton.A));
		Assert.True(pad.Up(GamepadButton.A));
	}

	[Fact]
	public void GamepadDisconnectReleasesButtonsAndAxes()
	{
		_input.BeginFrame();
		_input.OnGamepadButton(0, GamepadButton.X, true);
		_input.OnGamepadAxis(0, GamepadAxis.RightTrigger, 1f);
		Assert.True(_input.Gamepad(0).IsConnected);

		_input.BeginFrame();
		_input.OnGamepadConnected(0, false);
		var pad = _input.Gamepad(0);
		Assert.False(pad.IsConnected);
		Assert.False(pad.Down(GamepadButton.X));
		Assert.Equal(0f, pad.Axis(GamepadAxis.RightTrigger));
		Assert.Empty(_input.Gamepads);
	}

	[Fact]
	public void GamepadAxesApplyTheDeadZone()
	{
		var input = new InputTracker(config: new InputConfig { GamepadDeadZone = 0.2f });
		input.BeginFrame();

		input.OnGamepadAxis(0, GamepadAxis.LeftX, 0.1f);
		input.OnGamepadAxis(0, GamepadAxis.LeftY, 0.1f);
		Assert.Equal(Vector2.Zero, input.Gamepad(0).LeftStick);

		input.OnGamepadAxis(0, GamepadAxis.LeftX, 1f);
		input.OnGamepadAxis(0, GamepadAxis.LeftY, 0f);
		Assert.Equal(1f, input.Gamepad(0).Axis(GamepadAxis.LeftX), 5);

		input.OnGamepadAxis(0, GamepadAxis.LeftX, -0.6f);
		Assert.Equal(-0.5f, input.Gamepad(0).Axis(GamepadAxis.LeftX), 5);

		input.OnGamepadAxis(0, GamepadAxis.RightTrigger, 0.15f);
		Assert.Equal(0f, input.Gamepad(0).Axis(GamepadAxis.RightTrigger));
		input.OnGamepadAxis(0, GamepadAxis.RightTrigger, 0.6f);
		Assert.Equal(0.5f, input.Gamepad(0).Axis(GamepadAxis.RightTrigger), 5);

		// Values are clamped to their range.
		input.OnGamepadAxis(0, GamepadAxis.LeftTrigger, -3f);
		Assert.Equal(0f, input.Gamepad(0).Axis(GamepadAxis.LeftTrigger));
		input.OnGamepadAxis(0, GamepadAxis.RightY, 7f);
		Assert.Equal(1f, input.Gamepad(0).RightStick.Y, 5);
	}

	[Fact]
	public void GamepadEdgesFollowTheFixedStepView()
	{
		var loop = new Loop();
		var input = new InputTracker(loop);

		loop.Stage = GameLoopStage.First;
		input.BeginFrame();
		input.OnGamepadButton(0, GamepadButton.B, true);
		loop.Stage = GameLoopStage.Update;

		loop.Stage = GameLoopStage.First;
		input.BeginFrame();
		loop.Stage = GameLoopStage.Update;
		Assert.False(input.Gamepad(0).Pressed(GamepadButton.B));

		loop.FixedStepCount++;
		loop.Stage = GameLoopStage.FixedUpdate;
		Assert.True(input.Gamepad(0).Pressed(GamepadButton.B));
		loop.FixedStepCount++;
		Assert.False(input.Gamepad(0).Pressed(GamepadButton.B));
		Assert.True(input.Gamepad(0).Down(GamepadButton.B));
	}

	[Fact]
	public void StepsAllocateNothing()
	{
		var loop = new Loop();
		var input = new InputTracker(loop);

		void Frame()
		{
			loop.Stage = GameLoopStage.First;
			input.BeginFrame();
			input.OnKey(Key.A, true, false, ModifierKeys.Shift);
			input.OnKey(Key.A, false, false, ModifierKeys.Shift);
			input.OnMouseMove(new Vector2(loop.Frame, 2));
			input.OnMouseButton(MouseButton.Left, true);
			input.OnWheel(1);
			input.OnText('x');
			input.OnGamepadButton(0, GamepadButton.A, (loop.Frame & 1) == 0);
			input.OnGamepadAxis(0, GamepadAxis.LeftX, 0.5f);
			loop.FixedStepCount++;
			loop.Stage = GameLoopStage.FixedUpdate;
			_ = input.Pressed(Key.A) && input.Text.Length > 0 && input.Gamepad(0).LeftStick.X > 0;
			loop.Stage = GameLoopStage.Update;
			_ = input.Pressed(Key.A, ModifierKeys.Shift) && input.Down(MouseButton.Left) && input.Gamepads.Count > 0;
			loop.Frame++;
		}

		for (var i = 0; i < 10; i++) Frame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) Frame();
		Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
	}
}
