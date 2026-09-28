---
title: Installation
description: Install the .NET 10 SDK, the Ion templates and the ion command line tool, and check that a game builds and runs.
sidebar:
  order: 2
---

To make Ion games you need the .NET 10 SDK, a way to create a project (the `ion` tool or the `dotnet new` templates) and,
for windowed games, a GPU driver with Vulkan or OpenGL ES. Headless games and tests need nothing beyond the SDK.

## 1. Install the .NET 10 SDK

Ion targets `net10.0`. Install the .NET 10 SDK from [dot.net](https://dotnet.microsoft.com/download) and check the
version:

```bash
dotnet --version    # 10.0.100 or later
```

The Ion repository pins the SDK in `global.json` (`10.0.100`, `rollForward: latestFeature`), so any 10.0 feature band
works.

| Requirement | Why |
|---|---|
| .NET 10 SDK | Ion targets `net10.0` and uses C# 14 (`LangVersion` `latest`). |
| .NET SDK 9.0.200 or later (any .NET 10 SDK qualifies) | The schedule generator emits C# interceptors, which need Roslyn 4.12. With an older compiler the generator reports `ION014` and the game runs on the slower runtime path. |
| `clang` and `zlib1g-dev` (Linux, only for NativeAOT publishing) | NativeAOT links a native executable. See [Native AOT](/Ion/platforms/native-aot/). |

:::tip[Generators load in older compilers too]
The source generators target `netstandard2.0` on Roslyn 4.4, so they load in any compiler from the .NET 8 SDK onwards.
Only the interceptors need the newer compiler. You still need the .NET 10 SDK to build `net10.0` projects.
:::

## 2. Install a way to create projects

There are two equivalent ways to create a game. Both produce the same files: a game project, a test project with a
headless test and a snapshot test, `appsettings.json`, and a `CLAUDE.md` for coding agents. The contents are described
in [Project templates](/Ion/getting-started/templates/).

### Option A: the ion tool

The `ion` command line tool (package `Ion.Tools`, command name `ion`) embeds the templates, so `ion new` works offline.
It also runs games headless, prints the schedule, captures traces, compares screenshots, publishes and serves the MCP
server for coding agents.

```bash
dotnet tool install -g Ion.Tools
ion new 2d MyGame
```

If `Ion.Tools` is not on a NuGet feed you can reach, pack it from a checkout of the repository and install it from the
local folder:

```bash
git clone https://github.com/jimbuck/Ion.git
cd Ion
dotnet pack Ion/Ion.Tools -c Release -o out
dotnet tool install -g Ion.Tools --add-source out
```

You can also run it in place without installing: `dotnet Ion/Ion.Tools/bin/Release/net10.0/Ion.Tools.dll new 2d MyGame`.

`ion new` takes these options:

| Syntax | Meaning |
|---|---|
| `ion new <2d\|3d\|ecs> [name]` | Creates the game from a template. The name defaults to `MyGame`; it must start with a letter and contain only letters, digits and `_`. |
| `--output <dir>` | Writes into this folder instead of a folder named after the game. |
| `--ion-source <repo>` | Builds against an Ion source checkout (project references) instead of the NuGet packages. |
| `--force` | Writes into a folder that is not empty. |

### Option B: dotnet new templates

The same templates are a `dotnet new` template pack, `Ion.Templates`, with the short names `ion-2d`, `ion-3d` and
`ion-ecs`:

```bash
dotnet new install Ion.Templates
dotnet new ion-2d -n MyGame
```

From a checkout, pack the templates project and install the package file:

```bash
dotnet pack templates/Ion.Templates.csproj -c Release -o out
dotnet new install out/Ion.Templates.*.nupkg
```

Each template has one parameter, `IonSource` (`--IonSource <path>`), the equivalent of `--ion-source`.

## 3. Packages or sources

A generated game references the `Ion` package (and `Ion.Testing` in the test project) at the version in
`Directory.Build.props`:

```xml title="Directory.Build.props"
<IonVersion>0.3.0</IonVersion>
<IonSource></IonSource>
```

When `IonSource` holds the path of an Ion checkout, the projects switch to `ProjectReference`s into that checkout
(including the `Ion.Generators` analyzer) and import its publishing presets. This is how you work against the latest
engine, or when the packages are not available on your feed:

```bash
ion new ecs Arena --ion-source ~/src/Ion
```

:::caution[The repository does not publish packages from CI]
The repository's workflows build, test and publish samples but do not push NuGet packages. If `dotnet restore` cannot
find `Ion` 0.3.0, create the game with `--ion-source` (or set `IonSource` in `Directory.Build.props` by hand) and point
it at a clone of the repository.
:::

When you reference Ion by `ProjectReference` in a project that was not made from a template, add the generator and its
interceptor namespace yourself (the packages do this for you through their build props):

```xml title="MyGame.csproj"
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Ion.Generated</InterceptorsNamespaces>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/Ion/Ion/Ion/Ion.csproj" />
  <ProjectReference Include="path/to/Ion/Ion/Ion.Generators/Ion.Generators.csproj"
                    OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

## 4. Native dependencies

Most native libraries come with the NuGet packages. What you may need to install is a graphics driver.

| Component | Where it comes from | What you install |
|---|---|---|
| Window and input (GLFW or SDL) | The Silk.NET windowing packages | Nothing on Windows and macOS. On Linux, the usual X11 or Wayland client libraries (a desktop already has them). |
| Vulkan | Your GPU driver's Vulkan loader | A current GPU driver. On macOS, ship `libMoltenVK.dylib` with the game (for example from `Silk.NET.MoltenVK.Native`). |
| OpenGL ES (fallback, handhelds) | The GPU driver, or Mesa | Nothing on a desktop with a driver; Mesa on Linux devices. |
| Audio (OpenAL Soft) | `Silk.NET.OpenAL.Soft.Native` (Windows, macOS, Linux) | Nothing. If no device or library is found, Ion logs a warning and uses a silent output; startup never fails because of audio. |
| Box2D (2D physics) | `Box2D.NET.Bindings.Release` | Nothing. |
| Headless rendering (screenshots in CI) | Mesa | On Linux: `mesa-vulkan-drivers` (lavapipe) for Vulkan, or `libegl-mesa0` for OpenGL ES. |

Plain headless runs (`--headless`) use null graphics and audio backends and need no GPU, window or audio device at all.

:::note[Linux CI]
The repository's CI installs `mesa-vulkan-drivers libvulkan1 xvfb` plus the X11, Wayland and EGL client libraries so
that headless rendering and windowed tests (under `xvfb-run`) work on a machine without a GPU. Without a driver, the
rendering tests are skipped rather than failed.
:::

## 5. Verify the installation

Create a game, run its tests (headless, no GPU needed), then run it in a window:

```bash
ion new 2d Hello
cd Hello
dotnet build          # zero warnings expected
dotnet test           # headless and snapshot tests
cd Hello
dotnet run
```

A window opens with a paddle and a ball; the arrow keys (or A and D) move the paddle. If there is no display, run it
headless instead and let it stop after a fixed number of frames:

```bash
dotnet run -- --headless --Ion:Run:Frames=600
```

With the `ion` tool you get a summary of the run as JSON (and a non-zero exit code if the game threw):

```bash
ion run --headless --frames 600 --seed 1 --summary out/run.json
```

### Troubleshooting

| Symptom | Cause and fix |
|---|---|
| Restore fails to find `Ion` 0.3.0 | The packages are not on your feed. Recreate the game with `--ion-source <checkout>`. |
| Build warning `ION014` | The compiler is older than Roslyn 4.12 or `Ion.Generated` is missing from `InterceptorsNamespaces`. The game still runs, on the runtime path. See [Source generators](/Ion/concepts/source-generators/). |
| The game fails at Init with a Vulkan (or OpenGL ES) error | With `PreferredBackend` `Auto`, Ion probes the backends in platform order and, when none is available, uses the first one so that its own error names what is missing. Update the GPU driver, or force a backend with `--Ion:Graphics:PreferredBackend=OpenGLES`. See [Graphics backends](/Ion/rendering/graphics-backends/). |
| `FileNotFoundException` for an asset | The file is not copied to the output folder, or its casing differs (Linux and macOS are case-sensitive). The message names the resolved path and any file whose name differs only in casing. |

## See also

- [Your first game](/Ion/getting-started/first-game/): build a small game step by step.
- [Project templates](/Ion/getting-started/templates/): what `ion new` generates.
- [The ion CLI](/Ion/tooling/ion-cli/): every command of the tool.
- [Platforms](/Ion/platforms/overview/): what each target needs.
