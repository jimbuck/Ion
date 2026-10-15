---
title: Publishing
description: Publish an Ion game as a NativeAOT executable with the IonTarget presets or ion publish, what each preset sets, what lands in the publish folder, and how to use the presets outside the repository.
sidebar:
  order: 5
---

An Ion game ships as a NativeAOT executable: one native binary per platform, with its native libraries (SDL2, GLFW,
OpenAL) and content next to it, and no .NET runtime to install. A **publishing preset** picks every setting for a
platform, so a publish is one property.

```bash
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=linux-x64
dotnet publish Ion.Examples/Ion.Examples.Breakout.ECS -p:IonTarget=r36s -p:IonArm64SysRoot=/path/to/sysroot
ion publish Ion.Examples/Ion.Examples.Breakout.ECS --target r36s --sysroot /path/to/sysroot
```

## The presets

| `IonTarget` | Runtime identifier | Kind | Build on | Notes |
|---|---|---|---|---|
| `win-x64` | `win-x64` | desktop | Windows | |
| `win-arm64` | `win-arm64` | desktop | Windows | |
| `osx-arm64` | `osx-arm64` | desktop | macOS | Vulkan through MoltenVK (the game brings `libMoltenVK.dylib`) |
| `osx-x64` | `osx-x64` | desktop | macOS | as above |
| `linux-x64` | `linux-x64` | desktop | Linux | needs `clang` and `zlib1g-dev` |
| `linux-arm64` | `linux-arm64` | desktop | Linux arm64, or Linux x64 cross (clang, lld, a sysroot) | |
| `r36s` | `linux-arm64` | handheld | as `linux-arm64` | OpenGL ES, SDL, fullscreen 640 x 480, ArkOS SD card layout ([R36S](/Ion/platforms/r36s/)) |

:::caution[NativeAOT does not cross-compile between operating systems]
Publish the Windows presets on Windows and the macOS presets on macOS. From Linux, `win-x64` stops at the SDK's check
("Cross-OS native compilation is not supported"), and `osx-arm64` compiles the whole program to a Mach-O object and then
fails at the link, which needs Apple's linker and the macOS SDK. CI's `publish-desktop` job publishes on Windows and
macOS runners when the repository variable `ION_PUBLISH_CI` is `true`. Linux x64 to Linux arm64 is the one cross
build that works.
:::

## What every preset sets

| Property | Value | Why |
|---|---|---|
| `RuntimeIdentifier` | the preset's | One platform per publish; the restore fetches its runtime and NativeAOT packs. |
| `SelfContained`, `PublishAot` | `true` | A native executable, no runtime on the machine. |
| `PublishReadyToRun`, `PublishSingleFile` | `false` | Not used with NativeAOT. |
| `IlcGenerateStackTraceData`, `StackTraceSupport` | `true` | Readable stack traces in release builds (about 1 MB). |
| `EventSourceSupport` | `true` on desktop, `false` on the handheld | `dotnet-trace` and `dotnet-counters` on desktop; nothing to attach on the device. |
| `InvariantGlobalization` | `true` | No ICU: games do not need culture data, and ArkOS and minimal images do not ship it. |
| `StripSymbols` | `true` | Symbols go to a separate `.dbg` (Linux) or `.dSYM` (macOS) file next to the executable. |
| `TrimMode` | `full` | With the trim analyzer on (`EnableTrimAnalyzer`). |
| `DebuggerSupport`, `MetadataUpdaterSupport`, `HttpActivityPropagationSupport`, `UseNativeHttpHandler` | `false` | Feature switches a shipped game does not use. |
| `UseSystemResourceKeys` | `false` | Exception messages stay readable in logs. |
| `SatelliteResourceLanguages` | `en` | No localized satellite assemblies. |
| `PublishDocumentationFiles`, `PublishReferencesDocumentationFiles`, `AllowedReferenceRelatedFileExtensions` | off | No `.xml` or `.pdb` of the referenced assemblies. |

