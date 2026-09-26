# Ion UI (`Ion.Extensions.UI`)

Status: implemented (Stage 5b, UI half, September 2026). This document describes the API, the layout model, the tree
contract that the remote protocol exposes (`ui.tree`, `ui.click`) and the theme. The roadmap context is section 4.12 and
Stage 5b of [the engine review](../plans/2026-09-engine-review-and-roadmap.md).

## 1. Shape

```
Ion.Extensions.UI.Abstractions   IUiTree, UiNodeInfo, UiNodeKind, UiRect: the inspection and command surface, no dependencies
Ion.Extensions.UI                Ui (the immediate-mode context), UiStyle, UiTheme, UiNineSlice, UiOptions, AddUi/UseUi
```

The UI is immediate mode: every frame, Update steps describe the whole UI by calling widget methods on the `Ui` context,
and interactive widgets report what happened to them (`if (ui.Button("Play")) ...`). Nothing is declared ahead of time
and there is no markup. Underneath, the calls of a frame are recorded as a tree of nodes that is laid out once at the end
of Update and kept until the end of the next one: it is the hit-test tree for the next frame's input, the draw list for
Render, and the inspectable tree published through `IUiTree`. So the API is immediate, and what hit testing and tools see
is retained and stable.

Registration:

```csharp
builder.Services.AddIon(builder.Configuration);
builder.Services.AddUi(options => options.AutoFocus = true);   // Ui, IUiTree, the UI system
app.UseIon().UseUi();

public sealed class MenuSystem(Ui ui, IAssetManager assets)
{
    bool _music = true; float _volume = 0.8f; int _difficulty = 1; string _name = "Player";
    static readonly string[] Difficulties = ["Easy", "Normal", "Hard"];

    [Init] public void Load(GameTime dt)
    {
        ui.Theme = UiTheme.Default with { Font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20) };
        ui.RootStyle = new UiStyle { Justify = UiJustify.Center, AlignItems = UiAlign.Center };
    }

    [Update] public void Build(GameTime dt)
    {
        using (ui.Panel("options", new UiStyle { Width = 560 }))
        {
            ui.Label("Options", scale: 1.6f);
            ui.Toggle("Music", ref _music);
            ui.Slider("Volume", ref _volume, 0, 1, step: 0.05f);
            ui.List("Difficulty", Difficulties, ref _difficulty);
            ui.TextInput("Name", ref _name, maxLength: 16);
            if (ui.Button("Back") || ui.BackPressed) { ... }
        }
    }
}
```

The complete sample is `Ion.Examples/Ion.Examples.Menu`.

## 2. Frame and schedule

| Stage, order | Step | What happens |
|---|---|---|
| Update, `StageOrder.UiFrame` (-550), scope begin | `UiSystem.BeginFrame` | queued `IUiTree` commands are applied; input is read against the previous frame's layout (hover, press, click, wheel, focus navigation, text editing); the root opens with the window size |
| Update, any order after -550 | game and scene steps | widget calls record nodes and return results |
| Update, scope end (in a `finally`) | `UiSystem.EndFrame` | containers left open are closed (with a warning), the nodes are laid out, the focus is validated, the tree is published |
| Render, `StageOrder.Ui` (700) | `UiSystem.Draw` | the laid-out frame is submitted to `ISpriteBatch` |

`-550` sits in the engine setup band after coroutines (-600) and before scenes (-500), so both scene steps and the game's
own Update steps can build UI. `700` is inside the sprite batch scope (-850), after the game's own Render steps (0), and
before the metrics overlay (800), which therefore stays on top. Because input is read
against the previous frame's layout, a widget reacts to input from the frame after it first appears; a screen change
made in response to a click shows from the next frame.

`Ui` can also be driven without the schedule (tests, tools, benchmarks): `BeginFrame(deltaSeconds, viewport)`, widget
calls, `EndFrame()`, `Draw(spriteBatch)`.

## 3. API

Containers return a `UiScope`, a struct: dispose it (`using (ui.Row()) { ... }`, no allocation) or call `ui.End()`.
Closing out of order throws.

