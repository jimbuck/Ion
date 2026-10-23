---
title: Layout and styling
description: Position UI with UiStyle's flex-style layout (direction, sizes, grow, gap, justify, align, wrap) and style it with UiTheme, fonts and nine-slice skins.
sidebar:
  order: 3
---

Every widget method takes an optional `UiStyle` that controls its layout, and the whole UI shares one `UiTheme` that
controls its look. Layout is a subset of CSS flexbox, computed once per frame at the end of Update.

```csharp
ui.RootStyle = new UiStyle { Justify = UiJustify.Center, AlignItems = UiAlign.Center };   // center everything

using (ui.Panel("dialog", new UiStyle { Width = 420, Gap = 12 }))
{
	ui.Label("Save changes?", scale: 1.3f);
	using (ui.Row(style: new UiStyle { Gap = 8, Justify = UiJustify.End }))
	{
		if (ui.Button("Discard")) Discard();
		if (ui.Button("Save")) Save();
	}
}
```

## The layout model

- The **root** is a column that always fills the viewport (the window). Style it with `ui.RootStyle`. By default its
  children are at the top left and stretched to its width.
- Every **container** (panel, row, column, scroll view, list) lays out its children along its **main axis**: vertical
  for a column (the default), horizontal for a row. The other axis is the **cross axis**.
- A **leaf** widget measures itself from its content: text measured with the theme's font, plus theme metrics such as
  `ControlHeight`, `ButtonPadding` and `SliderWidth`.
- `default(UiStyle)` means: an auto-sized child laid out as a column, stretched on the cross axis.

The algorithm runs in two passes over the node array:

1. **Measure**, bottom-up: leaves carry their intrinsic size; containers sum their children along the main axis
   (plus gaps and padding) and take the largest child across. Fixed, minimum and maximum sizes are applied.
2. **Place**, top-down, line by line: base sizes, then free main-axis space shared by `Grow`, then `Justify` if nothing
   grew, then cross-axis alignment, the scroll offset of scroll views, and each child's clip rectangle.

Children larger than their container overflow it: there is no shrinking. Put overflowing content in a `ScrollView`.

## UiStyle reference

`UiStyle` is a mutable struct; set only what you need with an object initializer.

| Property | Type | Default | Meaning |
|---|---|---|---|
| `Direction` | `UiDirection` | `Column` | The main axis of a container's children. Ignored by `Row` and `Column`, which set it. |
| `Width`, `Height` | `float` | `0` | A fixed size in pixels. 0 sizes from the content, or from the parent when stretched or grown. |
| `MinWidth`, `MinHeight` | `float` | `0` | Minimum size. |
| `MaxWidth`, `MaxHeight` | `float` | `0` | Maximum size; 0 for none. Maximums are applied before minimums, so a minimum wins. |
| `Grow` | `float` | `0` | The share of the container's free main-axis space this node takes. |
| `Padding` | `UiThickness?` | null | Space inside the edges. Null: the theme's `PanelPadding` for panels, 0 otherwise. |
| `Gap` | `float?` | null | Space between children and between wrapped lines. Null: `UiTheme.Gap` (a quarter of it in lists). |
| `Justify` | `UiJustify` | `Start` | Placement of children along the main axis when nothing grows. |
| `AlignItems` | `UiAlign` | `Stretch` | Placement of children on the cross axis. |
| `AlignSelf` | `UiAlignSelf` | `Auto` | This node's cross-axis placement, overriding its container's `AlignItems`. |
| `Wrap` | `bool` | `false` | Move children that do not fit onto new lines. |
| `Background` | `Color?` | null | A background color. Null: the widget's default (the panel color for panels, none for rows and columns). |

### Sizes

```csharp
new UiStyle { Width = 300 }                        // exactly 300 px wide
new UiStyle { MinWidth = 200, MaxWidth = 480 }     // from the content, clamped
new UiStyle { Height = 48 }                        // a taller button
```

A stretched child with a fixed cross size keeps it; otherwise it spans the container's cross size (or the wrapped
line's), within its minimum and maximum.

### Grow

`Grow` shares the free space along the main axis in proportion to each child's factor. Distribution respects each
child's maximum: a child that reaches it keeps it, and the rest is shared again among the others.

```csharp
using (ui.Row("split", new UiStyle { Width = 600, Height = 300 }))
{
	using (ui.Column("sidebar", new UiStyle { Grow = 1, MaxWidth = 180 })) { /* ... */ }
	using (ui.Column("content", new UiStyle { Grow = 3 })) { /* ... */ }
}
```

