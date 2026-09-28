---
title: Introduction
description: What the Ion game engine is, the ideas it is built on, the modules it ships and the platforms it runs on.
sidebar:
  order: 1
---

Ion is a code-first 2D and 3D game engine for C# on .NET 10. A game is an ordinary console program: you create an
application builder, register the modules and systems you want, build the application, add those systems to its
schedule and run it. If you have written an ASP.NET Core app, the shape is familiar.

```csharp title="Program.cs"
using Ion;
using Ion.Extensions.Ecs.Rendering;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddSystem<ModelSystem>();

using var game = builder.Build();
game.UseEcsRendering3D().UseSystem<ModelSystem>();
game.Run();
```

That is the whole entry point of the `Ion.Examples.Model` sample: a glTF model, PBR materials, shadows and a skybox, all
on the built-in ECS. `AddEcsRendering3D()` pulls in everything it depends on (the engine core, the 3D renderer and the
ECS module), and `UseEcsRendering3D()` adds their systems to the game loop.

## Design goals

Ion was reshaped during the 0.3 cycle around a short list of goals. They explain most of the API decisions you will see
in the rest of these docs.

| Goal | What it means in practice |
|---|---|
| **Middleware and ECS** | Game code is plain classes ("systems") whose attributed methods are steps of the game loop's stages. Steps are ordered declaratively (`Order`, `[After<T>]`, `[Before<T>]`), and `[Begin]`/`[End]` scopes replace the old `next(dt)` middleware. An [Arch](https://github.com/genaray/Arch)-based ECS sits alongside, with generated queries. You can use either or both. |
| **Performance** | No allocation per frame in the engine's hot paths (events, input, sprite batching, the 3D renderer, coroutines, UI). The schedule dispatches steps as direct calls. |
| **Generated, not reflected** | Roslyn source generators compile the schedule, the event bus, ECS queries, network serializers and web routes into plain C#. Diagnostics (`ION001` and up) turn schedule mistakes into compiler errors. |
| **Native AOT** | Games publish as a single NativeAOT executable per platform, with no runtime to install and no reflection on the hot path. |
| **No async in the loop** | Steps are synchronous (`async` steps are error `ION005`). Waiting across frames is done with coroutines or state, never `Task`. |
| **Agent-friendly** | Every game runs headless and deterministically, writes a JSON run summary, can be driven over a remote JSON-RPC protocol and an MCP server, and ships with a `CLAUDE.md` when created from a template. |
| **Testable** | `Ion.Testing` runs your real `Program.cs` headless on a fixed clock, frame by frame, with scripted input, JSON snapshots and golden images. |

:::note[Still 0.x]
Ion is pre-1.0 and its API still moves between releases. Obsolete APIs are kept as adapters for one release and flagged
by the compiler (for example the legacy middleware form, reported as `ION010`). The
[changelog](/Ion/reference/changelog/) lists every breaking change.
:::

## How a game is put together

Four ideas carry most of the weight. Each has its own page in [Core concepts](/Ion/concepts/application/).

1. **The application builder.** `IonApplication.CreateBuilder(args)` gives you configuration (command line,
   `appsettings.json`, environment variables) and a service collection. Modules register themselves on it with
   `builder.AddX()`. See [The application](/Ion/concepts/application/).
2. **Stages.** The loop runs `Init` once, then every frame `First`, `FixedUpdate` (zero or more times at a fixed rate),
   `Update`, `Render` and `Last`, and finally `Destroy` once. See [Stages](/Ion/concepts/stages/).
3. **Systems.** A system is a class registered in dependency injection. Each public method marked `[Update]`,
   `[Render]` and so on is a step of that stage. See [Systems](/Ion/concepts/systems/).
4. **Events.** Systems talk through typed, unmanaged event structs on a frame event bus that never allocates. See
   [Events](/Ion/concepts/events/).

```csharp title="PlayerSystem.cs"
using Ion;
using Ion.Extensions.Graphics;

public sealed class PlayerSystem(IInputState input, ISpriteBatch sprites)
{
    private float _x = 100;

    [Update]
    public void Move(GameTime dt)
    {
        if (input.Down(Key.Right)) _x += 200 * dt.Delta;
        if (input.Down(Key.Left)) _x -= 200 * dt.Delta;
    }

    [Render]
    public void Draw(GameTime dt) =>
        sprites.DrawRect(Color.White, new System.Numerics.Vector2(_x, 300), new System.Numerics.Vector2(32, 32));
}
```

## Modules

Ion is a set of packages that build on `Ion.Core`. The `Ion` package bundles the engine core (metrics, assets, graphics
and input, the 2D renderer, audio, scenes, coroutines and the remote protocol) behind a single `AddIon()`/`UseIon()`
pair, and ships the 3D renderer as well (`AddRendering3D()` turns it on). Everything else is opt-in, and every module
registers what it depends on. "Part of the core" below means the package comes with `Ion`.

