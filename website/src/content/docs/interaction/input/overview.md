---
title: Input overview
description: How Ion captures keyboard, mouse, gamepad and touch input once per frame, and how edges and deltas behave in Update and FixedUpdate.
sidebar:
  order: 1
---

You read input by injecting `IInputState` into a system and polling it. Ion captures every device once per frame, at
the start of the `First` stage, and the state is read-only for the rest of the frame, so every system in a frame sees
the same input.

```csharp title="PlayerSystem.cs"
using System.Numerics;
using Ion;

public sealed class PlayerSystem(IInputState input)
{
	public Vector2 Position;

	[Update]
	public void Move(GameTime dt)
	{
		var direction = Vector2.Zero;
		if (input.Down(Key.A) || input.Down(Key.Left)) direction.X -= 1;
		if (input.Down(Key.D) || input.Down(Key.Right)) direction.X += 1;
		direction += input.Gamepad(0).LeftStick;

		Position += direction * 300f * dt.Delta;

		if (input.Pressed(Key.Space) || input.Gamepad(0).Pressed(GamepadButton.A)) Jump();
	}

	private void Jump() { }
}
```

Input is part of the engine core: `builder.AddIon()` (or any module that pulls it in, such as `AddUi` or
`AddEcsRendering`) registers it, and `game.UseIon()` adds the input system. There is nothing to add for input alone.

## The state model

`IInputState` answers three kinds of questions:

| Kind | Members | Meaning |
|---|---|---|
| Level | `Down(key)`, `Up(key)`, `Down(button)`, `Up(button)`, `MousePosition`, `Modifiers`, gamepad `Down`, `Axis`, `LeftStick`, `RightStick` | The state right now. Same answer in every stage. |
| Edge | `Pressed(...)`, `Released(...)` for keys, mouse buttons and gamepad buttons | Something changed. True for exactly one frame (or one fixed step, see below). |
| Delta | `MouseDelta`, `WheelDelta`, `Text` | What accumulated since the last frame (or fixed step). |

Plus the per-frame touch list, `Touches`, covered in [Touch](/Ion/interaction/input/touch/).

Every event of a frame counts. A key pressed and released within one frame reports both `Pressed` and `Released` that
frame and is not `Down` afterwards, so a quick tap is never lost. Key auto-repeat marks a key as held but never counts
as a new `Pressed`.

## Where the state comes from

Both input backends keep their state in one shared, backend-independent `InputTracker` (in `Ion.Core.Abstractions`),
which uses fixed-size storage and allocates nothing per frame: `ulong` bitsets indexed by `Key` for held, pressed and
released keys, a 32-bit mask for mouse buttons, mouse position and delta, the wheel, up to 256 characters of text per
frame, eight gamepad slots and ten touches.

| Backend | Registered by | Implementation | Feeds the tracker from |
|---|---|---|---|
| Windowed | `AddIon` (not headless) | `SilkInputState` | Silk.NET keyboard, mouse and gamepads (GLFW or SDL), SDL finger events |
| Headless | `AddIon` with `--headless` | `NullInputState` | A script: `Press`, `Tap`, `Click`, `Type`, `SetAxis`, `TouchDown`... |

Your systems depend only on `IInputState`, so the same code runs windowed, headless and in tests. Resolve
`NullInputState` (or use `IonTestHost.Input`) to script input in tests.

### The Input stage

The input system runs in `First` at order `StageOrder.Input` (-940), right after the window pumps its events
(`StageOrder.Window`, -950). The Silk.NET callbacks fire inside that pump and only queue `InputEvent` values; the input
step then begins the tracker's frame (clearing edges, deltas and text) and applies the queued events in order. Recorded
playback and scripted input are applied at the same point. Everything after `-940` in the frame, engine and game steps
alike, reads a complete, stable state.

```
First -950  window pumps OS events      (callbacks queue InputEvents)
First -940  input: BeginFrame, apply queued, playback and scripted events
First   0+  your First steps            (input is ready)
FixedUpdate / Update / Render / Last     (same state; edges per the rules below)
```

See [Stage order](/Ion/reference/stage-order/) for every engine step.

## Polling, not callbacks

Ion's input API is polled. There are no `KeyPressed` events on the event bus; ask `Pressed(Key.X)` in the step that
cares. This keeps input deterministic and lets the fixed-step view below work. Window-level events do exist as ordinary
[events](/Ion/concepts/events/): `WindowFocusLostEvent` and `WindowFocusGainedEvent`.

The raw event stream is still reachable when you need it: `IInputEventSink` is the interface the tracker, the recorder
and the player share, and `InputEvent` is one event as a value (`InputEvent.ForKey`, `ForMouseButton`, `ForMouseMove`,
`ForWheel`, `ForText`, `ForGamepadButton`, `ForGamepadAxis`, `ForGamepadConnection`, `ForTouch`, `ForReleaseAll`).
That is how recording, playback and remote input work; see
[Recording and playback](/Ion/interaction/input/recording-and-playback/).

## Edges in Update and FixedUpdate

Edges, deltas and text depend on the stage that reads them:

- From **First, Update, Render and Last** they describe the current frame: an edge is true for exactly one frame, a
  delta is the movement since the previous frame.
- From **FixedUpdate** they describe everything since the previous fixed step: an edge is true in exactly one fixed
  step (the first one that runs on or after the frame it happened in), and a delta is everything accumulated since.

This holds whatever the ratio of `Ion:MaxFPS` to `Ion:FixedUpdateRate`. When the frame rate is higher than the fixed
rate, some frames run no fixed step; an edge on such a frame is carried to the next fixed step. When a slow frame runs
several fixed steps, only the first sees the edge. So a click is seen exactly once by a FixedUpdate system and exactly
once by an Update system:

```csharp
public sealed class ShipSystem(IInputState input)
{
	[FixedUpdate]
	public void Physics(GameTime dt)
	{
		// Seen by exactly one fixed step, even at 240 FPS with a 60 Hz fixed rate.
		if (input.Pressed(Key.Space)) Fire();

		// Level queries are the same everywhere.
		if (input.Down(Key.W)) Thrust(dt.Delta);
	}

	[Update]
	public void Hud(GameTime dt)
	{
		// Also seen exactly once, in the frame it happened.
		if (input.Pressed(Key.Space)) FlashCrosshair();
	}

	private void Fire() { }
	private void Thrust(float dt) { }
	private void FlashCrosshair() { }
}
```

The stage is read from `ILoopContext`, which the game loop publishes. Outside a running loop (a bare `InputTracker` in
a unit test) every query uses the per-frame view.

:::caution[Touches have only a per-frame view]
`Touches` is the same list from FixedUpdate as from Update, so a touch that begins on a frame without a fixed step is
never seen as `Pressed` by a fixed step. Read touches from First, Update or Render. See
[Touch](/Ion/interaction/input/touch/).
:::

For more on the frame and fixed-step structure, see [Game loop](/Ion/concepts/game-loop/) and
[Time and determinism](/Ion/concepts/time-and-determinism/).

## Focus loss

When the window loses focus, key-up events go to another window and would never arrive. The windowed input system therefore
releases every held key and mouse button (without a `Released` edge) and cancels every touch when it sees
`WindowFocusLostEvent`. Gamepads are not affected: they do not depend on window focus. In tests you can simulate it with
`NullInputState.ReleaseAll()`.

## Function steps

For small games or prototypes you can read input in a function step instead of a system class:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon();

using var game = builder.Build();
game.UseIon();
game.Update((GameTime dt, IInputState input) =>
{
	if (input.Pressed(Key.Escape)) Console.WriteLine("Escape pressed");
});
game.Run();
```

## Configuration

`InputConfig` is bound from `Ion:Input`:

| Key | Default | Meaning |
|---|---|---|
| `GamepadDeadZone` | `0.15` | Stick and trigger values below this magnitude read as zero; values above are rescaled to the full range. See [Gamepad](/Ion/interaction/input/gamepad/). |

Window options that affect input (`Ion:Window:Platform` for GLFW or SDL, `Ion:Window:ShowCursor`) are described in
[Windowing](/Ion/rendering/windowing/).

## Action mapping

Ion has no action-mapping layer yet: there is no `InputAction`, binding table or rebinding UI. Map devices to actions
in your own code. A small struct keeps it in one place and makes the Update code read like the design:

```csharp
public readonly struct PlayerActions(IInputState input)
{
	public bool Jump => input.Pressed(Key.Space) || input.Gamepad(0).Pressed(GamepadButton.A);
	public bool Pause => input.Pressed(Key.Escape) || input.Gamepad(0).Pressed(GamepadButton.Start);

	public float Horizontal
	{
		get
		{
			var x = input.Gamepad(0).LeftStick.X;
			if (input.Down(Key.A) || input.Down(Key.Left)) x -= 1;
			if (input.Down(Key.D) || input.Down(Key.Right)) x += 1;
			return Math.Clamp(x, -1f, 1f);
		}
	}
}

public sealed class JumperSystem(IInputState input)
{
	[Update]
	public void Update(GameTime dt)
	{
		var actions = new PlayerActions(input);
		if (actions.Jump) { /* ... */ }
	}
}
```

## In this section

- [Keyboard and mouse](/Ion/interaction/input/keyboard-and-mouse/): keys, modifiers, text, the cursor and the wheel.
- [Gamepad](/Ion/interaction/input/gamepad/): the SDL layout, sticks, triggers and dead zones.
- [Touch](/Ion/interaction/input/touch/): fingers on phones and tablets.
- [Recording and playback](/Ion/interaction/input/recording-and-playback/): record sessions, replay them as tests, and
  inject input from tools.

## See also

- [Stages](/Ion/concepts/stages/) and [Stage order](/Ion/reference/stage-order/).
- [UI focus navigation](/Ion/interaction/ui/focus-navigation/): how the UI module consumes keyboard and gamepad input.
- [Testing](/Ion/tooling/testing/): scripted input with `IonTestHost.Input`.
- Source: [Ion.Core.Abstractions/Input](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Core.Abstractions/Input/).
