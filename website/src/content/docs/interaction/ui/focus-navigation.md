---
title: Focus navigation
description: Drive Ion UI menus with the keyboard and gamepad, including spatial and Tab navigation, activate and back, key repeat, auto-focus and gamepad-only devices like the R36S.
sidebar:
  order: 4
---

Every interactive widget (buttons, toggles, sliders, text inputs and list items) can take the focus. With the focus,
a menu works entirely without a pointer: arrows or the D-pad move between widgets, Enter or gamepad A activates, Escape
or B goes back. That is what makes the same UI work on a desktop, a phone with a controller, and a handheld with only
buttons.

```csharp
public sealed class TitleSystem(Ui ui)
{
	[Update]
	public void Build(GameTime dt)
	{
		using (ui.Panel("title"))
		{
			if (ui.Button("Start")) StartGame();         // focused first (AutoFocus)
			if (ui.Button("Options")) OpenOptions();     // D-pad down, then A
			if (ui.Button("Quit")) Quit();
		}
	}

	private void StartGame() { }
	private void OpenOptions() { }
	private void Quit() { }
}
```

## Controls

| Action | Keyboard | Gamepad |
|---|---|---|
| Move focus (spatial) | Arrow keys | D-pad, or the left stick past `UiTheme.StickThreshold` |
| Move focus (tree order, wrapping) | Tab, Shift+Tab | none |
| Activate | Enter, keypad Enter, Space | A |
| Back | Escape | B |
| Adjust a focused slider | Left, Right | D-pad left, right (or the stick) |

Gamepad input comes from every connected gamepad, or only from slot `UiOptions.Gamepad` when you set it (for example
to let player 1 own the menu):

```csharp
builder.AddUi(options => options.Gamepad = 0);
```

"A" and "B" are positions in the SDL layout (bottom and right face buttons); see
[Gamepad](/Ion/interaction/input/gamepad/#buttons).

## What activate does

| Focused widget | Activate |
|---|---|
| `Button` | Reports a click: the `Button` call returns true this frame. |
| `Toggle` | Flips the value. |
| `ListItem` | Selects the item. |
| `TextInput` | Starts editing (the caret goes to the end). |
| `Slider` | Nothing; use Left/Right. |

If nothing has the focus, the first move or activate press focuses the first focusable widget instead.

## Spatial navigation

Arrow keys and the D-pad move to the nearest focusable widget in that direction:

1. Only widgets entirely beyond the focused widget's edge in that direction are candidates (1 pixel of overlap is
   allowed).
2. Each candidate is scored by its distance along the direction plus four times its gap across it, so a widget
   straight below beats a nearer one off to the side.
3. The lowest score wins. With no candidate the focus does not move: spatial navigation never wraps.

On a focused slider, Left and Right adjust the value instead of moving, so put sliders in a column (Up and Down still
leave them).

Tab and Shift+Tab move through focusable widgets in tree order (the order of your calls) and wrap around at the ends.
Disabled widgets are skipped by both.

## Back

`ui.BackPressed` is true in a frame where Escape, gamepad B or `IUiTree.Back()` was pressed and no text field used it.
Use it to leave a screen:

```csharp
if (ui.Button("Back") || ui.BackPressed) Screen = MenuScreen.Main;
```

While a text field is being edited, Escape and B stop editing instead, and `BackPressed` stays false. A second press
then goes back.

## Repeat

Holding a direction repeats it after `UiTheme.RepeatDelay` (0.4 s), then every `UiTheme.RepeatInterval` (0.08 s). The
same applies to editing keys (Left, Right, Backspace, Delete) in a text field, and to adjusting a slider. The timing
uses the frame's delta, not the wall clock, so repeats are deterministic under a fixed-step clock and in tests.

The left stick counts as a direction when it crosses `UiTheme.StickThreshold` (0.5) on its dominant axis; crossing
the threshold is a press, holding it past the threshold repeats. The stick value is read after the input dead zone.

```csharp
ui.Theme = ui.Theme with { RepeatDelay = 0.3f, RepeatInterval = 0.06f, StickThreshold = 0.6f };
```

## Auto-focus

With `UiOptions.AutoFocus` (the default), whenever nothing has the focus at the end of a frame, the first focusable,
enabled widget takes it: at startup, after a screen change, or when the focused widget disappears. A gamepad-only
device therefore always has something to act on, and the first press of A works immediately.

