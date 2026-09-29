---
title: Desktop (Windows, macOS, Linux)
description: Run and ship Ion games on Windows, macOS and Linux, with the windowing, graphics and audio each desktop uses and the system packages it needs.
sidebar:
  order: 2
---

On Windows, macOS and Linux an Ion game runs with `dotnet run` during development and ships as a NativeAOT executable
built with a publishing preset. This page covers what each desktop needs, which native pieces the engine loads, and
the settings you are most likely to change.

## Run a game

```bash
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS -- --Ion:Graphics:PreferredBackend=OpenGLES
dotnet run --project Ion.Examples/Ion.Examples.Breakout.ECS -- --Ion:Window:Platform=Sdl --Ion:Window:Fullscreen=true
```

Anything after `--` is configuration (see [Services and configuration](/Ion/concepts/services-and-configuration/)), so
every setting on this page can also live in `appsettings.json`.

## What the desktop stack is made of

`builder.AddIon()` registers the windowed stack unless the game runs headless:

| Layer | Package | Desktop behaviour |
|---|---|---|
| Window and input | `Ion.Extensions.Windowing.SilkNet` | A GLFW window by default (`Ion:Window:Platform` = `Auto` or `Glfw`), or SDL2 (`Sdl`). Created at Init and pumped by Ion's loop in `First`; keyboard, mouse, text and gamepads feed the shared `InputTracker`. |
| Graphics | `Ion.Extensions.Graphics.Vulkan`, `Ion.Extensions.Graphics.GLES` | `Auto` tries Vulkan first, then OpenGL ES (on Linux arm64 the order is reversed). |
| 2D and 3D rendering | `Ion.Extensions.Rendering2D`, `Ion.Extensions.Rendering3D` | Written once against the RHI; both backends render the same pixels. |
| Audio | `Ion.Extensions.Audio` | OpenAL Soft (binaries from `Silk.NET.OpenAL.Soft.Native`); falls back to the null output with a warning when no device is available. |

Games depend only on the interfaces (`IWindow`, `IInputState`, `ISpriteBatch`, `IAudioManager`, the asset
interfaces), so the same systems run windowed, headless and on every platform.

## Per-OS notes

### Windows

- Vulkan comes with current GPU drivers. If Vulkan cannot start, `Auto` moves on to OpenGL ES.
- The game loop raises the Windows timer resolution to 1 ms (`timeBeginPeriod`) while it runs and paces frames with a
  sleep plus a short spin, so `Ion:MaxFPS` holds steadily. With `Ion:VSync = true` the loop does no pacing of its own.
- Publish on Windows: NativeAOT does not cross-compile between operating systems.

```powershell
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=win-x64
```

### macOS

- Vulkan runs over **MoltenVK**. The Vulkan device enables `VK_KHR_portability_enumeration` and
  `VK_KHR_portability_subset` automatically when the loader reports them, but the game must ship
  `libMoltenVK.dylib`, for example through the `Silk.NET.MoltenVK.Native` package:

  ```xml
  <ItemGroup>
    <PackageReference Include="Silk.NET.MoltenVK.Native" Version="2.23.0" />
  </ItemGroup>
  ```

- Audio uses OpenAL Soft from the NuGet natives, or the system `OpenAL.framework`.
- Publish on a Mac (`osx-arm64` or `osx-x64`). A publish from Linux compiles the whole program to a Mach-O object and
  then fails at the link, which needs Apple's linker and the macOS SDK.

### Linux

- Vulkan needs a Vulkan driver (your GPU's, or Mesa's `mesa-vulkan-drivers`, which includes the lavapipe CPU driver).
  OpenGL ES needs EGL and GLES from Mesa (`libegl1`, `libegl-mesa0`).
- GLFW and SDL load the X11, xkb, Wayland and GL client libraries at run time. Without them neither platform is
  applicable and no window opens. The package list CI installs on Ubuntu is a good reference:

  ```bash
  sudo apt-get install -y mesa-vulkan-drivers libvulkan1 xvfb \
    libx11-6 libx11-xcb1 libxcursor1 libxi6 libxinerama1 libxrandr2 libxrender1 libxext6 libxfixes3 libxss1 \
    libxxf86vm1 libxkbcommon0 libwayland-client0 libwayland-cursor0 libwayland-egl1 libdecor-0-0 \
    libgl1 libglx-mesa0 libegl1 libegl-mesa0 libgl1-mesa-dri libgbm1 libdrm2
  ```

- The windowing module finds the GLFW and SDL natives from NuGet on distributions Silk.NET's own resolver does not map
  to `linux-x64` (Ubuntu among them): `SilkNativeLibraries.EnsureResolver()` adds the app-local
  `runtimes/{rid}/native` folders to the search, so you do not need the system `libglfw3` or `libsdl2` packages.
- NativeAOT needs `clang` and `zlib1g-dev` on the build machine.

```bash
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=linux-x64
```

:::tip[No display?]
On a server or in a container, run with `--Ion:Headless=true`. Add `--Ion:Headless:Render=true` to render offscreen
on lavapipe or llvmpipe (no X server needed), or wrap windowed runs in `xvfb-run -a`.
:::