| Method | Node kind | Result and behavior |
|---|---|---|
| `Panel(key, style)` | `Panel` | a container with the theme's background and padding; blocks the pointer from reaching what is under it |
| `Row(key, style)`, `Column(key, style)` | `Row`, `Column` | transparent containers with a fixed direction |
| `ScrollView(key, style)` | `ScrollView` | clips its children to its rectangle (a scissor segment) and scrolls them along its main axis: wheel over it, or the focus moving to a child outside it; its tree value is the offset |
| `Disabled(bool)` | none | widgets created inside are drawn dimmed and ignore pointer, focus and tree commands |
| `Label(text, key, scale, color, style)` | `Label` | text; the `ReadOnlySpan<char>` overload interns the string, so formatted text that repeats costs nothing |
| `Button(text, key, style)` | `Button` | true in the frame it is clicked |
| `Toggle(text, ref bool, key, style)` | `Toggle` | flips the value when clicked; true when it changed |
| `Slider(text, ref float, min, max, step, key, style)` | `Slider` | pointer drag, Left/Right while focused (a step, or a twentieth of the range), clamped and snapped; true when it changed |
| `TextInput(label, ref string, maxLength, key, style)` | `TextInput` | click or activate to edit; text, Backspace, Delete, Left, Right, Home, End; Enter, Escape, gamepad A or B, a click elsewhere, Tab or Up/Down stop; a new string is allocated per edit, never per frame; true when it changed |
| `List(label, ReadOnlySpan<string> items, ref int selected, key, style)` | `List` + `ListItem` children | click or activate an item to select it; true when the selection changed |
| `Spacer(size)` | `Spacer` | fixed space along the parent's main axis, or (0) a spacer that grows |

Other members: `Theme`, `RootStyle`, `Tree`, `BackPressed` (gamepad B, Escape or `IUiTree.Back()` this frame and not
used by a text field), `IsPointerOverUi` (the game can ignore a click that landed on the UI), `IsEditingText`,
`FocusedPath`, `MeasureText`, `NodeCount`, and the static `DrawNineSlice`.

**Identity.** A widget's numeric id, which keys its state (text buffer, scroll offset, focus), comes from its explicit
key when given, else from its call site (`[CallerFilePath]`, `[CallerLineNumber]`) and its parent's id. Several calls with
the same identity in one frame (a loop at one call site) are told apart by call order: the n-th repeat gets the n-th id of
a fixed sequence. So a loop keeps per-widget state without keys as long as its items keep their order; give keys when
they can reorder.

**Input.** Pointer: the left mouse button (touch reaches the UI as the mouse, as the windowing layer reports it). A
widget is clicked when the button is pressed and released over it. Keyboard and gamepad focus navigation:

| Input | Keyboard | Gamepad (every connected pad, or `UiOptions.Gamepad`) |
|---|---|---|
| Move focus | arrows (spatial), Tab / Shift+Tab (tree order, wrapping) | D-pad, left stick past `UiTheme.StickThreshold` |
| Activate | Enter, keypad Enter, Space | A |
| Back | Escape | B |
| Adjust a focused slider | Left / Right | D-pad left / right |

Spatial navigation moves to the focusable widget entirely beyond the focused one's edge in that direction that is
nearest, weighting distance across the direction four times (so the widget straight below wins over a nearer one off to
the side); nothing beyond the edge means no move (no wrap). Held directions and editing keys repeat after
`UiTheme.RepeatDelay` every `UiTheme.RepeatInterval`, timed with the frame's delta, so repeats are deterministic under a
fixed-step clock. With `UiOptions.AutoFocus` (the default) the first focusable widget takes the focus whenever nothing
has it (at startup, after a screen change), so a gamepad-only device (the R36S) always has something to act on.

## 4. Layout model

`UiStyle` is a flex subset. `default` means: an auto-sized child laid out as a column, stretched on the cross axis.

- **Direction** (`Column` default, `Row`): the main axis of a container. `Row()` and `Column()` set it.
- **Size**: `Width`, `Height` (0: from the content, or from the parent when stretched or grown), `MinWidth`,
  `MinHeight`, `MaxWidth`, `MaxHeight` (0: none). Maximums are applied before minimums (a minimum wins).