Every one of them can be overridden in the game's project or on the command line, for example
`-p:InvariantGlobalization=false` if your game formats culture-specific text.

On Linux x64 the `linux-arm64` and `r36s` presets also set `LinkerFlavor=lld` and pass `IonArm64SysRoot` (a property
or an environment variable) as `SysRoot` for the cross build.

## Which projects take a preset

Only executable projects. `IonTarget` flows to project references as a global property, so the presets are written to
be harmless for everything else:

| Project | `IonPublishable` | Effect of `IonTarget` |
|---|---|---|
| A game (`OutputType` `Exe` or `WinExe`) | `true` | The preset applies. |
| An engine library (`IonAotLibrary`) | `false` | Ignored. |
| `*.Tests`, `*.Benchmarks`, `*Generators` | `false` | Ignored. |
| Any other non-executable project | `true` | Error `IONPUB002`: publish the game project, not a library or a solution. |

You can set `<IonPublishable>false</IonPublishable>` in a project to opt it out (the mobile heads do this when they
build as libraries).

## Errors and warnings

| Code | Kind | When | Fix |
|---|---|---|---|
| `IONPUB001` | error | `IonTarget` is not one of the seven presets. | Use `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64` or `r36s`. |
| `IONPUB002` | error | `IonTarget` on a project that is not an executable. | Publish the game project. |
| `IONPUB003` | warning | `r36s` cross-compiled without `IonArm64SysRoot`. | Build a glibc 2.27 sysroot with `build/arm64-sysroot.py` and pass it. |
| `IONPUB004` | error | The ArkOS layout target ran without a published executable. | Publish with `-p:IonTarget=r36s` first. |
| `IONMOB001` | error | `IonMobileHeads=true` without the Android or iOS workload. | `dotnet workload install android` (or `ios`). |

## ion publish

`ion publish` is the same `dotnet publish` with the preset filled in:

```text
ion publish [project] --target <preset> [-c Release] [--output <dir>] [--sysroot <dir>] [-- msbuild args]
```

It runs `dotnet publish <project> -c Release -p:IonTarget=<preset>`, adds `-o <dir>` for `--output` and
`-p:IonArm64SysRoot=<dir>` for `--sysroot`, and passes everything after `--` on. An unknown preset is rejected before
`dotnet` starts. See the [ion CLI](/Ion/tooling/ion-cli/) page for the other commands.

## What ends up in the publish folder

A `linux-x64` publish of Breakout ECS (September 2026):

| File | Size | Notes |
|---|---|---|
| `Ion.Examples.Breakout.ECS` | 12.6 MB | The NativeAOT executable. |
| `Ion.Examples.Breakout.ECS.dbg` | 21 MB | Debug symbols; keep them for crash analysis, do not ship them. |
| `libSDL2-2.0.so` | 2.1 MB | SDL2, for `Ion:Window:Platform=Sdl` (and gamepads through SDL). |
| `libopenal.so` | 1.2 MB | OpenAL Soft, the audio output. |
| `libglfw.so.3` | 0.4 MB | GLFW, the default desktop window platform. |
| `appsettings.json` | | Copied because the project marks it as content. |
| `Assets/` | 0.35 MB | The game's textures, fonts and sounds. |

That is 16.4 MB shipped. Windows gets `.dll` and macOS `.dylib` equivalents. Box2D is not there because Breakout ECS
links it statically:

```xml title="Ion.Examples.Breakout.ECS.csproj"
<PropertyGroup>
  <!-- NativeAOT: link Box2D's static library into the executable instead of shipping libbox2d next to it. -->
  <Box2DStaticLink>true</Box2DStaticLink>
</PropertyGroup>
```

With `Box2DStaticLink` the preset drops `libbox2d` from the publish list. Without it, a game that uses
[2D physics](/Ion/physics/physics-2d/) ships the shared library next to the executable.