`ui.Spacer()` is a node with `Grow = 1`, the quickest way to push the following siblings to the far end.

### Justify

`UiJustify` places children along the main axis when they do not fill it and nothing grows:

| Value | Placement |
|---|---|
| `Start` | At the start (the default). |
| `Center` | Centered. |
| `End` | At the end. |
| `SpaceBetween` | Free space between children; the first and last touch the edges. |
| `SpaceAround` | Free space around children: half a share before the first and after the last. |

### Align

`UiAlign` (for `AlignItems`) and `UiAlignSelf` (for `AlignSelf`, which adds `Auto` to follow the parent):

| Value | Placement on the cross axis |
|---|---|
| `Stretch` | Span the container (or line), within min/max. The default. |
| `Start` | Left (in a column) or top (in a row). |
| `Center` | Centered. |
| `End` | Right or bottom. |

```csharp
// A column of buttons that keep their natural width, centered.
using (ui.Column(style: new UiStyle { AlignItems = UiAlign.Center }))
{
	ui.Button("Resume");
	ui.Button("Settings");
	ui.Label("v0.3", style: new UiStyle { AlignSelf = UiAlignSelf.End });
}
```

### Padding and gap

`UiThickness` holds four distances. It converts from a single `float`, and has constructors for all four sides, for
horizontal and vertical, and for one value:

```csharp
new UiStyle { Padding = 24 }                                  // all sides
new UiStyle { Padding = new UiThickness(24, 12) }             // horizontal 24, vertical 12
new UiStyle { Padding = new UiThickness(8, 16, 8, 0), Gap = 4 }
```

### Wrap

With `Wrap = true`, children that do not fit move to a new line, lines separated by `Gap`. Measuring a wrapping
container needs a definite main size: `Width` or `MaxWidth` for a row (`Height` or `MaxHeight` for a column). When
placing, the size the parent gives is used. Scroll views never wrap.

```csharp
using (ui.Row("tags", new UiStyle { Width = 320, Gap = 6, Wrap = true, AlignItems = UiAlign.Start }))
{
	foreach (var tag in _tags) ui.Button(tag);
}
```

## Themes

`UiTheme` is a struct holding the font, colors, metrics, skins and input timing of every widget. Start from
`UiTheme.Default` and change what you need with `with`. Set it in `Init`, or any time: changes apply from the next
widget call.

```csharp
[Init]
public void Load()
{
	var font = assets.Load<IFontSet>("Bungee-Regular.ttf").CreateStyle(20);
	ui.Theme = UiTheme.Default with
	{
		Font = font,
		LabelWidth = 140,          // line up the captions of sliders and text inputs
		InputWidth = 200,
		Accent = new Color(255, 120, 60, 255),
		FocusOutline = Color.White,
	};
}
```

You can also pass a theme through `builder.AddUi(options => options.Theme = ...)`, though loading a font usually needs
the asset manager, which is easier in an `Init` step.

### Font

`Font` is an `IFont`, created with `IFontSet.CreateStyle(size)` (see [Text](/Ion/rendering/text/)). Without a font,
text is not drawn and is measured as `FallbackFontSize` (16) pixels tall and half that wide per character, so layout,
hit testing and the tree still work. Headless tests rely on this: the null backend's font measures the same way.

### Colors

| Property | Default (RGBA) | Used for |
|---|---|---|
| `Text` | 230, 232, 238, 255 | Text. |
| `TextDisabled` | 120, 124, 136, 255 | Text of disabled widgets. |
| `PanelBackground` | 28, 31, 40, 240 | Panels. |
| `Button` | 56, 62, 80, 255 | Buttons and the toggle box. |
| `ButtonHover` | 76, 84, 108, 255 | A button under the pointer. |
| `ButtonPressed` | 40, 112, 196, 255 | A button held down. |
| `ButtonDisabled` | 42, 45, 54, 255 | A disabled button. |
| `Accent` | 64, 156, 255, 255 | A checked toggle, the filled part of a slider, the caret. |
| `Track` | 16, 18, 24, 255 | Slider track. |
| `Thumb` | 232, 234, 240, 255 | Slider thumb. |
| `InputBackground` | 16, 18, 24, 255 | Text fields. |
| `ListItemHover` | 58, 64, 82, 255 | A list item under the pointer. |
| `ListItemSelected` | 36, 86, 150, 255 | The selected list item. |
| `FocusOutline` | 255, 196, 60, 255 | The outline around the focused widget. |
| `ScrollBar` | 255, 255, 255, 90 | A scroll view's position indicator. |