- **Grow**: the share of the container's free main-axis space. Distribution respects each child's maximum: a child that
  reaches it keeps it and the rest is shared again among the others.
- **Padding** (null: the theme's panel padding for panels, 0 otherwise) and **Gap** (null: `UiTheme.Gap`; lists use a
  quarter of it).
- **Justify** (`Start`, `Center`, `End`, `SpaceBetween`, `SpaceAround`): the free main-axis space left when nothing grows.
- **AlignItems** (`Stretch` default, `Start`, `Center`, `End`) and **AlignSelf** (`Auto` follows the parent): the cross
  axis. A stretched child with a fixed cross size keeps it; otherwise it spans the line, within its min/max.
- **Wrap**: children that do not fit move to a new line (lines are separated by `Gap`). Measuring a wrapping container
  needs a definite main size (`Width` or `MaxWidth` for a row); when placing, the size the parent gives is used. Scroll
  views do not wrap.
- **Background**: a color, or null for the widget's default.

The algorithm runs once per frame, in two passes over the node array (children always have higher indices than their
parent): measure bottom-up (a reverse walk), where leaves carry their intrinsic size from the widget call (text measured
with the theme's font, plus theme metrics) and containers sum their children along the main axis and take the maximum
across; then place top-down (a forward walk), per line: base sizes, grow distribution, justification, cross alignment,
the scroll offset for scroll views, and each child's clip rectangle (the parent's, intersected with a scroll view's
rectangle). Children larger than their container overflow it; there is no shrink. The root is a container that always
fills the viewport, styled by `Ui.RootStyle`.

## 5. Drawing

`Ui.Draw` walks the tree in order (parents under children, later siblings over earlier ones) inside a sprite batch
segment of its own with default options (pixel space, submission order). Panels and buttons are solid rectangles or
nine-slices (`UiTheme.PanelSkin`, `ButtonSkin`: `UiNineSlice(texture, border in texels, scale)`, corners kept, edges and
center stretched, tinted with the state color); text goes through `ISpriteBatch.DrawString` with the theme's `IFont` (the
2D renderer's FontStashSharp fonts, whose layouts are cached per string, so no text path was added to
`Ion.Extensions.Rendering2D`); each scroll view's content is drawn in a nested segment whose `SpriteBatchOptions.Scissor`
is its clip rectangle, and nodes entirely outside their clip are not submitted. The focused widget gets an outline
(`FocusOutline`, `FocusThickness`), a scroll view with overflow a position indicator.

## 6. The tree contract (`IUiTree`)

`IUiTree` (in the dependency-free abstractions assembly; plain structs and strings) is what the remote protocol exposes as
`ui.tree`, `ui.click` and friends, and what the sample's tests use to drive the menu without pixels.

**Nodes.** `Count`, the indexer and `Snapshot()` give the nodes of the last frame in pre-order (parents before children,
siblings in call order), each a `UiNodeInfo`:

| Field | Meaning |
|---|---|
| `Path` | segments joined with `/` (below) |
| `Kind` | `Panel`, `Row`, `Column`, `ScrollView`, `Label`, `Button`, `Toggle`, `Slider`, `TextInput`, `List`, `ListItem`, `Spacer` |
| `Text` | the caption or text shown |
| `Rect` | the layout rectangle in window pixels (`UiRect`), not clipped |
| `Enabled`, `Focusable`, `Focused` | state |
| `Visible` | false when scrolled entirely out of an enclosing scroll view |
| `Value` | toggle `true`/`false`; slider the number (invariant culture); text input the text; list the selected item's text; list item `true` when selected; scroll view the offset |
| `Depth`, `Parent` | nesting depth (0 for top-level nodes) and the parent's index (-1 at the top) |

**Paths.** A node's segment is its explicit key, else its caption (buttons, toggles, sliders, text inputs, lists, list
items; `/` becomes `_`), else its kind in lower case (`panel`, `row`, `column`, `scroll`, `label`, `list`, `item`,
`spacer`). A segment repeated among siblings gets `#2`, `#3`, ... in call order. Example: `options/Difficulty/Hard`.
Paths are stable as long as the same calls are made: the path strings are cached per widget (the same instances every
frame), `Version` changes only when the sequence of paths changes, and a label's changing text does not change its path
(labels are named by key or `label`).