:::note[Native libraries are found next to the executable]
Silk.NET opens its native libraries from the executable's folder first (`AppContext.BaseDirectory`), which is why the
R36S launcher can swap in the system SDL2 by replacing `libSDL2-2.0.so`, and why the loader's other probing paths
(deps.json, `runtimes/`) are stubbed out in NativeAOT builds.
:::

Mark your own files as content so they are copied:

```xml
<ItemGroup>
  <Content Include="appsettings*.json" CopyToOutputDirectory="PreserveNewest" />
  <Content Include="Assets\**\*" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

## Sizes and startup

Every sample, `linux-x64` preset (executable size; startup is one headless run to the end of one frame):

| Sample | Executable | Start to exit after one frame | Ion trim/AOT warnings |
|---|---|---|---|
| Breakout | 12.6 MB | 64 ms | 0 |
| Breakout ECS | 12.6 MB | 80 ms | 0 |
| Cubes | 12.4 MB | 36 ms | 0 |
| Menu | 11.7 MB | 91 ms | 0 |
| Model | 12.4 MB | 50 ms | 0 |
| Quad | 7.7 MB | 218 ms (renders the frame on lavapipe) | 0 |
| Scenes | 8.2 MB | 54 ms | 0 |
| Sprites100k | 11.6 MB | 109 ms | 0 |

Breakout ECS measured ten times: 46 ms median on `linux-x64`, 611 ms on `r36s` under `qemu-aarch64` (emulated, not the
device). To measure your own game:

```bash
python3 build/arm64-sysroot.py /tmp/sysroot-bionic-arm64          # once, for the arm64 cross build
SAMPLE=path/to/MyGame IonArm64SysRoot=/tmp/sysroot-bionic-arm64 \
  docs/plans/benchmarks/2026-09-stage7-publish/measure.sh ./artifacts/publish
```

`measure.sh` reads `SAMPLE`, `TARGETS` (default `linux-x64 r36s`), `RUNS` (default 10), `IonArm64SysRoot` and
`DESKTOP_LIMIT_MB` (default 30; a desktop executable above it fails the script), and writes `results.md`,
`results.json`, the per-run times and the publish and run logs.

## Publishing a game outside the repository

The presets live in `build/Ion.Publish.props` and `build/Ion.Publish.targets`. The repository's
`Directory.Build.props` and `Directory.Build.targets` import them, so every executable project in the repository has
them. A game outside the repository imports the same two files. The [templates](/Ion/getting-started/templates/) do
this when you point them at an Ion source checkout (`IonSource`):

```xml title="Directory.Build.props"
<Project>
  <Import Project="$(IonSource)/build/Ion.Publish.props" Condition="'$(IonSource)' != '' and Exists('$(IonSource)/build/Ion.Publish.props')" />
</Project>
```

```xml title="Directory.Build.targets"
<Project>
  <Import Project="$(IonSource)/build/Ion.Publish.targets" Condition="'$(IonSource)' != '' and Exists('$(IonSource)/build/Ion.Publish.targets')" />
</Project>
```

The props file must be imported **before** the project body (a `Directory.Build.props`), so the SDK's runtime
identifier inference, output paths and restore all see the preset.

Without the presets, the equivalent manual publish is:

```bash
dotnet publish MyGame -c Release -r linux-x64 -p:PublishAot=true -p:InvariantGlobalization=true -p:StripSymbols=true
```

## Mobile and browser

Android and iOS do not use `IonTarget`; their head projects build with `-p:IonMobileHeads=true` (see
[Mobile](/Ion/platforms/mobile/)). There is no browser preset: the browser target is not built yet (see the
[platforms overview](/Ion/platforms/overview/#the-browser)).

## See also

- [NativeAOT](/Ion/platforms/native-aot/): what makes the executable warning-free, and the CI lane.
- [R36S](/Ion/platforms/r36s/): the handheld preset and SD card layout.
- [Desktop](/Ion/platforms/desktop/): per-OS requirements.
- [ion CLI](/Ion/tooling/ion-cli/).
