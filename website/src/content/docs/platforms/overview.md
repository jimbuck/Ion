---
title: Platforms overview
description: Which operating systems and devices Ion targets, the graphics backend and windowing each one uses, and what has been verified in CI versus what is untested on real hardware.
sidebar:
  order: 1
---

Ion games are ordinary .NET 10 console programs, so anything that runs `dotnet run` runs an Ion game during
development. For shipping, Ion publishes a **NativeAOT executable** per platform: one native binary with its native
libraries and assets next to it, and no .NET runtime to install. This page lists the platforms, what each one uses and
how far each has been tested, so you know what to expect before you pick a target.

## Support matrix

| Platform | Publish preset (`IonTarget`) | Graphics (`Auto` order) | Windowing | Status |
|---|---|---|---|---|
| Windows x64 | `win-x64` | Vulkan, then OpenGL ES | GLFW (SDL optional) | Built and tested in CI; NativeAOT publish in an opt-in CI job |
| Windows arm64 | `win-arm64` | Vulkan, then OpenGL ES | GLFW (SDL optional) | Preset only; not built in CI |
| macOS arm64 | `osx-arm64` | Vulkan over MoltenVK, then OpenGL ES | GLFW (SDL optional) | Built and tested in CI; NativeAOT publish in an opt-in CI job |
| macOS x64 | `osx-x64` | Vulkan over MoltenVK, then OpenGL ES | GLFW (SDL optional) | Preset only; not built in CI |
| Linux x64 | `linux-x64` | Vulkan, then OpenGL ES | GLFW (SDL optional) | Built, tested, rendered (lavapipe and llvmpipe) and AOT-published on every pull request |
| Linux arm64 | `linux-arm64` | OpenGL ES, then Vulkan | GLFW (SDL optional) | Cross-compiled from x64 in CI; runs under QEMU; not run on arm64 hardware |
| R36S handheld (ArkOS) | `r36s` | OpenGL ES (forced by the preset) | SDL (KMSDRM) | Published with the ArkOS layout in CI; runs headless under QEMU; not run on the device |
| Android | head project (`IonMobileHeads`) | Vulkan, then OpenGL ES | SDL (`SilkActivity`) | Compiles for `net10.0-android`; not packaged or run on a device |
| iOS | head project (`IonMobileHeads`) | Vulkan over MoltenVK, then OpenGL ES | SDL (`SilkMobile`) | Wired into an opt-in CI job; not run on a device |
| Browser (WebAssembly) | none | none | none | Not supported yet (planned) |

The "Graphics" column is the order `GraphicsBackend.Auto` tries backends in. `GraphicsBackendSelector.AutoOrder()`
puts Vulkan first on desktop and Android and OpenGL ES first on Linux arm64, where the handhelds Ion targets (the R36S:
RK3326, Mali-G31, Panfrost) have a stable OpenGL ES driver and only an experimental Vulkan one. Each backend is
probed once; the first one that can start wins. You can always force one with `Ion:Graphics:PreferredBackend`
(`Vulkan` or `OpenGLES`).

:::note[Reserved backends]
`GraphicsBackend` also has `Direct3D12`, `Metal` and `WebGPU`. They are reserved names: no backend exists for them and
choosing one throws `NotSupportedException`. Apple platforms use Vulkan through MoltenVK instead of Metal.
:::

## What "verified" means here

Ion's CI (`.github/workflows/check-pr.yml`) runs on every pull request to `main`:

| Job | Runner | What it proves |
|---|---|---|
| `build` | `ubuntu-latest`, `windows-latest`, `macos-latest` | The solution builds in Release and the test suite passes on all three operating systems. |
| `build` (Linux only) | `ubuntu-latest` | Mesa lavapipe (a CPU Vulkan driver) and EGL with llvmpipe run the headless rendering and golden-image tests on both backends, with the Khronos validation layer; the Vulkan backend's windowed tests run under Xvfb. |
| `benchmarks` | `ubuntu-latest` | Every benchmark still builds and runs (a dry job, not a performance gate). |
| `aot` | `ubuntu-latest` | NativeAOT publishes of Breakout ECS (`linux-x64` and `r36s`), Quad, Menu, Companion and Breakout Net, failing on any trim or AOT warning from Ion's own code; size and startup are measured. |
| `publish-desktop` | `windows-latest`, `macos-latest` | NativeAOT publish of Breakout ECS with `win-x64` and `osx-arm64`. Runs only when the repository variable `ION_PUBLISH_CI` is `true`. |
| `mobile-android`, `mobile-ios` | `ubuntu-latest`, `macos-15` | The Android APK publish and the iOS simulator build. Run only when the repository variable `ION_MOBILE_CI` is `true`. |

