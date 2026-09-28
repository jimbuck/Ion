---
title: Module map
description: Every Ion package and project, what it is for, its AddX and UseX entry points, and what it depends on.
sidebar:
  order: 4
---

Ion is a set of modules on top of `Ion.Core`. You reference the packages you need, register modules on the builder with
`builder.AddX()`, and add their systems to the schedule with `game.UseX()`. A module registers and adds what it depends
on, and a module registered twice (directly or through another one) is registered once.

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering3D().AddPhysics3D().AddSystem<GameSystem>();   // pulls in the ECS module, the 3D renderer and the engine core

using var game = builder.Build();
game.UseEcsRendering3D().UsePhysics3D().UseSystem<GameSystem>();
game.Run();
```

Every package targets `net10.0`. The templates reference `Ion`, `Ion.Extensions.Ecs` and `Ion.Testing` by package
(`IonVersion` 0.3.0); the samples in the repository use project references. Package ids are the project names below.

## What a module pulls in

| Builder and application | Registers and adds |
|---|---|
| `AddIon`, `UseIon` | The engine core: metrics, assets, graphics and input (windowed, or headless with `Ion:Headless`), the 2D renderer, audio, scenes, coroutines, the remote protocol. `AddAudio`, `AddMetrics`, `AddScenes`, `AddCoroutines`, `AddAssets` and `AddRemote` on the builder register it too. |
| `AddRendering3D`, `UseRendering3D` | The 3D renderer and the engine core. |
| `AddEcs`, `UseEcs` | The ECS module only. `AddEcsSerialization` adds the world serializers. |
| `AddEcsRendering`, `UseEcsRendering` | The 2D extraction, the ECS module and the engine core. |
| `AddEcsRendering3D`, `UseEcsRendering3D` | The 3D extraction, the ECS module and the 3D renderer (with the engine core). |
| `AddPhysics2D`, `UsePhysics2D` | 2D physics, the ECS module and the engine core (the debug drawing's sprite batch). `AddPhysics2DRemote` adds its remote methods. |
| `AddPhysics3D`, `UsePhysics3D` | 3D physics, the ECS module and the 3D renderer (the debug drawing). |
| `AddUi`, `UseUi` | The UI module and the engine core. `AddUiRemote` adds its remote methods. |
| `AddNetworking`, `UseNetworking` | Networking and the ECS module. `AddLiteNetLibTransport` and `AddLoopbackTransport` add a transport and networking. |
| `AddWeb`, `UseWeb` | The web server only (its HTTP listener is `Ion.Extensions.Http`). |
| `AddInputRecording`, `AddInputPlayback`, `AddScriptedInput` | Input recording, playback and scripted input. |

The `IServiceCollection` forms (`builder.Services.AddRendering3D(builder.Configuration)` and so on) remain for
composing the engine by hand, as the Scenes and Quad samples do. With them you register dependencies yourself.

## The engine

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion` | The batteries-included engine package: the `AddIon`/`UseIon` composition, headless selection, run settings (`Ion:Run:*`), and the builder forms of the core modules. Ships the schedule generator as an analyzer. | `AddIon`, `UseIon`, `AddRendering3D`, `UseRendering3D`, `AddGraphics`, `UseGraphics`, builder `AddAudio`, `AddMetrics`, `AddScenes`, `AddCoroutines`, `AddAssets`, `AddRemote` | `Ion.Core`, Assets, Audio, Coroutines, Metrics, Graphics.Null, Graphics.Headless, Windowing.SilkNet, Rendering2D, Rendering3D, Scenes, Remote |
| `Ion.Core` | The application builder, game loop, schedule runtime, event bus, storage and input plumbing. Ships the schedule generator as an analyzer. | `IonApplication.CreateBuilder`, `AddSystem`, `UseEvents`, `AddScriptedInput`, `AddInputRecording`, `AddInputPlayback` | `Ion.Core.Abstractions`, `Ion.Extensions.Metrics.Abstractions`, Microsoft.Extensions.Hosting |
| `Ion.Core.Abstractions` | The types games and libraries compile against: stages and step attributes, `StageOrder`, `GameTime`, `IEvents`, `IInputState` and `InputTracker`, `GameConfig`, schedule model, diagnostics codes, frame stats. | `UseSystem`, `AddInputTracker`, function steps (`app.Update(...)` and friends) | Microsoft.Extensions abstractions |
| `Ion.Generators` | Source generator: the compile-time schedule and composition root, interceptors, the generated event bus, `[Query]` loops, diagnostics `ION001` to `ION014`, `ION101` to `ION106`, `ION301` to `ION307`. | (analyzer) | Roslyn 4.4 (`netstandard2.0`) |

