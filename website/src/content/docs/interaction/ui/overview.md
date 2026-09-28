---
title: UI overview
description: Build menus and HUDs with Ion's immediate-mode UI, and understand its frame, its retained tree and how it fits the schedule.
sidebar:
  order: 1
---

`Ion.Extensions.UI` is an immediate-mode UI for menus, options screens and HUDs. Every frame, your Update steps
describe the whole UI by calling widget methods on the `Ui` context, and interactive widgets tell you what happened to
them: `if (ui.Button("Play")) ...`. There is no markup and nothing to declare ahead of time. It works with the mouse,
keyboard, gamepad and touch, needs no font to lay out, and publishes an inspectable tree that tests and agents can
drive by path.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Assets;
using Ion.Extensions.Graphics;
using Ion.Extensions.UI;

var builder = IonApplication.CreateBuilder(args);
builder.AddUi().AddSystem<PauseMenuSystem>();

using var game = builder.Build();
game.UseUi().UseSystem<PauseMenuSystem>();
game.Run();

public sealed class PauseMenuSystem(Ui ui, IAssetManager assets, IEvents events)
{
	private bool _music = true;
	private float _volume = 0.8f;

	[Init]
	public void Load()
	{
		ui.Theme = UiTheme.Default with { Font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20) };
		ui.RootStyle = new UiStyle { Justify = UiJustify.Center, AlignItems = UiAlign.Center };
	}

	[Update]
	public void Build(GameTime dt)
	{
		using (ui.Panel("pause", new UiStyle { Width = 360 }))
		{
			ui.Label("Paused", scale: 1.6f);
			ui.Toggle("Music", ref _music);
			ui.Slider("Volume", ref _volume, 0f, 1f, step: 0.05f);
			if (ui.Button("Quit")) events.Emit(new ExitGameEvent());
		}
	}
}
```

## Setup

| Call | What it registers or adds |
|---|---|
| `builder.AddUi(options => ...)` | The `Ui` context (a singleton), its tree as `IUiTree`, and the UI system. Also registers the engine core (`AddIon`), whose input, window and sprite batch the UI uses. `options` configures `UiOptions`. |
| `game.UseUi()` | The UI system, plus the engine's systems (`UseIon`). |
| `builder.AddUiRemote()` | The UI's remote protocol methods (`ui.tree`, `ui.click`, ...), plus `AddUi`. See [UI remote](/Ion/interaction/ui/ui-remote/). |

Both are idempotent: call them alongside `AddIon`/`UseIon` or on their own, in any order. The
[Menu](/Ion/examples/menu/) sample does both:

```csharp
builder.AddIon(graphics => graphics.ClearColor = new Color(0x14, 0x17, 0x20, 0xFF))
	.AddUi().AddUiRemote().AddSystem<MenuSystem>();