Tests that need a GPU driver or a display are skipped, not failed, when the machine has none, so a green run on
Windows or macOS proves the code and the headless paths, not rendering on a real GPU.

:::caution[Nothing has run on a physical handheld or phone]
As of September 2026 no Ion build has been exercised on real R36S, Android or iOS hardware. The R36S build is measured
under `qemu-aarch64` user-mode emulation, the Android head compiles but has not been packaged, and the iOS head needs
macOS with Xcode. Treat those targets as a starting point that still needs a first run on the device. The details are
on the [R36S](/Ion/platforms/r36s/) and [Mobile](/Ion/platforms/mobile/) pages.
:::

## Measured sizes and startup

Measured by `docs/plans/benchmarks/2026-09-stage7-publish/measure.sh` (Intel Xeon 2.1 GHz VM, .NET SDK 10.0.112,
September 2026). Startup is process start to exit after one headless frame (`--Ion:Headless=true --Ion:Run:Frames=1`),
an upper bound on start to first frame.

| Target | Executable | Shipped (without `.dbg`) | Startup (median of 10) | Where |
|---|---|---|---|---|
| `linux-x64` (Breakout ECS) | 12.6 MB | 16.4 MB | 46 ms | native |
| `r36s` (Breakout ECS) | 11.6 MB | 15.0 MB | 611 ms | `qemu-aarch64` user mode on x64 (emulated, not the device) |

The acceptance line is under 30 MB and under 300 ms on desktop, and under one second on the handheld. The CI lane
fails only when a desktop executable passes 30 MB. See [Publishing](/Ion/platforms/publishing/) for every sample's
numbers.

## Choosing what to run on

Most of the engine is platform-neutral. What changes per platform is a small set of registrations that `AddIon`
chooses for you from configuration:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddSystem<GameSystem>();

using var game = builder.Build();
game.UseIon().UseSystem<GameSystem>();
game.Run();
```

- **Windowed** (the default): the Silk.NET window (`Ion:Window:Platform`: `Auto`, `Glfw` or `Sdl`), the RHI backend
  picked from `Ion:Graphics:PreferredBackend`, the 2D renderer and OpenAL audio.
- **Headless** (`--Ion:Headless=true`, or `--headless`): no window, GPU or audio device. Add
  `--Ion:Headless:Render=true` (or `--headless-render`) to render into an offscreen target with Vulkan or OpenGL ES.
  This is how CI and the tests run every sample, and how a dedicated game server runs.

The same program runs on every platform in the matrix; per-platform settings live in configuration files such as
`appsettings.r36s.json`, which the `r36s` preset writes for you.

```json title="appsettings.json"
{
  "Ion": {
    "Window": { "Platform": "Auto", "Width": 1280, "Height": 720 },
    "Graphics": { "PreferredBackend": "Auto", "VSync": true }
  }
}
```

## The browser

Running in a browser is not supported. It was out of scope for the Stage 7 platform work, and what remains is listed
in the roadmap (section 4.11): a `net10.0-browser` target (the `wasm-tools` workload, single-threaded) for the core,
renderers and ECS; a WebGPU RHI backend; a canvas windowing and input shim in place of SDL; `requestAnimationFrame`
driving the game loop; the managed Box2D port behind the physics module (the native one does not build for
WebAssembly); a publish preset and a CI lane. The `GraphicsBackend.WebGPU` value is reserved for it.

If you need a browser today, the [web server module](/Ion/networking/overview/) can serve a companion page that talks
to a native game over HTTP and WebSockets, as the [Companion sample](/Ion/examples/companion/) does.

## See also

- [Desktop](/Ion/platforms/desktop/): Windows, macOS and Linux requirements and settings.
- [Mobile](/Ion/platforms/mobile/): the Android and iOS heads and touch input.
- [R36S](/Ion/platforms/r36s/): the Linux arm64 handheld profile.
- [Publishing](/Ion/platforms/publishing/): the `IonTarget` presets and the publish layout.
- [NativeAOT](/Ion/platforms/native-aot/): trimming, warnings and the CI AOT lane.
- [Graphics backends](/Ion/rendering/graphics-backends/): Vulkan, OpenGL ES and backend selection.
