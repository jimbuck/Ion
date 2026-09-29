# Publishing Ion games

An Ion game ships as a NativeAOT executable: one native binary per platform, with its native libraries (SDL2, GLFW,
OpenAL) and content next to it, and no .NET runtime to install. The publishing presets pick every setting for a
platform, so a publish is one property:

```sh
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=linux-x64
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=r36s -p:IonArm64SysRoot=/path/to/sysroot
ion publish Ion.Examples/Ion.Examples.Breakout.ECS --target r36s --sysroot /path/to/sysroot
```

`ion publish [project] --target <preset> [-c Release] [--output <dir>] [--sysroot <dir>] [-- msbuild args]` is the same
`dotnet publish` with `-p:IonTarget=<preset>` (and `-p:IonArm64SysRoot=` for `--sysroot`); anything after `--` is passed
on. The presets live in `build/Ion.Publish.props` and `build/Ion.Publish.targets`, imported by the repository's
`Directory.Build.props` and `Directory.Build.targets`, so every executable project in the repository has them. A game
outside the repository imports the two files the same way.

## Presets

| `IonTarget` | Runtime identifier | Kind | Build on | Notes |
|---|---|---|---|---|
| `win-x64` | `win-x64` | desktop | Windows | |
| `win-arm64` | `win-arm64` | desktop | Windows | |
| `osx-arm64` | `osx-arm64` | desktop | macOS | Vulkan through MoltenVK (the game brings `libMoltenVK.dylib`) |
| `osx-x64` | `osx-x64` | desktop | macOS | as above |
| `linux-x64` | `linux-x64` | desktop | Linux | needs `clang` and `zlib1g-dev` |
| `linux-arm64` | `linux-arm64` | desktop | Linux arm64, or Linux x64 cross (clang, lld, a sysroot) | |
| `r36s` | `linux-arm64` | handheld | as `linux-arm64` | OpenGL ES, SDL, fullscreen 640x480, ArkOS SD card layout ([r36s.md](r36s.md)) |

Every preset sets:

| Property | Value | Why |
|---|---|---|
| `RuntimeIdentifier` | the preset's | one platform per publish; the restore fetches its runtime and NativeAOT packs |
| `SelfContained`, `PublishAot` | `true` | a native executable, no runtime on the machine |
| `PublishReadyToRun`, `PublishSingleFile` | `false` | not used with NativeAOT |
| `IlcGenerateStackTraceData`, `StackTraceSupport` | `true` | readable stack traces in release builds (about 1 MB) |
| `EventSourceSupport` | `true` on desktop, `false` on the handheld | `dotnet-trace` and `dotnet-counters` on desktop; nothing to attach on the device |
| `InvariantGlobalization` | `true` | no ICU (games do not need culture data; ArkOS and minimal images do not ship it) |
| `StripSymbols` | `true` | symbols go to a separate `.dbg` (Linux) or `.dSYM` (macOS) file next to the executable |
| `TrimMode` | `full` | with the trim analyzer on |
| `DebuggerSupport`, `MetadataUpdaterSupport`, `HttpActivityPropagationSupport`, `UseNativeHttpHandler` | `false` | feature switches a shipped game does not use |
| `UseSystemResourceKeys` | `false` | exception messages stay readable in logs |
| `SatelliteResourceLanguages` | `en` | no localized satellite assemblies |
| `PublishDocumentationFiles`, `PublishReferencesDocumentationFiles`, `AllowedReferenceRelatedFileExtensions` | off | no `.xml` or `.pdb` of the referenced assemblies |

Any of them can be overridden in the game's project or on the command line. Only executable projects take a preset:
engine libraries, tests, benchmarks and generators ignore `IonTarget` (it flows to project references as a global
property), and an unknown preset or a library project is an error (`IONPUB001`, `IONPUB002`). A game that links Box2D
statically (`<Box2DStaticLink>true</Box2DStaticLink>`, as Breakout ECS does) does not ship `libbox2d` next to it.

`linux-arm64` and `r36s` cross-compile from a Linux x64 machine with clang and lld (`LinkerFlavor=lld`, set by the
preset) against the sysroot in `IonArm64SysRoot` (a property or an environment variable). `build/arm64-sysroot.py
<dir>` builds a glibc 2.27 sysroot from Ubuntu 18.04's arm64 packages; without one, the Ubuntu cross packages link and
the binary needs the build machine's glibc (warning `IONPUB003` for `r36s`, whose target runs glibc 2.30).

