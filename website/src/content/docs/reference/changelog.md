---
title: Changelog
description: A summary of what changed in Ion, from the 0.1 releases to the unreleased 0.3 engine work, with a link to the full changelog.
sidebar:
  order: 5
---

This page summarizes the changes. The full, detailed list (every renamed type, every new option, every fix) is
[CHANGELOG.md on GitHub](https://github.com/jimbuck/Ion/blob/main/CHANGELOG.md).

## Unreleased (the 0.3 engine)

The 0.3 work rebuilt most of the engine. Released versions stop at 0.2.5; the templates already reference
`IonVersion` 0.3.0.

### Breaking and behaviour changes

- **Schedule of ordered steps instead of middleware chains.** Each stage is an ordered list of steps. A system is any
  class added with `UseSystem<T>()`; each public method with a stage attribute is a step, with `Order`,
  `[After<T>]`/`[Before<T>]` and `[Begin]`/`[End]` scopes. Steps run and return: there is no `next`.
- **Registration order no longer matters.** Engine steps use reserved order bands (`-1000..-500` and `500..1000`), see
  [Stage order](/Ion/reference/stage-order/).
- **Validation at build.** `IonApplication.Build()` throws `IonScheduleException` listing every schedule error
  (`ION001` to `ION013`); see [Diagnostics](/Ion/reference/diagnostics/).
- **Legacy middleware** (`GameLoopDelegate next`) keeps working for one release as opaque middleware, reported as
  `ION010`.
- **Setup on the builder.** Modules register on the builder (`builder.AddX()`) and pull in their dependencies;
  `UseSystem` of a system already in the schedule does nothing; `app.UseRendering3D()` comes from the `Ion` namespace
  and adds `UseIon()`.
- **Samples and templates run from `Program.cs`.** Every sample's setup moved from a static `XxxApp` class into
  `Program.cs`, and its tests run that `Program.cs` with `UseEntryPoint<Program>()`.
- **Events v2.** `IEvent` is removed; events are unmanaged structs on typed channels (`IEvents`, `EventReader<T>`).
  `IEventEmitter` and `IEventListener` are obsolete adapters for one release.
- **.NET 10.** Every project targets `net10.0`; the source generators target `netstandard2.0` on Roslyn 4.4.
- **Veldrid removed; `AddIon` uses the Silk.NET stack** (Silk.NET windowing, the Vulkan and OpenGL ES RHI backends,
  the 2D renderer on the RHI). `ISpriteBatch` gains `Begin(SpriteBatchOptions)`, `End()` and `SetRenderTarget`.
- **Audio rewritten without NAudio**: an engine mixer with an OpenAL output.
- **Metrics v2**: `Ion.Extensions.Debug*` is now `Ion.Extensions.Metrics*`; the 0.2 names are obsolete adapters.
- **Fixed step decoupled from `MaxFPS`**: `FixedUpdateRate` (default 60 Hz) sets the fixed step; `MaxFPS` only paces
  rendering.
- **Input v2**: the shared `InputTracker`, stage-aware edges (a click is seen by exactly one fixed step), gamepads,
  text input, touch.
- **Scene enum overloads** are the generic `UseScene<TScene>` and `EmitChangeScene<TScene>`.
- **Stage 7 windowing change**: `SilkWindow.View` is now the Silk.NET `IView` (a view on Android and iOS).

### Features, by stage of the work

| Area | Highlights |
|---|---|
| Streamlined setup | Modules on the builder that pull in their dependencies; `IonTestHost.UseEntryPoint<TProgram>()` and `RunEntryPoint<TProgram>(frames)` to test the real `Program.cs`. |
| Compile-time schedule | `Ion.Generators`: pre-bound registrations, a generated schedule with direct calls, registration summaries across assemblies, compile-time diagnostics, clean stack traces. |
| Events v2 | Typed channels, a generated event bus with compile-time ids, `ION101` to `ION106`. |
| Metrics v2 | Frame profiler ring, `FrameStats`, frame log (JSON Lines), Chrome traces, the `Ion` meter, overlay, Tracy. |
| Silk.NET graphics | A WebGPU-shaped RHI, Vulkan and OpenGL ES backends, build-time shaders, headless rendering and golden images, NativeAOT substitutions. |
| 2D renderer | An instanced sprite batch on the RHI, sort modes, render targets, cached text layouts. |
| 3D | `Ion.Extensions.Rendering3D`: culling, batching, a render graph, PBR, shadows, glTF 2.0, cube maps. |
| ECS | `Ion.Extensions.Ecs` on Arch 2.1: a world per scope, `Commands`, generated `[Query]` loops, transforms, sprite and 3D extraction, model spawning, serialization. |
| Physics | `Ion.Extensions.Physics2D` (Box2D v3) and `Ion.Extensions.Physics3D` (BepuPhysics v2), deterministic replay tests. |
| UI | `Ion.Extensions.UI`: immediate-mode widgets, flex layout, themes, gamepad focus, `IUiTree`. |
| Agentic toolchain | The remote protocol, the `ion` tool, the MCP server, templates, run settings in every game. |
| Web | `Ion.Extensions.Web` with generated routing, `Ion.Extensions.Http`, the Companion sample. |
| Networking | `Ion.Extensions.Networking`: replication, delta snapshots, messages, prediction, interpolation, lag compensation, loopback and LiteNetLib transports. |
| Native and multi-platform builds | `IonTarget` publishing presets, `ion publish`, the R36S ArkOS layout, size and startup tracking, the Android and iOS heads, touch input. |
| Audio | A mixer with voices, buses, fades and pitch; WAV, OGG and MP3 decoding at load time; OpenAL and null outputs. |
| Testing | `Ion.Testing` with a deterministic clock, scripted input, event collection, screenshots and golden images. |

### Selected fixes

- Generators pin Roslyn 4.4 so they load in every SDK from 8.0 onwards.
- Publishing with `-p:PublishAot=true` no longer fails with `NETSDK1207` (generator projects ignore `PublishAot`).
- Silk.NET finds its GLFW and SDL natives on Ubuntu and other distributions its own resolver does not map to
  `linux-x64`.
- Veldrid's transitive `Newtonsoft.Json` 9.0.1 (GHSA-5crp-9r3c-p9vr) was lifted to 13.0.4 before Veldrid was removed.

## Released versions

| Version | Date | Summary |
|---|---|---|
| [0.2.5](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.5) | 2025-01-02 | Fixed generator references. |
| [0.2.4](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.4) | 2024-11-25 | Repository housekeeping (funding file). |
| [0.2.3](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.3) | 2024-11-24 | Initial ECS graphics components. |
| [0.2.2](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.2) | 2024-10-08 | Scene generators. |
| [0.2.1](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.1) | 2024-06-07 | Codebase cleanup. |
| [0.2.0](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.0) | 2024-06-05 | Initial SDL3 and WGPU integration (a hard-coded triangle). |
| [0.1.8](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.8) | 2024-01-24 | Sprite font support. |
| [0.1.7](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.7) | 2024-01-17 | .NET 8 and the audio API. |
| [0.1.6](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.6) | 2024-01-16 | Basic asset loading (Texture2D). |
| [0.1.5](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.5) | 2024-01-10 | Basic Texture2D loading. |
| [0.1.4](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.4) | 2024-01-08 | Renamed the project to Ion. |
| [0.1.3](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.3) | 2024-01-08 | Middleware architecture. |
| [0.1.2](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.2) | 2023-04-19 | Core engine. |
| [0.1.1](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.1) | 2022-10-10 | Entity methods. |
| [0.1.0](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.0) | 2022-10-09 | Initial commit, first ECS implementation, build and changelog automation. |

## Upgrading from 0.2

The changes most likely to touch a 0.2 game, and where to read about them:

| 0.2 code | Now | Read |
|---|---|---|
| `void M(GameTime dt, GameLoopDelegate next)` middleware | A plain step, or a `[Begin]`/`[End]` scope | [Systems](/Ion/concepts/systems/), `ION010` |
| `IEventEmitter`, `IEventListener` | `IEvents.Emit`, `EventReader<T>` | [Events](/Ion/concepts/events/) |
| `AddDebugUtils`, `ITraceTimer<T>` | `AddMetrics`, `IMetrics` | [Metrics and tracing](/Ion/tooling/metrics-and-tracing/) |
| `UseScene(Scene.X, ...)` from the scenes generator | `UseScene<TScene>` (library method) | [Scenes](/Ion/ecs/scenes/) |
| Veldrid types (`Texture2D`, `FontSet`, `IGraphicsContext`) | `ITexture2D`, `IFontSet`, the RHI | [Rendering overview](/Ion/rendering/overview/) |
| Setup in a static class | `Program.cs` on the builder API | [Application](/Ion/concepts/application/) |

```csharp title="Program.cs"
// 0.3 style: register on the builder, add to the schedule, run.
var builder = IonApplication.CreateBuilder(args);
builder.AddIon().AddSystem<GameSystem>();

using var game = builder.Build();
game.UseIon().UseSystem<GameSystem>();
game.Run();
```

## See also

- [Full CHANGELOG.md](https://github.com/jimbuck/Ion/blob/main/CHANGELOG.md).
- [Module map](/Ion/reference/module-map/).
- [Introduction](/Ion/getting-started/introduction/).
