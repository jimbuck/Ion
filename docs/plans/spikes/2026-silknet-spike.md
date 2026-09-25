# Spike: Silk.NET windowing and Vulkan under Ion's loop (Stage 4)

Date: 2026-09-25. Time box: one session. Code: [`silknet-spike/`](silknet-spike/) (a standalone console app, not in `Ion.sln`).

Question: can `Silk.NET.Windowing` be created, pumped and torn down by hand (never `IWindow.Run`), with the platform registered
explicitly (no reflection-based discovery), give a Vulkan surface on `Silk.NET.Vulkan`, and does all of it publish with
NativeAOT? And does the headless (surface-less) path work for CI?

Answer: yes on all counts, on Linux under `xvfb-run` with Mesa lavapipe. Go.

## Environment

| Item | Version |
|---|---|
| .NET SDK | 10.0.112 (net10.0) |
| Silk.NET (all packages below) | 2.23.0 |
| Vulkan driver | Mesa lavapipe (`llvmpipe (LLVM 20.1.2, 256 bits)`), Vulkan 1.4, loader 1.3.275 |
| X server | `xvfb-run -a` (Xvfb, default screen) |
| Native GLFW / SDL | `Ultz.Native.GLFW` 3.4.0, `Ultz.Native.SDL` (both pulled in by the Silk.NET platform packages) |

Packages referenced: `Silk.NET.Windowing.Common`, `Silk.NET.Windowing.Glfw`, `Silk.NET.Windowing.Sdl`, `Silk.NET.Input.Common`,
`Silk.NET.Input.Glfw`, `Silk.NET.Input.Sdl`, `Silk.NET.Vulkan`, `Silk.NET.Vulkan.Extensions.KHR`,
`Silk.NET.Vulkan.Extensions.EXT`, `Silk.NET.Shaderc` + `.Native`, `Silk.NET.SPIRV.Cross` + `.Native`.

## What was tried and what worked

1. **Explicit platform registration.** `Window.ShouldLoadFirstPartyPlatforms(false)` and
   `InputWindowExtensions.ShouldLoadFirstPartyPlatforms(false)` turn off the reflection-based discovery (`Window.TryAdd(string)`
   with `Activator.CreateInstance`), then `GlfwWindowing.RegisterPlatform()` + `GlfwInput.RegisterPlatform()` (or the `Sdl*`
   pair) register exactly one platform. Works for both GLFW and SDL.
2. **Manual loop.** `Window.Create(WindowOptions.DefaultVulkan with { ... })`, `Initialize()`, `DoEvents()` every frame,
   `Reset()` and `Dispose()` at the end. `Run` is never called. The input contexts hook the view's internal `ProcessEvents`
   (checked by reflection: `GlfwInputContext.ProcessEvents`, `SdlInputContext.ProcessEvents`), so keyboard, mouse and
   gamepad state is updated by `DoEvents()` alone; `DoUpdate()`/`DoRender()` are not needed.
3. **Vulkan surface.** `window.VkSurface.GetRequiredExtensions` gives `VK_KHR_surface` + `VK_KHR_xcb_surface` (GLFW) or
   `VK_KHR_xlib_surface` (SDL); `VkSurface.Create(instance.ToHandle(), null)` gives the `SurfaceKHR`. Instance, physical
   device (`llvmpipe`), device with `VK_KHR_swapchain`, swapchain (3 images, FIFO), acquire, clear, copy to a host buffer,
   present: all `Success`. Surface formats offered: `B8G8R8A8Srgb`, `B8G8R8A8Unorm`; the swapchain usage includes
   `TransferSrc`, so the presented image can be read back before present.
4. **Readback.** A clear to (255, 128, 0) reads back as BGRA `0,128,255,255` on an Unorm target and `0,188,255,255` on the
   Srgb swapchain format (the expected sRGB encoding of 128/255). Ion picks `B8G8R8A8Unorm` for the swapchain so colors
   match the Veldrid backend (no implicit sRGB conversion); the offscreen target is `R8G8B8A8Unorm` so readback is RGBA as is.
5. **Headless.** With no window the same code creates an instance without surface extensions, renders into an offscreen
   `VkImage` and reads it back. This is the CI path; it needs only a Vulkan ICD (lavapipe) and no X server.
