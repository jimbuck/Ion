---
title: Next steps
description: Where to go after your first Ion game, with reading paths for 2D, 3D, ECS, multiplayer, tooling and shipping.
sidebar:
  order: 5
---

You have a game that builds, runs windowed and headless, and has tests. This page suggests what to read next, depending
on what you want to build. Every path starts with the same few concept pages, because the builder, the stages and the
schedule show up everywhere.

## Read the core concepts first

These pages explain the model behind everything else, in about half an hour of reading:

| Page | You learn |
|---|---|
| [The application](/Ion/concepts/application/) | `CreateBuilder`, `builder.AddX()` versus `builder.Services.AddX(...)`, `AddSystem`, `Build`, `UseX`, and which module brings which. |
| [Stages](/Ion/concepts/stages/) | What runs in `Init`, `First`, `FixedUpdate`, `Update`, `Render`, `Last` and `Destroy`, and the engine's order bands. |
| [Systems](/Ion/concepts/systems/) | Step signatures, injected parameters, `Order`, `[After<T>]`/`[Before<T>]`, `[Begin]`/`[End]` scopes and function steps. |
| [Events](/Ion/concepts/events/) | Emitting and reading typed events, how long they live, and the generated bus. |
| [Services and configuration](/Ion/concepts/services-and-configuration/) | Dependency injection, `appsettings.json`, command-line switches and the options pattern. |
| [Time and determinism](/Ion/concepts/time-and-determinism/) | `GameTime`, the fixed step, clocks and seeds. |
| [The game loop](/Ion/concepts/game-loop/) | Frame structure, pacing, headless runs and exiting. |
| [Source generators](/Ion/concepts/source-generators/) | What is generated at compile time and how to keep your code generator-friendly. |

## Pick a path

### A 2D game

1. [Rendering overview](/Ion/rendering/overview/) and [Sprites](/Ion/rendering/sprites/): the sprite batch, sort and
   blend modes, render targets.
2. [Text](/Ion/rendering/text/) and [Assets](/Ion/rendering/assets/): fonts, textures and hot reload.
3. [2D cameras](/Ion/rendering/cameras-2d/): scrolling and zooming a world.
4. [Keyboard and mouse](/Ion/interaction/input/keyboard-and-mouse/), [Gamepad](/Ion/interaction/input/gamepad/) and
   [Audio](/Ion/interaction/audio/).
5. Examples: [Breakout](/Ion/examples/breakout/) (plain systems) and [Sprites 100k](/Ion/examples/sprites-100k/) (the
   batch under load).

### A game on the ECS

1. [ECS overview](/Ion/ecs/overview/), [Entities and commands](/Ion/ecs/entities-and-commands/) and
   [Queries](/Ion/ecs/queries/): the `World`, `Commands` and `[Query]` methods.
2. [Transforms](/Ion/ecs/transforms/) and [ECS rendering](/Ion/ecs/ecs-rendering/): sprites and meshes drawn from
   entities.
3. [Scenes](/Ion/ecs/scenes/) and [Coroutines](/Ion/ecs/coroutines/).
4. [Physics overview](/Ion/physics/overview/) and [2D physics](/Ion/physics/physics-2d/).
5. Examples: [Breakout ECS](/Ion/examples/breakout-ecs/) and [Scenes](/Ion/examples/scenes/).

### A 3D game

1. [3D overview](/Ion/rendering/3d/overview/), [Meshes and materials](/Ion/rendering/3d/meshes-and-materials/) and
   [Lighting and shadows](/Ion/rendering/3d/lighting-and-shadows/).
2. [glTF models](/Ion/rendering/3d/models-gltf/) and [3D cameras](/Ion/rendering/3d/cameras/).
3. [3D physics](/Ion/physics/physics-3d/).
4. Examples: [Cubes](/Ion/examples/cubes/) and [Model](/Ion/examples/model/).

### Menus and UI

1. [UI overview](/Ion/interaction/ui/overview/), [Widgets](/Ion/interaction/ui/widgets/) and
   [Layout and styling](/Ion/interaction/ui/layout-and-styling/).
2. [Focus navigation](/Ion/interaction/ui/focus-navigation/) for gamepad-only devices.
3. Example: [Menu](/Ion/examples/menu/).

### Multiplayer and companion apps

1. [Networking overview](/Ion/networking/overview/) and [Multiplayer](/Ion/networking/multiplayer/overview/):
   replication, messages, prediction and a dedicated server.
2. [HTTP server](/Ion/networking/http-server/) and [Web endpoints](/Ion/networking/web-endpoints/) for companion apps.
3. Examples: [Breakout Net](/Ion/examples/breakout-net/) and [Companion](/Ion/examples/companion/).

### Tooling, testing and coding agents

1. [Testing](/Ion/tooling/testing/) and [Snapshots and goldens](/Ion/tooling/snapshots-and-goldens/).
2. [The ion CLI](/Ion/tooling/ion-cli/), [Metrics and tracing](/Ion/tooling/metrics-and-tracing/) and
   [Benchmarks](/Ion/tooling/benchmarks/).
3. [Agentic development](/Ion/tooling/agentic-development/), the [remote protocol](/Ion/tooling/remote-protocol/) and
   the [MCP server](/Ion/tooling/mcp-server/).

### Shipping

1. [Platforms](/Ion/platforms/overview/) and [Desktop](/Ion/platforms/desktop/).
2. [Native AOT](/Ion/platforms/native-aot/) and [Publishing](/Ion/platforms/publishing/): one-property presets such as
   `-p:IonTarget=linux-x64`.
3. [Mobile](/Ion/platforms/mobile/) and the [R36S handheld](/Ion/platforms/r36s/).

## Habits that pay off

- **Run `ion schedule` when a step does not run when you expect.** It prints every stage in run order with orders,
  scopes and constraints. `--Ion:PrintSchedule=true` prints the same at startup.
- **Keep registrations in `Program.cs`.** The generator compiles what it sees there into direct calls, and the tests run
  that file as it is.
- **Simulate in `FixedUpdate`, draw in `Render`.** Fixed-step simulation plus a seed (`Ion:Seed`) makes runs repeatable,
  which is what makes snapshot tests and replays work.
- **Keep the build warning-free.** Schedule, event, query, network and web mistakes are compile-time diagnostics with
  the rule in the message. See [Diagnostics](/Ion/reference/diagnostics/).
- **Add a headless test for every behavior you care about.** `IonTestHost.RunEntryPoint<Program>(frames)` makes it a
  few lines.

## Reference

- [Stage order](/Ion/reference/stage-order/): every engine step's order value.
- [Configuration](/Ion/reference/configuration/): every `Ion:*` key.
- [Diagnostics](/Ion/reference/diagnostics/): every `ION` diagnostic id.
- [Module map](/Ion/reference/module-map/) and [Changelog](/Ion/reference/changelog/).