### Metrics

| Property | Default | Meaning |
|---|---|---|
| `PanelPadding` | 16 | Default padding of panels. |
| `Gap` | 8 | Default gap between children. |
| `ControlHeight` | 36 | Minimum height of buttons, toggles, sliders, text inputs and list items. |
| `ButtonPadding` | 16 | Horizontal padding around button and list item text. |
| `ToggleSize` | 20 | Side of a toggle's box. |
| `SliderWidth` | 160 | Minimum width of a slider's track. |
| `ThumbWidth` | 10 | Width of a slider's thumb. |
| `ValueWidth` | 56 | Width reserved for a slider's value. |
| `InputWidth` | 180 | Minimum width of a text field. |
| `LabelWidth` | 0 | Width reserved for slider and text input captions; 0 sizes each to its text. |
| `FocusThickness` | 2 | Thickness of the focus outline. |
| `ScrollBarWidth` | 4 | Width of the scroll indicator. |
| `ScrollSpeed` | 40 | Pixels scrolled per wheel notch. |
| `FallbackFontSize` | 16 | Text size used for measuring when `Font` is null. |

The input timing properties (`RepeatDelay` 0.4 s, `RepeatInterval` 0.08 s, `StickThreshold` 0.5) are described in
[Focus navigation](/Ion/interaction/ui/focus-navigation/#repeat).

### Per-widget overrides

The theme applies to every widget. The only per-widget visual override is `UiStyle.Background` (panels, rows, columns,
scroll views, lists and buttons), and `Label`'s `color` and `scale` arguments:

```csharp
using (ui.Panel("warning", new UiStyle { Background = new Color(120, 30, 30, 230) }))
{
	ui.Label("Unsaved changes", color: new Color(255, 220, 220, 255));
	if (ui.Button("Quit anyway", style: new UiStyle { Background = new Color(180, 40, 40, 255) })) Quit();
}
```

The theme is read when widgets measure themselves and again when the frame is drawn in Render, so switching themes in
the middle of a frame does not give two looks: everything is drawn with the theme set at draw time. To give one screen
a different look, set `ui.Theme` when you switch to that screen, not around individual widgets.

## Nine-slice skins

`PanelSkin` and `ButtonSkin` replace the solid rectangles of panels and buttons with a textured nine-slice:
the texture is cut by a border into corners that keep their size, edges that stretch along one axis, and a center that
stretches both ways. The state color (panel background, button hover and so on) tints it.

```csharp
var frame = assets.Load<ITexture2D>("ui/frame.png");
ui.Theme = ui.Theme with
{
	PanelSkin = new UiNineSlice(frame, Border: new UiThickness(12), Scale: 2f),
	ButtonSkin = new UiNineSlice(assets.Load<ITexture2D>("ui/button.png"), new UiThickness(6, 6, 6, 8)),
	PanelBackground = Color.White,   // untinted
};
```

| `UiNineSlice` parameter | Meaning |
|---|---|
| `Texture` | The `ITexture2D` (premultiplied, as the 2D renderer loads it). |
| `Border` | Left, top, right and bottom border widths in texels. |
| `Scale` | On-screen size of one border texel (default 1). |

`Ui.DrawNineSlice(spriteBatch, skin, rect, color)` draws one yourself.

## Drawing order

The UI draws at Render order 700 (`StageOrder.Ui`), after your own Render steps at the default order, in its own sprite
batch segment in pixel space with submission order. Within the UI, parents draw under children and later siblings over
earlier ones. Scroll views draw their content in a nested segment with a scissor rectangle; nodes entirely outside
their clip are not submitted. The focused widget gets an outline (`FocusOutline`, `FocusThickness`) and a scroll view
with overflow a position indicator.

## Resolution and scaling

Layout is in window pixels, and the root fills the window. There is no UI scale factor or virtual resolution: on a
small screen (the R36S's 640x480) or a high-DPI one, pick sizes and a font size for the target, for example from
`window.Size` at `Init`.

## See also

- [Widgets](/Ion/interaction/ui/widgets/)
- [UI overview](/Ion/interaction/ui/overview/)
- [Text](/Ion/rendering/text/) and [Sprites](/Ion/rendering/sprites/)
- [Menu example](/Ion/examples/menu/)