using var game = builder.Build();
game.UseIon().UseUi().UseSystem<MenuSystem>();
```

`UiOptions` is configured in code only (it is not bound from `appsettings.json`):

| Option | Default | Meaning |
|---|---|---|
| `AutoFocus` | `true` | When nothing has the focus at the end of a frame, focus the first focusable widget. See [Focus navigation](/Ion/interaction/ui/focus-navigation/). |
| `Gamepad` | `-1` | The gamepad slot that navigates, or -1 for every connected gamepad. |
| `Theme` | null | The starting theme; `UiTheme.Default` when null. |

## Immediate API, retained tree

You write the UI as if it were rebuilt from scratch each frame. Underneath, the calls of a frame are recorded as a tree
of nodes, laid out once at the end of Update, and kept until the end of the next Update. That retained tree is:

- the **hit-test tree** for the next frame's pointer input,
- the **draw list** for Render,
- the **inspectable tree** published through `IUiTree`, which the remote protocol and tests read.

So the API is immediate and what hit testing and tools see is stable. A widget's state (the text buffer of a text
field, a scroll offset, the focus) is kept between frames by its identity (see [Identity](#identity)).

## The UI frame

The UI system brackets Update with a scope and draws in Render:

| Stage, order | Step | What happens |
|---|---|---|
| Update, `StageOrder.UiFrame` (-550), scope begin | `BeginFrame` | Queued `IUiTree` commands are applied; input is read against the previous frame's layout (hover, press, click, wheel, focus navigation, text editing); the root opens at the window size. |
| Update, after -550 | your steps and scene steps | Widget calls record nodes and return results. |
| Update, scope end | `EndFrame` | Containers left open are closed (with a warning), nodes are laid out, the focus is validated, the tree is published. Runs in a `finally`, so also after a step threw. |
| Render, `StageOrder.Ui` (700) | `Draw` | The laid-out frame is submitted to `ISpriteBatch`. |

-550 sits in the engine setup band after coroutines (-600) and before scenes (-500), so both scene steps and your own
Update steps (order 0 by default) can build UI. 700 is after your Render steps (0), so the UI draws on top of the game,
and before the metrics overlay (800). See [Stage order](/Ion/reference/stage-order/).

:::caution[Widgets only in Update]
Widget methods throw `InvalidOperationException` outside `BeginFrame`/`EndFrame`: call them from `Update` steps (or
scene Update steps), with `UseUi()` in the schedule. Calling them from `FixedUpdate`, `Render` or `Init` fails.
:::

### One frame of latency

Input is read at `BeginFrame` against the previous frame's layout. Consequences:

- A widget reacts to input from the frame after it first appears.
- A screen change made in response to a click shows from the next frame.
- Tree commands (`IUiTree.Click` and friends) are applied at the next `BeginFrame`, and their effect is visible in
  the tree published at the end of that frame.

This is what makes the UI deterministic and testable: every interaction takes effect at a well-defined frame.

## Screens

Keep the current screen in a field and describe only that screen each frame. The Menu sample:

```csharp
public enum MenuScreen { Main, Options, Play }

public sealed class MenuSystem(Ui ui, MenuSettings settings, IEvents events)
{
	public MenuScreen Screen { get; private set; }

	[Update]
	public void Build(GameTime dt)
	{
		switch (Screen)
		{
			case MenuScreen.Main: Main(); break;
			case MenuScreen.Options: Options(); break;
			default: Play(); break;
		}
	}

	private void Main()
	{
		using (ui.Panel("main", new UiStyle { Width = 360, Gap = 12 }))
		{
			ui.Label("Ion Menu", "title", scale: 1.6f);
			if (ui.Button("Play")) Screen = MenuScreen.Play;
			if (ui.Button("Options")) Screen = MenuScreen.Options;
			if (ui.Button("Quit")) events.Emit(new ExitGameEvent());
		}
	}

	private void Options()
	{
		using (ui.Panel("options", new UiStyle { Width = 560, Gap = 10 }))
		{
			ui.Label("Options", "title", scale: 1.6f);
			ui.Slider("Volume", ref settings.Volume, 0f, 1f, 0.05f);
			if (ui.Button("Back") || ui.BackPressed) Screen = MenuScreen.Main;
		}
	}

