using System.Numerics;

namespace Ion.Extensions.Graphics.Tests;

public class NullInputScriptingV2Tests
{
	private readonly NullInputState _input = new();

	[Fact, Trait(CATEGORY, UNIT)]
	public void TypedTextIsTheNextFramesText()
	{
		_input.Type("Hi ");
		_input.Type("there");
		Assert.True(_input.Text.IsEmpty);

		_input.Step();
		Assert.Equal("Hi there", _input.Text.ToString());

		_input.Step();
		Assert.True(_input.Text.IsEmpty);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScriptedGamepadButtonsAndSticks()
	{
		_input.ConnectGamepad(2);
		_input.Step();
		Assert.True(_input.Gamepad(2).IsConnected);
		Assert.Equal([2], _input.Gamepads.Select(p => p.Index));

		_input.Press(2, GamepadButton.A);
		_input.SetLeftStick(2, new Vector2(1, 0));
		_input.SetAxis(2, GamepadAxis.RightTrigger, 1f);
		_input.Step();
		var pad = _input.Gamepad(2);
		Assert.True(pad.Pressed(GamepadButton.A));
		Assert.True(pad.Down(GamepadButton.A));
		Assert.Equal(1f, pad.LeftStick.X, 5);
		Assert.Equal(1f, pad.Axis(GamepadAxis.RightTrigger), 5);

		_input.Step();
		Assert.False(pad.Pressed(GamepadButton.A));
		Assert.True(pad.Down(GamepadButton.A));
		Assert.Equal(1f, pad.LeftStick.X, 5);

		_input.Tap(2, GamepadButton.B);
		_input.Release(2, GamepadButton.A);
		_input.Step();
		Assert.True(pad.Pressed(GamepadButton.B));
		Assert.True(pad.Released(GamepadButton.B));
		Assert.True(pad.Released(GamepadButton.A));
		Assert.False(pad.Down(GamepadButton.A));

		_input.DisconnectGamepad(2);
		_input.Step();
		Assert.False(pad.IsConnected);
		Assert.Empty(_input.Gamepads);
		Assert.Equal(Vector2.Zero, pad.LeftStick);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScriptingAButtonConnectsTheGamepad()
	{
		_input.Press(0, GamepadButton.Start);
		_input.Step();
		Assert.True(_input.Gamepad(0).IsConnected);
		Assert.True(_input.Gamepad(0).Pressed(GamepadButton.Start));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void RepeatMarksDownWithoutAPress()
	{
		_input.Repeat(Key.Right);
		_input.Step();
		Assert.True(_input.Down(Key.Right));
		Assert.False(_input.Pressed(Key.Right));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ModifiersFollowHeldModifierKeys()
	{
		_input.Press(Key.ControlLeft);
		_input.Press(Key.S, ModifierKeys.Control);
		_input.Step();
		Assert.Equal(ModifierKeys.Control, _input.Modifiers);
		Assert.True(_input.Pressed(Key.S, ModifierKeys.Control));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ApplicationInputStateIsBuiltOnTheSharedTracker()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Configuration["Ion:Input:GamepadDeadZone"] = "0.3";
		builder.Services.AddNullGraphics(builder.Configuration);
		using var app = builder.Build();

		var input = app.Services.GetRequiredService<NullInputState>();
		var tracker = app.Services.GetRequiredService<InputTracker>();
		Assert.Same(tracker, input.Tracker);
		Assert.Same(input, app.Services.GetRequiredService<IInputState>());
		Assert.Equal(0.3f, tracker.DeadZone, 5);
	}
}
