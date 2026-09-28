---
title: Widgets
description: Every widget of Ion's UI module (containers, labels, buttons, toggles, sliders, text inputs, lists, spacers) with examples and their input and tree behavior.
sidebar:
  order: 2
---

Widgets are methods on the `Ui` context. Call them from an `Update` step; each one records a node for this frame and,
for interactive widgets, returns what happened. This page lists every widget. The examples assume a system with a `Ui`
injected:

```csharp
using Ion;
using Ion.Extensions.UI;

public sealed class HudSystem(Ui ui)
{
	[Update]
	public void Build(GameTime dt)
	{
		// widget calls go here
	}
}
```

## Summary

| Method | Node kind | Returns |
|---|---|---|
| `Panel(key, style)` | `Panel` | `UiScope` (dispose to close) |
| `Row(key, style)`, `Column(key, style)` | `Row`, `Column` | `UiScope` |
| `ScrollView(key, style)` | `ScrollView` | `UiScope` |
| `Disabled(disabled)` | none | `UiScope` |
| `Label(text, key, scale, color, style)` | `Label` | nothing |
| `Button(text, key, style)` | `Button` | true in the frame it is clicked |
| `Toggle(text, ref value, key, style)` | `Toggle` | true when `value` changed |
| `Slider(text, ref value, min, max, step, key, style)` | `Slider` | true when `value` changed |
| `TextInput(label, ref value, maxLength, key, style)` | `TextInput` | true when `value` changed |
| `List(label, items, ref selected, key, style)` | `List` with `ListItem` children | true when `selected` changed |
| `Spacer(size)` | `Spacer` | nothing |