## Window settings

Bound from `Ion:Window` (`WindowConfig`):

| Key | Type | Default | Notes |
|---|---|---|---|
| `Ion:Window:Width`, `Ion:Window:Height` | int | 960 x 540 | The window size in pixels (the Silk.NET window uses 960 x 540 when unset). |
| `Ion:Window:WindowX`, `Ion:Window:WindowY` | int | platform default | The window position (used only when both are set). |
| `Ion:Window:WindowState` | `WindowState` | `Normal` | `Normal`, `FullScreen`, `Maximized`, `Minimized`, `BorderlessFullScreen` or `Hidden`. `FullScreen` is exclusive, at the window size; `BorderlessFullScreen` is borderless at the monitor size. |
| `Ion:Window:Fullscreen` | bool | `false` | Shorthand for `WindowState = FullScreen`. |
| `Ion:Window:Resizable` | bool | `true` | Whether the user can resize the window. |
| `Ion:Window:ShowCursor` | bool | `true` | Whether the mouse cursor is visible. |
| `Ion:Window:Platform` | `WindowPlatform` | `Auto` | `Auto` (GLFW on desktop), `Glfw` or `Sdl`. |

Systems change the window at run time through `IWindow` (for example `window.Size`, `window.IsResizable`,
`window.IsFullscreen`, `window.IsMouseGrabbed`, `window.IsCursorVisible`), as the Breakout samples do.

## Graphics settings

Bound from `Ion:Graphics` (`GraphicsConfig`):

| Key | Default | Notes |
|---|---|---|
| `Ion:Graphics:PreferredBackend` | `Auto` | `Auto`, `Vulkan` or `OpenGLES`. |
| `Ion:Graphics:VSync` | `false` | FIFO presentation; off uses mailbox where supported, else immediate. |
| `Ion:Graphics:FramesInFlight` | `2` | 2 or 3 frames the CPU may record ahead of the GPU. |
| `Ion:Graphics:Validation` | Debug builds: on | Vulkan validation layer and debug messenger, when the Khronos layer is installed. |
| `Ion:Graphics:Adapter` | none | Picks the first GPU whose name contains this text. Otherwise discrete, then integrated. |
| `Ion:Graphics:RetainLastFrame` | `false` | Copies each presented frame so windowed screenshots work (costs a full-frame copy). |
| `Ion:Graphics:ClearColorHex` | black | The clear color as `RGB`, `RGBA`, `RRGGBB` or `RRGGBBAA`. |

The full list, with the frame pacing keys (`Ion:MaxFPS`, `Ion:VSync`, `Ion:FixedUpdateRate`), is in the
[configuration reference](/Ion/reference/configuration/).

## A per-machine configuration file

`IonApplication.CreateBuilder` uses the host's `appsettings.{Environment}.json` convention, so a machine-specific file
loads over `appsettings.json` when `DOTNET_ENVIRONMENT` names it. This is how the R36S launcher selects its profile,
and it works the same on desktop:

```json title="appsettings.laptop.json"
{
  "Ion": {
    "Graphics": { "PreferredBackend": "OpenGLES", "VSync": true },
    "Window": { "Width": 1280, "Height": 720 }
  }
}
```

```bash
DOTNET_ENVIRONMENT=laptop dotnet run --project MyGame
```

Remember to copy the file to the output folder (`<Content Include="appsettings.*.json" CopyToOutputDirectory="PreserveNewest" />`).

## Common problems

| Symptom | Cause and fix |
|---|---|
| No window opens on Linux, and the log says neither GLFW nor SDL is applicable | The X11, xkb, Wayland or GL client libraries are missing. Install the package list above. |
| The log says Vulkan could not start and the game runs on OpenGL ES | No Vulkan driver (or an old one). `Auto` did the right thing; install `mesa-vulkan-drivers` or your GPU's driver to get Vulkan back, or set `Ion:Graphics:PreferredBackend=OpenGLES` to stop probing. |
| A warning that the audio output failed and the null output replaced it | No audio device (a server, a container, a headless VM). Sound is mixed but not heard; nothing else changes. |
| `dotnet publish -p:IonTarget=win-x64` fails on Linux with "Cross-OS native compilation is not supported" | NativeAOT does not cross-compile between operating systems. Publish on Windows (or macOS for `osx-*`). |
| A NativeAOT publish on Linux fails at the link step | `clang` or `zlib1g-dev` is missing on the build machine. |
| `appsettings.laptop.json` is ignored | The file is not copied to the output folder, or `DOTNET_ENVIRONMENT` is not set to `laptop`. |

## See also

- [Platforms overview](/Ion/platforms/overview/): the support matrix.
- [Windowing](/Ion/rendering/windowing/) and [Graphics backends](/Ion/rendering/graphics-backends/).
- [Publishing](/Ion/platforms/publishing/): the `win-*`, `osx-*` and `linux-*` presets.
- [NativeAOT](/Ion/platforms/native-aot/): what the executable contains and why it has no warnings.