## Assets, audio, coroutines, scenes, metrics

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion.Extensions.Assets` | Asset loading and caching by path, hot reload. | `AddAssets`, `UseAssets` | `Ion.Extensions.Assets.Abstractions` |
| `Ion.Extensions.Assets.Abstractions` | `IAssetManager`, loaders, asset ids, `AssetReloadedEvent`. | | Core.Abstractions |
| `Ion.Extensions.Audio` | The engine mixer: voices, buses, fades, WAV/OGG/MP3 decoding, the OpenAL and null outputs. | `AddAudio`, `UseAudio`, `AddNullAudio`, `UseNullAudio` | Audio.Abstractions, NVorbis, NLayer, `Silk.NET.OpenAL.Soft.Native` |
| `Ion.Extensions.Audio.Abstractions` | `IAudioManager`, `ISoundEffect`, `AudioBus`, `VoiceHandle`. | | Assets.Abstractions |
| `Ion.Extensions.Coroutines` | The shared coroutine runner, stepped in Update. | `AddCoroutines`, `UseCoroutines` | Coroutines.Abstractions |
| `Ion.Extensions.Coroutines.Abstractions` | `ICoroutineRunner`, `Wait`, `IWait`. | | Core.Abstractions |
| `Ion.Extensions.Coroutines.Generators` | An early generator that only adds a `[Coroutine]` marker attribute; no other project references it. | (analyzer) | Roslyn |
| `Ion.Extensions.Scenes` | Scenes with their own scope and schedule, run by `SceneSystem`. | `AddScenes`, `UseScene`, `UseScene<TScene>`, `EmitChangeScene` | Scenes.Abstractions |
| `Ion.Extensions.Scenes.Abstractions` | `ISceneBuilder`, scene events, `scene.UseSystem`. | | Core.Abstractions |
| `Ion.Extensions.Scenes.Generators` | Source generator for scene enums (`Scene`/`Scenes`, a `[ScenesEnum]` marker) and the `Use{Stage}<TService...>` overloads on scene builders. | (analyzer) | Roslyn |
| `Ion.Extensions.Metrics` | Frame profiler, frame stats, frame log, Chrome traces, the `Ion` meter, the overlay. | `AddMetrics`, `UseMetrics` (obsolete: `AddDebugUtils`, `UseDebugUtils`) | Metrics.Abstractions, Graphics.Abstractions, Assets.Abstractions |
| `Ion.Extensions.Metrics.Abstractions` | `IMetrics`, counters, gauges, histograms, span ids. | | Core.Abstractions |
| `Ion.Extensions.Metrics.Tracy` | Live Tracy zones, frame marks and plots (native TracyClient, win-x64 and linux-x64). | `UseMetricsTracy` | Core.Abstractions, Tracy-CSharp |

## Graphics and rendering

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion.Extensions.Graphics.Abstractions` | `IWindow`, `ISpriteBatch`, textures and fonts, `GraphicsConfig`, `WindowConfig`, the 3D data types (`Transform`, `Camera`, lights, materials), the RHI (`Ion.Extensions.Graphics.Rhi`), `GraphicsBackendSelector`, `IGraphicsFrame`. | | Core.Abstractions, Assets.Abstractions |
| `Ion.Extensions.Windowing.SilkNet` | A GLFW or SDL window and input pumped by Ion's loop; touch through SDL; NativeAOT substitutions for Silk.NET. | `AddSilkWindowing`, `UseSilkWindowing` | Graphics.Abstractions, Silk.NET windowing and input (GLFW, SDL) |
| `Ion.Extensions.Graphics.Vulkan` | The Vulkan RHI backend (MoltenVK on Apple platforms). | `AddVulkanGraphics`, `UseVulkanGraphics` | Graphics.Abstractions, `Silk.NET.Vulkan` |
| `Ion.Extensions.Graphics.GLES` | The OpenGL ES 3.1 RHI backend with ES 3.0 fallbacks, windowed or through EGL. | `AddGlesGraphics`, `UseGlesGraphics` | Graphics.Abstractions, `Silk.NET.OpenGLES` |
| `Ion.Extensions.Graphics.Headless` | Backend selection for a window or offscreen (`RhiGraphics`), headless rendering with capture and PNG screenshots. | `AddRhiGraphics`, `UseRhiGraphics`, `AddHeadlessRendering`, `UseHeadlessRendering`, `AddHeadlessGraphics`, `UseHeadlessGraphics` | Graphics.Null, Graphics.Vulkan, Graphics.GLES, Rendering2D |
| `Ion.Extensions.Graphics.Null` | The headless backend: `NullWindow`, `NullInputState` (scripted input), the recording `NullSpriteBatch`, header-reading loaders. | `AddNullGraphics`, `UseNullGraphics` | Graphics.Abstractions, ImageSharp |
| `Ion.Extensions.Rendering2D` | The sprite batch on the RHI, texture and font loaders, glyph atlas, `TextureFactory`, render targets. | `AddRendering2D`, `UseRendering2D` | Graphics.Abstractions, FontStashSharp, ImageSharp |
| `Ion.Extensions.Rendering3D` | The 3D renderer: culling, batching, render graph, PBR and unlit materials, shadows, glTF and cube maps. | `AddRendering3D` (services); builder `AddRendering3D`/`UseRendering3D` come from `Ion` | Graphics.Abstractions, Rendering2D, ImageSharp |
| `Ion.Shaders` | Build-time shader compilation (`IonShader` items to SPIR-V and GLSL ES). Not a package: import `Ion.Shaders.targets`. | `<IonShader Include="..." />` | Shaderc, SPIRV-Cross |

