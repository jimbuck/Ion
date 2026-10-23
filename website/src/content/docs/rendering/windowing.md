---
title: Windowing
description: Configure the Silk.NET window (GLFW or SDL), its size, full screen, resizing and cursor, and react to resize, focus and close events.
sidebar:
  order: 3
---

Ion's window comes from `Ion.Extensions.Windowing.SilkNet`: a Silk.NET window on **GLFW** (desktop) or **SDL**
(desktop, Android, iOS, handhelds). It is created by the engine at Init, pumped once per frame by Ion's own game loop,
and exposed to your game as `IWindow`. The same module feeds keyboard, mouse, gamepad and touch input into
`IInputState` (see [Input](/Ion/interaction/input/overview/)).

`AddIon()` registers it for windowed runs. In headless runs (`--headless`) a `NullWindow` with the same interface takes
its place, so code that uses `IWindow` runs unchanged.

## Configuring the window

Window settings are bound from `Ion:Window` into `WindowConfig`. The title comes from `Ion:Title` (on `GameConfig`),
which also names the per-user data folder.

```json title="appsettings.json"
{
	"Ion": {
		"Title": "My Game",
		"Window": {
			"Width": 1280,
			"Height": 720,
			"Resizable": true,
			"WindowState": "Normal",
			"ShowCursor": true,
			"Platform": "Auto"
		}
	}
}
```