6. **Shaders.** `Silk.NET.Shaderc` compiles GLSL 4.5 to SPIR-V (1036 bytes for the quad vertex shader, first call about
   100 ms including loading `libshaderc_shared.so`); `Silk.NET.SPIRV.Cross` turns that SPIR-V into `#version 310 es` GLSL ES
   source with the `layout(location)` qualifiers kept. Both native libraries load from the NuGet `runtimes/` folders under
   `dotnet run` and from the publish folder under NativeAOT.
7. **Textured quad.** Carried into the real backend rather than duplicated in the spike: the
   `Ion.Extensions.Graphics.Vulkan.Tests` headless test renders a checkerboard-textured quad through the RHI and asserts
   pixels; the windowed E2E test does the same through a swapchain under `xvfb-run`.

## HiDPI and framebuffer sizing

Under Xvfb there is no content scale: `Size` and `FramebufferSize` are both 320x240. On macOS (Retina) and on Wayland with
scaling they differ, so Ion keeps them apart: `IWindow.Size`/`Width`/`Height` are window coordinates (what mouse positions are
in), `IWindowSurface.FramebufferSize` is pixels, and the Vulkan swapchain uses the surface's `currentExtent` (falling back
to `FramebufferSize` when the extent is `0xFFFFFFFF`). The resize handler listens to `FramebufferResize`, not `Resize`.
Not verified on real HiDPI hardware in this spike (no macOS or scaled Wayland host available); the Stage 4 acceptance runs
on the three desktops still has to cover it.

## NativeAOT publish

`dotnet publish -c Release -r linux-x64 -p:PublishAot=true -p:TrimmerSingleWarn=false` succeeds in about 8 s and the
native binary runs all three modes (headless, GLFW, SDL) under xvfb. Warnings, all from Silk.NET or its dependencies,
none from the spike's code:

| Source | Warning | Why it is harmless for Ion |
|---|---|---|
| `Silk.NET.Windowing.Window.TryAdd(String)` | IL2072 (`Activator.CreateInstance` on an attribute's `Type`) | reflection-based platform discovery; disabled with `ShouldLoadFirstPartyPlatforms(false)` and never reached |
| `Silk.NET.Input.InputWindowExtensions.TryAdd(String)` | IL2072 (same) | same, for input platforms |
| `Silk.NET.Core.Loader.DefaultPathResolver` | IL3000 (`Assembly.Location`), IL3002 (`Assembly.CodeBase`, `DependencyContext.Default`) | fallback probing for native libraries; NativeAOT publish copies them next to the executable and the resolver finds them through `AppContext.BaseDirectory` first |
| `Microsoft.Extensions.DependencyModel.DependencyContext..cctor` | IL3002 | pulled in by the Silk.NET loader (same as above); returns null under single-file/AOT, which the loader handles |

In the default single-warning mode these collapse to two IL2104 lines (`Silk.NET.Windowing.Common`, `Silk.NET.Input.Common`)
plus the IL3000/IL3002 lines. Ion lists them as known third-party warnings; the AOT CI lane fails only on warnings whose
subject is `Ion.*`.

Output: a 3.2 MB executable plus `libglfw.so.3` (0.4 MB), `libSDL2-2.0.so` (2.1 MB), `libshaderc_shared.so` (9.2 MB) and
`libspirv-cross.so` (3.9 MB). The shader libraries are build-time only in Ion (shaders are compiled at build time), so a game
does not ship them.

## Startup time

Measured from process start (`Stopwatch` at the top of `Main`), lavapipe, xvfb:

| Build | Window initialized | Instance + device + swapchain + first frame read back |
|---|---|---|
| JIT (`dotnet SilkNetSpike.dll`, Debug) | 298 ms (includes Shaderc/SPIRV-Cross, about 200 ms) | 368 ms |
| NativeAOT (Release) | 139 ms (includes Shaderc/SPIRV-Cross, about 100 ms) | 164 ms |

Without the shader compilation (which Ion does at build time) the AOT path from process start to a presented frame is about
60 ms, most of it in lavapipe device creation.

## Findings that shaped the implementation

- Silk.NET 2.23.0 everywhere; no 3.0 packages exist.
- The platform is chosen from configuration (`Ion:Window:Platform` = `Glfw`, `Sdl` or `Auto`) and registered with the calls
  above; `Auto` means GLFW on desktop and SDL on Android/iOS (and SDL wherever GLFW fails to initialize).
- GLFW exposes 16 gamepad slots up front (`IInputContext.Gamepads.Count == 16`, disconnected ones report
  `IsConnected == false`); SDL adds gamepads through `ConnectionChanged`. Ion maps both onto the first
  `InputTracker.MaxGamepads` (8) slots and feeds connection events explicitly.
- Silk's `IKeyboard.KeyDown` has no repeat flag; Ion derives `repeat` from the key already being down.
- Input callbacks fire inside `DoEvents()` (order `StageOrder.Window`), before the input system begins the frame
  (`StageOrder.Input`), so the Silk input module queues `InputEvent` values and applies them after `InputTracker.BeginFrame()`.
- No Khronos validation layer is installed on this host (only `VK_LAYER_INTEL_nullhw` and the Mesa overlay); the backend
  enables `VK_LAYER_KHRONOS_validation` only when present and logs when it is missing.
- There is no Vulkan memory allocator in Silk.NET 2.x (`Silk.NET.Vulkan.Extensions` has none), so Ion allocates one
  `VkDeviceMemory` per buffer/texture and uses a per-frame linear staging ring for uploads; sub-allocation is a later item.
- macOS: Vulkan runs over MoltenVK (`Silk.NET.MoltenVK.Native`), which needs `VK_KHR_portability_enumeration` on the instance
  and `VK_KHR_portability_subset` on the device. The backend enables both when the loader reports them. Not tested here.

## Follow-up: the quad sample on the implemented stack

After the spike, the same checks on `Ion.Examples.Quad` (Silk.NET window or offscreen, the Vulkan RHI backend, shaders
compiled at build time), NativeAOT, linux-x64, lavapipe:

| Check | Result |
|---|---|
| Publish warnings from `Ion.*` | none |
| Publish warnings from Silk.NET before substitutions | IL2072 x2 (`Window.TryAdd`, `InputWindowExtensions.TryAdd`), IL3000 x2 and IL3002 x3 (`DefaultPathResolver`), IL3002 x1 (`DependencyContext..cctor`) |
| After the ILLink substitutions (`Ion.Extensions.Windowing.SilkNet/buildTransitive`) | IL3000 x1 and IL3002 x1, both in the `DefaultPathResolver` static resolver lambda (`<.cctor>b__24_3`) that reads `Assembly.Location`/`CodeBase` inside a try/catch; it cannot be stubbed by signature safely, and it does nothing under NativeAOT (the libraries sit next to the executable) |
| Executable size | 8.7 MB, plus `libglfw.so.3` (0.4 MB) and `libSDL2-2.0.so` (2.1 MB); no shader libraries |
| Headless: process start, device, 1 frame, PNG written, exit | 127 ms |
| GLFW under Xvfb: 60 frames including Xvfb startup | 416 ms |
| SDL under Xvfb | runs; same output |
| Khronos validation with synchronization validation | clean after three barrier fixes it found (repeated writes to one buffer or image in the same upload batch, a readback buffer reused every frame) |

The substitutions stub `Window.TryAdd` and `InputWindowExtensions.TryAdd` (never reached: first-party discovery is off)
and `DefaultPathResolver.TryLocateNativeAssetFromDeps`/`TryLocateNativeAssetInRuntimesFolder` (deps.json and the NuGet
`runtimes/` folder do not exist in a NativeAOT publish). They are applied only when the assemblies are referenced, so a
headless-only app (Vulkan without windowing) gets only the Silk.NET.Core one.

## Follow-up: the OpenGL ES backend (Stage 4, second wave)

`Ion.Extensions.Graphics.GLES` implements the same RHI on `Silk.NET.OpenGLES` 2.23 for the R36S (Mali-G31, Panfrost,
OpenGL ES 3.1) and as the fallback where Vulkan is missing. Checked on the same host: Mesa 25.2.8 llvmpipe, which offers
OpenGL ES 3.2 through EGL and GLX, and Xvfb.

**Contexts.** Windowed, `SilkWindow` creates the window with `ContextAPI.OpenGLES` 3.1 (no depth or stencil buffer; it
retries with 3.0 when 3.1 fails) whenever the resolved backend is OpenGL ES, and the backend renders through the window's
`IGLContext` (GLFW uses GLX under Xvfb, SDL its EGL or GLX path). Headless, the backend creates its own context through EGL:
Silk.NET 2.x has no EGL bindings (`Silk.NET.EGL` stopped at 1.9), so `EglContext` loads `libEGL.so.1` with `NativeLibrary`
and calls a dozen entry points through unmanaged function pointers (NativeAOT-clean). It prefers Mesa's surfaceless
platform (`EGL_MESA_platform_surfaceless` through `eglGetPlatformDisplayEXT`), which needs no X server, GBM device or
window, and uses no surface at all with `EGL_KHR_surfaceless_context` (a 1x1 pbuffer otherwise). The display is initialized
once per process and never terminated, because tests create devices in parallel.

**Conventions.** WebGPU's clip space is kept by the shader translation rather than a uniform: SPIRV-Cross negates
`gl_Position.y` (`FlipVertexY`) and remaps depth (`FixupDepthConvention`). Rendering is therefore upside down in GL terms,
which makes texture row 0 the top row, as in WebGPU and the Vulkan backend: uploads, render-to-texture, sampling and
`glReadPixels` readback need no flips, viewports and scissors take top-left origins as given, and front faces are inverted
(`GL_CW` for WebGPU's counter-clockwise). The one flip is at present, where the offscreen target is blitted to the default
framebuffer with a reversed destination rectangle. Bind groups are flattened to `group * 8 + binding` for uniform blocks and
texture units, written into the GLSL ES by the shader build together with a flattening table (the ES 3.0 path, whose GLSL
ES 3.00 has no binding qualifiers, assigns the slots by name from it); the sampler object bound to a texture unit is the
sampler the shader combined with that texture.

**Results.**

| Check | Result |
|---|---|
| Contract tests (shared with Vulkan) | pass headless at ES 3.2, 3.1 and 3.0 (no display) and windowed on GLFW and SDL (Xvfb): 81 GLES tests, 62 Vulkan tests |
| Goldens | the same PNGs as Vulkan, pixel for pixel (max channel difference 0), for the textured quad, the test host frame and the quad sample at 320x240, headless and windowed |
| NativeAOT linux-x64 | 6.9 MB executable (both backends linked in); no `Ion.*` warnings; the same two `Silk.NET.Core` `DefaultPathResolver` warnings (IL3000, IL3002) as before; process start to a written PNG in about 125 ms on either backend |
| NativeAOT linux-arm64 | cross-published from x64 with clang/lld: against Ubuntu 24.04's cross packages the executable needs glibc 2.34 (too new for ArkOS, glibc 2.30); against an Ubuntu 18.04 arm64 sysroot (`-p:SysRoot=... -p:LinkerFlavor=lld`) it needs `GLIBC_2.17` at most. 7.1 MB. Same warnings as x64 |
| linux-arm64 run | under `qemu-aarch64` with Ubuntu 18.04's arm64 Mesa 20.0.8 (llvmpipe, OpenGL ES 3.1, the Panfrost level): `Auto` picks OpenGL ES (the linux-arm64 order), headless through EGL, frame identical to the golden at ES 3.1 and ES 3.0 |

**Not verified.** R36S hardware and the Panfrost driver, SDL's KMSDRM video driver on the device (the profile and
deployment notes are in [../../platforms/r36s.md](../../platforms/r36s.md)), the windowed ES 3.0 context fallback (llvmpipe
always grants 3.1 or later), multisampled render targets (renderbuffers, attachment only, not exercised), storage buffers,
float render targets on a driver without `EXT_color_buffer_float`, Windows and macOS (GLES there needs ANGLE, not wired), and
GPU frame times on a tiler: the per-frame target ring and whole-buffer orphaning are there for Mali, but nothing measured
them on one.