**Commands.** `Click(path)`, `SetValue(path, value)`, `Focus(path)`, `Type(path, text)` and `Back()` are thread-safe. Each
is checked against the published tree when called and returns false, queuing nothing, when the path does not exist, the
node is disabled or the command does not apply (a click on a label, a toggle value that is not `true`/`false`, a slider
value that is not a number). Accepted commands are queued and applied in order at the start of the next frame's Update,
before input and before any widget call, exactly as the equivalent input would be: a clicked button returns true from its
call in that frame, a set toggle value is written through its `ref bool`, typed text is inserted at the caret. Clicks
also move the focus. The effect is visible in the tree published at the end of that frame; a screen change it causes
shows one frame later. Several clicks on one button in one frame count once. `FocusedPath` and `Frame` complete the
picture; `Snapshot()` is the call to use from another thread.

| Command | Applies to |
|---|---|
| `Click` | `Button` (clicked), `Toggle` (flipped), `ListItem` (selected), `TextInput` (focused, editing) |
| `SetValue` | `Toggle` (`true`/`false`), `Slider` (a number, clamped and snapped), `TextInput` (the text, cut to its maximum length), `List` (an item's text or its index) |
| `Focus` | any focusable node |
| `Type` | `TextInput` (focused, editing, text inserted at the caret) |
| `Back` | the frame reports `Ui.BackPressed` (or a text input being edited stops editing) |

## 7. Theme

`UiTheme` is a struct: `UiTheme.Default with { Font = font, LabelWidth = 140 }`.

- **Font**: `IFont` (from `IFontSet.CreateStyle(size)`). Without one text is not drawn and is measured as
  `FallbackFontSize` pixels tall and half that wide per character, so layout, hit testing and the tree still work.
- **Colors**: `Text`, `TextDisabled`, `PanelBackground`, `Button`, `ButtonHover`, `ButtonPressed`, `ButtonDisabled`,
  `Accent` (checked toggle, slider fill, caret), `Track`, `Thumb`, `InputBackground`, `ListItemHover`,
  `ListItemSelected`, `FocusOutline`, `ScrollBar`.
- **Metrics**: `PanelPadding`, `Gap`, `ControlHeight` (buttons, toggles, sliders, inputs, list items), `ButtonPadding`,
  `ToggleSize`, `SliderWidth`, `ThumbWidth`, `ValueWidth`, `InputWidth`, `LabelWidth` (0: captions sized to their text;
  set it so a column of sliders and inputs lines up), `FocusThickness`, `ScrollBarWidth`, `ScrollSpeed`.
- **Skins**: `PanelSkin`, `ButtonSkin` (`UiNineSlice?`).
- **Input timing**: `RepeatDelay`, `RepeatInterval`, `StickThreshold`.

## 8. Performance

No allocation per frame in steady state (a test runs 200 frames of a screen with every widget kind, focus moves and
scrolling done during warm-up, and asserts 0 bytes): nodes live in two arrays that swap every frame (current and
retained), per-widget state is created when a widget first appears and dropped after 120 unseen frames once states
outnumber widgets, and paths, measured text and formatted values are cached. `UiBenchmarks` measures a 500-widget screen
(build, layout, tree publication, and the draw submission into the 2D renderer's sprite batch): 53 us for the build,
layout and publication and 89 us with the draw submission on the 2.1 GHz Xeon VM of the roadmap's measurements
(`--job short`), 0 B per frame.

## 9. Not done

Keyboard text selection, clipboard and IME composition display; multi-line text and text wrapping; flex shrink; nested
scroll views scrolling each other into view (only the nearest scroll view follows the focus); touch gestures (drag to
scroll); right-to-left text; per-widget style overrides beyond `UiStyle.Background` and the theme; a separate Dear ImGui
debug overlay (roadmap 4.12 keeps it as a different module).
