using System.Numerics;

namespace Ion;

/// <summary>
/// A gamepad button, in the SDL game controller layout (the positions of an Xbox controller: <see cref="A"/> is the bottom
/// face button, <see cref="Y"/> the top one).
/// </summary>
public enum GamepadButton
{
	A,
	B,
	X,
	Y,
	Back,
	Guide,
	Start,
	LeftStick,
	RightStick,
	LeftShoulder,
	RightShoulder,
	DPadUp,
	DPadDown,
	DPadLeft,
	DPadRight,
	Misc1,
	Paddle1,
	Paddle2,
	Paddle3,
	Paddle4,
	Touchpad,
	LastButton
}

/// <summary>
/// A gamepad axis. Sticks range from -1 to 1 (<see cref="LeftY"/> and <see cref="RightY"/> are positive downwards, as in
/// SDL); triggers range from 0 to 1.
/// </summary>
public enum GamepadAxis
{
	LeftX,
	LeftY,
	RightX,
	RightY,
	LeftTrigger,
	RightTrigger,
	LastAxis
}

/// <summary>
/// The state of one gamepad slot. Obtained from <see cref="IInputState.Gamepad(int)"/> (any slot, connected or not) or
/// <see cref="IInputState.Gamepads"/> (connected ones only). A disconnected slot reports every button up and every axis at
/// zero.
/// </summary>
/// <remarks>
/// Edge queries (<see cref="Pressed"/>, <see cref="Released"/>) follow the same stage rules as the keyboard (see
/// <see cref="IInputState"/>): per frame everywhere but FixedUpdate, and since the previous fixed step in FixedUpdate.
/// Axis values have the dead zone (<see cref="InputConfig.GamepadDeadZone"/>) applied: radially to each stick, and to each
/// trigger on its own, rescaled so that the output still spans the full range.
/// </remarks>
public interface IGamepadState
{
	/// <summary>The slot index, from 0 to <see cref="InputTracker.MaxGamepads"/> minus one.</summary>
	int Index { get; }

	/// <summary>True while a gamepad is connected to this slot.</summary>
	bool IsConnected { get; }

	/// <summary>True while <paramref name="button"/> is held.</summary>
	bool Down(GamepadButton button);

	/// <summary>True while <paramref name="button"/> is not held.</summary>
	bool Up(GamepadButton button);

	/// <summary>True when <paramref name="button"/> went down this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Pressed(GamepadButton button);

	/// <summary>True when <paramref name="button"/> went up this frame (from FixedUpdate: since the previous fixed step).</summary>
	bool Released(GamepadButton button);

	/// <summary>The value of <paramref name="axis"/> with the dead zone applied.</summary>
	float Axis(GamepadAxis axis);

	/// <summary>The left stick (<see cref="GamepadAxis.LeftX"/>, <see cref="GamepadAxis.LeftY"/>) with the dead zone applied.</summary>
	Vector2 LeftStick { get; }

	/// <summary>The right stick (<see cref="GamepadAxis.RightX"/>, <see cref="GamepadAxis.RightY"/>) with the dead zone applied.</summary>
	Vector2 RightStick { get; }
}

/// <summary>
/// Input settings, bound from <c>Ion:Input</c>.
/// </summary>
public class InputConfig
{
	/// <summary>The default <see cref="GamepadDeadZone"/>.</summary>
	public const float DefaultGamepadDeadZone = 0.15f;

	/// <summary>
	/// Stick and trigger values whose magnitude is below this (0 to 1) read as zero; values above it are rescaled to span
	/// the full range. Defaults to <see cref="DefaultGamepadDeadZone"/>.
	/// </summary>
	public float GamepadDeadZone { get; set; } = DefaultGamepadDeadZone;
}
