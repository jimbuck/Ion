---
title: Touch
description: Read fingers on phones, tablets and touch screens through IInputState.Touches, and test touch code headless.
sidebar:
  order: 4
---

On touch screens, `IInputState.Touches` lists the fingers of the current frame. Touch works on the SDL windowing
platform, which is the default on Android and iOS, and on desktop when you select SDL. Mouse-driven code and the UI
keep working with a finger, because SDL also turns touches into mouse events.

```csharp
using Ion;

public sealed class TapSystem(IInputState input)
{
	[Update]
	public void Update(GameTime dt)
	{
		foreach (var touch in input.Touches)
		{
			if (touch.Pressed) Console.WriteLine($"finger {touch.Id} down at {touch.Position}");
			if (touch.Released) Console.WriteLine($"finger {touch.Id} up at {touch.Position}");
		}
	}
}
```

## The touch list

`Touches` is a `ReadOnlySpan<TouchPoint>` holding, in the order they began, every finger that is down plus those that
were lifted or cancelled this frame. It is empty without a touch screen. Like `Text`, the span is only valid until the
next frame starts.

`TouchPoint` is a `readonly record struct`:

| Member | Meaning |
|---|---|
| `Id` | Stable from the frame the touch began to the frame it ended, unique among the fingers down at the same time. Ids are small (0 to 9 on the Silk.NET backend) and are reused after a touch ends. |
| `Position` | Window coordinates, the same space as `MousePosition`. |
| `Delta` | Movement this frame. |
| `StartPosition` | Where the touch began. |
| `Phase` | `Began`, `Moved`, `Stationary`, `Ended` or `Canceled`, as of this frame. |
| `Pressed` | True in the frame the touch began. |
| `Released` | True in the frame it ended or was cancelled (`Phase` is `Ended` or `Canceled`). |
| `IsDown` | True while the finger is down (not released). |

Find one finger by id with `input.TryGetTouch(id, out var touch)`, which returns false once the touch is gone.

### Phases

| `TouchPhase` | When |
|---|---|
| `Began` | The finger went down this frame (and may have moved since, it still reads `Began`). |
| `Moved` | Down and moved this frame. |
| `Stationary` | Down and did not move this frame. |
| `Ended` | Lifted this frame. Gone next frame. |
| `Canceled` | The system took the touch this frame (a system gesture, or the window lost focus). Gone next frame. |

Like keys, a touch that goes down and up within one frame is reported once, with both `Pressed` and `Released` true, so
a quick tap is never lost. A pointer-style consumer can treat `Pressed` as a press at `StartPosition` and `Released` as
the release at `Position`.

:::caution[Read touches outside FixedUpdate]
Touches have a per-frame view only. From `FixedUpdate` you get the same list as from `Update`, so a touch that begins on
a frame that runs no fixed step is never seen as `Pressed` by a fixed step. Read touches from `First`, `Update` or
`Render`, and hand the result to your fixed-step code if needed.
:::

## Example: drag and release

The [Breakout ECS](/Ion/examples/breakout-ecs/) sample's paddle follows the first finger on phones and tablets, and
lifting the finger launches a ball. The mouse path of the same system runs in `FixedUpdate`; the touch path is an
`Update` step for the reason above:

```csharp
/// Touch screens: the paddle follows the first finger, and lifting it launches a ball.
[Update]
public void Touch(GameTime dt)
{
	var touches = input.Touches;
	if (touches.IsEmpty) return;

	var touch = touches[0];
	ref var paddleTransform = ref _paddle.Get<Transform2D>();
	paddleTransform.Position = new Vector2(touch.Position.X, paddleTransform.Position.Y);

	if (touch.Released)
	{
		events.Emit(new LaunchBallCommand());
	}
}
```

## Example: a two-finger pinch

Track fingers by id across frames:

```csharp
public sealed class PinchSystem(IInputState input)
{
	public float Zoom { get; private set; } = 1f;

	[Update]
	public void Update(GameTime dt)
	{
		var touches = input.Touches;
		if (touches.Length < 2 || !touches[0].IsDown || !touches[1].IsDown) return;

		var a = touches[0];
		var b = touches[1];
		var now = Vector2.Distance(a.Position, b.Position);
		var before = Vector2.Distance(a.Position - a.Delta, b.Position - b.Delta);
		if (before > 1f) Zoom = Math.Clamp(Zoom * now / before, 0.5f, 4f);
	}
}
```

There are no built-in gestures (tap, swipe, pinch recognizers); build them from `Touches` as above.

## Platforms

| Platform | Touch source |
|---|---|
| Android, iOS | SDL finger events (the SDL platform is the default there). |
| Windows, macOS, Linux | SDL finger events when `Ion:Window:Platform` is `Sdl`. The default GLFW platform has no touch input. |
| Headless | Scripted with `NullInputState`. |

Silk.NET's input layer has no touch devices, so the windowing module hooks SDL directly with an event watch. SDL
reports finger positions normalized to the window; they are scaled to window pixels. SDL's 64-bit finger ids are mapped
to the lowest free small id. SDL can deliver finger events on another thread (the Java UI thread on Android), so they
are queued under a lock and applied at the input step with everything else.

At most 10 touches (`InputTracker.MaxTouches`) are tracked at once; a finger that goes down while 10 are down is
dropped along with its later events.

### Touch as the mouse

SDL synthesizes mouse events from touches by default, so:

- `MousePosition` follows the finger and `Pressed(MouseButton.Left)` fires on a tap.
- The [UI module](/Ion/interaction/ui/overview/) reads the mouse, so buttons, toggles, lists and sliders work with a
  finger with no extra code.
- Games written for the mouse keep working on a phone.

If you handle both, avoid reacting twice to the same tap (once through `Touches`, once through the mouse).

For building and running on phones, see [Mobile](/Ion/platforms/mobile/). The mobile run of Breakout sets the SDL
platform, full screen and a hidden cursor from its arguments:

```csharp
var builder = IonApplication.CreateBuilder(
[
	"--Ion:Window:Platform=Sdl",
	"--Ion:Window:Fullscreen=true",
	"--Ion:Window:ShowCursor=false",
]);
```

## Testing touch code

`NullInputState` scripts touches. Each call is applied at the start of the next frame:

| Method | Queues |
|---|---|
| `TouchDown(id, position)` | A finger going down. |
| `TouchMove(id, position)` | The finger moving. |
| `TouchUp(id, position)` | The finger lifting. |
| `TouchTap(id, position)` | Down and up within the next frame (one touch, both `Pressed` and `Released`). |

```csharp title="PinchTests.cs"
using System.Numerics;
using Ion.Testing;
using Xunit;

public class PinchTests
{
	[Fact]
	public void SpreadingTwoFingersZoomsIn()
	{
		using var host = new IonTestHost().WithSystem<PinchSystem>();
		host.Input.TouchDown(0, new Vector2(100, 300));
		host.Input.TouchDown(1, new Vector2(200, 300));
		host.Step();

		host.Input.TouchMove(0, new Vector2(50, 300));
		host.Input.TouchMove(1, new Vector2(250, 300));
		host.Step();

		Assert.True(host.Get<PinchSystem>().Zoom > 1f);
	}
}
```

Scripted touches go only to `Touches`: unlike SDL on a device, the headless backend does not synthesize mouse events
from them. Touches are recorded and replayed by [input recording](/Ion/interaction/input/recording-and-playback/). The
remote protocol's `input.send` has no touch event type.

## Not available yet

- Gesture recognizers, drag-to-scroll in UI scroll views, and multi-touch in the UI (it sees one pointer).
- Pressure, finger size and stylus details.
- Touch on the GLFW platform.

## See also

- [Input overview](/Ion/interaction/input/overview/)
- [Mobile](/Ion/platforms/mobile/)
- [Breakout ECS example](/Ion/examples/breakout-ecs/)
- [Windowing](/Ion/rendering/windowing/): the GLFW and SDL platforms.