NativeAOT does not cross-compile between operating systems: the Windows presets publish on Windows and the macOS
presets on macOS (CI's `publish-desktop` job, enabled with the repository variable `ION_PUBLISH_CI`). Tried from the
Linux x64 container (September 2026): the runtime packs of both (`Microsoft.NETCore.App.Runtime.win-x64`,
`.osx-arm64` and their `NativeAOT` packs) restore through the NuGet feed; `win-x64` then stops at the SDK's check
("Cross-OS native compilation is not supported"), and `osx-arm64` compiles the whole program to a Mach-O object
(47 MB, the same two Silk.NET.Core warnings and none from Ion) and fails at the link, which needs Apple's linker and the
macOS SDK.

## r36s: the ArkOS layout

Besides the publish folder, the `r36s` preset writes `<publish dir>-arkos/` (or `IonArkOSDir`):

```
README.md                         what to copy where on the SD card
ports/<name>.sh                   the launcher shown in the Ports menu
ports/<name>/<executable>         the binary, libSDL2-2.0.so, libopenal.so, appsettings.json,
ports/<name>/appsettings.r36s.json   the handheld defaults (SDL, fullscreen 640x480, OpenGL ES, no cursor)
ports/<name>/Assets/...
```

`<name>` is the assembly name unless `IonArkOSName` is set (`IonArkOSTitle` names the game in the README and script).
The launcher switches to the system SDL2 (KMSDRM and the device's controls), sets `DOTNET_ENVIRONMENT=r36s` so
`appsettings.r36s.json` is loaded over `appsettings.json`, and writes the game's output to `<name>/log.txt`. The debug
symbols and the unused GLFW library stay out. The layout can be rebuilt from an existing publish folder with
`dotnet msbuild <game> -t:IonArkOSLayout -p:IonTarget=r36s -p:PublishDir=<publish dir>/`. Details and what has been
verified are in [r36s.md](r36s.md).

## Sizes and startup

Measured by `docs/plans/benchmarks/2026-09-stage7-publish/measure.sh` (see its [README](../plans/benchmarks/2026-09-stage7-publish/README.md)),
which the CI AOT lane runs on every pull request and uploads as the `Stage7PublishResults` artifact. Startup is process
start to exit after one headless frame (`--Ion:Headless=true --Ion:Run:Frames=1`), an upper bound on start to first
frame; 2.1 GHz Xeon VM, .NET SDK 10.0.112, September 2026.

Breakout ECS:

| Target | Executable | Shipped (no `.dbg`) | Startup (median of 10) | Where |
|---|---|---|---|---|
| linux-x64 | 12.6 MB | 16.4 MB | 46 ms | native |
| r36s (linux-arm64) | 11.6 MB | 15.0 MB | 611 ms | `qemu-aarch64` user mode on x64 (emulated; not the device) |

Every sample, `linux-x64` preset (executable size; startup is one run, headless, one frame):

| Sample | Executable | Headless start to exit after one frame | Ion trim/AOT warnings |
|---|---|---|---|
| Breakout | 12.6 MB | 64 ms | 0 |
| Breakout ECS | 12.6 MB | 80 ms | 0 |
| Cubes | 12.4 MB | 36 ms | 0 |
| Menu | 11.7 MB | 91 ms | 0 |
| Model | 12.4 MB | 50 ms | 0 |
| Quad | 7.7 MB | 218 ms (renders the frame on lavapipe) | 0 |
| Scenes | 8.2 MB | 54 ms | 0 |
| Sprites100k | 11.6 MB | 109 ms | 0 |

Every sample publishes with the `linux-x64` preset; the only ILC warnings are the two of Silk.NET.Core's library loader
(`IL3000`, `IL3002`, not reached by Ion), and the Menu sample has none.

The acceptance line for Stage 7 is under 30 MB and under 300 ms on desktop, and under one second on the handheld; the CI
lane fails only when a desktop executable passes 30 MB. The r36s number is an emulated upper bound: on the Cortex-A35 it
is not measured yet.

## Mobile

The phone and tablet heads of Breakout ECS (`Ion.Examples.Breakout.ECS.Android`, `Ion.Examples.Breakout.ECS.iOS`) build
with `-p:IonMobileHeads=true` on a machine with the workload (see `build/Ion.Mobile.props`):

```sh
dotnet workload install android      # plus a JDK and the Android SDK
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS.Android -c Release -p:IonMobileHeads=true -p:RuntimeIdentifier=android-arm64
dotnet workload install ios          # macOS with Xcode only
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS.iOS -c Release -p:IonMobileHeads=true
```

Both use the SDL windowing platform (Silk.NET's `SilkActivity` on Android and `SilkMobile` on iOS; SDL is view-only
there, so `SilkWindow` creates a full-screen view), `GraphicsBackend.Auto` (Vulkan, then OpenGL ES; MoltenVK on iOS),
NativeAOT in Release, and touch input (SDL finger events mapped into `IInputState.Touches`; the game moves the paddle
with the first finger and launches a ball when it lifts). Without `IonMobileHeads` (the default, and in `Ion.sln`) each
head builds as a `net10.0` library of the shared game code, so the code stays compiling everywhere; with it and no
workload the build stops with `IONMOB001`. CI has `mobile-android` and `mobile-ios` jobs behind the repository variable
`ION_MOBILE_CI`.

Status (September 2026): the Android head compiles for `net10.0-android` (the android workload installs through the
NuGet feed), but packaging needs the Android SDK, which is not reachable from the build container; the iOS workload
does not install on Linux. Neither head has run on a device. The Breakout play field is a fixed 2030x984 window-space
layout: on a phone, as on the R36S's 640x480, it needs a scaled view (a virtual resolution in the 2D renderer), which is
not done.

## Browser

Not in scope for Stage 7. What remains (roadmap 4.11): a `net10.0-browser` target (the `wasm-tools` workload, Mono
interpreter or AOT, single-threaded) for `Core`, the renderers and `Ecs`; a `Graphics.WebGPU` RHI backend over
`Silk.NET.WebGPU` or Emscripten's `webgpu.h`; a canvas windowing and input shim in place of SDL; `requestAnimationFrame`
driving `GameLoop.Step`; the managed Box2D port behind the physics module (the native one does not build for wasm); a
publish preset and a CI lane.
