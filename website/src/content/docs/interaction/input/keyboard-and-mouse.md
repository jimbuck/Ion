---
title: Keyboard and mouse
description: Read keys, modifier shortcuts, typed text, the mouse position, buttons, motion and wheel, and control the cursor.
sidebar:
  order: 2
---

Keyboard and mouse input comes from `IInputState`, captured at the start of every frame. This page covers each part of
it. Read [Input overview](/Ion/interaction/input/overview/) first for the frame model and how edges behave in
`FixedUpdate`.

## Keys

```csharp
using Ion;

public sealed class DebugKeysSystem(IInputState input)
{
	[Update]
	public void Update(GameTime dt)
	{
		if (input.Down(Key.W)) { /* held this frame */ }
		if (input.Up(Key.W)) { /* not held */ }
		if (input.Pressed(Key.F3)) { /* went down this frame */ }
		if (input.Released(Key.F3)) { /* went up this frame */ }
	}
}
```

| Query | True when |
|---|---|
| `Down(key)` | The key is held now. |
| `Up(key)` | The key is not held now. |
| `Pressed(key)` | The key went down this frame (from FixedUpdate: since the previous fixed step). Auto-repeat does not count. |
| `Released(key)` | The key went up this frame (from FixedUpdate: since the previous fixed step). |

A key pressed and released between two frames reports both `Pressed` and `Released` in the next frame and is not `Down`.

### Key names

`Key` values are layout-independent physical keys. Some have aliases, which are the same value:

| Group | Values |
|---|---|
| Letters and digits | `A` to `Z`, `Number0` to `Number9` |
| Function keys | `F1` to `F35` |
| Arrows and navigation | `Up`, `Down`, `Left`, `Right`, `Home`, `End`, `PageUp`, `PageDown`, `Insert`, `Delete` |
| Editing | `Enter`, `Escape`, `Space`, `Tab`, `BackSpace` (alias `Back`) |
| Modifiers | `ShiftLeft`/`LShift`, `ShiftRight`/`RShift`, `ControlLeft`/`LControl`, `ControlRight`/`RControl`, `AltLeft`/`LAlt`, `AltRight`/`RAlt`, `WinLeft`/`LWin`, `WinRight`/`RWin`, `Menu` |
| Keypad | `Keypad0` to `Keypad9`, `KeypadDivide`, `KeypadMultiply`, `KeypadSubtract` (`KeypadMinus`), `KeypadAdd` (`KeypadPlus`), `KeypadDecimal` (`KeypadPeriod`), `KeypadEnter` |
| Punctuation | `Tilde` (`Grave`), `Minus`, `Plus`, `BracketLeft`, `BracketRight`, `Semicolon`, `Quote`, `Comma`, `Period`, `Slash`, `BackSlash`, `NonUSBackSlash` |
| Locks and system | `CapsLock`, `ScrollLock`, `NumLock`, `PrintScreen`, `Pause`, `Clear`, `Sleep` |

On US layouts the `=` key reports `Key.Plus`. Keys the backend cannot map are dropped (they never reach the tracker).