| Key (`Ion:Window:...`) | Type | Default | Meaning |
|---|---|---|---|
| `Width`, `Height` | `int?` | 960 x 540 | The initial window size in window coordinates. Also the size of the offscreen target in headless rendering |
| `WindowX`, `WindowY` | `int?` | centered by the platform | The initial position. Both must be set |
| `WindowState` | `WindowState` | `Normal` | `Normal`, `Maximized`, `Minimized`, `Hidden`, `FullScreen` (exclusive, at the window size) or `BorderlessFullScreen` (borderless at the monitor's size) |
| `Fullscreen` | `bool` | `false` | Shorthand: `true` sets `WindowState` to `FullScreen` |
| `Resizable` | `bool` | `true` | Whether the user can resize the window |
| `ShowCursor` | `bool` | `true` | Whether the mouse cursor is visible initially |
| `Platform` | `WindowPlatform` | `Auto` | `Auto`, `Glfw` or `Sdl` (see below) |

Every key works on the command line too:

```bash
dotnet run -- --Ion:Window:Width=640 --Ion:Window:Height=480 --Ion:Window:Fullscreen=true
```

`VSync` is a graphics setting (`Ion:Graphics:VSync`), passed to the window when it is created. See
[Graphics backends](/Ion/rendering/graphics-backends/#graphics-options).

## GLFW or SDL

`Ion:Window:Platform` picks the windowing library:

| Value | Result |
|---|---|
| `Auto` (default) | GLFW on Windows, macOS and Linux; SDL on Android, iOS and tvOS |
| `Glfw` | GLFW. Desktop only |
| `Sdl` | SDL2. Desktop, Android, iOS, and handhelds such as the R36S where no GLFW build exists |

With `Auto`, if GLFW fails to create the window on desktop, Ion logs a warning and retries with SDL.

The platform is registered explicitly, with Silk.NET's reflection-based platform discovery turned off, so the window
works under [NativeAOT](/Ion/platforms/native-aot/).

On Android and iOS, SDL is *view-only*: Ion creates a full-screen view instead of a window. `SilkWindow.IsViewOnly` is
`true`, and the window-only members (title, position, size and state setters, borders) do nothing.

## Using IWindow

Inject `IWindow` into any system:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class TitleSystem(IWindow window, IInputState input)
{
	[Init]
	public void Init(GameTime dt)
	{
		window.Title = "My Game";
		window.Size = new Vector2(1280, 720);
		window.IsCursorVisible = false;
	}

	[Update]
	public void Update(GameTime dt)
	{
		// Alt+Enter toggles full screen.
		if (input.Pressed(Key.Enter, ModifierKeys.Alt)) window.IsFullscreen = !window.IsFullscreen;
	}
}
```

| Member | Meaning |
|---|---|
| `Width`, `Height`, `Size` | The window size in **window coordinates** (what mouse positions are in). Settable |
| `IsClosing`, `IsClosed` | The user asked to close; the window was closed |
| `IsActive` | The window has focus |
| `IsVisible`, `IsMaximized`, `IsMinimized`, `IsFullscreen`, `IsBorderless` | Window state, settable |
| `IsResizable` | Whether the user can resize the window |
| `Title` | The title bar text |
| `IsCursorVisible` | Show or hide the cursor |
| `IsMouseGrabbed` | Capture the mouse: hidden, confined to the window, reporting unbounded movement (for mouse look) |

:::caution[HiDPI]
`IWindow.Size` is in window coordinates. The framebuffer, and therefore the sprite batch's pixel space and the 3D
viewport, is in **pixels**, which is larger on HiDPI displays (macOS Retina, scaled Windows displays). When you need the
render target size, read `IGraphicsFrame.Width` and `Height`, or the concrete window's `SilkWindow.FramebufferSize`
(`IWindowSurface.FramebufferSize`).
:::

## Resize, focus and close events

The window reports through the [event bus](/Ion/concepts/events/):

| Event | When |
|---|---|
| `WindowResizeEvent(Width, Height)` | Once at Init with the initial size, then whenever the size changes |
| `WindowFocusGainedEvent`, `WindowFocusLostEvent` | Focus changes |
| `WindowClosedEvent` | The user closed the window |

A closed window ends the game: the window system turns `WindowClosedEvent` into `ExitGameEvent` at the end of that
frame's Render stage (`StageOrder.WindowClose`). You do not need to handle it yourself.

To lay out for the window size, read resize events from a reader created once:

```csharp
using System.Numerics;

using Ion;
using Ion.Extensions.Graphics;

public sealed class LayoutSystem(IWindow window, IEvents events)
{
	// Create readers once and keep them in a mutable field.
	private EventReader<WindowResizeEvent> _resizes = events.Reader<WindowResizeEvent>();
	private EventReader<WindowFocusLostEvent> _focusLost = events.Reader<WindowFocusLostEvent>();

	public Vector2 Center { get; private set; }
	public bool Paused { get; private set; }

	[Update]
	public void Update(GameTime dt)
	{
		if (_resizes.TryReadLatest(out var resize)) Center = new Vector2(resize.Width, resize.Height) / 2;
		if (_focusLost.Any()) Paused = true;
		_focusLost.Skip();
	}
}
```

Because the first `WindowResizeEvent` is emitted at Init, a reader created in the constructor sees the initial size on
the first frame.

When the window is resized, the graphics backend reconfigures the swapchain at the start of the next frame. While the
window is minimized (a zero-sized framebuffer), `IGraphicsFrame.IsRendering` is false and nothing is drawn; your Render
steps still run, and the sprite batch simply discards the frame.

## Frame timing

The Silk.NET window is never run with its own loop (`IWindow.Run` is not called, and Silk's `FramesPerSecond` and
`UpdatesPerSecond` are 0). Ion's game loop calls `DoEvents()` in the First stage (`StageOrder.Window`) and paces frames
with `Ion:MaxFPS`, or lets the swapchain pace them with VSync. See [The game loop](/Ion/concepts/game-loop/).

## Native libraries

Silk.NET's NuGet packages ship the native GLFW and SDL2 libraries under `runtimes/{rid}/native`. Silk.NET's own
resolver maps distribution-specific runtime identifiers to the portable `linux-x64` folder only for a fixed list of
distributions, which does not include Ubuntu. Without help, a framework-dependent game on Ubuntu without system GLFW
and SDL packages would report every window platform as unavailable.

Ion fixes this with `SilkNativeLibraries.EnsureResolver()`, called automatically before the first platform
registration. It adds a resolver that maps a bare library name to
`{AppContext.BaseDirectory}/runtimes/{rid}/native/{name}` for the process's runtime identifier and its portable
fallbacks (for example `linux-x64`, then `linux`). Under NativeAOT the publish puts the natives next to the executable
and the resolver is a no-op.

On the R36S the launcher swaps the bundled `libSDL2-2.0.so` for the system SDL2, which ArkOS builds with KMSDRM and the
device's controls. See [R36S](/Ion/platforms/r36s/) and [Publishing](/Ion/platforms/publishing/).

## Registering the window by hand

`AddIon()` does this for you. Without it:

```csharp
using Ion.Extensions.Windowing;

builder.Services.AddSilkWindowing(builder.Configuration);   // SilkWindow as IWindow and IWindowSurface, SilkInputState as IInputState
builder.Services.AddRhiGraphics(builder.Configuration);

using var app = builder.Build();
app.UseSilkWindowing();   // window at Init (StageOrder.Window), events pumped in First, input at StageOrder.Input
app.UseRhiGraphics();     // after the window: the device needs the native window
```

The window is destroyed in the Destroy stage at `StageOrder.WindowClose`, after the graphics backend has released its
surface.

## Handheld profile

A full-screen 640x480 SDL window with OpenGL ES and no cursor, as used for the R36S:

```json title="appsettings.r36s.json"
{
	"Ion": {
		"Window": { "Platform": "Sdl", "Fullscreen": true, "Width": 640, "Height": 480, "ShowCursor": false },
		"Graphics": { "PreferredBackend": "OpenGLES", "VSync": true, "FramesInFlight": 2 }
	}
}
```

## See also

- [Graphics backends](/Ion/rendering/graphics-backends/)
- [Input overview](/Ion/interaction/input/overview/) and [Touch](/Ion/interaction/input/touch/)
- [Events](/Ion/concepts/events/)
- [Desktop](/Ion/platforms/desktop/), [Mobile](/Ion/platforms/mobile/) and [R36S](/Ion/platforms/r36s/)