	private void Play() { /* ... */ }
}
```

`ui.BackPressed` is true when gamepad B, Escape or `IUiTree.Back()` was pressed this frame and no text field used it,
the conventional way to go back a screen.

## Identity

A widget's identity keys its per-widget state. It comes from, in order:

1. its explicit `key` argument, when given (combined with its parent's identity);
2. otherwise its call site (`[CallerFilePath]` and `[CallerLineNumber]`, filled in by the compiler) and its parent.

Several calls with the same identity in one frame (a loop at one call site) are told apart by call order: the n-th
repeat gets the n-th id of a fixed sequence. So a loop keeps per-widget state without keys as long as its items keep
their order. Give keys when items can be reordered, inserted or removed:

```csharp
foreach (var save in saves)
{
	// The key keeps each row's state (and its tree path) attached to its save, not its position.
	using (ui.Row(save.Id))
	{
		ui.Label(save.Name);
		if (ui.Button("Load")) Load(save);
	}
}
```

The key also becomes the node's path segment in the tree; see [UI remote](/Ion/interaction/ui/ui-remote/#paths).

## Useful members

| Member | Meaning |
|---|---|
| `Theme`, `RootStyle` | The look and the root container's style. See [Layout and styling](/Ion/interaction/ui/layout-and-styling/). |
| `BackPressed` | Back was pressed this frame and not used by a text field. |
| `IsPointerOverUi` | The pointer is over an interactive widget or a panel. Ignore game clicks when it is true. |
| `IsEditingText` | A text field is being edited; keyboard text goes to it. Ignore game key bindings when it is true. |
| `FocusedPath` | The focused widget's path, or null. |
| `Tree` | The `IUiTree` (the same instance is registered in the services). |
| `MeasureText(text, scale)` | Text size with the theme's font. |
| `NodeCount`, `Frame`, `Viewport` | Nodes recorded this frame, frames begun, the viewport size. |

```csharp
[Update]
public void Shoot(GameTime dt)
{
	// Clicking a HUD button should not also fire the weapon.
	if (input.Pressed(MouseButton.Left) && !ui.IsPointerOverUi) Fire();
	if (input.Pressed(Key.R) && !ui.IsEditingText) Reload();
}
```

`IsPointerOverUi` reflects the previous frame's layout, read at `BeginFrame`, so it is valid for any Update step.

## Performance

Nothing is allocated per frame in steady state: nodes live in two arrays that swap every frame, per-widget state is
created when a widget first appears and dropped after 120 unseen frames once there are many more states than widgets,
and paths, measured text and formatted numbers are cached. A test runs 200 frames of a screen with every widget kind
and asserts zero bytes allocated. For text that changes, pass a `ReadOnlySpan<char>` to `Label` (it interns the
string), as in [Widgets](/Ion/interaction/ui/widgets/#label). The engine's `UiBenchmarks` measure a 500-widget screen
at about 53 microseconds for build, layout and tree publication.

## Driving a Ui yourself

The `Ui` class does not need the schedule. Tests and tools can create one over any `IInputState` and drive it:

```csharp
var input = new NullInputState();
var ui = new Ui(input, new UiOptions { AutoFocus = false });

input.Step();
ui.BeginFrame(1f / 60f, new Vector2(800, 600));
using (ui.Panel("p")) ui.Button("OK");
ui.EndFrame();
ui.Draw(spriteBatch);   // any ISpriteBatch
```

## Known gaps

The UI is deliberately small. Not done yet:

- Keyboard text selection, clipboard, and display of IME composition (composed text is inserted once committed).
- Multi-line text and text wrapping.
- Flex shrink (children larger than their container overflow it).
- Nested scroll views scrolling each other into view (only the nearest follows the focus).
- Touch gestures such as drag to scroll, and multi-touch (touch reaches the UI as the mouse).
- Right-to-left text.
- Per-widget style overrides beyond `UiStyle.Background` and the theme.
- A separate Dear ImGui-style debug overlay.

## In this section

- [Widgets](/Ion/interaction/ui/widgets/): every widget with examples.
- [Layout and styling](/Ion/interaction/ui/layout-and-styling/): the flex layout model, themes and nine-slices.
- [Focus navigation](/Ion/interaction/ui/focus-navigation/): keyboard and gamepad menus, the R36S.
- [UI remote](/Ion/interaction/ui/ui-remote/): inspect and drive the UI from tests, tools and agents.

## See also

- [Menu example](/Ion/examples/menu/)
- [Text](/Ion/rendering/text/): fonts and `IFontSet.CreateStyle`.
- [Stages](/Ion/concepts/stages/)
- Source: [Ion.Extensions.UI](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.UI/).