:::note[Physical keys, not characters]
`Key.Z` is the key in the Z position of a US layout. On an AZERTY keyboard the user presses a key labeled W to get it.
Use keys for game controls and [`Text`](#text-input) for anything the player types.
:::

## Modifiers and shortcuts

`Pressed(key, modifiers)` and `Released(key, modifiers)` check the modifiers that were held when that key event
happened, so `Pressed(Key.S, ModifierKeys.Control)` is Ctrl+S whichever of the two keys went down first:

```csharp
if (input.Pressed(Key.S, ModifierKeys.Control)) Save();
if (input.Pressed(Key.Z, ModifierKeys.Control | ModifierKeys.Gui)) Undo();   // Ctrl+Z or Cmd+Z
if (input.Modifiers.HasFlag(ModifierKeys.Shift)) speed *= 2;               // Shift held now
if (input.Down(Key.ShiftLeft)) { /* modifier keys are ordinary keys too */ }
```

| Member | Meaning |
|---|---|
| `ModifierKeys` | Flags: `None`, `Alt`, `Control`, `Shift`, `Gui` (the Windows or Command key). |
| `Pressed(key, modifiers)` | `Pressed(key)` and at least one of the requested flags was held with that press. |
| `Released(key, modifiers)` | The same for the release. |
| `Modifiers` | The modifiers held now, derived from the held modifier keys (the level state). |

:::caution[`ModifierKeys.None` never matches]
`Pressed(Key.S, ModifierKeys.None)` is always false. Use `Pressed(Key.S)` to ignore modifiers. To require that no
modifier is held, combine `Pressed(Key.S)` with `input.Modifiers == ModifierKeys.None`.
:::

The flags are "any of", not "exactly": `Pressed(Key.S, ModifierKeys.Control)` is also true for Ctrl+Shift+S. If a key
is pressed several times in one frame, the modifiers of every press are combined.

## Text input

`Text` is what the player typed this frame, after keyboard layout processing and IME composition by the operating
system. Use it for name entry, chat and consoles:

```csharp
public sealed class ConsoleSystem(IInputState input)
{
	private readonly System.Text.StringBuilder _line = new();

	[Update]
	public void Update(GameTime dt)
	{
		_line.Append(input.Text);   // ReadOnlySpan<char>, no allocation

		if (input.Pressed(Key.BackSpace) && _line.Length > 0) _line.Length--;
		if (input.Pressed(Key.Enter))
		{
			Run(_line.ToString());
			_line.Clear();
		}
	}

	private void Run(string command) { }
}
```

- `Text` is a `ReadOnlySpan<char>` valid only until the next frame starts. Copy it (`ToString()`, `Append`) to keep it.
- At most 256 characters are kept per frame (`InputTracker.TextCapacity`); the rest are dropped.
- From FixedUpdate it holds everything typed since the previous fixed step.
- Editing keys (Backspace, Delete, arrows) do not produce text; read them as keys.

The [UI module](/Ion/interaction/ui/widgets/)'s `TextInput` widget does all of this for you, with a caret.

:::note[IME]
Composed characters arrive in `Text` once the operating system commits them. Ion does not display the composition
string while it is being typed, and has no API for the IME candidate window position.
:::

## Mouse position and buttons

```csharp
var position = input.MousePosition;            // window coordinates, origin top-left
if (input.Pressed(MouseButton.Left)) Shoot(position);
if (input.Down(MouseButton.Right)) Aim();
if (input.Released(MouseButton.Middle)) { }
```

`MouseButton` has `Left`, `Middle`, `Right` and `Button1` to `Button9` for extra buttons (the fourth physical button
reports `Button1`). The same `Down`, `Up`, `Pressed` and `Released` rules apply as for keys.

`MousePosition` is in window coordinates, the same space as sprite drawing without a camera. If you draw the world with
a 2D camera, convert the position to world space with that camera; see [2D cameras](/Ion/rendering/cameras-2d/).

## Motion and the wheel

| Member | Meaning |
|---|---|
| `MouseDelta` | Movement this frame (from FixedUpdate: since the previous fixed step), as a `Vector2`. |
| `WheelDelta` | Vertical wheel movement this frame (from FixedUpdate: since the previous fixed step). Positive is away from the user. |

```csharp
// Zoom with the wheel, orbit with right drag.
_zoom = Math.Clamp(_zoom - input.WheelDelta * 0.1f, 0.25f, 4f);
if (input.Down(MouseButton.Right)) _yaw += input.MouseDelta.X * 0.005f;
```

Only the vertical wheel is reported; horizontal scrolling is ignored.

## The cursor

The cursor belongs to the window. `IWindow` (in `Ion.Extensions.Graphics`) controls it:

| Member | Effect |
|---|---|
| `IsCursorVisible` | Shows or hides the cursor over the window. |
| `IsMouseGrabbed` | Captures the mouse: the cursor is hidden and locked to the window, and motion keeps arriving in `MouseDelta` at the edges (Silk.NET's disabled cursor mode). Use it for mouse-look and paddle games. |
| `Ion:Window:ShowCursor` | The initial cursor visibility (default `true`). |

`IInputState.SetMousePosition(position)` (or `SetMousePosition(x, y)`) moves the cursor.

The [Breakout](/Ion/examples/breakout/) sample grabs the mouse on the first click and releases it with Escape:

```csharp
public sealed class CursorSystem(IWindow window, IInputState input)
{
	[Update]
	public void Update(GameTime dt)
	{
		if (input.Pressed(Key.Escape) && window.IsMouseGrabbed)
		{
			window.IsMouseGrabbed = false;
			window.IsCursorVisible = true;
		}

		if (input.Pressed(MouseButton.Left) && !window.IsMouseGrabbed)
		{
			window.IsMouseGrabbed = true;
			window.IsCursorVisible = false;
		}
	}
}
```

## Backend details

The windowed backend (`SilkInputState`, Silk.NET on GLFW or SDL) has a few behaviors worth knowing:

- Silk.NET does not flag auto-repeat, so a key-down for a key that is already down is reported as a repeat.
- Modifiers sent with each key event are computed from the modifier keys held at that moment.
- On focus loss every held key and button is released without a `Released` edge.
- On SDL platforms (Android, iOS, the R36S, or `Ion:Window:Platform=Sdl`), SDL also turns touches into mouse events by
  default, so mouse-driven games and the UI work with a finger. See [Touch](/Ion/interaction/input/touch/).

## Testing keyboard and mouse code

The headless backend's `NullInputState` scripts the same input. Calls are queued and applied at the start of the next
frame, exactly like device events. With `IonTestHost`:

```csharp title="CursorTests.cs"
using System.Numerics;
using Ion;
using Ion.Testing;
using Xunit;

public class CursorTests
{
	[Fact]
	public void TheFirstClickGrabsTheMouse()
	{
		using var host = new IonTestHost().WithSystem<CursorSystem>();
		host.Step();

		host.Input.SetMousePosition(new Vector2(100, 200));
		host.Input.Click(MouseButton.Left);
		host.Step();
		Assert.True(host.Window.IsMouseGrabbed);

		host.Input.Tap(Key.Escape);
		host.Step();
		Assert.False(host.Window.IsMouseGrabbed);
	}
}
```

| `NullInputState` method | Queues |
|---|---|
| `Press(key, modifiers)`, `Release(key, modifiers)` | A key down or up. |
| `Tap(key, modifiers)` | A press and a release in the same frame. |
| `Repeat(key, modifiers)` | An auto-repeat key-down. |
| `Press(button)`, `Release(button)`, `Click(button = Left)` | Mouse buttons. |
| `SetMousePosition(position)` | A mouse move. |
| `Scroll(delta)` | A wheel movement. |
| `Type(text)` | Text input, one character at a time. |
| `ReleaseAll()` | A focus loss. |

See [Testing](/Ion/tooling/testing/) for the host, and
[Recording and playback](/Ion/interaction/input/recording-and-playback/) for replaying real sessions.

## See also

- [Input overview](/Ion/interaction/input/overview/)
- [Gamepad](/Ion/interaction/input/gamepad/)
- [Windowing](/Ion/rendering/windowing/): window options and platforms.
- [UI focus navigation](/Ion/interaction/ui/focus-navigation/): which keys the UI uses.
