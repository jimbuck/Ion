---
title: Gamepad
description: Read gamepad buttons, sticks and triggers in the SDL layout, handle several players and hot-plugging, and tune the dead zone.
sidebar:
  order: 3
---

Gamepads are part of `IInputState`. Ion reads up to eight of them, reports buttons in the SDL game controller layout
(the positions of an Xbox controller), and applies a dead zone to sticks and triggers before your code sees them.

```csharp
using Ion;

public sealed class PadPlayerSystem(IInputState input)
{
	[Update]
	public void Update(GameTime dt)
	{
		var pad = input.Gamepad(0);          // never null
		if (!pad.IsConnected) return;

		var move = pad.LeftStick;            // dead zone applied, each axis in [-1, 1]
		var aim = pad.RightStick;
		var throttle = pad.Axis(GamepadAxis.RightTrigger);   // [0, 1]

		if (pad.Pressed(GamepadButton.A)) Jump();
		if (pad.Down(GamepadButton.RightShoulder)) Fire();
	}

	private void Jump() { }
	private void Fire() { }
}
```

## Slots and players

| Member | Returns |
|---|---|
| `input.Gamepad(index)` | The gamepad in slot `index` (0 to 7). Never null: an empty slot, or an out-of-range index, reports `IsConnected` false, every button up and every axis at zero. |
| `input.Gamepads` | The connected gamepads only, by ascending slot index. |
| `IGamepadState.Index` | The slot. |
| `IGamepadState.IsConnected` | True while a gamepad is in this slot. |

Use `Gamepad(n)` when players own fixed slots, and `Gamepads` when any controller may drive the game:

```csharp
// Any connected gamepad moves the paddle (from the Companion sample).
var direction = 0f;
var pads = input.Gamepads;
for (var i = 0; i < pads.Count; i++)
{
	var pad = pads[i];
	direction += pad.LeftStick.X;
	if (pad.Down(GamepadButton.DPadLeft)) direction -= 1;
	if (pad.Down(GamepadButton.DPadRight)) direction += 1;
}

direction = Math.Clamp(direction, -1f, 1f);
```

`InputTracker.MaxGamepads` is 8. On the windowed backend, devices are mapped onto slots by their Silk.NET index: GLFW
exposes 16 joystick slots up front and SDL adds devices as they connect; devices beyond slot 7 are ignored.

### Connecting and disconnecting

There is no connection event on the event bus. Compare `IsConnected` with the previous frame when you want to react:

```csharp
private readonly bool[] _wasConnected = new bool[8];

[Update]
public void Hotplug(GameTime dt)
{
	for (var i = 0; i < _wasConnected.Length; i++)
	{
		var connected = input.Gamepad(i).IsConnected;
		if (connected && !_wasConnected[i]) OnJoined(i);
		if (!connected && _wasConnected[i]) OnLeft(i);   // for example, pause the game
		_wasConnected[i] = connected;
	}
}
```

Disconnecting releases the pad's buttons without `Released` edges and zeroes its axes. A button or axis event for a
slot that was not connected connects it first.

## Buttons

`GamepadButton` follows the SDL game controller layout: names describe positions, not labels. `A` is the bottom face
button (Cross on a PlayStation pad, B on a Nintendo pad), `Y` the top one.

| Button | Position |
|---|---|
| `A`, `B`, `X`, `Y` | Face buttons: bottom, right, left, top. |
| `Back`, `Start`, `Guide` | Select/View, Start/Menu, the home button. |
| `LeftShoulder`, `RightShoulder` | The bumpers. |
| `LeftStick`, `RightStick` | Clicking the sticks. |
| `DPadUp`, `DPadDown`, `DPadLeft`, `DPadRight` | The D-pad. |
| `Misc1`, `Paddle1` to `Paddle4`, `Touchpad` | Extra buttons of some controllers (see the note below). |