## ECS and physics

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion.Extensions.Ecs` | The ECS module on Arch 2.1: a `World` per scope, `Commands` playback, `[Query]` binding, transform propagation, sprite animation, `NameRegistry`, world serialization. | `AddEcs`, `UseEcs`, `scene.UseEcs()`, `AddEcsSerialization` | Ecs.Abstractions, `Ion.Core`, Scenes.Abstractions, Remote.Abstractions |
| `Ion.Extensions.Ecs.Abstractions` | `[Query]` and filters, `Commands`, built-in components (`Transform2D`, `Sprite`, `Parent`/`Children`, ...), `SpawnModel`, `EcsComponents.Register<T>()`. | | Core.Abstractions, Graphics.Abstractions, Arch |
| `Ion.Extensions.Ecs.Rendering` | Sprite and 3D extraction from entities. | `AddEcsRendering`, `UseEcsRendering`, `AddEcsRendering3D`, `UseEcsRendering3D` (and the scene forms) | `Ion.Extensions.Ecs`, `Ion` |
| `Ion.Extensions.Physics2D` | 2D physics on Box2D v3, synchronized with `Transform2D`, with events, queries and debug drawing. | `AddPhysics2D`, `UsePhysics2D`, `scene.UsePhysics2D()` | Physics2D.Abstractions, `Ion.Extensions.Ecs`, `Ion`, Box2D.NET |
| `Ion.Extensions.Physics2D.Abstractions` | `RigidBody2D`, `Collider2D`, `Joint2D`, `Collision2D`, `Trigger2D`, `IPhysicsWorld2D`, `Physics2DConfig`. | | Core.Abstractions, Ecs.Abstractions |
| `Ion.Extensions.Physics2D.Remote` | Remote methods `physics2d.bodies` and `physics2d.raycast`. | `AddPhysics2DRemote` | Physics2D, Remote.Abstractions |
| `Ion.Extensions.Physics3D` | 3D physics on BepuPhysics v2, synchronized with `Transform`, with events, queries and debug drawing. | `AddPhysics3D`, `UsePhysics3D`, `scene.UsePhysics3D()` | Physics3D.Abstractions, `Ion.Extensions.Ecs`, `Ion`, BepuPhysics |
| `Ion.Extensions.Physics3D.Abstractions` | `RigidBody3D`, `Collider3D`, `Joint3D`, `Collision3D`, `Trigger3D`, `IPhysicsWorld3D`, `Physics3DConfig`. | | Core.Abstractions, Ecs.Abstractions |

## UI, web, networking, remote

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion.Extensions.UI` | Immediate-mode UI on the sprite batch with flex layout, themes and focus navigation. | `AddUi`, `UseUi` | UI.Abstractions, `Ion`, Graphics.Abstractions |
| `Ion.Extensions.UI.Abstractions` | `IUiTree`: the inspectable widget tree and its commands. No dependencies. | | none |
| `Ion.Extensions.UI.Remote` | Remote methods `ui.tree`, `ui.click`, `ui.set_value`, `ui.focus`, `ui.type`, `ui.back`. | `AddUiRemote` | UI, Remote.Abstractions |
| `Ion.Extensions.Http` | The embedded HTTP/1.1 and WebSocket server core shared by the remote and web modules. | | Logging abstractions |
| `Ion.Extensions.Web` | The web server module: generated routing, static files, push channels, `/rpc`. | `AddWeb`, `UseWeb`, `AddWebRoutes` | Web.Abstractions, `Ion.Extensions.Http`, `Ion.Core` |
| `Ion.Extensions.Web.Abstractions` | `[Http]`, `[WebSocket]`, `[FromBody]`, `[WebJson]`, `WebRequest`/`WebResponse`, `IWebServer`. | | Core.Abstractions, Http |
| `Ion.Extensions.Web.Generators` | Source generator: route tables, diagnostics `ION401` to `ION407`. | (analyzer) | Roslyn |
| `Ion.Extensions.Networking` | Multiplayer: replication, snapshots, messages, prediction, interpolation, lag compensation, interest, the loopback transport. | `AddNetworking`, `UseNetworking`, `AddLoopbackTransport` | Networking.Abstractions, `Ion.Extensions.Ecs`, `Ion.Core`, Metrics.Abstractions, Remote.Abstractions |
| `Ion.Extensions.Networking.Abstractions` | `[Replicated]`, `[Predicted]`, `[Interpolated]`, `[NetworkMessage]`, `NetworkId`, `NetworkConfig`, the session, world, message and prediction interfaces. | | Core.Abstractions, Ecs.Abstractions |
| `Ion.Extensions.Networking.Generators` | Source generator: serializers, delta encoders, registries, diagnostics `ION201` to `ION210`. | (analyzer) | Roslyn |
| `Ion.Extensions.Networking.LiteNetLib` | The UDP transport on LiteNetLib. | `AddLiteNetLibTransport` | `Ion.Extensions.Networking`, LiteNetLib |
| `Ion.Extensions.Remote` | The remote inspection protocol (JSON-RPC over HTTP, WebSocket and stdio). | `AddRemote`, `UseRemote` | Remote.Abstractions, Http, `Ion.Core`, Metrics, Graphics.Abstractions, Scenes |
| `Ion.Extensions.Remote.Abstractions` | `IRemoteMethodProvider`, remote resources and events, the `Ion.Remote.IsSupported` switch. | `AddRemoteMethods`, `AddRemoteResource`, `AddRemoteEvent` | Core.Abstractions |

