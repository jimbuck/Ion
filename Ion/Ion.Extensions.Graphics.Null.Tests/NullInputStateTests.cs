namespace Ion.Extensions.Graphics.Tests;

public class NullInputStateTests
{
	private readonly NullInputState _input = new();

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScriptedKey_IsPressedThenDownThenReleasedAcrossFrames()
	{
		_input.Press(Key.Space);
		Assert.False(_input.Down(Key.Space)); // applied at the next frame, not immediately

		_input.Step();
		Assert.True(_input.Pressed(Key.Space));
		Assert.True(_input.Down(Key.Space));
		Assert.False(_input.Released(Key.Space));

		_input.Step();
		Assert.False(_input.Pressed(Key.Space));
		Assert.True(_input.Down(Key.Space));

		_input.Release(Key.Space);
		_input.Step();
		Assert.True(_input.Released(Key.Space));
		Assert.False(_input.Down(Key.Space));
		Assert.True(_input.Up(Key.Space));

		_input.Step();
		Assert.False(_input.Released(Key.Space));
		Assert.False(_input.Pressed(Key.Space));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScriptedTouches_AreAppliedAtTheNextFrame()
	{
		_input.TouchDown(0, new System.Numerics.Vector2(10, 10));
		Assert.True(_input.Touches.IsEmpty);

		_input.Step();
		var touch = Assert.Single(_input.Touches.ToArray());
		Assert.True(touch.Pressed);
		Assert.Equal(TouchPhase.Began, touch.Phase);

		_input.TouchMove(0, new System.Numerics.Vector2(20, 10));
		_input.Step();
		Assert.Equal(new System.Numerics.Vector2(10, 0), Assert.Single(_input.Touches.ToArray()).Delta);

		_input.TouchUp(0, new System.Numerics.Vector2(20, 10));
		_input.TouchTap(1, new System.Numerics.Vector2(5, 5));
		_input.Step();
		var touches = _input.Touches.ToArray();
		Assert.Equal(2, touches.Length);
		Assert.All(touches, t => Assert.True(t.Released));
		Assert.True(touches[1].Pressed);

		_input.Step();
		Assert.True(_input.Touches.IsEmpty);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Tap_PressesAndReleasesWithinOneFrame()
	{
		_input.Tap(Key.Enter);
		_input.Step();

		Assert.True(_input.Pressed(Key.Enter));
		Assert.True(_input.Released(Key.Enter));
		Assert.False(_input.Down(Key.Enter));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Modifiers_AreReportedWithThePress()
	{
		_input.Press(Key.S, ModifierKeys.Control);
		_input.Step();

		Assert.True(_input.Pressed(Key.S, ModifierKeys.Control));
		Assert.False(_input.Pressed(Key.S, ModifierKeys.Alt));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void Click_IsPressedAndReleasedForOneFrame()
	{
		_input.Click(MouseButton.Left);
		_input.Step();

		Assert.True(_input.Pressed(MouseButton.Left));
		Assert.True(_input.Released(MouseButton.Left));
		Assert.False(_input.Down(MouseButton.Left));

		_input.Step();
		Assert.False(_input.Pressed(MouseButton.Left));
		Assert.False(_input.Released(MouseButton.Left));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void HeldMouseButton_StaysDownUntilReleased()
	{
		_input.Press(MouseButton.Right);
		_input.Step();
		_input.Step();

		Assert.True(_input.Down(MouseButton.Right));
		Assert.False(_input.Pressed(MouseButton.Right));

		_input.Release(MouseButton.Right);
		_input.Step();

		Assert.True(_input.Released(MouseButton.Right));
		Assert.True(_input.Up(MouseButton.Right));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void MousePositionAndWheel_AreAppliedAtTheNextFrame()
	{
		_input.SetMousePosition(10, 20);
		_input.Scroll(1f);
		_input.Scroll(2f);
		Assert.Equal(Vector2.Zero, _input.MousePosition);

		_input.Step();
		Assert.Equal(new Vector2(10, 20), _input.MousePosition);
		Assert.Equal(3f, _input.WheelDelta);

		_input.Step();
		Assert.Equal(new Vector2(10, 20), _input.MousePosition);
		Assert.Equal(0f, _input.WheelDelta);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ReleaseAll_DropsHeldInputWithoutReleasedEdge()
	{
		_input.Press(Key.A);
		_input.Press(MouseButton.Left);
		_input.Step();

		_input.ReleaseAll();
		_input.Step();

		Assert.False(_input.Down(Key.A));
		Assert.False(_input.Down(MouseButton.Left));
		Assert.False(_input.Released(Key.A));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ScriptedInput_IsAppliedInTheFirstStageOfTheGameLoop()
	{
		var observed = new List<(bool Pressed, bool Down, bool Released)>();

		using var app = TestApp.CreateNullGraphics(use: app => app.UseUpdate((GameLoopDelegate next, IInputState input) => dt =>
		{
			observed.Add((input.Pressed(Key.Space), input.Down(Key.Space), input.Released(Key.Space)));
			next(dt);
		}));

		var input = app.Services.GetRequiredService<NullInputState>();
		Assert.Same(input, app.Services.GetRequiredService<IInputState>());

		var loop = app.Build();
		loop.Init(TestApp.FrameTime);

		input.Press(Key.Space);
		loop.Step(TestApp.FrameTime);
		loop.Step(TestApp.FrameTime);
		input.Release(Key.Space);
		loop.Step(TestApp.FrameTime);

		Assert.Equal([(true, true, false), (false, true, false), (false, false, true)], observed);
	}
}