The same four queries as for keys apply: `Down`, `Up`, `Pressed` and `Released`. Edges follow the stage rules of the
[overview](/Ion/interaction/input/overview/#edges-in-update-and-fixedupdate): per frame everywhere but `FixedUpdate`,
and since the previous fixed step in `FixedUpdate`.

:::note[Extra buttons]
The Silk.NET backend maps the standard buttons (face, shoulders, sticks, D-pad, Back, Start, Guide). `Misc1`, the
paddles and `Touchpad` exist in the enum and can be scripted and recorded, but no device reports them yet.
:::

## Sticks and triggers

`GamepadAxis` has `LeftX`, `LeftY`, `RightX`, `RightY`, `LeftTrigger` and `RightTrigger`.

| Member | Range | Notes |
|---|---|---|
| `LeftStick`, `RightStick` | each axis -1 to 1 | `Vector2`, dead zone applied radially. |
| `Axis(GamepadAxis.LeftX)` etc. | -1 to 1 | The same values as the stick vectors, one axis at a time. |
| `Axis(GamepadAxis.LeftTrigger)`, `Axis(GamepadAxis.RightTrigger)` | 0 to 1 | Dead zone applied to each trigger on its own. |

:::caution[Y points down]
`LeftY` and `RightY` are positive downwards, as in SDL and as in window coordinates. Pushing the stick up gives a
negative Y. That matches screen-space movement (`position += stick * speed`); negate it for a Y-up world.
:::

Triggers are normalized to [0, 1] on both platforms: GLFW reports them in [-1, 1] with -1 at rest and the backend
converts them.

## Dead zone

Worn sticks rarely rest at exactly zero. Ion applies one dead zone, `Ion:Input:GamepadDeadZone` (default `0.15`):

- **Sticks** use a radial dead zone: inside the circle of that radius the stick reads zero; outside it, the magnitude is
  rescaled so it still reaches 1 at full deflection, and the direction is kept.
- **Triggers** read zero below the dead zone and are rescaled above it.

```json title="appsettings.json"
{
  "Ion": {
    "Input": { "GamepadDeadZone": 0.2 }
  }
}
```

Values are clamped to [0, 0.99]. Silk.NET's own dead zone is turned off so the value is applied exactly once. If you
need a different response curve (for example squaring the magnitude for finer aiming), apply it to the stick vector in
your code:

```csharp
var stick = pad.RightStick;
var length = stick.Length();
var aim = length > 0 ? stick / length * (length * length) : Vector2.Zero;
```

## Platforms

| Platform | Source |
|---|---|
| Windows, macOS, Linux (default GLFW) | GLFW gamepads through Silk.NET. |
| SDL (`Ion:Window:Platform=Sdl`, Android, iOS, the R36S) | SDL game controllers through Silk.NET. |
| Headless | `NullInputState` scripting. |
| Phones as controllers | Virtual gamepads injected with `ScriptedInput` (see below). |

The R36S handheld's built-in controls reach the game through SDL as a gamepad, which is why the UI module
auto-focuses a widget for gamepad-only devices; see [Focus navigation](/Ion/interaction/ui/focus-navigation/) and
[R36S](/Ion/platforms/r36s/). Gamepad input on the device itself has not been verified yet.

## Virtual gamepads

Anything can act as a gamepad by injecting events through `ScriptedInput`. The [Companion](/Ion/examples/companion/)
sample turns phones connected over a WebSocket into gamepads 1 to 3, so the game only reads ordinary gamepad input:

```csharp
// On connect:
script.Enqueue(InputEvent.ForGamepadConnection(pad, true));
// On each message {"x": 0.5}:
script.Enqueue(InputEvent.ForGamepadAxis(pad, GamepadAxis.LeftX, x));
// On disconnect:
script.Enqueue(InputEvent.ForGamepadAxis(pad, GamepadAxis.LeftX, 0));
script.Enqueue(InputEvent.ForGamepadConnection(pad, false));
```

Register it with `builder.AddScriptedInput()` and inject `ScriptedInput`. Injected axis values are raw: the dead zone
applies to them like to a device. See [Recording and playback](/Ion/interaction/input/recording-and-playback/#scripted-input).

## Testing gamepad code

`NullInputState` (and `IonTestHost.Input`) scripts gamepads:

```csharp title="PadTests.cs"
using System.Numerics;
using Ion.Testing;
using Xunit;

public class PadTests
{
	[Fact]
	public void TheStickMovesThePlayer()
	{
		// PlayerSystem is the example from the Input overview page.
		using var host = new IonTestHost().WithSystem<PlayerSystem>();
		host.Input.ConnectGamepad(0);
		host.Step();

		var start = host.Get<PlayerSystem>().Position;
		host.Input.SetLeftStick(0, new Vector2(1, 0));
		host.Step(10);

		Assert.True(host.Get<PlayerSystem>().Position.X > start.X);
	}
}
```

| Method | Queues |
|---|---|
| `ConnectGamepad(index = 0)`, `DisconnectGamepad(index = 0)` | A connection change. |
| `Press(pad, button)`, `Release(pad, button)`, `Tap(pad, button)` | Button events (`Tap` is a press and release in one frame). |
| `SetAxis(pad, axis, value)` | A raw axis value. |
| `SetLeftStick(pad, vector)`, `SetRightStick(pad, vector)` | Both axes of a stick. |

The [Menu](/Ion/examples/menu/) sample's tests drive every screen with the D-pad and A/B this way.

## Not available yet

- Rumble and haptics, LEDs, gyroscopes and the touchpad surface.
- Controller names, types (Xbox, PlayStation, Nintendo) and button glyph lookup.
- Remapping or custom SDL mapping strings from configuration.

## See also

- [Input overview](/Ion/interaction/input/overview/)
- [Focus navigation](/Ion/interaction/ui/focus-navigation/): gamepad-driven menus.
- [Recording and playback](/Ion/interaction/input/recording-and-playback/)
- [Companion example](/Ion/examples/companion/)