## Tooling

| Package | Purpose | Entry points | Depends on |
|---|---|---|---|
| `Ion.Testing` | `IonTestHost`: headless, deterministic stepping, scripted input, event collection, screenshots and golden images, entry point tests. | `new IonTestHost()`, `UseEntryPoint<Program>()`, `IonTestHost.RunEntryPoint<Program>(frames)`, `GoldenImage` | `Ion`, `Ion.Extensions.Ecs`, ImageSharp |
| `Ion.Tools` | The `ion` command line (a dotnet tool): `new`, `run`, `schedule`, `bench`, `trace`, `diff`, `remote`, `publish`, `mcp`. | `ion ...` | `Ion.Tools.Mcp` |
| `Ion.Tools.Mcp` | Remote protocol client, game runner, image diff and the MCP server for coding agents. | `ion mcp` | ImageSharp |
| `Ion.Templates` | `dotnet new ion-2d`, `ion-3d`, `ion-ecs` (in `templates/`). | `dotnet new` | |

## Choosing packages

| You want | Reference | Call |
|---|---|---|
| A 2D game | `Ion` | `AddIon` / `UseIon` |
| A 2D game on entities | `Ion`, `Ion.Extensions.Ecs`, `Ion.Extensions.Ecs.Rendering` | `AddEcsRendering` / `UseEcsRendering` |
| A 3D game on entities | the same | `AddEcsRendering3D` / `UseEcsRendering3D` |
| Physics | add `Ion.Extensions.Physics2D` or `Ion.Extensions.Physics3D` | `AddPhysics2D` / `AddPhysics3D` |
| Menus | add `Ion.Extensions.UI` | `AddUi` / `UseUi` |
| Multiplayer | add `Ion.Extensions.Networking` and `Ion.Extensions.Networking.LiteNetLib` | `AddNetworking().AddLiteNetLibTransport()` / `UseNetworking` |
| A companion web page | add `Ion.Extensions.Web` | `AddWeb` / `UseWeb` |
| Tests | `Ion.Testing` in the test project | `new IonTestHost().UseEntryPoint<Program>()` |

The generator packages must be referenced as analyzers when you use project references (the `Ion` and `Ion.Core`
packages bring `Ion.Generators` automatically); see [Source generators](/Ion/concepts/source-generators/).

## See also

- [Application](/Ion/concepts/application/): the builder and modules.
- [Installation](/Ion/getting-started/installation/) and [Templates](/Ion/getting-started/templates/).
- [Configuration keys](/Ion/reference/configuration/).