In the [Menu](/Ion/examples/menu/) sample, `main/Play` is focused on the first frame and `options/Fullscreen` right
after switching to the options screen, without any code. Turn it off when the UI is an overlay on a game that also
reads the keyboard or gamepad, so the UI does not hold a focus the player did not ask for:

```csharp
builder.AddUi(options => options.AutoFocus = false);
```

## Focus and the pointer

Pressing the mouse button over a focusable widget focuses it; a click on a text field also starts editing, and a click
outside stops editing. The focused widget draws an outline (`UiTheme.FocusOutline`, `FocusThickness`), whether it was
focused by the pointer, keys or the gamepad. Moving the focus with keys, the gamepad or `IUiTree` to a widget outside a
scroll view's visible area scrolls it into view (the nearest scroll view only).

`ui.FocusedPath` is the focused widget's path, and `IUiTree.Focus(path)` moves the focus from code or tools:

```csharp
// Put the focus on "Continue" when the pause menu opens.
if (_justOpened && ui.Tree.Focus("pause/Continue")) _justOpened = false;
```

Commands like `Focus` apply at the start of the next frame, and only to paths in the published tree, so call it once
the screen has been built for a frame.

## Games that also read the controls

The UI reads the same `IInputState` your game does; it does not consume input. When a menu is open over gameplay:

- Stop your game's own reaction to A, B, Escape and the arrows while the menu is open (a paused flag, a scene, or
  checking your own screen state).
- `ui.IsEditingText` is true while a text field is being edited: ignore keyboard bindings then.
- `ui.IsPointerOverUi` is true when the pointer is over the UI: ignore game clicks then.

## Handhelds and the R36S

The R36S has a D-pad, face buttons, shoulders and two sticks, reported through SDL as a gamepad, and no pointer or
keyboard. The UI covers it with the defaults: auto-focus puts the focus on the first widget, the D-pad and left stick
move it, A activates, B goes back, and sliders adjust with the D-pad. Tips for such devices:

- Keep every screen reachable with the D-pad alone: avoid layouts where a widget has nothing beyond it in any
  direction and cannot be reached by spatial moves.
- Give every screen a Back path through `ui.BackPressed`.
- `TextInput` has no on-screen keyboard; avoid it on gamepad-only targets, or offer a list of choices instead.
- Size fonts and panels for 640x480 (there is no UI scaling); see
  [Layout and styling](/Ion/interaction/ui/layout-and-styling/#resolution-and-scaling).

The gamepad path is tested with scripted input (the Menu sample's `MenuTests`); it has not been verified on the
device itself yet. See [R36S](/Ion/platforms/r36s/) for building and deploying.

## Testing navigation

Script the gamepad or keyboard with `IonTestHost.Input` and assert on `IUiTree.FocusedPath`. From the Menu sample's
tests:

```csharp title="MenuTests.cs"
[Fact]
public void TheGamepadAloneReachesAndChangesTheOptions()
{
	using var host = new IonTestHost().UseEntryPoint<Program>();
	host.Input.ConnectGamepad(0);
	host.Step();
	var tree = host.Get<IUiTree>();
	var settings = host.Get<MenuSettings>();

	host.Input.Tap(0, GamepadButton.DPadDown);
	host.Step();
	Assert.Equal("main/Options", tree.FocusedPath);
	host.Input.Tap(0, GamepadButton.A);
	host.Step(2);                                  // the screen changes the frame after the click
	Assert.Equal("options/Fullscreen", tree.FocusedPath);

	host.Input.Tap(0, GamepadButton.A);
	host.Step();
	Assert.True(settings.Fullscreen);

	host.Input.Tap(0, GamepadButton.DPadDown);
	host.Step();
	host.Input.Tap(0, GamepadButton.DPadLeft);     // adjust the focused slider by one step
	host.Step();
	Assert.Equal(0.75f, settings.Volume, 5);

	host.Input.Tap(0, GamepadButton.B);
	host.Step(2);
	Assert.Equal(MenuScreen.Main, host.Get<MenuSystem>().Screen);
}
```

The keyboard works the same way: `host.Input.Tap(Key.Tab)`, `Tap(Key.Enter)`, `Type("Grace")`.

## See also

- [Widgets](/Ion/interaction/ui/widgets/)
- [Gamepad](/Ion/interaction/input/gamepad/) and [Keyboard and mouse](/Ion/interaction/input/keyboard-and-mouse/)
- [UI remote](/Ion/interaction/ui/ui-remote/): focus and click by path.
- [R36S](/Ion/platforms/r36s/)