Every widget method also has `[CallerFilePath]` and `[CallerLineNumber]` parameters that the compiler fills in to
identify the call site. Do not pass them. `key` is optional everywhere: it sets the widget's identity and its
path segment in the tree (see [Identity](/Ion/interaction/ui/overview/#identity)). `style` is a `UiStyle` (see
[Layout and styling](/Ion/interaction/ui/layout-and-styling/)).

## Containers

Containers open a scope. Everything called until the scope closes becomes their child. Close it by disposing the
returned `UiScope` (a struct, so `using` allocates nothing) or by calling `ui.End()`:

```csharp
using (ui.Panel("settings"))
{
	using (ui.Row())
	{
		ui.Button("Apply");
		ui.Button("Cancel");
	}
}

// Equivalent, without using blocks:
ui.Panel("settings");
ui.Row();
ui.Button("Apply");
ui.Button("Cancel");
ui.End();   // closes the row
ui.End();   // closes the panel
```

Closing scopes out of order throws `InvalidOperationException`. A container still open at the end of the frame is
closed for you, with a warning in the log.

### Panel

A container with the theme's background (`UiTheme.PanelBackground`, or the `PanelSkin` nine-slice) and padding
(`UiTheme.PanelPadding`). Its children are laid out in a column unless `style.Direction` is `Row`. Pointer input over a
panel does not reach anything drawn under it, and `IsPointerOverUi` is true over it.

```csharp
using (ui.Panel("inventory", new UiStyle { Width = 400, Gap = 6 }))
{
	ui.Label("Inventory", scale: 1.4f);
}
```

### Row and Column

Transparent containers with a fixed direction: `Row` lays its children left to right, `Column` top to bottom (the
direction in `style` is ignored). No background unless you set `style.Background`, in which case they also block the
pointer.

```csharp
using (ui.Row(style: new UiStyle { Gap = 12, Justify = UiJustify.End }))
{
	if (ui.Button("Back")) GoBack();
	if (ui.Button("Next")) GoNext();
}
```

### ScrollView

Clips its children to its rectangle (a scissor segment when drawn) and scrolls them along its main axis: the mouse
wheel over it scrolls by `UiTheme.ScrollSpeed` pixels per notch, and moving the focus to a child outside the visible
area scrolls it into view. Give it a size (`Height`, `MaxHeight` or `Grow` for a column), otherwise it grows to its
content and never scrolls. A scroll view with overflow draws a position indicator (`UiTheme.ScrollBar`,
`ScrollBarWidth`). Its tree value is the scroll offset in pixels. Scroll views do not wrap.

```csharp
using (ui.Panel("levels", new UiStyle { Width = 320 }))
using (ui.ScrollView("list", new UiStyle { MaxHeight = 240 }))
{
	for (var i = 0; i < _levels.Length; i++)
	{
		if (ui.Button(_levels[i])) Start(i);
	}
}
```

Children of a scroll view that are scrolled entirely out of view are reported with `Visible` false in the tree, and
are not drawn or hit.

### Disabled

Not a container node but a scope: widgets created inside are drawn dimmed (`UiTheme.TextDisabled`,
`ButtonDisabled`) and ignore pointer, focus and tree commands. `Disabled(false)` does nothing, so you can pass a
condition:

```csharp
using (ui.Disabled(!_hasSave))
{
	if (ui.Button("Continue")) LoadSave();   // never true while disabled
}
```

## Label

Static text. Its path segment is its `key`, or `label`: the text can change every frame without changing the path.

```csharp
ui.Label("Game Over", "title", scale: 2f);
ui.Label("Press A to continue", color: UiTheme.Default.TextDisabled);
```

| Parameter | Default | Meaning |
|---|---|---|
| `text` | | The text. |
| `key` | null | The identity and path segment. |
| `scale` | `1` | Text scale (for example 1.6 for a title). |
| `color` | null | The color, or null for `UiTheme.Text`. |

For text that changes (a score, a timer), use the `ReadOnlySpan<char>` overload. It interns the string, so text that
repeats costs no allocation:

```csharp
Span<char> buffer = stackalloc char[32];
if (_score.TryFormat(buffer, out var written))
{
	ui.Label(buffer[..written], "score", scale: 1.5f);
}
```

The Menu sample builds its greeting the same way, concatenating into a `stackalloc` buffer.

## Button

Returns true in the frame it is clicked:

- pointer pressed and released over it (touch counts, as the mouse),
- Enter, keypad Enter, Space or gamepad A while it has the focus,
- `IUiTree.Click(path)` (and the remote `ui.click`).

```csharp
if (ui.Button("Play")) Screen = MenuScreen.Play;
if (ui.Button("Delete", key: "delete-save")) DeleteSave();
```

The caption is also the path segment unless you give a key. Background colors follow the state: `Button`,
`ButtonHover`, `ButtonPressed` (held with the pointer over it) and `ButtonDisabled`, or `style.Background` when set.
With `UiTheme.ButtonSkin` the button is a tinted nine-slice. Several clicks on one button in one frame count once.

## Toggle

A checkbox with a caption. Clicking or activating it flips the `ref bool`; `IUiTree.SetValue(path, "true")` sets it.
Returns true when the value changed this frame.

```csharp
if (ui.Toggle("Fullscreen", ref settings.Fullscreen)) window.IsFullscreen = settings.Fullscreen;
ui.Toggle("Subtitles", ref settings.Subtitles);
```

The box is `UiTheme.ToggleSize` pixels, filled with `Accent` when checked. The tree value is `true` or `false`.

## Slider

A caption, a track and the value. It changes by:

- dragging with the pointer on the track,
- Left/Right keys, D-pad left/right or the left stick while it has the focus: one `step`, or a twentieth of the range
  when `step` is 0 (held directions repeat, see [Focus navigation](/Ion/interaction/ui/focus-navigation/#repeat)),
- `IUiTree.SetValue(path, "0.35")` (invariant culture).

The value is kept within [`min`, `max`] and snapped to `step`. Returns true when it changed.

```csharp
if (ui.Slider("Music", ref _music, 0f, 1f, step: 0.05f)) audio.SetBusVolume(AudioBus.Music, _music);
ui.Slider("FOV", ref _fov, 60f, 110f, step: 1f);
ui.Slider("Sensitivity", ref _sensitivity, 0.1f, 5f);   // continuous
```

| Parameter | Default | Meaning |
|---|---|---|
| `min`, `max` | `0`, `1` | The range (swapped if `max < min`). |
| `step` | `0` | Snapping; 0 is continuous. |

The value shown and published in the tree is formatted with up to three decimals, invariant culture (`0.35`). Widths
come from the theme: the caption takes `LabelWidth` (or its text width when 0), the track at least `SliderWidth`, the
value `ValueWidth`.

## TextInput

A single-line text field with a caption.

- Clicking it, or activating it (Enter, Space, A) while focused, starts editing. The caret goes to the end.
- While editing: typed text is inserted at the caret, and Backspace, Delete, Left, Right, Home and End edit. Held
  editing keys repeat.
- Enter, Escape, gamepad A or B, a click elsewhere, Tab, or Up/Down stop editing (Tab and Up/Down also move the focus).
- `IUiTree.SetValue(path, text)` replaces the text; `IUiTree.Type(path, text)` types into it.

Returns true when `value` changed. A new string is allocated per edit, never per frame.

```csharp
ui.TextInput("Name", ref settings.PlayerName, maxLength: 16);

if (ui.TextInput("Search", ref _query, maxLength: 32)) Filter(_query);
```

| Parameter | Default | Meaning |
|---|---|---|
| `maxLength` | `64` | Maximum characters (at least 1); longer input and `SetValue` text are cut. |

While a field is being edited, `ui.IsEditingText` is true and Escape or B stop editing instead of setting
`BackPressed`. Check `IsEditingText` before reacting to keys in your game code.

:::caution[Editing is basic]
There is no text selection (Shift+arrows, double-click), no clipboard (Ctrl+C, Ctrl+V), no multi-line editing and no
display of IME composition: text composed with an IME is inserted when the operating system commits it. On devices
without a keyboard (gamepads, the R36S) there is no on-screen keyboard; `TextInput` is reachable but can only be typed
into through `IUiTree` or injected text input (`ScriptedInput`, the remote `input.send`).
:::

## List

A single-selection list: a caption, then one focusable item per entry. Clicking or activating an item selects it;
`IUiTree.SetValue` on the list accepts an item's text or its index. Returns true when `selected` changed. `selected`
is -1 for none.

```csharp
private static readonly string[] Difficulties = ["Easy", "Normal", "Hard"];
private int _difficulty = 1;

[Update]
public void Build(GameTime dt)
{
	if (ui.List("Difficulty", Difficulties, ref _difficulty)) ApplyDifficulty(_difficulty);
}
```

`items` is a `ReadOnlySpan<string>`, so an array or a collection expression works. Items are `ListItem` children in the
tree, with their text as the path segment (`options/Difficulty/Hard`) and a value of `true` when selected; the list's
value is the selected item's text. The list is always a column. The selected item uses `ListItemSelected`, the hovered
one `ListItemHover`. For long lists, put it in a `ScrollView`.

## Spacer

Empty space along the parent's main axis: `size` pixels, or with 0 (the default) a spacer that grows to take the free
space, pushing what follows to the far end.

```csharp
using (ui.Row("toolbar", new UiStyle { Width = 600 }))
{
	ui.Label("Level 3");
	ui.Spacer();                 // grows
	if (ui.Button("Pause")) Pause();
	ui.Spacer(8);                // 8 px
	if (ui.Button("Menu")) OpenMenu();
}
```

## A complete options screen

The [Menu](/Ion/examples/menu/) sample's options screen uses most widgets:

```csharp
private void Options()
{
	using (ui.Panel("options", new UiStyle { Width = 560, Gap = 10 }))
	{
		ui.Label("Options", "title", scale: 1.6f);
		if (ui.Toggle("Fullscreen", ref settings.Fullscreen)) window.IsFullscreen = settings.Fullscreen;
		ui.Slider("Volume", ref settings.Volume, 0f, 1f, 0.05f);
		ui.List("Difficulty", MenuSettings.Difficulties, ref settings.Difficulty);

		ui.TextInput("Name", ref settings.PlayerName, maxLength: 16);
		if (ui.Button("Back") || ui.BackPressed) Screen = MenuScreen.Main;
	}
}
```

Its tree, as a test or agent sees it: `options`, `options/title`, `options/Fullscreen`, `options/Volume`,
`options/Difficulty` (with `options/Difficulty/Easy`, `/Normal`, `/Hard`), `options/Name` and `options/Back`.

## Custom drawing

There is no custom widget API. For decorations, draw with `ISpriteBatch` in a Render step using rectangles from the
tree (`ui.Tree.TryFind(path, out var node)` gives `node.Rect`); steps at the default order 0 draw under the UI (700),
steps above 700 draw over it. `Ui.DrawNineSlice(spriteBatch, skin, rect, color)` is public for drawing nine-slices
yourself.

## See also

- [UI overview](/Ion/interaction/ui/overview/)
- [Layout and styling](/Ion/interaction/ui/layout-and-styling/)
- [Focus navigation](/Ion/interaction/ui/focus-navigation/)
- [UI remote](/Ion/interaction/ui/ui-remote/): the tree values and commands per widget kind.
- [Menu example](/Ion/examples/menu/)