| Area | Module (package) | Builder call | Read more |
|---|---|---|---|
| Engine core | `Ion`, `Ion.Core` | `AddIon()` / `UseIon()` | [The application](/Ion/concepts/application/) |
| 2D rendering | `Ion.Extensions.Rendering2D` (part of the core) | `AddIon()` | [Rendering overview](/Ion/rendering/overview/), [Sprites](/Ion/rendering/sprites/) |
| Graphics backends | `Ion.Extensions.Graphics.Vulkan`, `.GLES`, `.Headless`, `.Null` | `AddIon()` picks one | [Graphics backends](/Ion/rendering/graphics-backends/) |
| 3D rendering | `Ion.Extensions.Rendering3D` (part of the core) | `AddRendering3D()` | [3D overview](/Ion/rendering/3d/overview/) |
| Assets and hot reload | `Ion.Extensions.Assets` (part of the core) | `AddIon()` | [Assets](/Ion/rendering/assets/) |
| ECS | `Ion.Extensions.Ecs` | `AddEcs()` | [ECS overview](/Ion/ecs/overview/) |
| ECS rendering | `Ion.Extensions.Ecs.Rendering` | `AddEcsRendering()`, `AddEcsRendering3D()` | [ECS rendering](/Ion/ecs/ecs-rendering/) |
| Scenes | `Ion.Extensions.Scenes` (part of the core) | `UseScene(...)` | [Scenes](/Ion/ecs/scenes/) |
| Coroutines | `Ion.Extensions.Coroutines` (part of the core) | `AddIon()` | [Coroutines](/Ion/ecs/coroutines/) |
| Audio | `Ion.Extensions.Audio` (part of the core) | `AddAudio(...)` | [Audio](/Ion/interaction/audio/) |
| Input | `Ion.Core.Abstractions` plus the windowing module | `AddIon()` | [Input](/Ion/interaction/input/overview/) |
| UI | `Ion.Extensions.UI` | `AddUi()` | [UI overview](/Ion/interaction/ui/overview/) |
| 2D physics (Box2D v3) | `Ion.Extensions.Physics2D` | `AddPhysics2D()` | [2D physics](/Ion/physics/physics-2d/) |
| 3D physics (BepuPhysics v2) | `Ion.Extensions.Physics3D` | `AddPhysics3D()` | [3D physics](/Ion/physics/physics-3d/) |
| Web server | `Ion.Extensions.Web` | `AddWeb()` | [HTTP server](/Ion/networking/http-server/) |
| Multiplayer | `Ion.Extensions.Networking`, `.LiteNetLib` | `AddNetworking()` | [Multiplayer](/Ion/networking/multiplayer/overview/) |
| Metrics and tracing | `Ion.Extensions.Metrics`, `.Metrics.Tracy` | `AddMetrics(...)` | [Metrics and tracing](/Ion/tooling/metrics-and-tracing/) |
| Remote inspection | `Ion.Extensions.Remote` (part of the core) | `--remote` | [Remote protocol](/Ion/tooling/remote-protocol/) |
| Testing | `Ion.Testing` | `IonTestHost` | [Testing](/Ion/tooling/testing/) |
| Command line and MCP | `Ion.Tools` (the `ion` tool) | `ion new`, `ion run`, `ion mcp` | [The ion CLI](/Ion/tooling/ion-cli/) |

The [module map](/Ion/reference/module-map/) lists every package with its dependencies.

## Platforms

| Platform | Status | Graphics |
|---|---|---|
| Windows x64 | Built and tested in CI | Vulkan, OpenGL ES fallback |
| Windows arm64 | Publish preset only; not built in CI | Vulkan, OpenGL ES fallback |
| Linux x64 | Built, tested, rendered and AOT-published on every pull request | Vulkan, OpenGL ES fallback |
| Linux arm64 | Cross-compiled in CI and run under QEMU; not yet run on arm64 hardware | OpenGL ES first, then Vulkan |
| macOS arm64 | Built and tested in CI | Vulkan through MoltenVK (you ship `libMoltenVK.dylib`) |
| macOS x64 | Publish preset only; not built in CI | Vulkan through MoltenVK |
| R36S handheld (ArkOS, linux-arm64) | Dedicated publish preset (`r36s`), measured under QEMU; not yet run on the device | OpenGL ES 3.1 through Panfrost, SDL, 640x480 |
| Android, iOS | Head projects compile with the mobile workloads; not yet run on a device | Vulkan then OpenGL ES; MoltenVK on iOS |
| Headless (CI, servers, tests) | Supported everywhere | None, or offscreen Vulkan (Mesa lavapipe) or OpenGL ES (EGL) |
| Browser (WebAssembly, WebGPU) | Planned, not built | |

See [Platforms](/Ion/platforms/overview/) and [Publishing](/Ion/platforms/publishing/) for the presets and their
requirements.

## Credits

Ion builds on [Silk.NET](https://github.com/dotnet/Silk.NET) (windowing, input, Vulkan, OpenGL ES, OpenAL, Shaderc and
SPIRV-Cross), [Arch](https://github.com/genaray/Arch) (ECS), Box2D and BepuPhysics (physics),
[FontStashSharp](https://github.com/FontStashSharp/FontStashSharp) (text) and
[ImageSharp](https://github.com/SixLabors/ImageSharp) (image decoding). It is
[MIT licensed](https://github.com/jimbuck/Ion/blob/main/LICENSE).

## See also

- [Installation](/Ion/getting-started/installation/): the SDK, the templates and the `ion` tool.
- [Your first game](/Ion/getting-started/first-game/): from an empty folder to a tested game.
- [Core concepts](/Ion/concepts/application/): the builder, stages, systems and events in depth.
- [Examples](/Ion/examples/): walkthroughs of every sample in the repository.
