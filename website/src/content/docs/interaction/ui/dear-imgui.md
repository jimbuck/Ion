---
title: Dear ImGui
description: Debug windows, inspectors and editors with Dear ImGui (Hexa.NET.ImGui) on Ion's input, clock and RHI, drawn on top of the game on every backend.
sidebar:
  order: 6
---

`Ion.Extensions.ImGui` runs [Dear ImGui](https://github.com/ocornut/imgui) inside an Ion game. It is the tool for
debug windows, entity inspectors, tweak panels and in-game editors. For the game's own menus, which need gamepad focus
navigation, skins and an inspectable tree, use [Ion's UI](/Ion/interaction/ui/overview/).

The bindings are [Hexa.NET.ImGui](https://github.com/HexaEngine/Hexa.NET.ImGui) (MIT, Dear ImGui 1.92), which ship the
native `cimgui` library for Windows, Linux and macOS (x64 and arm64) and Android. Ion feeds it input and time and draws
its output with a pipeline of its own on the RHI, so it works on Vulkan, OpenGL ES and headless.

## Setup

```csharp
builder.Services.AddImGui(builder.Configuration);   // ImGuiHost, options from Ion:ImGui
app.UseImGui();                                     // with UseIon
```

Then call `ImGui.*` from any FixedUpdate, Update or Render step:

```csharp
using Hexa.NET.ImGui;

public sealed class DebugPanel(World world, ImGuiHost imgui)
{
    private float _speed = 1f;

    [Update]
    public void Draw(GameTime dt)
    {
        ImGui.Begin("Debug");
        ImGui.Text($"Entities: {world.Size}");
        ImGui.SliderFloat("Speed", ref _speed, 0f, 4f);
        if (ImGui.Button("Spawn")) Spawn();
        ImGui.End();
    }

    [Update(Order = 10)]
    public void Shoot(GameTime dt)
    {
        if (imgui.WantCaptureMouse) return;   // the click was on a window, not on the game
        // ...
    }
}
```

## The frame

| Stage | Order | What happens |
|---|---|---|
| Init | `StageOrder.ImGui` (-865) | The renderer is created on the graphics device. |
| First | `StageOrder.ImGui` (-865) | After input: Ion's mouse, keys, modifiers and text go to ImGui, then `ImGui.NewFrame()`. |
| FixedUpdate, Update, Render | any | Your `ImGui.*` calls. |
| End of Render | the scope of `StageOrder.ImGui` | `ImGui.Render()`, the textures ImGui asked for are created or updated, and the draw data is drawn over the frame. |
| Destroy | `ImGuiSystem.DestroyOrder` | The GPU resources are released, before the device. |

The Render scope opens before the 3D renderer's and the sprite batch's, so it closes after them: ImGui is drawn last, on
top of the 3D scene, the sprites, Ion's UI and the metrics overlay.

## Input

Ion's `IInputState` is translated every frame: mouse position, buttons (left, right, middle and two extra) and wheel,
the keyboard (letters, digits, function keys, arrows, editing keys, punctuation, the keypad), the Ctrl, Shift, Alt and
Super modifiers, and typed text (`IInputState.Text`, so IME and on-screen keyboard text arrive too). A press and a
release within one frame (a quick tap) still reach ImGui as two events.

ImGui never takes input from the game by itself. Read `ImGuiHost` to decide whether the game should react:

| Property | True when |
|---|---|
| `WantCaptureMouse` | The mouse is over an ImGui window or dragging one of its widgets. |
| `WantCaptureKeyboard` | An ImGui widget has the keyboard focus. |
| `WantTextInput` | An ImGui text field is active (show an on-screen keyboard on a device without one). |

They describe the last finished frame. Gamepad navigation and touch are not mapped yet.

## Textures

ImGui creates its own textures (the font atlas, which in 1.92 grows as new glyphs and sizes are used). The renderer
creates, updates and destroys them on request (`ImGuiBackendFlags.RendererHasTextures`). To show one of yours, register
an RHI texture and pass the id to `ImGui.Image`:

```csharp
var id = imgui.Register(renderTarget);           // an ITexture of the RHI
ImGui.Image(new ImTextureRef(null, id), new Vector2(256, 144));
imgui.Unregister(id);                            // before disposing the texture
```

A texture loaded by the 2D renderer is a `SpriteTexture`, whose `Texture` property is the RHI texture.

## Options

`Ion:ImGui` (or the `configure` callback of `AddImGui`):

| Option | Default | Does |
|---|---|---|
| `Enabled` | `true` | Off: the frames still run, so ImGui calls stay valid, but nothing is drawn and no input reaches ImGui. Bind it to a key to hide the debug tools. |
| `KeyboardNavigation` | `true` | `ImGuiConfigFlags.NavEnableKeyboard`. |
| `IniFile` | `null` | Where window positions are saved. Null saves nothing, so no `imgui.ini` is written next to the game. |
| `Style` | `Dark` | `Dark`, `Light` or `Classic`. |
| `Scale` | `1` | Scales every size and the font, for dense screens and handhelds. |

## Headless and tests

Without a GPU (`--Ion:Headless=true`, the default test host) the frames run normally and widgets respond to input, but
nothing is drawn (`ImGuiHost.IsRendering` is false). Tests drive ImGui through the test host's scripted input and read
widget state from the step that drew it. With headless rendering (`WithRendering`) the screenshot shows ImGui over the
game.

Dear ImGui's current context is one per process. Each application's context is made current at the start of each
ImGui step, which is enough for one game. Tests that run several hosts at once must not run in parallel: put them in
one xunit collection with `DisableParallelization = true`.

`Ion.Extensions.ImGui.Tests` covers a click through Ion's input pressing a button and capturing the mouse, typed text
and Backspace in a focused text field, the disabled option, and, on Vulkan and OpenGL ES, ImGui drawn over a sprite
filling the frame.
