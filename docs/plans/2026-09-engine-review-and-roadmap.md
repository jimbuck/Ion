# Ion Engine: Review and Multi-Stage Roadmap (September 2026)

This document is the output of a full review of the repository at commit `67d2dd4` (v0.2.5): the code, the samples, the build on a fresh Linux machine, a NativeAOT publish, and a new benchmark suite (`Ion/Ion.Benchmarks`). It ends with a staged plan for turning Ion into a code-first, high-performance engine that is also the best engine for agentic development.

The goals it is written against, as stated by the owner:

1. Code-first, high-performance game engine. Keep the middleware pattern and the "no async" design.
2. Best engine for agentic development (AI coding agents can build, run, verify and debug a game without a human in the loop).
3. Maximum performance via source generation and native compilation, with stack traces that show user code, not engine wrappers.
4. Replace the stale Veldrid backend with Silk.NET, keeping Ion's own game loop. Multiple platform builds including WebGPU.
5. Ready-to-use 3D rendering, and a story for how 2D/3D rendering integrates with an ECS (Arch or Friflo, whichever is faster).
6. Modules beyond rendering: audio is P0 (it must work on every target), UI and physics are P1, a web server for companion apps/integrations and multiplayer networking are P2.

Decisions confirmed by the owner after the first draft (they replace the open questions that used to close this document):

- **Silk.NET is the rendering platform, full stop**, regardless of whether the output is Vulkan, OpenGL ES, Metal or WebGPU. Web deployment is an option to keep open, not a requirement.
- **Primary platforms are desktop (Windows, macOS, Linux), tablets and phones (iOS, Android), and the R36S handheld** (Rockchip RK3326, quad Cortex-A35, Mali-G31 GPU, ArkOS/Linux arm64; OpenGL ES 3.1 through Panfrost, Vulkan only experimentally through PanVK). That last target makes an OpenGL ES backend mandatory, not optional.
- **Move to the latest .NET (10) now.**
- **The generated middleware dispatch is important for its own sake** (stack traces, AOT, validation), not just for the nanoseconds.
- **Fix the event bus**, and use source generation for it too if that buys memory, speed, stack traces, AOT safety or validation (it does, see 4.4).
- **ECS: Arch or Friflo, decided by measurement** (see 5.7 and 4.9).
- **End goal:** an ECS-and-middleware-based 2D/3D engine that is very fast, flexible, and built for agentic development.

---

## 1. Executive summary

**What is good and worth keeping.** The core idea is sound and distinctive: an `IonApplication.CreateBuilder()` that mirrors `Host.CreateApplicationBuilder`, systems that are plain classes with `[Init]/[First]/[FixedUpdate]/[Update]/[Render]/[Last]/[Destroy]` methods, and the middleware `next(dt)` semantics that let a system wrap the rest of the stage (the way `GraphicsSystem` brackets user rendering between `BeginFrame` and `EndFrame`). Scenes as DI scopes, a single-threaded pull-based coroutine runner, and a frame-event bus are all consistent with "no async". The sprite batch is instanced and pooled. The module split (`Core`, `Extensions.*`, `*.Abstractions`) is the right shape for a NuGet-distributed engine.

**What is broken today.** The engine does not run cleanly on a fresh machine:

- The generators are compiled against Roslyn 4.10 so the `net8.0` SDK 8.0.1xx line (Roslyn 4.8) silently drops them and `Ion.Examples.Scenes` fails to compile with CS1593. It builds with SDK 10.
- `GraphicsContext` casts Ion's `GraphicsBackend` enum onto Veldrid's enum with different ordinals, so requesting Vulkan runs OpenGL, OpenGL runs Metal, and Metal runs OpenGL ES. Every sample that asks for Vulkan gets OpenGL.
- `WindowConfig` is never bound to configuration, so the `Ion:Window` section in every `appsettings.json` is ignored (windows come up at 960x540).
- Audio is `DirectSoundOut`, so it is Windows-only; the non-ECS Breakout also loads `Bonk.wav` from a file named `bonk.wav`, which fails on case-sensitive filesystems.
- `NullTraceTimer<T>.Start` boxes a struct through an interface on every call, so every `trace.Start()` in every system allocates when the Debug package is not installed, and the `#if DEBUG` guards mean Release builds have no tracing at all.
- Tests: 8 tests, 1 skipped, no coverage of coroutines, trace, assets, audio, storage, timing, or the binder. No headless or deterministic harness exists (`GameLoop.Run` owns a `Stopwatch` and `Thread.Sleep`).
- The WGPU experiment is a hardcoded triangle with dead input, invalid WGSL and dangling native pointers; `Ion_old` does not build. Neither is in the solution.

**Where to go.** The plan below keeps the API shape that users write against and replaces the machinery under it in stages: a fixed, tested, headless-capable .NET 10 baseline (Stage 0-1); a source-generated, reflection-free schedule with clean stack traces (Stage 2); a source-generated typed event bus, bitset input, a real metrics story and a cross-platform audio module (Stage 3); a Silk.NET graphics layer with Vulkan and OpenGL ES backends behind one RHI and a rewritten 2D renderer (Stage 4); built-in ECS integration and the 3D SDK (Stage 5), then UI and physics (Stage 5b); the agentic toolchain (headless CLI, screenshots, remote inspection, MCP) (Stage 6), then the web server and networking modules (Stage 6b); and native publishing for desktop, the R36S handheld, mobile and optionally the browser (Stage 7). Each stage has acceptance criteria and benchmark targets so an agent can drive it.

---

## 2. What was actually run

| Step | Result |
|---|---|
| `dotnet restore` + `dotnet build -c Release` with SDK 8.0.131 | Engine and both Breakout samples build. `Ion.Examples.Scenes` fails: 7x `CS1593` because the `UseUpdate<TService...>` overloads come from `Ion.Core.InternalGenerators`, which references Roslyn 4.10 and is rejected by the 4.8 compiler (`CS9057`). |
| Same with SDK 10.0.112 | Everything builds, 4 warnings (`SceneSystem._activeTransition` never assigned, `TraceManager._nextId` unused, unread primary-constructor parameters). |
| `dotnet test --filter Category!=E2E` | 8 tests: 7 pass, 1 skipped (`SceneGeneratorTests` is `Skip="WIP"`). ~1.7 s. |
| `Ion.Examples.Breakout.ECS` under Xvfb + Mesa lavapipe (Vulkan) | Runs (window, device, shaders, sprite buffer, resize). Reaching Vulkan required requesting `Direct3D12` in config because of the enum bug (the Veldrid backend has since been removed). |
| Same with OpenGL (the backend the samples actually get) | `Unable to create OpenGL Context` under Xvfb (Veldrid requests a D32_S8 GL visual). |
| `Ion.Examples.Breakout` (non-ECS) | Crashes at init: `Could not find file .../Assets/Bonk.wav` (file is `bonk.wav`). |
| `Ion.Examples.Scenes` | Crashes at init: asks for Vulkan in code, gets OpenGL, cannot create a GL context. |
| NativeAOT publish of the ECS sample (`linux-x64`, .NET 8 ILC via SDK 10) | Succeeds: 24 MB binary, starts, creates a Vulkan device and shaders. 66 warnings, only 2 from Ion itself (`Configure<GraphicsConfig>` reflection binder in the Veldrid project). The rest: Aether.Physics2D XML serialization (32), NAudio COM (5), Vortice DXGI (3), Arch `ArrayRegistry`/`ComponentRegistry` dynamic code (2), ImageSharp (2), `Microsoft.Extensions.DependencyModel` pulled in by Veldrid's `NativeLibraryLoader` (1). Publishing the sample project directly fails with `NETSDK1207` because `PublishAot` flows into the `netstandard2.0` generator projects. |
| `Ion/Ion.Benchmarks` (new) | See section 5 for the numbers. |

The crash trace from the first failed run is a good illustration of the stack-trace problem the owner wants solved. Half of the frames are engine wrappers:

```
at Ion.Extensions.Graphics.GraphicsSystem.Init(GameTime dt, GameLoopDelegate next) in .../Systems/GraphicsSystem.cs:line 13
at Ion.SystemMiddlewareBinder.GameTimeNextVoidMiddlewareBinder.<>c__DisplayClass4_0.<CreateMiddleware>b__0(GameTime dt) in .../SystemMiddlewareBinder.cs:line 59
at Ion.Extensions.Graphics.WindowSystem.Init(GameTime dt, GameLoopDelegate next) in .../Systems/WindowSystem.cs:line 16
at Ion.SystemMiddlewareBinder.GameTimeNextVoidMiddlewareBinder.<>c__DisplayClass4_0.<CreateMiddleware>b__0(GameTime dt) in .../SystemMiddlewareBinder.cs:line 59
at Ion.Core.GameLoop.Run() in .../GameLoop.cs:line 65
```

---

## 3. Current-state assessment

### 3.1 System registration and configuration

How it works today (`Ion.Core.Abstractions/Builder/UseSystemExtensions.cs:49-60`, `SystemMiddlewareBinder.cs`): `UseSystem<T>()` reflects over the public methods of `T`, looks for one of seven attributes, picks one of three supported signatures (`GameLoopDelegate(GameLoopDelegate next)`, `void(GameTime, GameLoopDelegate)`, `void()`), resolves the instance from DI at pipeline-build time and wraps it in a closure. `MiddlewarePipelineBuilder.Build()` folds the closures right-to-left into one `GameLoopDelegate` per stage. `Ion.Core.InternalGenerators` emits 56 `Use{Stage}<TService0..7>` overloads that resolve services from `app.Services` inside the closure.

Assessment:

- Ordering is implicit and unenforced. Systems must be `UseSystem`'d after `UseIon()` or `Draw` throws (`SpriteRenderer.cs:219`); `UseDebugUtils` must be first; `UseScene` terminates every stage so anything registered after it is dead (the Scenes sample even contains a `"NEVER GETTING CALLED!"` middleware). There is no way to say "run after X" or "run before Render".
- Reflection binding is annotated for trimming but produces closure frames in every stack trace and an indirect delegate call per hop. Under NativeAOT there is no dynamic PGO, so those calls never get devirtualized.
- DI lifetimes are inconsistent: `IEventListener` is transient and never detached (`EventEmitter.AttachListener` list grows forever); `ICoroutineRunner` is transient (every injection is a different runner, nothing drives `Update`); both samples use `AddScoped` without scenes (resolved from the root provider, which `ValidateScopes` would reject in Development).
- Interfaces are downcast to concrete types throughout (`(Window)window`, `(EventEmitter)eventEmitter`, `(TraceManager)traceManager`, `(Font)font`), so the abstractions are not substitutable for testing.
- Configuration binding is reflection-based in the Veldrid project (no `EnableConfigurationBindingGenerator`), `GraphicsConfig.ClearColor` is a get-only `Color` so it cannot be bound from JSON at all, `GraphicsConfig.MaxFPS` is unused (the loop reads `GameConfig.MaxFPS`), and `WindowConfig` is never registered.
- `GameLoop.Run` (`Ion.Core/GameLoop.cs:53-135`) is a fixed-timestep accumulator loop with `Thread.Sleep`-based pacing (1 ms granularity on Windows unless the timer resolution is raised) and a 100 ms max frame clamp. `FixedGameTime.Delta` is `1/MaxFPS`, which conflates render pacing with the physics step. There is no injectable clock, so nothing about timing is testable.
- Hot reload (`HotReloadHandler.cs`) sets `Rebuild = true` on metadata update and rebuilds the pipelines, which is a nice agentic property to keep.

### 3.2 Events

`EventEmitter` keeps two `RingBuffer<IEvent>` (current and previous frame). `Emit<T>` creates an `Event<T>` record struct and stores it as `IEvent`, which boxes (one 40-56 byte allocation per event). `EventListener.On<T>` scans both buffers linearly, type-tests each entry, and tracks seen ids in two `HashSet<ulong>` per listener. `EventId` is a `uint` stored in `HashSet<ulong>`. `EventType` is `typeof(T).GetHashCode()`, which is not stable across runs. `Handled` is settable on the interface but nothing sets it. Cost scales as listeners x calls x total events per frame regardless of type. See section 5 for measured allocations.

### 3.3 Rendering (Veldrid backend)

Detailed findings are in `Ion.Extensions.Graphics.Veldrid` review notes; the ones that matter for the plan:

- `GraphicsDevice.WaitForIdle()` every frame (`GraphicsContext.cs:119`) serializes CPU and GPU. It exists to make single-buffered `Map(Write)` of the dynamic instance buffer safe.
- One GPU buffer plus two resource sets per texture, kept forever (`SpriteRenderer.cs:45,321-343`); disposed textures are never evicted.
- 80-byte `SpriteInstance` with a 16-byte float `Color` and a 16-byte scissor that is always the default; the scissor costs two `Vector4.Transform` per sprite on the CPU and a per-pixel `discard` in the fragment shader, which disables early-Z.
- No sorting (the `CompareTo` is unused); cross-texture order is dictionary insertion order; depth test `GreaterEqual` with depth write and alpha blend means translucent sprites across textures fight.
- Projection matrix re-uploaded per texture group; two command lists submitted per frame.
- Text goes through FontStashSharp layout every frame, one `trace.Start` per glyph.
- `ISpriteBatch` has no `Begin/End`, camera matrix, blend/sampler state, sort mode, scissor, render target or custom effect; `IFont.MeasureString` throws `NotImplementedException`; `DrawString` with a default color draws transparent text.
- Veldrid drags in `NativeLibraryLoader` -> `Microsoft.Extensions.DependencyModel 2.0.3` -> `Newtonsoft.Json 9.0.1` and `Vortice`/`SharpGen` COM, plus `Veldrid.SPIRV` compiling GLSL at runtime. `Assimp` and `Veldrid.ImageSharp` are referenced but unused. The window is always created with the SDL OpenGL flag and `threadedProcessing: true`, which is unsupported on macOS.

### 3.4 Input

`InputState.Step()` copies the Veldrid snapshot into two dictionaries and a `HashSet<Key>`; each query is a hash lookup. Press and release of the same key in one frame keeps only the last event so `Pressed` is lost; `_downKeys` is never cleared on focus loss (stuck keys); `Down(MouseButton)` reads the raw snapshot while `Pressed` reads the copy. The abstraction has no text input, gamepad, mouse delta, relative mouse mode or event enumeration, and the `Key`/`ModifierKeys`/`MouseButton` enums are copies of Veldrid's. The WGPU backend's input is entirely commented out.

### 3.5 Metrics

The only instrumentation is the Debug package's trace timers, which produce Chrome trace JSON on `Destroy`. When enabled (DEBUG only) each `Start` does an `Interlocked.Increment`, a `Stopwatch` read and a boxed `TraceTimerInstance`; each `Stop` concatenates strings and pushes into an unbounded `ConcurrentBag` (the committed `trace.json` is 33.8 MB). `IsEnabled` is read once at construction. There are no counters, gauges, frame-time histograms, GC or allocation counters, draw-call or sprite counts, `System.Diagnostics.Metrics` integration, overlay, or Release-build support. `Ion_old/Metrics.cs` has the right counter shape but nothing populates it.

### 3.6 Scenes, coroutines, assets, audio, storage

- Scenes: a process-wide static `_scenesAdded` means the second `IonApplication` in a process never gets a `SceneSystem`; `SceneSystem` never calls `next`; unknown scene ids warn and then throw `KeyNotFoundException`; transitions are dead code; `Dispose` skips the scene's `Destroy`. The scoped-DI-per-scene design itself is good.
- Coroutines: pull-based and correct for "no async", but transient runner, boxed `IWait` structs per yield, LINQ in `IsActive`, `FindIndex` + `RemoveAt` in `Stop`, and a source generator that only emits an unused attribute.
- Assets: synchronous only, no caching for textures or sounds (every `Load` creates a new GPU texture), scoped manager never disposes, loaders hard-cast, `AssetBatch` fully commented out, paths rooted at `Environment.CurrentDirectory` rather than `AppContext.BaseDirectory`.
- Audio: NAudio `DirectSoundOut` (Windows-only), `pitchShift` computed and discarded, `MasterVolume` defaults to 10, fixed 48 kHz stereo with no resampling, trace timer leaked on `volume == 0`.
- Storage: `File.OpenWrite` without truncation.

### 3.7 Generators

`Ion.Core.InternalGenerators` and `Ion.Extensions.Scenes.Generators` only emit boilerplate overloads and an enum-to-int extension; `Ion.Generators` (not in the solution) is an abandoned "scene class" generator; `Coroutines.Generators` emits an unused attribute. All reference `Microsoft.CodeAnalysis.CSharp 4.7-4.10` and `SourceGeneratorUtils.SourceGeneration 0.0.2`; they should target the oldest supported Roslyn (4.4 for .NET 8 SDKs) or be replaced by the schedule generator in Stage 2.

### 3.8 Confirmed bugs (fix in Stage 0)

| # | Where | Bug |
|---|---|---|
| 1 | `Graphics.Veldrid/GraphicsContext.cs:60` | `(Veldrid.GraphicsBackend)config.PreferredBackend` is an off-by-one enum cast (Ion inserted `Direct3D12` at ordinal 1). Vulkan -> OpenGL, OpenGL -> Metal, Metal -> OpenGLES. |
| 2 | `Graphics.Veldrid/BuilderExtensions.cs` | `WindowConfig` is never `Configure<>`'d; `Ion:Window` in appsettings is ignored. |
| 3 | `Ion.Examples.Breakout/Program.cs` | Loads `Bonk.wav`; asset is `bonk.wav`. Fails on Linux/macOS. |
| 4 | `Ion.Core.InternalGenerators.csproj`, `Scenes.Generators.csproj` | Roslyn 4.10 reference breaks 8.0.1xx SDKs (`CS9057` then `CS1593`). |
| 5 | `Ion.Core/Debug/NullTraceTimer.cs:9,17` | `new NullTimerInstance()` boxed through `ITraceTimerInstance` on every `Start`. |
| 6 | `Extensions.Scenes/BuilderExtensions.cs:7` | Static `_scenesAdded` guard. |
| 7 | `Extensions.Scenes/Systems/SceneSystem.cs` | Never calls `next`; `_activeTransition` never assigned; unknown id throws after warning. |
| 8 | `Graphics.Veldrid/2D/SpriteBatch.cs:58` | `DrawString` default color is transparent; `Draw` conflates `Color.Transparent` with default. |
| 9 | `Graphics.Veldrid/Assets/Font.cs:71` | `IFont.MeasureString` throws `NotImplementedException` (both backends). |
| 10 | `Graphics.Abstractions/Color.cs:66-73` | `Color(uint)` 4-digit branch assigns `g` twice and ignores alpha. |
| 11 | `Graphics.Veldrid/Input/InputState.cs:38-46` | Press+release in one frame drops `Pressed`; stuck keys on focus loss. |
| 12 | `Extensions.Audio/AudioManager.cs` | Windows-only backend; pitch shift never applied; `MasterVolume = 10`; no resampling. |
| 13 | `Extensions.Coroutines/CoroutineRunner.cs:23-25` | `Stop` of an unknown routine throws `ArgumentOutOfRange`. |
| 14 | `Core/Storage/PersistentStorage.cs` | Rooted at `Environment.CurrentDirectory`; `OpenWrite` does not truncate. |
| 15 | `Graphics.Veldrid/Assets/Texture2DLoader.cs` | Leaks staging texture, command list and decoded images; loaded textures never registered with the asset manager. |
| 16 | `Ion.Core.Abstractions.Tests` | Stale: `net7.0`, misnamed csproj, links a file that does not exist. |

---

## 4. Target architecture

### 4.1 Principles (decisions, not options)

1. **The user-facing shape stays.** `IonApplication.CreateBuilder(args)`, `builder.Services.Add*`, `app.UseSystem<T>()`, plain classes with stage attributes, scenes as DI scopes, pull-based coroutines, frame events. Wrapping a stage becomes an explicit `Begin`/`End` scope instead of `next(dt)` (section 4.3). Existing games should migrate with mechanical edits.
2. **Everything under it becomes compile-time.** Reflection is replaced by a Roslyn source generator plus C# interceptors (stable since the .NET 9.0.200 SDK). The reflection path stays as the fallback when the generator is not present (the same policy ASP.NET Core uses for its Request Delegate Generator).
3. **No async anywhere in the frame.** Long operations (asset decode, shader compile, network) are expressed as polled jobs on engine-owned worker threads with results consumed on the main thread in a stage. `Task` never appears in the public API.
4. **Determinism is a first-class feature.** The clock, the RNG seed, and input are injectable so a game can be run headless for N fixed steps and produce the same state and the same pixels every time. This is what makes the engine testable by agents.
5. **One thin graphics abstraction shaped like WebGPU, implemented on Silk.NET.** Vulkan is the reference backend (desktop, mobile, MoltenVK on Apple) and OpenGL ES 3.1 the second (R36S and fallback). WebGPU-shaped because it is the common subset of the modern APIs and keeps a browser backend possible later.
6. **ECS is a first-class extension, not the core.** Non-ECS games remain fully supported; the ECS module adds a `World` per scene scope, generated queries, a command buffer, and renderer extraction systems.
7. **Measure everything.** Every stage below has a benchmark or a test that gates it, and the engine ships a metrics/trace surface that agents and humans can read.

### 4.2 Package layout after the plan

```
Ion.Core.Abstractions      stage attributes, GameTime, IClock, events, input, storage interfaces (no deps beyond M.E.*)
Ion.Core                   builder, schedule runner, typed events, clock, storage, metrics ring buffer
Ion.Generators             schedule + query + config generators (netstandard2.0, Roslyn 4.4), shipped as analyzer
Ion.Extensions.Graphics.Abstractions   IGraphicsDevice/RHI (WebGPU-shaped), ISpriteBatch v2, camera/material/mesh types, IWindow, input enums
Ion.Extensions.Graphics.Vulkan         Silk.NET.Vulkan backend (desktop, mobile; MoltenVK on macOS/iOS)
Ion.Extensions.Graphics.GLES           Silk.NET.OpenGLES 3.1 backend (R36S handheld, fallback everywhere)
Ion.Extensions.Graphics.WebGPU         optional later: Silk.NET.WebGPU backend for a browser build
Ion.Extensions.Windowing.SilkNet       Silk.NET windowing + input driven by Ion's loop (GLFW or SDL platform registered explicitly for AOT)
Ion.Extensions.Graphics.Headless       offscreen render target + PNG readback; no window (agents, CI)
Ion.Extensions.Rendering2D             sprite batch v2, text, atlases, 2D camera, tilemaps (backend-agnostic, uses RHI)
Ion.Extensions.Rendering3D             meshes, materials (Unlit/PBR), lights, cameras, render graph, glTF importer
Ion.Extensions.Ecs                     ECS integration (Arch or Friflo, section 5.7): World per scope, [Query] codegen, Commands stage, Transform hierarchy
Ion.Extensions.Ecs.Rendering           extraction systems: Sprite/MeshRenderer/Camera/Light components -> renderer submissions
Ion.Extensions.Audio                   P0: engine mixer + Silk.NET.OpenAL output, NullAudioOutput for headless (Stage 3)
Ion.Extensions.UI                      P1: immediate-mode UI on the 2D renderer, remote-inspectable tree (Stage 5b)
Ion.Extensions.Physics2D / Physics3D   P1: Box2D v3 or Aether; BepuPhysics v2; ECS adapter systems (Stage 5b)
Ion.Extensions.Web                     P2: embedded HTTP/WebSocket server for companion apps and integrations (Stage 6b)
Ion.Extensions.Networking              P2: UDP/WebSocket transport + generated ECS replication (Stage 6b)
Ion.Extensions.Scenes, Coroutines, Assets, Debug (trace/metrics exporters)
Ion.Extensions.Remote                  JSON-RPC inspection protocol over HTTP/stdio + MCP server (Stage 6)
Ion.Tools (dotnet tool `ion`)          new/run/screenshot/bench/trace commands
Ion                                    meta-package with AddIon()/UseIon() defaults
```

`Ion_old`, `Ion.Extensions.Graphics.WGPU` (Alimer experiment), `Ion.Generators` (old) and `Ion.Extensions.Graphics.Veldrid` are removed once Stage 4 lands (Veldrid stays until the WebGPU backend passes the same sample and snapshot tests).

### 4.3 The loop cycle, stages and systems: redesign analysis (Stage 2)

The owner asked whether attributed methods (`[Update] void Move(...)` on a class) are the right model, or whether each stage should simply be an ordered group of systems. Below are the options that were weighed, then the design this plan adopts.

**What exists today.** Seven stages (`Init`, `First`, `FixedUpdate`, `Update`, `Render`, `Last`, `Destroy`). A system is any class; `UseSystem<T>()` reflects over its public methods, and every method with a stage attribute becomes a middleware in that stage's chain. Every method can take `next` and wrap everything registered after it. Consequences observed in the review: ordering is only registration order (and reflection's method order, which is unspecified); nothing distinguishes "leaf" systems (95 percent of game code) from "wrapping" systems (a handful of engine ones); a system that forgets `next(dt)` silently kills the rest of the stage (the Scenes sample has exactly this bug); wrapping makes the stage opaque, so nothing can be parallelised or validated; and each hop is a closure and an indirect call.

**Option A: keep it as is, generate the closures.** Cheapest change; keeps every existing game compiling. Removes reflection but none of the structural problems: ordering stays implicit, the body-splitting needed to get rid of delegates while keeping `next` is the hardest part of the generator, and stages remain opaque chains. Rejected as the end state, kept as the fallback path.

**Option B: stages as interfaces.** `IInitSystem.Init(GameTime)`, `IUpdateSystem.Update(GameTime)`, and so on; `app.UseSystem<T>()` adds the instance to every stage group whose interface it implements. Compile-time checked without any generator, trivially discoverable, and the runtime fallback is an array of interface references per stage (one virtual call per system, no closures). Costs: fixed method names (`Update` in every class rather than `MovePaddle`), at most one method per stage per class (forces artificial class splits), and interfaces say nothing about ordering or wrapping.

**Option C: functions as systems (Bevy style).** `app.Update(PhysicsSystem.Step)` where `Step(GameTime dt, World world, Commands commands)` gets its parameters injected. Smallest possible unit for an agent to write and read, naturally stateless (state lives in the ECS or in services), and the generator binds parameters the same way it binds constructors. Costs: C# cannot infer parameter injection without a generator, state that today lives in fields (loaded assets, cached queries) needs a home, and method groups from instance classes still need an instance.

**Option D: stages as ordered groups of leaf steps, with explicit scopes for the few things that wrap.** Attributes stay as the marker (they read well, let one class span stages with descriptive method names, and the generator needs a marker anyway), but the default step has no `next`: it runs, in order, and returns. Ordering is explicit (`Order`, `[Before<T>]`, `[After<T>]`) and validated. The wrapping behaviour that middleware provided is expressed as a **scope**: a pair of methods `[Render.Begin]`/`[Render.End]` (or a `IStageScope` with `Begin`/`End`) that the generator nests like middleware around all steps of that stage with a lower/higher order, using `try/finally` so an exception in a step still runs `End`. This is what `GraphicsSystem` (BeginFrame/EndFrame), `SpriteBatch` (Begin/End), `TraceTimerSystem` and `SceneSystem` actually do; none of them needs to run code "in the middle" of the downstream chain. The generator emits a flat method per stage:

```csharp
public void Render(GameTime dt)
{
    _graphics.BeginFrame(dt);                  // [Render.Begin(Order = -1000)]
    try
    {
        _sprites.Begin(dt);                    // [Render.Begin(Order = -900)]
        try
        {
            _extract.Extract(dt);              // [Render(Order = -300)] leaf
            _score.Draw(dt);                   // [Render] leaf
        }
        finally { _sprites.End(dt); }
    }
    finally { _graphics.EndFrame(dt); }
}
```

Comparison on the three axes the owner named:

| | A: chains, generated | B: interfaces | C: functions | D: groups + scopes |
|---|---|---|---|---|
| Dispatch cost (generated) | delegate per hop, or body splitting | direct/virtual call | direct call | direct call |
| Dispatch cost (no generator) | closures + reflection | array of interface calls, no reflection | needs generator | reflection or small `IStep` adapter |
| Ordering | registration only | registration only | explicit | explicit, validated, printable |
| Wrapping semantics | any method, implicit | none | none | explicit `Begin`/`End` scopes, exception-safe |
| One class across stages, named methods | yes | fixed names, one per stage | n/a | yes |
| Generator complexity | high (split at `next`) | low | medium (parameter binding) | low (sort + nest) |
| Parallelism later | impossible (opaque chain) | possible | possible | possible: leaf steps with known reads/writes can be batched |
| Failure modes | forgotten `next` kills the stage silently | none | none | diagnostic if a scope has no matching end, if `Before/After` cycle |
| Agent readability | must understand middleware | very simple | very simple | simple; `--print-schedule` shows the exact nesting |

**Decision: Option D, with C as sugar.** Stages become ordered groups of steps; the only remaining "middleware" is the explicit `Begin`/`End` scope, which keeps everything the pattern was valued for (frame bracketing, profiling, scene scoping) without delegates, without body splitting and without a way to silently drop the rest of a stage. Existing `(GameTime dt, GameLoopDelegate next)` methods keep working through Option A's generated closures for one release, and the generator warns (`ION010`) with the mechanical rewrite. That fallback wraps the original method body unchanged, so its exception semantics are exactly the author's (no `finally` is added); only the new `Begin`/`End` scopes get the `try/finally`, and they get it by design. Stateless function systems (`app.Update(Physics.Step)`) are supported by the same generator as a convenience, since they are simply steps without a class.

**The seven stages themselves stay**, and they are the right granularity: they match Bevy's `First/PreUpdate/FixedMain/Update/PostUpdate/Last` and Unity's order without exposing a dozen labels. Two refinements: `FixedUpdate` runs zero to N times per frame with its own `GameTime` and the frame's `Alpha` is available to `Render` for interpolation (already so); and engine steps use reserved order bands (`-1000..-500` and `500..1000`) so user steps with the default `Order = 0` always run between engine setup and engine teardown, which removes the "register after UseIon()" trap. Scenes contribute their own steps into the same stages through a generated per-scene schedule invoked by a `SceneSystem` step, so scene systems obey the same ordering rules as root systems.

**Why this is also the performance choice.** With leaf steps and declared data access (the injected services and the `[Query]` component sets the generator already knows), a later stage of the plan can partition a stage into batches of steps that do not conflict and run them on a job system, exactly as Bevy's executor does. An opaque middleware chain can never do this. The `PipelineBenchmarks` numbers (section 5.1) show the single-threaded gain is real but small; the structural gain, that a stage becomes data the engine can reason about, is what matters.

**Registration surface after the change** (user code):

```csharp
public sealed class PaddleSystem(World world, IInputState input, IEvents events)
{
    [Init] public void Load(IAssetManager assets) { ... }              // extra parameters are injected
    [FixedUpdate(Order = 10), After<PhysicsSystem>] public void Move(GameTime dt) { ... }
    [Render] public void Draw(GameTime dt) { ... }
}

public sealed class GraphicsSystem(IGraphicsDevice device)
{
    [Render.Begin(Order = -1000)] public void BeginFrame(GameTime dt) { ... }
    [Render.End(Order = -1000)]   public void EndFrame(GameTime dt) { ... }
}

app.UseSystem<PhysicsSystem>().UseSystem<PaddleSystem>();
app.Update(Debug.PrintFps);                                                 // function step
```

**DI.** Systems are resolved once, so the generator also emits the composition root (constructor calls in dependency order, scoped instances per scene) and only falls back to `Microsoft.Extensions.DependencyInjection` for types it cannot see (plugins). `IOptions<T>` binding goes through the configuration-binding generator (`EnableConfigurationBindingGenerator` on every project) so NativeAOT publishes with zero Ion warnings.

**Stack traces.** Generated types carry `[StackTraceHidden]` (honoured by NativeAOT's ILCompiler as well as CoreCLR). The trace from section 2 becomes:

```
at Ion.Extensions.Graphics.GraphicsSystem.BeginFrame(GameTime dt) in .../Systems/GraphicsSystem.cs:line 13
at Program.<Main>$(String[] args) in .../Program.cs:line 40
```

**Validation and diagnostics** (`app.Build()` is generated): unknown stage, `Before/After` cycle, scope without a matching end, step registered after `UseScene` that can never run, a step method that is `async` or returns `Task`, a service that is scoped requested by a root system. `ion schedule` (or `--print-schedule`) prints the nesting above with orders, which is the first thing an agent reads when "my system does not run".

**As implemented (Stage 2, first half: runtime schedule, September 2026).** The model above is in place with reflection binding; the generator (second half) emits the same schedule as direct calls. Refinements and deviations from the sketch:

- *Attribute syntax.* Scopes are `[Begin(Stage.Render, Order = -900)]` / `[End(Stage.Render)]` (C# cannot resolve `[Render.Begin]` while the stage attribute is named `RenderAttribute`). `Stage` is a new enum whose values match `GameLoopStage`. `End.Order` is optional and, when set, must equal the begin order (`ION011`); `ScopeName` pairs several scopes of one system. `[After<T>]`/`[Before<T>]` are allowed on methods and on classes, and match a system whose service or implementation type is, derives from or implements `T`.
- *Scope semantics.* A scope opens at its position in the sorted stage and wraps every item after it; its end runs after all of them, in reverse order of opening, in a `finally`. Nothing in a stage can run after a scope's end, so teardown steps that used to run after `next(dt)` outside a scope (the window close check) are steps in the teardown band, inside the frame scopes. At equal order a scope opens before steps.
- *Sorting.* Kahn's algorithm over the Before/After edges of a stage, always taking the ready item with the lowest (order, scope-first, registration index, declaration index). Constraints therefore win over `Order`, and a constrained step can move later than its order suggests (see `SceneScheduleTests`). Declaration order is the metadata token order (reflection order under NativeAOT, which has no tokens). Registering the same system twice adds its steps twice (the benchmarks rely on it).
- *Data model for the generator.* `ScheduleModel` (registrations: `SystemEntry`, `FunctionEntry`, `MiddlewareEntry`, nested scene schedules) is planned into a `SchedulePlan`: seven `StagePlan`s, each a flat list of `StepPlan` in run order with `Kind` (`Step`, `Scope`, `Function`, `Middleware`), `Order`, `System`, `Method`/`EndMethod`, `After`/`Before`, registration and declaration indexes and `Depth`. Nesting is implied by the order (a `Scope` or `Middleware` wraps everything after it), which is exactly the shape of the generated method: calls in order, and a `try/finally` opened at each scope. The runtime `Schedule` binds a plan to instances: leaf steps become `GameLoopDelegate`s in a flat array per stage, and a stage with one step is that step's delegate.
- *Legacy form.* `(GameTime, next)` and `(next)` methods, `UseX(next => ...)` delegates and the generated `Use{Stage}<TService...>` overloads run as opaque middleware at their order (0 for delegates) through the old closure binding, and the runtime logs `ION010` with the rewrite. A middleware's `next` is the next middleware or single step directly, so a pure legacy chain costs what it did before.
- *Function steps.* `app.Update(...)` and friends take `Action<GameTime>` or up to four service parameters (generated per stage, for `IIonApplication` and `ISceneBuilder`). Without the generator C# cannot infer service types from a method group, so `app.Update<ILogger<Hud>>(Hud.Log)` needs them spelled out; lambdas with typed parameters infer. Lambdas print as `Program.lambda(IInputState, ICoroutineRunner)`.
- *Scenes.* `SceneSystem` is one step per stage at order -500 that runs the active scene's `Schedule`. Scene schedules are planned (validated) when the root schedule is built, by running the scene's configure callback once against a temporary scope, and printed after the root schedule.
- *Diagnostics.* Errors are thrown together as `IonScheduleException` at `Build()`: `ION001` unknown stage, `ION002` cycle (names the steps), `ION003` unpaired scope, `ION004` stage attribute on a non-public method, or a step added to a scene builder after the scene loaded, `ION005` async/`Task` step, `ION006` scoped system or scoped step parameter in the root schedule (constructor dependencies are left to DI's own scope validation), `ION007` unsupported signature, `ION008` unregistered parameter service, `ION009` unregistered system, `ION011` ambiguous scope. Warnings logged once under `Ion.Schedule`: `ION010` legacy middleware, `ION012` constraint on a system that is not in the schedule, `ION013` system without steps. "Registered after `UseScene`" is no longer a failure mode.
- *Printing.* `IIonApplication.PrintSchedule()` and `--Ion:PrintSchedule=true` (instead of `--print-schedule`, so it binds like any other configuration key).
- *Dispatch cost* (`PipelineBenchmarks`, `--job short`): 32 leaf steps 47-54 ns against 111 ns for the reflection-bound chain before, 8 steps 8.5-9 ns against 18.8 ns, one step 0.5 ns against 1.2 ns; the legacy middleware path 117 ns at 32. Zero bytes per frame everywhere.

**As implemented (Stage 2, second half: the source generator, September 2026).** `Ion.Generators` (netstandard2.0, compiled against Roslyn 4.4; `GetInterceptableLocation` of Roslyn 4.12+ is reached through reflection, and without it the generator reports `ION014` and emits nothing) produces the schedule at compile time. Design decisions and deviations:

- *Interception, not replacement.* Every `UseSystem` (generic, or `typeof` arguments), function step, legacy middleware delegate (including the `Use{Stage}<TService...>` overloads), `UseScene` and `Build()`/`BuildSchedule()`/`Run()`/`RunFrames()` call the generator can see is intercepted (`[InterceptsLocation(1, data)]`, the attribute declared file-local). All generated types are `file`-local in one `IonSchedule.g.cs`, so assemblies with `InternalsVisibleTo` between them never clash.
- *Pre-bound entries in the same `ScheduleModel`.* An intercepted `UseSystem` adds a `GeneratedSystem`: the system's steps, scopes, orders, merged constraints, service parameters and diagnostics computed by a port of `SchedulePlanner.DiscoverSystem` (same rules, same messages), with delegates that bind each method directly (a method group for `(GameTime)` steps, hidden adapters otherwise). The runtime planner plans these entries exactly like reflection-discovered ones, without reflection, so `PrintSchedule`, validation and warnings are unchanged and plugins registered by reflection coexist in the same model. Every entry carries its call site (`Assembly#n`).
- *The generated schedule is verified, not trusted.* For each application (the registrations made on a local or parameter before its `Build()`/`Run()` call in the same method) and each scene (its configure callback), the generator lists the registrations in the order they run, following helpers of the same compilation through their bodies and helpers of other assemblies through `[assembly: ScheduleRegistrations]` summaries that the generator writes for every method taking a builder. Registrations under a branch (if, loop, callback, `?.`, `&&`) are conditional and guarded in the generated code. It plans the union with the shared `ScheduleSorter` (the runtime's sort, linked into the generator) and emits a `GeneratedSchedule`: fields for the systems (resolved once from DI, `GetRequiredService` as the runtime does) and injected services, one method per stage with direct calls in plan order, `try/finally` per scope, and continuation methods (with delegates) for legacy middleware. At `Build()`, `GeneratedScheduleFactory` aligns the runtime registrations with the listed call sites and compares the runtime plan with the generated order restricted to the registrations present; on any difference the runtime binds the plan itself and logs why (`Debug`, `Ion.Schedule`). Systems the generated code cannot name (internal types of other assemblies, such as the engine's) are called through their pre-bound delegates in the flat method.
- *Composition root.* This wave keeps DI for construction (`GetRequiredService`); the `new`-based root is left for later.
- *Scenes.* `UseScene` is intercepted and passes a per-scene factory; each scene load (a new scope) builds a new generated scene schedule. The scenes generator's enum overloads became the generic `UseScene<TScene>`/`EmitChangeScene<TScene>` library methods, because a generator cannot bind calls to another generator's output.
- *Diagnostics.* ION001, ION003, ION004, ION005, ION007, ION010, ION011, ION013 at the method (or registration) with the runtime messages; ION002 for a cycle among unconditional registrations; ION012 when every registration of the schedule is visible; ION006, ION008 and ION009 for types declared in the project that no service registration call mentions (conservative: assembly scanning is invisible).
- *Stack traces.* Generated types and methods carry `[StackTraceHidden]`, `[DebuggerNonUserCode]`, `[GeneratedCode]` and `[ExcludeFromCodeCoverage]`; `GameLoop`'s run and step methods, `IonApplication.Run`/`RunFrames` and the scene system's dispatch steps are hidden too, so a step's exception shows the step and then the caller of `Run`.
- *Not replaced.* `UseDelegateServicesGenerator` and `UseDelegateServicesSceneGenerator` emit public overloads that users call; the schedule generator intercepts calls to the application ones but does not emit the overloads, so both stay.
- *Results.* `PipelineBenchmarks.Ion_GeneratedSchedule` at 32 systems 15.7 ns against `DirectCalls_FlatLoop` 13.3 ns (1.18x; the runtime-bound schedule 67 ns); 8 systems 4.3 ns against 3.5 ns; `FullFrameBenchmarks.Step_8Systems_GeneratedSchedule` 45.6 ns against 125.3 ns runtime-bound; 0 B per frame in every row (`docs/plans/benchmarks/2026-09-25-stage2-generator`). The Breakout ECS sample runs its generated schedule (headless: UseIon's null backends are matched through the summaries; with the Veldrid backend the audio module, not yet compiled with the generator, sends it to the runtime path) and publishes with NativeAOT with no Ion warnings.

### 4.4 Events v2 (Stage 3): typed channels, source-generated

Replace the boxed `RingBuffer<IEvent>` with one unboxed double-buffered channel per event type and a read cursor per reader:

```csharp
public interface IEvents
{
    void Emit<T>(in T e) where T : unmanaged;            // appends to Channel<T>.Current
    EventReader<T> Reader<T>() where T : unmanaged;      // stable per-system reader (created once, in the constructor)
}

// A plain struct (cursor + channel reference), so it can live in a field of a system class and be stored by the generator;
// the spans it hands out are the transient part.
public struct EventReader<T> where T : unmanaged
{
    public bool TryRead(out T e);        // advances the cursor
    public ReadOnlySpan<T> Read();       // everything unread, advances the cursor
    public bool Any();                   // peek
}
```

Semantics preserved from today: an event is visible for the frame it was emitted in and the next one (so a system earlier in the schedule still sees it), each reader sees each event once, `Emit` from any stage. Removed: `Handled` (it was never set), `EventId`, the type-hash `EventType`. The `EventBenchmarks` prototype in the benchmark project (`TypedChannel<T>`) is the reference for the data layout; the measured difference is in section 5.

**What source generation adds on top of the typed channels.** The same generator that builds the schedule sees every `Emit<T>` and `Reader<T>` call in the compilation, so it can:

- Emit a closed `EventBus` class with one strongly typed field per event type used anywhere in the game (`Channel<PaddleHitEvent> _paddleHit;`) and generate `Emit`/`Reader` overloads that touch that field directly. No generic dictionary lookup, no `static` generic class per `T`, no runtime registration, and under NativeAOT no generic virtual method instantiation; the call is a direct field access and an array write.
- Assign each event type a compile-time integer id, which is what the trace, the JSONL frame log and the remote protocol use to name events; `typeof(T).GetHashCode()` disappears.
- Size each channel from usage: the generator sees whether a type is emitted in a `[FixedUpdate]` loop or once at `[Init]`, and picks the initial capacity accordingly; channels never shrink and never reallocate in steady state.
- Validate at compile time, with diagnostics an agent can act on: an event type that is emitted but never read (`ION101`), read but never emitted (`ION102`), a reader created inside a stage method instead of a constructor (`ION103`, it would re-read every frame), a non-`unmanaged` payload (`ION104`), and a reader of an event that only fires in a later stage of the same frame (`ION105`, a one-frame latency the author probably did not intend).
- Generate the reader plumbing as ordinary named methods on the system's partial class, so the stack trace through an event handler is `PaddleSystem.OnPaddleHit(...)`, not an enumerator or a lambda.

The runtime (non-generated) fallback keeps the dictionary of channels keyed by type so plugins compiled without the generator still work; the generated bus is the fast path and the only one used by the templates.

**As implemented (Stage 3).** `IEvents`, `EventReader<T>` (plus `TryReadLatest`, `Count` and `Skip`), `EventChannel<T>`, `EventBus`, `EventReaderSet` and `EventId<T>` live in `Ion.Core.Abstractions`. A channel is one array holding the fixed-step backlog, the previous frame and the current frame, with a sequence number per event; a reader keeps the sequence number of its next unread event, so readers need no registration and `Read()` is a single span. At the end of a frame the window moves and the retained events are moved to the front when that copies no more than it drops, so each frame is written at the start of the same array. The fixed-step backlog of Stage 1 is kept: the game loop brackets its fixed steps with `EventBus.BeginFixedStep`/`EndFixedSteps` (which records where each channel stood when a fixed step started), and readers read the backlog while the bus is in fixed steps. The runtime bus finds a channel through a per-type slot index (the dictionary keyed by type is the store). The generator writes `IonEvents.g.cs`: an `[assembly: EventUsage(type, usage)]` summary per assembly, and for an application a file-local `GeneratedEventBus : EventBus` with one `EventChannel<T>` field per type (ids 1..N by type name, capacities from usage), installed by intercepting `IonApplication.CreateBuilder`; the application's own `Emit`/`Reader` calls are intercepted to use the field, and `IEvents` is re-implemented with `typeof` tests that fold to a field access for calls from other assemblies. Methods that emit or read for their caller are marked `[EmitsEvent]`/`[ReadsEvent]`. Diagnostics `ION101`..`ION105` as above, plus `ION106` (a reader in a `readonly` field or a property: reads advance a copy). Not done: the generated reader plumbing as named partial methods (the last bullet above); the JSONL frame log and remote protocol that would consume the ids do not exist yet (the event system logs channel growth by id).

### 4.5 Input v2 (Stage 3)

Fixed-size state: `ulong[]` bitsets for `Down`, `Pressed`, `Released` indexed by `Key` (max ~256), a 32-bit mask for mouse buttons, `Vector2` position and delta, wheel, text-input `ReadOnlySpan<char>` for the frame, and gamepad state (Silk.NET.Input exposes it). Press and release in the same frame set both bits. Focus loss clears `Down`. Input is captured from the windowing layer in `First` and is immutable for the rest of the frame. A `RecordedInput` implementation replays a stream for deterministic tests, and a `ScriptedInput` lets agents inject keys and clicks over the remote protocol.

### 4.6 Metrics v2 (Stage 3)

Three layers, all allocation-free on the hot path:

1. **Spans.** The generated schedule brackets every system call with `Stopwatch.GetTimestamp()` writes into a preallocated per-frame ring (`FrameProfile[]`, N frames deep). Enabled by a `static readonly bool` behind a feature switch so ILC removes it entirely from release builds that opt out, and toggleable at runtime in builds that keep it. No strings on the hot path: system and stage names are interned ids resolved at export time.
2. **Counters.** Engine counters (`draw_calls`, `sprites`, `triangles`, `entities`, `events_emitted`, `gc_gen0/1/2`, `allocated_bytes`, `frame_ms`, `fixed_steps`) live in a `FrameStats` struct written once per frame; games add their own with `Metrics.Counter("balls")`.
3. **Export.** Chrome trace JSON (Perfetto) and Tracy zones from the ring, `System.Diagnostics.Metrics` `Meter` for frame-level aggregates so `dotnet-counters` works, a JSONL "frame log" line per frame for agents, and an optional on-screen overlay drawn by the 2D renderer.

**As implemented (Stage 3, metrics wave).** The ring, the ids and the scope live in `Ion.Core.Abstractions` (`FrameProfiler`, `FrameProfile`, `SpanRecord`, `SpanId`/`MetricsIds`/`SpanIds`, `MetricsScope`, `FrameStats`, `IStepProfiler`, `IFrameStatsSource`, `IFrameListener`, `ISpanSink`), so the generator and the loop need nothing else; `Ion.Extensions.Debug*` became `Ion.Extensions.Metrics*` (`IMetrics`, instruments, export, capture, frame log, meter, overlay) with the 0.2 trace API kept as obsolete adapters. Decisions and deviations:

- The feature switch is `FrameProfiler.IsProfilingEnabled`, a `[FeatureSwitchDefinition("Ion.Metrics.Profiling")]` static property with an initializer (a `static readonly` backing field, folded by the JIT); the Ion props emit the `RuntimeHostConfigurationOption` (`Trim="true"`) and a `CompilerVisibleProperty` from `IonMetricsProfiling` (default true). With `false` the generator emits no brackets at all and ILC removes every other recording site: in the NativeAOT map of the Breakout ECS sample `FrameProfiler.BeginCore`/`EndCore` and the runtime schedule's `RunStepsProfiled` exist with the default and are absent with `IonMetricsProfiling=false` (the frame stats, frame log and meter remain).
- Each generated stage without legacy middleware is emitted twice, a bracketed copy run while `_prof.IsActive` and the plain copy, so profiling that is compiled in but off costs one check per stage instead of one per step. Stages split by legacy middleware keep a check per step. The runtime schedule checks once per stage runner.
- Scopes get one span from before `Begin` to after `End` (in the `finally`), so Perfetto nests the stage's steps under the scope. The loop adds a span per stage, per fixed step and for the idle time, and profiles Init and Destroy as their own entries of the ring.
- Frame stats are collected whenever metrics are installed (a profiler with history), profiling or not. `allocated_bytes` is the loop thread's allocation (`GC.GetAllocatedBytesForCurrentThread`, 7 ns): the process-wide total is either imprecise across collections or 600 ns with `precise: true`.
- An enabled span costs two `Stopwatch.GetTimestamp()` reads, which are 40 ns each on the benchmark VM (TSC clock source under virtualization), so the enabled target of 30 ns is not reachable with `Stopwatch` here; the rest of the path is a few nanoseconds and allocation-free. Bare metal reads the clock in 15 to 20 ns.
- Tracy is a live `ISpanSink` (zones cannot be emitted after the fact with the C API), in the separate `Ion.Extensions.Metrics.Tracy` project over Tracy-CSharp 0.13.1, whose `LibraryImport` bindings publish with NativeAOT without warnings; its natives cover win-x64 and linux-x64 only, and a DllImport resolver loads TracyClient from the application directory under NativeAOT. It was run headless (no Tracy server attached), not against a live profiler.
- The overlay is drawn by the metrics module itself through `ISpriteBatch` (any backend) when a font asset is configured, rather than in each backend's text path.
- Not done: a Tracy server session check, per-thread span buffers (spans from other threads use an atomic increment on the loop's current frame), and the ECS module's `entities` source (the hook exists; the Breakout ECS sample implements it for its Arch world).

### 4.7 Graphics on Silk.NET (Stage 4)

**Decision (owner).** Silk.NET is the rendering platform. The choice of graphics API underneath is per target, and the browser is an option for later, not a requirement.

**Findings that shape the design.** Silk.NET 2.23.0 (January 2026) is the current stable line and 3.0 has no public NuGet preview yet, so the engine builds on 2.x now and treats 3.0 as a future migration. `Silk.NET.Windowing` and `Silk.NET.Input` use reflection for platform discovery (issue #960) but work under NativeAOT when the platform is registered explicitly (`Window.Add(new GlfwPlatform())`, or the SDL platform on mobile). `Silk.NET.Vulkan`, `Silk.NET.OpenGLES`, `Silk.NET.OpenGL` and `Silk.NET.WebGPU` are generated bindings over function pointers and are AOT-clean. Nothing in Silk.NET is marked `IsAotCompatible`, so the engine's CI has to keep an AOT publish lane green itself.

**Backends, by primary target.**

| Target | Windowing/input | Graphics API through Silk.NET | Notes |
|---|---|---|---|
| Windows | `Silk.NET.Windowing` (GLFW) | Vulkan; D3D12 optional later | Vulkan is the desktop reference backend |
| Linux desktop | GLFW | Vulkan | X11 and Wayland via GLFW |
| macOS | GLFW | Vulkan over MoltenVK (`Silk.NET.MoltenVK.Native`) | Metal directly is out of scope; MoltenVK is what most Vulkan engines ship |
| R36S handheld (RK3326, Mali-G31, ArkOS arm64) | SDL platform (`Silk.NET.Windowing.Sdl`, no GLFW packages for that distro) | **OpenGL ES 3.1** (`Silk.NET.OpenGLES`) | Panfrost GLES is the only stable driver; PanVK is experimental. linux-arm64 NativeAOT. Gamepad through `Silk.NET.Input` |
| Android | SDL platform | Vulkan (GLES 3.1 fallback) | NativeAOT in .NET 10 |
| iOS/iPadOS | SDL platform | Vulkan over MoltenVK | NativeAOT since .NET 9 |
| Browser (optional, later) | canvas shim | WebGPU (`Silk.NET.WebGPU` or Emscripten `webgpu.h`) | only if a browser build is wanted; the RHI keeps it possible |

**RHI.** One thin abstraction (`IGraphicsDevice`, `IBuffer`, `ITexture`, `ISampler`, `IShaderModule`, `IBindGroup`, `IRenderPipeline`, `ICommandEncoder`, `IRenderPass`, `ISurface`), deliberately shaped like WebGPU because WebGPU is the common subset of Vulkan, Metal and D3D12 and maps cleanly onto GLES 3.1 (bind groups become uniform buffer bindings plus texture units; render passes become framebuffer binds). Two implementations in Stage 4: `Graphics.Vulkan` (desktop and mobile) and `Graphics.GLES` (R36S, and the debug fallback everywhere). `Graphics.WebGPU` is a third implementation added only if the browser target is picked up. Backend-specific code never appears above the RHI; the 2D and 3D renderers are written once.

**Windowing with Ion's loop.** `Silk.NET.Windowing` exposes exactly the primitives needed: `Initialize()`, `DoEvents()` (main thread), `FramebufferSize`, `Native` handles, `Reset()`. `WindowSystem` calls `DoEvents()` in `First` and never uses `IWindow.Run`. Vulkan surfaces come from `window.VkSurface`; GLES contexts from `window.GLContext`. On the R36S the SDL platform is registered explicitly and the window is created fullscreen at the panel's 640x480.

**Shaders.** Author once in a single language and cross-compile at build time: GLSL 4.5 (Vulkan dialect) as source, `Silk.NET.Shaderc` to SPIR-V for Vulkan, `Silk.NET.SPIRV.Cross` from SPIR-V to GLSL ES 3.10 for the GLES backend (and to MSL/WGSL if ever needed). A build task validates every shader on every platform's dialect and generates C# structs for uniform blocks and bind-group layouts from `Silk.NET.SPIRV.Reflect`, so a CPU/GPU layout mismatch is a compile error. Shaders ship as embedded SPIR-V plus pre-translated GLSL ES; nothing is compiled at runtime.

**2D renderer v2** (`Ion.Extensions.Rendering2D`): single instance buffer per frame with a 3-deep ring (no `WaitForIdle`), draws issued as ranges into that buffer per texture/material, packed `RGBA8` color, no per-sprite scissor (scissor becomes a batch-level render state), explicit `SpriteSortMode` (`Deferred`, `Texture`, `FrontToBack`, `BackToFront`), a `Camera2D` matrix per batch, blend/sampler presets (`AlphaBlend`, `Additive`, `Opaque`; `Point`, `Linear`), render-to-texture, `ReadOnlySpan<char>` text with cached layouts and a glyph atlas owned by the renderer, and an `ISpriteBatch` that keeps today's `Draw*` signatures. `MeasureString` implemented. On GLES 3.1 (no SSBO-in-vertex guarantees on Mali-G31) instances go through a vertex buffer with per-instance attributes rather than a storage buffer; the RHI hides that.

**As implemented (Stage 4, first wave: RHI, windowing, Vulkan, headless, September 2026).** Spike findings in [spikes/2026-silknet-spike.md](spikes/2026-silknet-spike.md) (Silk.NET 2.23; windowing driven by hand works on GLFW and SDL under Xvfb; Vulkan under lavapipe windowed and headless; NativeAOT publishes and runs; startup about 140 ms to a window and 60 ms from process start to a presented frame without shader compilation). What exists:

- RHI in `Ion.Extensions.Graphics.Abstractions` (namespace `Ion.Extensions.Graphics.Rhi`): 15 interfaces (`IGraphicsDevice`, `IQueue`, `IBuffer`, `ITexture`, `ITextureView`, `ISampler`, `IShaderModule`, `IBindGroupLayout`, `IBindGroup`, `IPipelineLayout`, `IRenderPipeline`, `ICommandEncoder`, `IRenderPassEncoder`, `ICommandBuffer`, `ISurface`) plus descriptors and enums. Deviations from WebGPU: frames in flight are explicit (`BeginFrame`/`EndFrame` on the device, called by the graphics system), surfaces are configured on `ISurface`, readback is a blocking `IBuffer.Read` (tests and screenshots only), no compute, no storage textures, no stencil state yet. Clip space is WebGPU's (y up, depth 0..1; Vulkan flips the viewport, GLES will use SPIRV-Cross's depth fixup). `IGraphicsFrame` is what renderers use: the frame's color and depth targets with clear-on-first-use attachments.
- `Ion.Extensions.Windowing.SilkNet`: `SilkWindow` (Init and First at `StageOrder.Window`), explicit GLFW/SDL registration from `Ion:Window:Platform`, input queued during `DoEvents` and applied at `StageOrder.Input` (gamepads included). Destroyed at `StageOrder.WindowClose` in Destroy, after the device.
- `Ion.Extensions.Graphics.Vulkan`: one queue, 2 or 3 frames in flight, per-frame command pools, fences, staging ring and deferred destruction, render pass cache keyed by formats, load/store operations and layouts (framebuffers are per pass, destroyed with the frame), one `VkDeviceMemory` per resource (no sub-allocation yet), conservative barriers, CPU-tracked image layouts. Clean under the Khronos validation layer with synchronization validation. `GraphicsFrameDriver` is written against the RHI only and is meant to be shared by the GLES backend.
- `Ion.Extensions.Graphics.Headless`: the same backend with an offscreen target; `Ion:Headless:Render=true` adds it to `AddIon`'s headless mode, and `IonTestHost.WithRendering()`/`Screenshot()` plus `GoldenImage` in `Ion.Testing` make golden-image tests possible.
- `Ion.Shaders` and `Ion.Shaders.targets`: build-time GLSL 4.5 to SPIR-V (Shaderc) and GLSL ES 3.10 (SPIRV-Cross, combined image samplers, depth fixup), embedded as `Shaders/<file>.spv` and `.es.glsl`. Not done: reflection-generated C# layouts. (The flattening of (set, binding) to GLES binding points came with the second wave, below.)
- `Ion.Examples.Quad`: the textured quad on the new stack, windowed or headless, NativeAOT with no Ion warnings (two Silk.NET.Core resolver warnings remain after ILLink substitutions).

**As implemented (Stage 4, second wave: OpenGL ES, September 2026).** Details and measurements in the spike document's GLES section; the handheld profile and the linux-arm64 publish in [../platforms/r36s.md](../platforms/r36s.md).

- `Ion.Extensions.Graphics.GLES`: the RHI on `Silk.NET.OpenGLES`, ES 3.1 with ES 3.2 native base vertex and ES 3.0 fallbacks (`GlesFeatureLevel`, `Ion:Graphics:Gles:MaxFeatureLevel`). Command buffers are recorded and replayed at submit, so queue ordering matches Vulkan; frames in flight are fence syncs with deferred deletion; bind groups are flattened to `group * 8 + binding` (`GlesBindings`, shared by the shader build and the backend); render passes are cached FBOs; readback goes through a pixel pack buffer. Clip-space y is negated in the GLSL ES translation so texture row 0 is the top row on every backend; the window surface is an offscreen target blitted with a flip at present. Contexts: the Silk.NET window's GL ES context (the window module creates one when the backend is OpenGL ES) or EGL for headless (Mesa surfaceless platform, pbuffer fallback; EGL is loaded by hand because Silk.NET 2.x has no EGL bindings).
- Backend selection: `GraphicsBackendSelector` (explicit backends are forced; `Auto` is Vulkan then OpenGL ES, OpenGL ES first on linux-arm64) and `RhiGraphics` in `Ion.Extensions.Graphics.Headless` (`AddRhiGraphics`, and headless rendering), so `Ion:Headless:Render` runs without Vulkan. `GraphicsFrameDriver` moved to the abstractions and is shared by both backends.
- Tests: the Vulkan suite became backend-agnostic contracts in `Ion.Extensions.Graphics.Rhi.Tests.Shared`, run by the Vulkan tests (62) and the GLES tests (81: the contract at ES 3.0, 3.1 and 3.2, windowed, and GLES-specific binding, std140, sampler, fence and readback tests). Both backends match one set of golden PNGs pixel for pixel on Mesa, windowed and headless.
- NativeAOT: the quad sample publishes for linux-x64 and cross-publishes for linux-arm64 (clang/lld against a glibc 2.27 sysroot) with no `Ion.*` warnings; the arm64 build runs under qemu-aarch64 on arm64 Mesa 20 (ES 3.1) and renders the golden. Not verified: R36S hardware, Panfrost, SDL KMSDRM, gamepad and frame times on the device.

**As implemented (Stage 4, third wave: 2D renderer, sample migration, Veldrid removed, September 2026).**

- `Ion.Extensions.Rendering2D` on the RHI only (runs on Vulkan and OpenGL ES unchanged): `SpriteBatch` records a 40-byte instance per sprite (origin corner and two edge vectors with scale and rotation folded in, so the vertex shader has no trigonometry; unorm16 UV rectangle; packed RGBA8 color; depth) into one CPU array per frame, sorts each `Begin`/`End` segment (`Deferred`, `Texture` by counting sort, `FrontToBack`/`BackToFront` by a stable radix sort), uploads everything once with `IQueue.WriteBuffer` into the frame slot's instance buffer (ring of `FramesInFlight`, no waits) and draws ranges bound by vertex buffer offset (so GLES 3.1 needs no base instance), one draw per run of equal textures. Blend presets (`AlphaBlend` premultiplied, `Additive`, `Opaque`, `NonPremultiplied`), sampler presets, a `Camera2D` transform and a scissor per segment (a uniform slot per segment), `SetRenderTarget` and `RenderTarget2D`, debug shapes through a 1x1 white texture, FontStashSharp text with a renderer-owned glyph atlas and per-font cached layouts, `MeasureString`. Loaders: ImageSharp textures premultiplied with CPU mips and in-place hot reload, font sets, `TextureFactory`.
- `AddIon`/`UseIon` use `AddGraphics`/`UseGraphics` (Silk.NET window, `AddRhiGraphics`, the 2D renderer); `Ion:Headless:Render` swaps the recording batch for the real one. Veldrid, its tests and its dependencies are deleted; `GraphicsBackend` keeps `Vulkan`, `OpenGLES`, `Auto` and the reserved `Direct3D12`, `Metal`, `WebGPU`.
- Samples: Breakout, Breakout ECS and Scenes run unchanged apart from setup moved into static classes for tests; golden-image tests at the game's own window size on both backends, windowed E2E runs on both backends under Xvfb with validation. `Ion.Examples.Sprites100k` is the stress sample.
- Measured (Xeon 2.1 GHz VM): `SpriteBatchBenchmarks` 5.7 ns per sprite with 1 texture and 6.9 ns with 16 against 12.4 and 14.6 ns for the old batcher's per-sprite work on the same machine (0.46x, 0.47x), 0 B per frame; 100k sprites on lavapipe 103 ms per frame headless and 117 ms windowed (CPU rasterization of 25 million blended pixels; 2.3 ms of it is recording), 136 ms on llvmpipe GLES. NativeAOT publish of the ECS sample: no `Ion.*`, Veldrid, Vortice, NativeLibraryLoader or DependencyModel warnings; what remains is `nkast.Aether` (9: XML serialization) and the Silk.NET.Core resolver lambda (2).

### 4.8 The 3D SDK (Stage 5)

The minimal set that Bevy, Stride, Unity Entities Graphics and Godot's RenderingServer all converge on:

**Components / data types (in `Graphics.Abstractions`, plain structs, usable with or without ECS):**

- `Transform` (position, rotation quaternion, scale) and `GlobalTransform` (world matrix); `Parent`/`Children` for hierarchy.
- `Camera` (projection: perspective or orthographic, near/far, viewport rect, clear color, render target, priority) plus `CameraMain` marker.
- `Mesh` (handle: vertex/index buffers, sub-meshes, bounds), `Material` (handle: `UnlitMaterial { Color, Texture }`, `PbrMaterial { BaseColor, Metallic, Roughness, Normal, Emissive, AlphaMode }`).
- `MeshRenderer { Mesh, Material, CastShadows, ReceiveShadows, LayerMask }`.
- `DirectionalLight`, `PointLight`, `SpotLight` (color, intensity, range, shadows flag).
- `Visibility` (user), `Aabb` (computed), `ViewVisibility` (computed per camera).
- Later: `SkinnedMesh`, `AnimationPlayer`, `Environment` (skybox/IBL).

**Engine-owned systems, in stage order:**

```
Update        user systems mutate Transform / components
Last  (-200)  TransformPropagation      Transform + Parent -> GlobalTransform (dirty-tree walk, parallel over roots)
Last  (-100)  BoundsUpdate               Mesh bounds * GlobalTransform -> Aabb
Render(-300)  Extract                    per camera: frustum-cull Aabb -> ViewVisibility; copy (mesh, material, matrix, key) into RenderWorld arrays
Render(-200)  Prepare                    upload per-object transforms/instance data, per-view uniforms, light lists, shadow matrices
Render(-100)  Queue + Sort               opaque: bin by (pipeline, material, mesh) key, front-to-back; transparent: back-to-front
Render(  0)   RenderGraph.Execute        shadow passes -> depth prepass (optional) -> opaque -> skybox -> transparent -> post -> 2D/UI overlay -> present
```

The render graph is a small DAG of passes with declared texture inputs/outputs; users add passes (e.g. a custom post effect) without touching phases or batching. Instanced batching by (mesh, material) is automatic; a `Material` with a custom WGSL shader is the extension point. glTF 2.0 import (via a managed loader, `SharpGLTF` or `Silk.NET.Assimp`) produces meshes, materials, textures and a node hierarchy.

**Immediate-mode escape hatch.** Non-ECS games get `IMeshBatch.Draw(mesh, material, in Matrix4x4 world)` and `ICamera3D` in the same way they get `ISpriteBatch` today. The ECS extraction system calls the same submission API, so there is exactly one renderer.

**As implemented (Stage 5, 3D half, September 2026).** Design, graph API, material conventions and the ECS contract in [../design/ion-rendering3d.md](../design/ion-rendering3d.md).

- *Data types* in `Graphics.Abstractions` as planned (unmanaged structs, integer handles): `Transform`, `Camera` (with `CameraClear` and a culling mask), `MeshRenderer`, the three lights, `SceneEnvironment` (not `Environment`, which would clash with `System.Environment` in every file importing `Ion.Extensions.Graphics`), `UnlitMaterial`, `PbrMaterial`, `Aabb`, `BoundingSphere`, `Frustum`, `MeshData` with `MeshPrimitives`, `IMeshBatch`/`IRenderer3D`, `IModel`, `ICubemap`. `GlobalTransform`, `Parent`/`Children`, `CameraMain`, `Visibility` and `ViewVisibility` are left to the ECS module (they are ECS concepts; the renderer takes world matrices). The immediate-mode API is `Submit(in MeshRenderer, in Matrix4x4 world)` plus `Draw(mesh, material, world)`, `AddCamera`/`SetCamera`, `AddLight`, `SetEnvironment`.
- *Stage placement.* Rather than separate Extract/Prepare/Queue steps at -300/-200/-100, the renderer is one scope at `StageOrder.Rendering3D` (-860) whose `End` runs prepare, queue and the graph: submissions are the extract (copies into flat arrays), so extraction systems can run at any Render order and the renderer sees all of them. The scope encloses the sprite batch's, whose submission is deferred to the graph's overlay pass, so 2D always draws on top.
- *Render graph* as planned: passes with declared reads and writes, ordered by pass order and derived dependencies, unused passes culled, transient textures pooled and aliased by lifetime. Built-in passes: shadow, depth prepass (option), opaque, skybox, transparent per camera, custom passes, 2D overlay. No post pass is built in.
- *Shaders* are GLSL 4.5 (the build pipeline of Stage 4, now with `#include`), not WGSL: the custom material extension point takes GLSL with the same bind group conventions; WGSL would be the input of a future WebGPU backend with the same numbers. PBR: Cook-Torrance GGX, one shadowed directional light (3x3 PCF), 8 point/spot lights, ambient from the skybox's mips. Standard depth, not reverse-Z (GLES has no clip control).
- *RHI additions*: cube map textures (`TextureDimension.Cube`, `TextureRegion.ArrayLayer`) and sampled depth with comparison samplers, on Vulkan and GLES, with contract tests; a GLES depth-only pipeline fix.
- *glTF* through an own reader over `System.Text.Json` (AOT clean without evaluating SharpGLTF): meshes, metallic-roughness materials, textures, node tree, GLB and data URIs; no skinning, animation, sparse accessors or compression.
- *Measured*: 10,000 mesh renderers, 3 materials: extract plus queue 1.20 ms with shadows (0.88 ms without, 1.42 ms with two cameras), 0 B per frame (`Renderer3DBenchmarks`, [benchmarks/2026-09-26-stage5-rendering3d](benchmarks/2026-09-26-stage5-rendering3d)). Vulkan and GLES renders of the samples match one golden per sample (at most one pixel differs by more than 2 levels).

### 4.9 ECS integration (Stage 5): Arch or Friflo, decided by measurement

**Decision (owner).** Whichever of Arch 2.1 and Friflo.Engine.ECS 3.6 is faster. The benchmark added for this (`EcsComparisonBenchmarks`, section 5.7) measures the three operations that matter for a renderer and a game loop on 10k entities: iteration with two components (delegate, struct functor, raw chunk spans), entity creation, and a structural churn pass. The numbers and the resulting pick are in section 5.7; the integration layer below is written so that the choice is confined to one module (`Ion.Extensions.Ecs.Arch` or `Ion.Extensions.Ecs.Friflo`) behind the same engine-facing surface.

Facts that weigh alongside speed: Friflo ships explicit NativeAOT and WASM support with no `unsafe`, per-system timing and allocation monitoring, hierarchy, relations, indexed components and JSON serialization in one actively released package, and throws a clear error on structural changes inside a query; its struct-functor query API is only in the 4.0 preview. Arch has the JobScheduler-based parallel chunk queries, a larger community, and the sample already uses it, but its AOT story rests on a 2024 generator (`ArrayRegistry`/`ComponentRegistry` emit IL3050 today) and its NuGet releases lag master.

**Integration surface (`Ion.Extensions.Ecs`):**

- `World` registered per scene scope (`AddEcs()`), disposed with the scope; a root `World` when there are no scenes.
- Systems inject `World` and use the library's queries directly, or the Ion `[Query]` attribute, which the schedule generator expands into the chunk-span loop (the fastest form on both libraries, section 5.7) as a plain named method, no lambdas:

```csharp
public sealed partial class SpriteExtractSystem(World world, ISpriteBatch sprites)
{
    [Render(Order = -300), Query, All<Sprite, GlobalTransform2D>, None<Hidden>]
    private void Extract(ref Sprite sprite, ref GlobalTransform2D transform)
        => sprites.Draw(sprite.Texture, transform.Matrix, sprite.Size, sprite.Color, sprite.Depth);
}
```

- A `Commands` service (the library's command buffer) injected into systems for structural changes; flushed automatically at the end of each stage by a generated step, so iteration is never invalidated mid-query. Structural changes inside a query throw a clear error naming the system and the entity.
- Built-in component modules: `Transform2D`/`Transform` hierarchy with propagation systems, `Sprite`, `SpriteAnimation`, `Camera2D`/`Camera`, `MeshRenderer`, lights, `Aabb`/visibility, `Name` (for the remote protocol).
- `Ion.Extensions.Ecs.Rendering` contains the extraction systems for 2D and 3D described in 4.8. Entities are never touched by the renderer; extraction copies into flat arrays every frame (Bevy's model), which also makes the renderer usable from non-ECS code.
- Physics is a P1 module (section 4.12) with adapter systems, following the Breakout ECS sample.

**As implemented (Stage 5, ECS half, September 2026).** Arch 2.1, as measured in 5.7. Packages `Ion.Extensions.Ecs.Abstractions` (attributes, `Commands`, components), `Ion.Extensions.Ecs` and `Ion.Extensions.Ecs.Rendering`; users keep Arch's `World`, `Entity` and queries. Decisions and deviations:

- *World per scope.* `World`, `Commands` and `NameRegistry` are transients whose factory returns the scope's instance (`EcsWorlds`): the root provider gets the root world, each scene scope its own, disposed with it. A scoped `World` would fail DI scope validation (and ION006) in the root schedule, which needs a world too. The built-in systems are transients for the same reason, so `app.UseEcs()` and `scene.UseEcs()` each get instances bound to their world.
- *`[Query]`.* The generator emits, into the partial system, a public hidden `__IonQuery_{Method}(GameTime, World[, Commands])` that loops over the chunks with `GetFirst`/`Unsafe.Add` in Arch's order (last to first within a chunk, as `World.Query`) and checks after each entity that neither the chunk's count nor the world's size changed (`StructuralChangeException` naming the step and the entity; `[Query(Unchecked = true)]` drops the check). It copies the method's stage and ordering attributes and carries `[ExpandedStep]`, so both the generated schedule and the reflection planner run it under the method's name; the generated schedule calls it directly, also for systems of referenced assemblies (through their metadata). Tie-break: expansions number after the type's other public methods, so at equal `Order` a query step runs after the system's other steps. Without the generator, `QueryAttribute` (a `StepBinderAttribute`, the new core hook) binds the method by reflection with boxed components (about 28x slower). Diagnostics ION301 to ION307. A module initializer registers the query components with Arch (`EcsComponents`), which NativeAOT needs; `Arch.AOT.SourceGenerator` 1.0.1 was not used: it targets Arch 1.x (`Arch.Core.Utils.ComponentType`) and brings Roslyn Workspaces as a runtime dependency.
- *Commands.* Arch's `CommandBuffer` plus hierarchy commands, played back by `EcsCommandsSystem` at `StageOrder.Ecs` (950) at the end of every stage.
- *Transform propagation* runs at `StageOrder.TransformPropagation` (-400, the start of the user band) in Last and again in Render before the extraction: Last alone would draw every frame one frame late (Render runs before Last in Ion's loop) and draw new entities at the origin. The second pass is the dirty check only when nothing moved since Last. Dirty tracking needs no change flags: the global component keeps the local transform and parent version it was computed from.
- *2D extraction* at `StageOrder.Extract` (-300: inside the sprite batch scope, after the propagation, before the game's Render steps at 0). It draws straight from the chunks when depths are already in order and sorts integer keys (depth bits, query position) otherwise. The main camera is the engine's `Camera2D` class used as a component with the `MainCamera` tag.
- *3D.* The 3D local transform is `Ion.Extensions.Graphics.Transform` (the 3D SDK's); `GlobalTransform.Matrix = local.ToMatrix() * parent`, the contract of `docs/design/ion-rendering3d.md` section 8. 3D extraction (`MeshRenderer`, cameras, lights) followed in the second ECS wave (next bullet).
- *Serialization* is Ion's own (JSON with source-generated System.Text.Json metadata, and a binary format), opt-in with `AddEcsSerialization()`. `Arch.Persistence` 2.0.0 does not load against Arch 2.1 (`TypeLoadException` on `Arch.Core.Utils.ComponentType`), pins a MessagePack prerelease with a known vulnerability (NU1902) and uses Utf8Json (IL emission, no NativeAOT).
- *3D extraction (second ECS wave).* `Scene3DExtractionSystem` (`AddEcsRendering3D()`/`UseEcsRendering3D()`, per world like the 2D one, which is unchanged) runs at `StageOrder.Extract`: `MeshRenderer` + `GlobalTransform` entities go to `IMeshBatch.Submit` (a chunk loop: the query switches on `RequireVisible`), `Camera` entities to `AddCamera`, the three lights to `AddLight` (`[Query]` steps), all skipping `Hidden`; the world's `SceneEnvironment` is a singleton component (`World.SetEnvironment`) passed with `SetEnvironment` only when it changes (and the default once removed). `World.SpawnModel`/`Commands.SpawnModel` instantiate an `IModel`: a root entity with the placement, one entity per node (local transform, `Parent`/`Children`, `EntityName`), a `MeshRenderer` per primitive on the node or on child entities when there are several; with `Commands` the root is a placeholder and the nodes are created at playback. `Hidden` is not inherited (`World.SetHidden(entity, hidden, recursive)` covers a subtree); per-camera visibility stays with `LayerMask`/`CullingMask`, and no `ViewVisibility` component is exposed. `Ion.Examples.Cubes` and `Ion.Examples.Model` moved onto the module (entities, a `[Query]` animation step, the model spawned) with their golden images unchanged on Vulkan and OpenGL ES and NativeAOT publishes free of `Ion.*` warnings. Measured (`Scene3DExtractionBenchmarks`, [benchmarks/2026-09-26-stage5-ecs3d](benchmarks/2026-09-26-stage5-ecs3d)): 10,000 mesh entities extract in 82 us against 64 us for `Submit` from flat arrays (the `Extract_Submit10k` shape, 1.28x), 1.22 ms with the renderer's CPU pipeline (1.20 ms from arrays), 127 us with the propagation's unchanged pass, 0 B per frame.
- *Results:* see 5.9. The Breakout ECS sample runs on the module (golden images unchanged: at most 156 pixels differ by 2/255 from rotated sprites placed from their origin), and publishes with NativeAOT for linux-x64 and linux-arm64 with no Ion, Arch or Collections.Pooled warnings.

### 4.10 Agentic development (Stage 6)

Concrete capabilities, in priority order, each with the engine feature that delivers it:

1. **One CLI entry that exits.** `ion run --headless --frames 600 --seed 42 --screenshot out/frame600.png --summary out/run.json` (also `dotnet run -- --headless ...`). Exit code reflects exceptions; the summary JSON has frame stats, counters, warnings, and the schedule.
2. **Deterministic stepping.** `IClock` with a `FixedStepClock`; `GameLoop.Step()` public and used by tests; `RecordedInput`/`ScriptedInput`.
3. **Headless rendering + screenshots.** `Graphics.Headless` renders to an offscreen texture and reads back PNG; also available in windowed mode via `window.Screenshot(path)`.
4. **Snapshot tests.** `Ion.Testing` helpers: `IonTestHost.Run<TGame>(frames)` returns state, counters and an image; golden-image comparison with tolerance and diff output; world state serialized via `Arch.Persistence`.
5. **Machine-readable output.** JSONL frame log, Chrome trace export, `--print-schedule`, structured exceptions that name stage/system/entity.
6. **Remote inspection.** `Ion.Extensions.Remote`: JSON-RPC over HTTP or stdio modelled on the Bevy Remote Protocol (`world.query`, `get/insert/mutate/remove_components`, `spawn/despawn`, `resources`, `+watch` streaming, `registry.schema`, `rpc.discover`, `input.send`, `screenshot`, `metrics`), and a small MCP server on top so Claude Code can drive a running game. Access control is part of the design, not an afterthought: the server is off unless enabled by configuration or `--remote`; it binds to `127.0.0.1` only (a non-loopback bind is an explicit, logged opt-in); every session presents a bearer token generated per run (printed once to the console and written to a mode-600 file the CLI and MCP server read); operations are split into `read` (`query`, `get`, `watch`, `schema`, `metrics`, `screenshot`) and `mutate` (`insert/mutate/remove`, `spawn/despawn`, `input.send`, `resources` writes), and the mutate scope is granted only with `--remote-allow-mutations` or the equivalent config key; stdio transport inherits the parent process's trust and needs no token; mutations are applied on the game thread at a stage boundary, are idempotent per request id so a retried or interrupted request cannot double-apply, and are rejected while a scene is loading; the release build compiles the module out unless `IonRemote=true` is set at publish time. The P2 web-server module (4.12) reuses this policy and adds origin checks for browser clients.
7. **Precise errors.** Generator diagnostics for schedule mistakes, DI errors that name the system and missing service, "system registered after UseScene" warnings, no swallowed exceptions.
8. **Small, typed, discoverable API.** Few namespaces, `Ion` meta-package, XML docs, analyzers for misuse (missing `next`, `async` in a stage, `Task` in a system).
9. **Hot reload.** Keep the metadata-update handler; rebuild the generated schedule on reload; reload shaders and assets on file change with results reported on the protocol.
10. **Templates.** `ion new 2d|3d|ecs` scaffolds a game with `CLAUDE.md`, `appsettings.json`, a headless test and a snapshot test.

**As implemented (Stage 6, September 2026).** Items 1, 4, 6 and 10 are in; the protocol is specified in `docs/design/ion-remote.md` and the agent workflow in `docs/agentic/README.md`. Decisions and deviations:

- *Run settings live in the engine, not only in the tool.* `Ion:Run:Frames` (honoured by `IonApplication.Run`), `Ion:Run:FixedStep` (a `FixedStepClock`, default on when headless with a frame count), `Ion:Run:Screenshot` and `Ion:Run:Summary` (a `RunReportSystem` added by `UseIon` that writes the PNG and the summary at Destroy, and the summary with the exception from an unhandled-exception hook) work in every game with `dotnet run -- ...`. `ion run` builds the project, passes them, and adds the process exit code to the summary (or writes a `crashed`/`build-failed` summary when the game left none). Short switches `--headless`, `--headless-render`, `--remote`, `--remote-allow-mutations`, `--remote-stdio` are rewritten by `IonApplication.CreateBuilder` (`IonCommandLine`).
- *Remote module.* One server with three transports on `System.Net.Sockets` and blocking I/O (HTTP/1.1, RFC 6455 WebSocket, newline-delimited stdio), requests applied on the game thread by a Last step at `StageOrder.Remote` = 970 (after the ECS command playback at 950, before the event stepping at 1000). The security model is item 6 as written, plus refusing requests with a browser `Origin` (unless allowed) and a non-loopback `Host` when bound to loopback. Two tokens per run (read, and mutate only with `--remote-allow-mutations`) rather than one token with a scope flag, so one server can hand out both. Idempotency is keyed by (id, method, params), so two clients reusing small integer ids do not collide on different requests. The compile-out is the `Ion.Remote.IsSupported` feature switch driven by `IonRemote` (default false for `Release`), guarding `AddRemote` and every `AddRemoteMethods`/`AddRemoteResource`/`AddRemoteEvent` registration so ILC drops the module. Additions to the BRP method set: `game.info/pause/resume/step/exit` (pausing blocks the game thread inside the remote step, serving requests; `game.step` answers after its frames ran), `schedule.get`, `metrics.get`, `screenshot`, `input.send`, `events.tail`, `log.tail`. Not done: `+watch` over HTTP server-sent events (watches need WebSocket or stdio), hot-reload results on the protocol (item 9).
- *Components over the protocol* are the world serializer's registry (`AddEcsSerialization`), not a `[RemoteVisible]` attribute and a new generator: registering a component for serialization is already the opt-in and already carries its source-generated JSON metadata, so the ECS module's `IRemoteMethodProvider` reuses it (entity references are entity ids on the wire). Item 4's world snapshots likewise use Ion's own serializer instead of `Arch.Persistence` (see 4.9).
- *Input injection* is Input v2's scripted path: `ScriptedInput` (thread-safe, frame-delayed events) attached to the shared `InputTracker` as `InputTracker.Script`, applied at `BeginFrame` next to device input and recorded like it.
- *MCP.* `Ion.Tools.Mcp` implements MCP (2024-11-05 to 2025-06-18) on stdio by hand (no SDK dependency, AOT-compatible) and is started by `ion mcp`; `ion_run` either runs to completion and returns the summary, or (`live=true`) launches the game with the remote protocol, pauses it after N frames and connects through the token file.
- *Templates* are embedded in the tool and packed as `Ion.Templates` (`dotnet new ion-2d|ion-3d|ion-ecs`); `--ion-source` (template parameter `IonSource`) builds a new game against an Ion checkout instead of the packages, which is how the template tests compile them.

### 4.11 Native compilation and platforms (Stages 2, 4, 7)

- **.NET 10 everywhere.** Engine, generators' consumers, samples and templates target `net10.0` from Stage 0; generators stay `netstandard2.0` on Roslyn 4.4 so any SDK 8.0+ can still compile a game that references the packages. .NET 10 brings the stable interceptors, escape analysis for delegates, the newer NativeAOT (Android support), and `net10.0-browser`, `net10.0-android`, `net10.0-ios` TFMs for the platform heads.
- **NativeAOT on desktop** (Windows, macOS, Linux; x64 and arm64) is the primary "native compilation" story and is already almost there: the engine's own code produced two AOT warnings. After Stage 2 (no reflection binder, generated config binding) and Stage 4 (no Veldrid/DependencyModel/Newtonsoft, no NAudio COM) the engine and templates publish with `PublishAot=true` and zero warnings. `IlcGenerateStackTraceData` stays on so traces remain readable; `EventSourceSupport` on for `dotnet-trace`.
- **R36S and other Linux arm64 handhelds** are a NativeAOT `linux-arm64` publish with the GLES backend, SDL windowing, and a fullscreen 640x480 default. A cheap board on CI (or QEMU user-mode for smoke tests) keeps that lane honest; the benchmark suite runs there too so budgets are set against the slowest primary target rather than a desktop.
- **Mobile.** iOS NativeAOT (since .NET 9) and Android NativeAOT (.NET 10) through the SDL windowing platform and the Vulkan backend (GLES fallback on older Android). Not scheduled before Stage 7, but nothing in the plan blocks it: no async, no reflection, no JIT dependency.
- **Browser** stays optional: `net10.0-browser` with the `wasm-tools` workload (Mono interpreter/AOT, single-threaded), a `Graphics.WebGPU` RHI backend, a canvas windowing/input shim, and `requestAnimationFrame` driving `GameLoop.Step`. The "no async" design and the WebGPU-shaped RHI keep this feasible whenever it is picked up.

**As implemented (Stage 7, September 2026).** Usage, presets and numbers in [../platforms/publishing.md](../platforms/publishing.md); the handheld in [../platforms/r36s.md](../platforms/r36s.md); measurements in [benchmarks/2026-09-stage7-publish](benchmarks/2026-09-stage7-publish/README.md). Decisions and deviations:

- *Presets are MSBuild, the CLI forwards.* `build/Ion.Publish.props` (imported by `Directory.Build.props`, so the runtime identifier is set before the SDK infers anything) defines `IonTarget` = `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64`, `r36s`: NativeAOT, self-contained, full trimming, `IlcGenerateStackTraceData`, `InvariantGlobalization`, `StripSymbols`, EventSource on desktop only, no debugger or metadata-update support. Libraries, tests, benchmarks and generators ignore the property (it flows to references as a global property). `ion publish --target <preset>` is `dotnet publish -p:IonTarget=<preset>`; the templates get the presets through the same `Directory.Build.*` imports when built against the source (`--ion-source`), and a packaged `Ion.Publish` props/targets NuGet for games outside the repository is not done.
- *r36s* is `linux-arm64` plus `appsettings.r36s.json` (SDL, fullscreen 640x480, OpenGL ES, no cursor; skipped when the game ships its own) and the ArkOS layout `<publish>-arkos/ports/<name>.sh` + `ports/<name>/` + `README.md`, built by an `AfterTargets="Publish"` target that can also run alone. The launcher uses the system SDL2 and `DOTNET_ENVIRONMENT=r36s`. Cross-linking from x64 uses lld and a glibc 2.27 sysroot built by `build/arm64-sysroot.py` (warning `IONPUB003` without one).
- *Measured.* Breakout ECS: linux-x64 12.6 MB executable (16.4 MB shipped with SDL2, OpenAL, GLFW and assets), 46 ms median from process start to exit after one headless frame; r36s 11.6 MB, `GLIBC_2.27` at most, 0.6 s under `qemu-aarch64` user-mode emulation on the x64 VM (an upper bound; not the device). Every sample publishes with the linux-x64 preset without Ion warnings (the two remaining ILC warnings are Silk.NET.Core's). The CI AOT lane runs `measure.sh` for linux-x64 and r36s (QEMU), uploads `Stage7PublishResults` and fails only above 30 MB for a desktop executable.
- *Cross-OS publishing is not possible.* NativeAOT compiles only for the host OS family, so `win-*` and `osx-*` presets publish on their own runners (CI `publish-desktop`, behind `ION_PUBLISH_CI`); from the Linux container their runtime packs restore through the NuGet feed, then `win-x64` stops at the SDK's cross-OS check and `osx-arm64` compiles its object file (no Ion warnings) but cannot link without Apple's linker (see publishing.md).
- *Mobile heads.* `Ion.Examples.Breakout.ECS.Android` (`SilkActivity`, APK assets unpacked to the files folder) and `.iOS` (`SilkMobile.RunApp`, bundle resources, MoltenVK) link the desktop sample's game sources and enter through `BreakoutMobile.Run`; SDL is view-only on those platforms, so `SilkWindow` now creates a Silk.NET view there (window-only setters are no-ops). Touch input (Input v2 `IInputState.Touches`, SDL finger events through an event watch) drives the paddle. The heads build for real only with `-p:IonMobileHeads=true` and the workload (`build/Ion.Mobile.props`, error `IONMOB001`), otherwise as `net10.0` libraries of the shared code, so `Ion.sln` builds everywhere; CI `mobile-android` and `mobile-ios` are skeletons behind `ION_MOBILE_CI`. The android workload installed here through the NuGet feed and the head compiles for `net10.0-android`, but packaging needs the Android SDK (dl.google.com is not reachable from the container); the ios workload does not install on Linux.
- *Not done.* Running on an R36S, an Android phone or an iPad (the acceptance line); windowed start-to-present timing; a scaled view for the fixed-size Breakout layout on 640x480 and phone screens; signing and store packaging; the browser target (4.11, unchanged).

### 4.12 Modules beyond rendering

Priorities from the owner: audio is P0 (it must work on every primary target before anything else ships), UI and physics are P1, a web server for companion apps/integrations and multiplayer networking are P2. All of them follow the same rules as the rest of the engine: no async in the frame, systems scheduled through the generator, state inspectable over the remote protocol, and a headless mode for tests.

**Audio (P0, `Ion.Extensions.Audio`, rewritten in Stage 3).** Today's module is NAudio over DirectSound, which exists only on Windows and cannot ship on the R36S, mobile or macOS. The replacement is a small engine-owned mixer (float32 interleaved, resampling on load, master/bus/voice gains, pitch, pan, looping, fade) feeding a platform output through `Silk.NET.OpenAL` (OpenAL Soft ships for every primary target including linux-arm64, iOS and Android, and Silk.NET already packages it). The mixer runs on the audio thread and is fed from a lock-free command queue written on the game thread in the `Last` stage, so playing a sound never blocks a frame. Decoding (WAV, OGG Vorbis via a managed decoder, MP3 optional) happens at load time through the asset pipeline; streaming music is a polled job. A `NullAudioOutput` keeps headless runs silent and deterministic. Acceptance: the Breakout samples play on all three desktops and on the R36S; the audio thread never allocates after warm-up; `pitchShift`, `MasterVolume` and per-voice volume verified by tests against the mixed buffer.

**UI (P1, `Ion.Extensions.UI`, Stage 5b).** An immediate-mode UI (the kind of API Dear ImGui popularised, but engine-native and retained where it matters for hit testing) is the best fit for an agentic, code-first engine: no separate markup, layout is C# and therefore generated-schedule friendly, and a screen can be described and diffed as data. It sits on the 2D renderer (sprites, nine-slices, text, clip rects), takes input from Input v2 (pointer, keyboard, gamepad focus navigation for the R36S), and exposes a tree that the remote protocol can query (`ui.tree`, `ui.click(path)`), which is what lets an agent drive menus without pixel-hunting. Flex-style layout with a fixed set of widgets (panel, label, button, toggle, slider, text input, list, scroll view) and a theme struct. Dear ImGui through `Silk.NET`-bound `ImGui.NET` remains available as a separate debug-overlay module, not the game UI. (Implemented in Stage 5b: see "As implemented (Stage 5b, UI)" in section 6 and [../design/ion-ui.md](../design/ion-ui.md).)

**Physics (P1, `Ion.Extensions.Physics2D` and `Physics3D`, Stage 5b).** 2D on Box2D v3 (through its C bindings, deterministic, SIMD, arm64-friendly) or Aether.Physics2D (pure managed, already used by the sample, but its XML serializer is the largest source of AOT warnings today); 3D on BepuPhysics v2 (pure managed, SIMD, no native dependency, well suited to NativeAOT) with Jolt as the alternative if a native library is acceptable. Both modules provide: a `PhysicsWorld` per scene scope, `RigidBody`/`Collider`/`Joint` ECS components with adapter systems (`FixedUpdate(Order = -100)` push transforms, step, pull transforms), collision and trigger events on the typed event bus, a fixed step decoupled from render rate, a debug-draw system on the 2D/3D renderers, and deterministic stepping for replay tests. Decision between the 2D candidates is a benchmark in Stage 5b (10k dynamic bodies, arm64 included).

**As implemented (Stage 5b, physics, September 2026).** Design in [../design/ion-physics.md](../design/ion-physics.md), measurements in [benchmarks/2026-09-physics2d](benchmarks/2026-09-physics2d/README.md). Decisions and deviations:

- *2D engine: Box2D v3.1, the C library* through `Box2D.NET.Bindings.Release` 3.1.0 (generated P/Invoke, natives for win, osx, linux, android and ios on x64 and arm64, static libraries for NativeAOT). Measured at 10,000 bodies, 600 steps, NativeAOT, one thread: 14.6 ms per step on x64 against 72.7 ms for ikpil's managed port of Box2D v3.1 (`Box2D.NET`, a third candidate found on the feed) and 340 ms for Aether.Physics2D; the same order under QEMU arm64. No managed allocation, no AOT warning. The other v3 wrapper on the feed (`Box2dNet`) has no arm64 natives.
- *Determinism across architectures is a build flag.* The packaged arm64 natives are compiled with fused multiply-add contraction and differ from x64 after a few steps; Box2D 3.1.0 built with `-ffp-contract=off` (as Box2D's CMake does; script in the benchmark folder) reproduces the x64 results bit for bit on arm64, at 10,000 bodies and on the 10,000-step replay scene. The 2D replay golden is therefore linux-x64 (`0x7DF39E42CA6D46A6`, also the arm64 hash with the contraction-free build; `0x5780ABC152A90013` with the packaged arm64 natives). BepuPhysics is identical across x64 and arm64 at equal `Vector<float>` width (4 lanes, including NativeAOT x64: `0x0987708E02C11079`; the JIT on AVX2 uses 8 lanes: `0x0A315BD5EFFEF90B`).
- *Order.* `StageOrder.Physics = -700` (FixedUpdate, engine band, before scenes and the game's fixed steps) rather than the sketch's `-100`, so user fixed steps see the step's result and their changes are simulated next; `StageOrder.PhysicsDebugDraw = 650` in Render (under the UI at 700), inside the sprite batch scope.
- *Components.* A collider makes a body (static without a rigid body); one collider per entity; components are the source of truth and the world keeps the last synchronized state per body to detect changes, so there are no change flags. Kinematic bodies are driven to their transform each step. Events are plain unmanaged records on `IEvents` (`Collision2D`/`Collision3D`, `Trigger2D`/`Trigger3D`) with begin and end phases; queries write into caller spans.
- *3D.* BepuPhysics 2.5.0-beta.29 (the maintained line; 2.4.0 targets net6.0). Restitution is approximated by the contact spring. Joints need bodies on both sides (Bepu constrains bodies only). Threads: single-threaded by default; `ThreadCount > 1` uses a Bepu `ThreadDispatcher` in deterministic mode that the step waits for.
- *Breakout ECS* runs on the module (balls, blocks, walls and a capsule paddle as entities; `Collision2D` translated into the game's events); Aether is gone, and its NativeAOT publish has no Ion warning and two in total (Silk.NET.Core), down from eleven.
- *Results* (`Ion.Benchmarks`, JIT, 0 B allocated everywhere): 2D step 0.77 ms at 1,000 bodies and 14.3 ms at 10,000; 3D step 1.73 ms at 1,000; the adapters' push and pull cost 68 ns (2D) and 40 ns (3D) per moved body.
- *Not done:* compound colliders, chains and segments, 3D triangle meshes, 3D joint motors and limits, shape casts, exact 3D overlaps, and contraction-free Box2D natives built on CI for every RID (until then cross-architecture lockstep needs the documented build).

**Web server (P2, `Ion.Extensions.Web`, Stage 6b).** A minimal HTTP/1.1 and WebSocket server embedded in the game process, running on its own thread with a lock-free queue into the game thread, so companion apps (second-screen controllers, level editors, dashboards) and integrations (Twitch, Discord, webhooks) can talk to a running game. It reuses the remote protocol from Stage 6 (JSON-RPC over HTTP and WebSocket) and adds routing for game-defined endpoints declared as ordinary methods on systems (`[Http("GET", "/score")]`), which the generator turns into a table (no reflection, AOT-clean). Kestrel is deliberately not used: it is async-first, large under AOT, and not needed for a handful of endpoints. (Implemented in Stage 6b: see "As implemented (Stage 6b, web)" in section 6 and [../design/ion-web.md](../design/ion-web.md).)

**Multiplayer networking (P2, `Ion.Extensions.Networking`, Stage 6b).** Designed in full in `docs/design/ion-networking.md` (a revision of the earlier network plugin drafts): hybrid model of generator-serialized `[Replicated]` components with delta encoding against each peer's acknowledged tick, plus typed `[NetworkMessage]` structs read through per-system `NetworkReader<T>` cursors that mirror `IEvents`; a snapshot ring buffer captured by a `FixedUpdate` `End` scope every tick, which is the shared foundation for interpolation, client-side prediction with reconciliation, and server lag compensation (bounded by `MaxRewindTicks` and RTT plausibility); server authority by default with per-component owner authority; automatic `NetworkId`; transports behind `INetworkTransport` (LiteNetLib UDP first, WebSocket second, `LoopbackTransport` with a seeded simulated network for `IonTestHost` tests); dedicated servers are the same game run headless with `Mode=Server`; handshake with protocol and registry hashes, optional join secret, per-message authority checks, rate limits, allocation-free receive path. Steps live in the engine order bands so user systems need no registration order.

---

## 5. Benchmark baseline

`Ion/Ion.Benchmarks` (BenchmarkDotNet, `--job short`, .NET 8 runtime, Linux x64 container, single run; treat as order-of-magnitude, re-run locally for precise numbers). Full tables are in `docs/plans/benchmarks/2026-09-25-baseline/`.

### 5.1 Middleware dispatch (`PipelineBenchmarks`, `InliningBenchmarks`)

| Systems | Direct calls (flat loop) | Ion reflection-bound chain | Hand-built closure chain |
|---|---|---|---|
| 1 | 0.4 ns | 1.0 ns | 0.6 ns |
| 8 | 3.5 ns | 19.9 ns | 15.4 ns |
| 32 | 14.5 ns | 112.6 ns | 93.1 ns |

Zero allocations in all three. The closure chain costs 3 to 3.5 ns per hop (indirect call plus closure load) versus 0.45 ns for a direct call; the reflection binder adds one more delegate layer per system (`CreateDelegate` result wrapped in a lambda). This is small in absolute terms (about 0.1 us per frame for 32 systems) and would be a measurable win only under NativeAOT, where delegates never get devirtualized. The real reasons to generate the schedule are the stack traces, startup, AOT cleanliness and schedule validation, not this number.

The struct-generic chain prototype (`InliningBenchmarks`) is the dispatch shape the generator will emit for factory-style middleware. Depth 8: direct calls 4.0 ns, closure chain 15.5 ns, struct-generic chain 12.5 ns, and 83 ns when the system slot is class-constrained (a generic virtual call per hop, the trap to avoid). So the constrained-call chain beats delegates by about 20 percent under the JIT but does not reach direct calls at this depth (inlining budget); it is the right shape only for factory-style middleware that cannot be split, and the flat generated schedule with direct calls is the default. Under NativeAOT the gap to delegates is expected to be larger because ILC cannot devirtualize delegates at all; that is to be measured in Stage 2 when the AOT benchmark lane exists.

### 5.2 Full frame engine tax (`FullFrameBenchmarks`)

| Configuration | Per `Step` | Allocated per frame |
|---|---|---|
| Event system only | 24 ns | 24 B |
| 8 systems binding all stages | 125 ns | 24 B |
| Same plus `AddDebugUtils` | 119 ns | 0 B |
| 8 systems inside a scene scope | 197 ns | 144 B |

The engine's fixed per-frame cost is negligible in time, but it allocates: 24 B per frame is one boxed `NullTimerInstance` from `EventSystem.StepEvents`, and a scene adds six more (`SceneSystem` calls `trace.Start` in every stage). At 120 fps that is 17 KB/s of garbage from the engine alone before a game draws anything. Installing the Debug package removes it because `TraceManager` caches its null instance. This is bug 5 in section 3.8.

### 5.3 Events (`EventBenchmarks`, 100 events of 4 types per frame)

| Scenario | 1 listener | 8 listeners | Allocated |
|---|---|---|---|
| `Emit` x100 + `Step` | 2.4 us | 2.4 us | 3,600 B |
| `Emit` x100, every listener drains all 4 types with `while (On<T>(out e))`, `Step` | 191 us | 1,322 us | 3,600 B |
| `Emit` x100, every listener calls `OnLatest<T>` for 2 types, `Step` | 7.0 us | 40 us | 3,600 B |
| Typed-channel prototype, same emit and drain | 0.23 us | 0.58 us | 0 B |

After Events v2 (`docs/plans/benchmarks/2026-09-25-stage3-events`, .NET 10, same machine; the old rows re-measured at 2.4 us / 180 us / 1,492 us before): `Emit` x100 + `Step` 200 ns (0 B); every reader drains all 4 types with `Read()` 258 ns with 1 reader and 650 ns with 8 (prototype 204 / 605 ns); with `TryRead` loops 383 ns / 1.8 us; `TryReadLatest` for 2 types 209 / 235 ns; the generated bus 329 / 758 ns; calling `Emit` through the `IEvents` interface (a generic virtual call per emit) 807 ns / 1.2 us; the obsolete `IEventListener` adapters 2.7 / 13.8 us. Nothing allocates.

This is the most important number in the suite. Each `Emit` boxes (36 B per event) and, worse, each `On<T>(out)` call rescans both frame buffers from the start and probes two `HashSet`s per entry, so draining N events of a type is O(N^2) and multiplies by the number of listeners and event types. Eight systems each reading four event types with 100 events in flight costs 1.3 ms, 8 percent of a 60 fps frame, for the event bus alone. The unboxed per-type channel with a cursor per reader (`TypedChannel<T>` in the benchmark project) does the same work in 0.6 us with no allocation, a 2,000x difference. Events v2 in section 4.4 is this design.

### 5.4 Trace timers (`TraceBenchmarks`)

| Timer | `Start` + `Stop` | Allocated |
|---|---|---|
| Core default `NullTraceTimer<T>` | 7.4 ns | 24 B |
| Debug package `TraceTimer<T>` (Release: disabled path) | ~0 ns (devirtualized and inlined by PGO) | 0 B |

Under NativeAOT the Debug path remains two interface calls per bracket; Metrics v2 replaces both with a generated timestamp write behind a static feature switch.

After Metrics v2 (`MetricsBenchmarks`, `docs/plans/benchmarks/2026-09-25-stage3-metrics`, .NET 10, 256 operations per invocation):

| Row | Mean | Allocated |
|---|---|---|
| `MetricsScope` enter and exit, profiling off | 0.77 ns | 0 B |
| `MetricsScope` enter and exit, profiling on | 76 ns | 0 B |
| Generated `Begin`/`End` bracket, off | 0.61 ns | 0 B |
| Generated `Begin`/`End` bracket, on | 74 ns | 0 B |
| `MetricsCounter.Increment` | 6.6 ns | 0 B |
| `FrameStats` write (`EndFrame` + `BeginFrame`) | 113 ns | 0 B |
| Obsolete `ITraceTimer` adapter, off | 0.88 ns | 0 B |
| Obsolete `ITraceTimer` adapter, on | 148 ns | 0 B |

The enabled rows are two `Stopwatch.GetTimestamp()` reads at 40 ns each on this VM. `FullFrameBenchmarks` after the change:

| Configuration | Per `Step` | Allocated per frame |
|---|---|---|
| Event system only | 15 ns | 0 B |
| 8 systems binding all stages (runtime schedule) | 100 ns | 0 B |
| Same, generated schedule (profiling compiled in, off) | 63 ns | 0 B |
| 8 systems with `AddMetrics`/`UseMetrics` (frame stats, meter, profiling off) | 254 ns | 0 B |
| Generated schedule with a frame profiler (stats only) | 181 ns | 0 B |
| Generated schedule, profiling on (about 46 spans per frame) | 3.57 us | 0 B |
| 8 systems inside a scene scope | 123 ns | 0 B |

### 5.5 Sprite batching CPU (`SpriteBatchBenchmarks`, 10,000 sprites)

| Textures | Without scissor transform | With the renderer's scissor transform |
|---|---|---|
| 1 | 269 us (27 ns/sprite) | 281 us |
| 16 | 267 us | 295 us |

Zero allocations; grouping by texture is free at this scale. 27 ns per sprite for the CPU write is acceptable but the 80-byte instance (16 B color, 16 B unused scissor) is what limits the GPU upload; the scissor transform adds 4 to 10 percent on the CPU and a per-pixel `discard` on the GPU. Renderer v2 targets 10 ns and 40 bytes per sprite.

After Stage 4 (`docs/plans/benchmarks/2026-09-25-stage4-rendering2d`, a different and noisier VM, so compare within the table): a whole frame of the new `SpriteBatch` (`Begin`, 10,000 `Draw`, `End` with sorting and draw ranges) against a verbatim copy of the old per-sprite work.

| Textures | Old batcher (80 B instance) | New `SpriteBatch`, `Deferred` (40 B) | with the upload copy | `Texture` sort | `BackToFront` sort |
|---|---|---|---|---|---|
| 1 | 124 us (12.4 ns/sprite) | 57 us (5.7 ns, 0.46x) | 67 us | 103 us | 179 us |
| 16 | 146 us (14.6 ns/sprite) | 69 us (6.9 ns, 0.47x) | 81 us | 101 us | 190 us |

0 B per frame in every row. The per-sprite CPU cost is halved (the target) and the instance is half the size.

### 5.6 Coroutines (`CoroutineBenchmarks`)

100 coroutines yielding `Wait.For` every frame: 2.5 us and 2.34 KB per frame (24 B per coroutine per frame from boxing the `WaitFor` record struct into `IWait`).

### 5.7 ECS: Arch 2.1 versus Friflo 3.6 (`EcsComparisonBenchmarks`, `ArchQueryBenchmarks`, 10,000 entities)

The benchmark project targets Arch 2.1.0 (the version the engine would adopt; the ECS sample is still on 1.2.8) and Friflo.Engine.ECS 3.6.0. The per-entity work is the rotated-corner math a sprite extraction system does.

| Operation | Arch 2.1 | Friflo 3.6 | Allocated |
|---|---|---|---|
| Iterate, delegate query | 87 us | 95 us | 88 B each |
| Iterate, struct functor | 77 us | not in 3.6 (`IEach` is declared, consumed only by the 4.0 preview) | 0 B |
| Iterate, raw chunk spans (what the `[Query]` generator emits) | 81 us | 82 us | 0 B |
| Create 10k entities (two components) | 457 us, 632 KB | 797 us, 1.69 MB | |
| Add then remove a tag on all 10k | 122 us (archetype-level bulk `Add<T>(in query)`) | 1,041 us (per-entity command buffer, played back) | 0 B |

Iteration is a wash: both libraries hit about 8 ns per entity for the chunk-span loop, which is the form the generator will emit, and Arch 2.1 is 2.6x faster than the 1.2.8 delegate query the sample uses today (215 us in the first baseline run versus 87 us). Arch creates entities 1.7x faster with 2.7x less garbage, and its bulk structural API is far faster than per-entity command-buffer churn (the Friflo row is not the same algorithm, Friflo has no bulk tag add on a query; it is what a game would write). On the numbers, **Arch 2.1 is the pick**, with two conditions carried into Stage 5: NativeAOT must be made warning-free with `Arch.AOT.SourceGenerator` on linux-x64 and linux-arm64, and structural changes in user code go through the engine's `Commands` (which maps to Arch's `CommandBuffer` and to the bulk operations when a whole query is affected). Friflo stays the documented fallback: same integration surface, and it wins if the AOT condition cannot be met or once its 4.0 functor API ships and the numbers change.

### 5.9 ECS module (`EcsQueryBenchmarks`, `TransformPropagationBenchmarks`, `SpriteExtractionBenchmarks`, `Scene3DExtractionBenchmarks`, 10,000 entities)

Same VM as 5.7 (noisy: repeated runs of one row vary by about 5 percent). The query work writes the rotated corner of 5.7 into a third component.

| Row | Mean | Ratio | Allocated |
|---|---|---|---|
| Query: `ChunkSpans` (hand-written, baseline) | 73.9 us | 1.00 | 0 B |
| Query: `World.Query` delegate | 69.5 us | 0.94 | 0 B |
| Query: generated `[Query]` (checked) | 78.4 us | 1.06 (0.99 in an earlier run) | 0 B |
| Query: generated `[Query(Unchecked = true)]` | 64.1 us | 0.87 | 0 B |
| Query: the generated loop by hand, without the check | 66.7 us | 0.90 | 0 B |
| Query: reflection binder (no generator) | 2,121 us | 28.7 | 1.76 MB |
| Propagation, 3-level tree, all dirty | 232 us (23 ns per entity) | | 0 B |
| Propagation, nothing changed | 152 us (15 ns per entity) | | 0 B |
| Extraction into the 2D batch, depths in order | 154 us (vs 80 us of direct draws) | 1.94 | 0 B |
| Extraction alone, depths in order | 77 us | | 0 B |
| Extraction alone, 8 interleaved depths (sorted) | 733 us | | 0 B |
| 3D extraction (`Scene3DExtractionBenchmarks`): 10,000 mesh entities, a camera, a light | 82 us (vs 64 us of `Submit` from arrays) | 1.28 | 0 B |
| 3D extraction plus the renderer's CPU pipeline | 1,224 us (1,202 us from arrays) | | 0 B |
| 3D: propagation (nothing changed) plus extraction | 127 us | | 0 B |

The generated loop is at the chunk-span baseline's cost within noise (0.99 and 1.06 in two runs); the structural-change check costs about 1.2 ns per entity on this trivial body (0.87 unchecked). Propagation is dominated by the per-child entity lookup (one lookup of the chunk and index per child).

### 5.8 Startup (`PipelineBuildBenchmarks`)

Building the host, binding by reflection and building the seven pipelines: 1.28 ms and 296 KB for 8 systems, 2.0 ms and 451 KB for 32 (dominated by `Host.CreateApplicationBuilder`). Not a problem for startup, but it is the cost paid on every hot reload and every scene switch (each scene rebuilds its pipelines the same way). Note that every `IonApplication` that is built and not disposed leaks a `FileSystemWatcher` (the host's `appsettings.json` reload watcher); the benchmark hit the Linux inotify limit of 128 instances until it disposed the application. Tests that build many apps must dispose them.

---

## 6. Staged roadmap

Each stage is sized so a single agent session (or a small PR series) can deliver it, and each has acceptance criteria that a machine can check. Stages 0-3 are pure engine work and do not touch graphics; Stage 4 is the Silk.NET migration; 5 adds ECS and 3D; 6 and 7 make the engine agent-friendly and native/multi-platform. Stages 3 and 4 can proceed in parallel; 5 depends on 4; 6 depends on 2, 3 and the headless backend from 4; 7 depends on 4 and 5.

### Stage 0: Make it build, run and measure (1 week)

- Fix the confirmed bugs in section 3.8 (enum mapping, `WindowConfig` binding, `Bonk.wav`, generator Roslyn pin to 4.4, `NullTraceTimer` boxing, static `_scenesAdded`, `SceneSystem.next`, `DrawString` color, `MeasureString`, `Color(uint)`, input edge cases, coroutine `Stop`, storage root/truncate, texture loader leaks, stale test project).
- Remove `Ion_old`, the old `Ion.Generators`, the unused `Assimp`/`Veldrid.ImageSharp` references; move the WGPU experiment to a `spikes/` folder or delete it (its reusable parts are the backend-agnostic sprite/font code, which already exists in the Veldrid project).
- Move every project to `net10.0` (generators stay `netstandard2.0` on Roslyn 4.4); bump `Microsoft.Extensions.*` to 10.x; add `global.json` (10.0 SDK, `rollForward: latestFeature`), `Directory.Build.props` with shared TFM/analyzers/`IsAotCompatible`/`EnableConfigurationBindingGenerator`, and `dotnet format` in CI.
- CI: build on all three OSes with the pinned SDK, run tests, run `Ion.Benchmarks --job short` and publish the results as an artifact, publish the ECS sample with `PublishAot` on Linux and fail on new Ion warnings.
- Acceptance: `dotnet build` clean on the 10.x SDK; all samples start under Xvfb+lavapipe with the backend they asked for; NativeAOT publish (linux-x64 and linux-arm64) has no `Ion.*` warnings; benchmark artifact produced.

### Stage 1: Testable, deterministic core (1-2 weeks)

- `IClock` (`StopwatchClock`, `FixedStepClock`, `ManualClock`) injected into `GameLoop`; separate `FixedUpdate` rate from `MaxFPS`; frame pacing via a high-resolution wait (spin-then-sleep with timer resolution raised on Windows) and a `VSync` option that defers pacing to the swapchain.
- `GameLoop.Run(CancellationToken)` and `RunFrames(int)`; `Step()` is the unit of testing.
- Remove all interface-to-concrete downcasts; `Ion.Extensions.Graphics.Null` becomes a complete headless backend (window, context, sprite batch, input, loaders) so any game runs without a GPU.
- `Ion.Testing` package: `IonTestHost` that builds a headless app, steps N frames, exposes services, counters and emitted events.
- Tests for: fixed-step accumulation, `MaxFPS` pacing with a manual clock, all three binder signatures, scene load/unload/`Destroy` counts and scope disposal, coroutine waits, assets cache/dispose, storage, events across frame boundaries.
- Acceptance: coverage above 70 percent on `Core`, `Scenes`, `Coroutines`, `Assets`; a Breakout ECS headless test that runs 600 frames and asserts score and entity counts.

### Stage 2: Source-generated schedule, DI and clean stack traces (2-3 weeks)

- New `Ion.Generators` (Roslyn 4.4, incremental): intercept `UseSystem<T>()`, function-step registrations and `UseScene(...)` builders; emit flat `Schedule` methods per stage (Option D in 4.3: ordered leaf steps nested inside `Begin`/`End` scopes with `try/finally`), the composition root, `[StackTraceHidden]` on all generated code, and diagnostics `ION001..ION0xx` for schedule mistakes.
- Leaf steps have no `next`; wrapping is expressed with `[Begin(Stage.X)]`/`[End(Stage.X)]` scope pairs; engine steps move to the reserved order bands. The old `(GameTime, GameLoopDelegate next)` form keeps working through generated closures for one release with an `ION010` rewrite hint. (Done at runtime with reflection binding in the first half of Stage 2, see "As implemented" in 4.3.)
- `Order` on stage attributes; `[After<T>]`/`[Before<T>]` constraints; `--Ion:PrintSchedule=true` (done at runtime in the first half).
- Options binding through the configuration-binding generator; `[LoggerMessage]` logging; `[OptionsValidator]`.
- Delete `Ion.Core.InternalGenerators` and `Scenes.Generators` (their overloads are generated by the new generator on demand).
- Acceptance: `PipelineBenchmarks.Ion_*` within 1.2x of `DirectCalls_FlatLoop` for 32 systems and zero bytes allocated per frame; the section 2 stack trace contains only user frames plus `Program.Main`; `ion schedule` prints the nesting for every sample; a scope whose step throws still runs its `End` (test); NativeAOT publish of all samples with zero warnings from `Ion.*`; hot reload rebuilds the generated schedule.
- Acceptance status (generator wave, see "As implemented, second half" in 4.3): `Ion_GeneratedSchedule` 1.18x of `DirectCalls_FlatLoop` at 32 systems, 0 B per frame (met). Stack traces through the generated schedule and through a scene contain only the user step and its caller (tests in `Ion.Examples.Breakout.ECS.Tests`, met). `--Ion:PrintSchedule=true` output is identical with and without the generator (test on the ECS sample and on every generator scenario, met). A scope's `End` runs when a step throws, in the generated schedule (test, met). NativeAOT publish of the ECS sample: no Ion warnings, runs headless on the generated schedule (met; the other samples were not published in this wave). Hot reload: `GameLoop.Build()` rebuilds from the same model, including the generated factory (not exercised by a test). Not done: the `new`-based composition root, options binding through the configuration-binding generator for every module, `[LoggerMessage]`/`[OptionsValidator]`, deleting `Ion.Core.InternalGenerators` and `Scenes.Generators` (their public overloads are still needed), and compiling `Ion.Extensions.Audio` with the generator (it was being rewritten in parallel; one `ProjectReference` line once merged).

### Stage 3: Events, input and metrics (2 weeks, parallel with Stage 4)

- Events v2 (4.4): typed channels, readers, zero allocation on emit and read; `IEventListener`/`IEventEmitter` kept as thin adapters for one release, then removed.
- Input v2 (4.5): bitsets, edge semantics fixed, text input, mouse delta, gamepads, `RecordedInput`.
- Metrics v2 (4.6): frame ring, counters, Chrome trace and Tracy export, `Meter` aggregates, JSONL frame log, overlay hook. Release builds keep tracing available behind a runtime toggle.
- Coroutines: singleton runner driven by the engine in `Update`, unboxed waits (`Wait` becomes a struct union), `Stop` safe, proper namespace.
- Assets: cache by (loader, path) with reference counting, scoped release on scene unload, hot reload on file change, polled background decode for images.
- Audio (P0, section 4.12): engine mixer on the audio thread fed by a lock-free command queue, `Silk.NET.OpenAL` output, WAV/OGG decoding at load, `NullAudioOutput` for headless; fixes pitch, master volume and resampling.
- Acceptance status (events wave, see "As implemented" in 4.4): `EventBenchmarks` allocates 0 B in every new row (met). Emit x100 + `Step` 200 ns and emit + `TryReadLatest` 209/235 ns are at the prototype's 204/605 ns; emit + `Read()` of all 4 types is 258 ns with 1 reader (1.27x the prototype) and 650 ns with 8 (1.07x), where the prototype does not keep the previous frame visible or the fixed-step backlog (partly met). The generated bus row (in `Ion.Benchmarks.GeneratedApp`, which also carries the loop context and the engine's channels) is 329/758 ns, not faster than the runtime bus in this micro-benchmark; its value today is the closed type list, compile-time ids and capacities, and the diagnostics (open). `ION101`..`ION106` are reported with golden tests, and in the Breakout ECS sample they found a wall/paddle-hit event nobody emitted and a ball-lost event nobody read (met). NativeAOT publish of the ECS sample: no Ion warnings, runs headless (met). `FullFrameBenchmarks.Step_8Systems` not re-measured in this wave.
- Acceptance status (metrics wave, see "As implemented" in 4.6): frame ring, counters, Chrome trace and Tracy export, `Meter` aggregates, JSONL frame log and overlay are in place and work in Release and NativeAOT builds behind the runtime toggle (met). Trace export is bounded by the ring: a run of any length keeps and writes at most `HistoryFrames` frames (met, test). `MetricsBenchmarks`: a disabled scope 0.77 ns and 0 B (target below 1 ns, met), an enabled scope 76 ns and 0 B (target below 30 ns: not met on this VM, where each `Stopwatch` read is 40 ns; the allocation target is met). `FullFrameBenchmarks` allocates 0 B in every row, with metrics installed and with profiling on (met). NativeAOT publish of the ECS sample: no Ion warnings with profiling on, off, and with the Tracy bridge; with `IonMetricsProfiling=false` the recording code is absent from the ILC map (met).
- Acceptance: `EventBenchmarks` at or below the `Prototype_TypedChannels` numbers with zero allocation and the generated bus emitting `ION10x` diagnostics for the misuse cases in 4.4; `FullFrameBenchmarks.Step_8Systems` allocates 0 bytes; trace export of a 10-minute run bounded in memory; audio plays on all three desktops and on the R36S with an allocation-free audio thread.

### Stage 4: Silk.NET graphics stack and 2D renderer v2 (5-7 weeks)

- Spike (one week, go/no-go on details, not on Silk.NET): `Silk.NET.Windowing` + `Silk.NET.Vulkan` textured quad on Windows, macOS (MoltenVK) and Linux driven by `GameLoop` and published with NativeAOT; the same quad on `Silk.NET.OpenGLES` under the SDL platform on an R36S (or a Mali/Panfrost board) as a `linux-arm64` NativeAOT publish. Verify HiDPI framebuffer sizing, Wayland, gamepad input, and startup time on the handheld.
- RHI (`Graphics.Abstractions`): the ~15 WebGPU-shaped interfaces; `Graphics.Vulkan` and `Graphics.GLES` implementations; `Graphics.Headless` (offscreen target + PNG readback, no window); windowing/input module with explicit platform registration.
- Shader build task: GLSL 4.5 source, Shaderc to SPIR-V, SPIRV-Cross to GLSL ES 3.10, reflection-generated C# layouts; validated on every platform dialect in CI.
- `Rendering2D` on the RHI (4.7): instance ring, sort modes, packed color, camera, blend/sampler presets, render targets, text with cached layout and atlas, `MeasureString`, debug lines/shapes, screenshot.
- Port the three samples; keep Veldrid selectable until the snapshot tests match; then delete it.
- Acceptance: all samples run on all three desktops, on the R36S at a steady 60 fps, and headless in CI with golden-image tests on both backends; `SpriteBatchBenchmarks` per-sprite CPU cost halved (no scissor transform, packed color); a 100k-sprite stress sample holds 60 fps on an integrated desktop GPU and 10k sprites hold 60 fps on the R36S, one draw call per texture; zero AOT warnings from `Ion.*`.
- Status (first wave, see "As implemented (Stage 4, first wave)" in 4.7): spike done on Linux (GLFW and SDL under Xvfb, lavapipe; macOS/MoltenVK, Windows, Wayland, HiDPI and the R36S/GLES half not verified). RHI, Silk.NET windowing/input, Vulkan backend, headless backend with PNG readback, build-time shaders with SPIRV-Cross translation, and the quad sample are in; the headless textured quad is checked by pixel asserts and golden PNGs in CI (lavapipe), the windowed quad by E2E tests under Xvfb, all under Vulkan validation. Zero AOT warnings from `Ion.*` for the quad sample (met for this sample). Next wave: `Rendering2D` on the RHI, `Graphics.GLES`, sample migration, then deleting Veldrid once the samples' snapshot tests match.
- Status (second wave, GLES, see "As implemented (Stage 4, second wave)" in 4.7): `Graphics.GLES` is in, with headless GLES through EGL, backend selection (`Auto`, OpenGL ES first on linux-arm64), and the contract tests and goldens shared with Vulkan (both backends pixel-identical on Mesa, windowed and headless). The quad sample cross-publishes for linux-arm64 with NativeAOT and runs under QEMU on arm64 Mesa at the ES 3.1 level. The R36S half of the spike (the device itself, Panfrost, SDL KMSDRM, 60 fps, startup on the handheld) is still open and needs hardware or a Mali/Panfrost board on CI.
- Status (third wave, see "As implemented (Stage 4, third wave)" in 4.7): `Rendering2D` is in and the three samples run on it windowed (Vulkan and OpenGL ES under Xvfb, validation clean) and headless with golden images on both backends; Veldrid is deleted. `SpriteBatchBenchmarks` per-sprite CPU cost halved (met: 0.46x to 0.47x, 0 B). One draw call per texture for the stress sample (met: 16 draw calls for 100k sprites and 16 textures). Zero AOT warnings from `Ion.*` for the ECS sample (met). Not verified: 60 fps for 100k sprites on an integrated desktop GPU and 10k on the R36S (only CPU rasterizers here), the three desktops beyond Linux.

### Stage 5: Built-in ECS and the 3D SDK (6-8 weeks)

- `Ion.Extensions.Ecs` (4.9) on the library picked in 5.7: `World` per scope, `[Query]` generation to chunk-span loops, `Commands` flush per stage, transform hierarchy, `Name`, world serialization; NativeAOT publish verified with zero warnings on linux-x64 and linux-arm64 (Arch needs `Arch.AOT.SourceGenerator` for this; Friflo works as is).
- `Ion.Extensions.Ecs.Rendering`: 2D extraction (Sprite, Camera2D, SpriteAnimation, Tilemap) first, then 3D.
- `Rendering3D` (4.8): mesh/material/camera/light types, transform propagation, bounds, frustum culling, extract/prepare/queue/sort, render graph with shadow, opaque, skybox, transparent, post and overlay passes, Unlit and PBR materials in WGSL, glTF import, instancing.
- Samples: `Ion.Examples.Cubes` (immediate-mode 3D, no ECS), `Ion.Examples.Sponza` (glTF, PBR, lights, ECS), Breakout ECS moved onto the built-in components.
- Acceptance: 10k `MeshRenderer` entities with 3 materials render in under 2 ms CPU on the extraction+queue path (benchmark added); glTF sample matches reference screenshots on all backends; ECS query benchmark on the built-in `[Query]` path matches `ChunkSpans` numbers.

- Status (3D half, see "As implemented (Stage 5, 3D half)" in 4.8): `Rendering3D` is in with mesh/material/camera/light types, bounds, frustum culling, extract/prepare/queue/sort, a render graph with shadow, depth prepass, opaque, skybox, transparent and 2D overlay passes (no built-in post pass), unlit and PBR materials in GLSL (not WGSL; custom materials use the same bind group conventions), glTF import and instancing. `Ion.Examples.Cubes` (immediate mode, no ECS) and `Ion.Examples.Model` (glTF, PBR, lights, skybox; the small CC0 Avocado model instead of Sponza, which is too large to commit) render one golden per sample on Vulkan and OpenGL ES and run windowed under validation; both publish with NativeAOT with no `Ion.*` warnings. The extraction+queue path takes 1.20 ms CPU for 10k mesh renderers with 3 materials (met, benchmark added; measured with the immediate-mode API, the ECS extraction adds its query). The ECS extraction systems are in (ECS 3D wave, see 4.9) and both samples run on them. Open: Sponza, and the arm64/R36S frame times.
- Status (ECS half, see "As implemented (Stage 5, ECS half)" in 4.9 and 5.9): `World` per scope, `[Query]` generation, `Commands` per stage, 2D and 3D transform hierarchy, `EntityName`/`NameRegistry`, serialization, 2D extraction (sprites, camera, animation) and the Breakout ECS sample on the built-in components are in. ECS query benchmark on the built-in `[Query]` path matches `ChunkSpans` (met within noise: 0.99 to 1.06). NativeAOT publish of the ECS sample: no warnings from Ion, Arch or its dependencies on linux-x64 and linux-arm64 (met; what remains is nkast.Aether and Silk.NET.Core). 3D extraction (mesh renderers, cameras, lights, environment) and model spawning are in (second ECS wave): 10,000 mesh entities extract in 82 us, 1.28x the submissions from flat arrays, 0 B (met: the target was about the 60 us of `Extract_Submit10k` plus the query). Not done: tilemaps, parallel queries, serialization of the 3D components (they hold renderer handles), inherited visibility.

### Stage 5b: UI and physics modules (P1, 4-6 weeks, parallel with Stage 6)

- `Ion.Extensions.UI` (4.12): immediate-mode widgets, flex layout, theme, gamepad focus navigation, remote-inspectable tree (`ui.tree`, `ui.click`), on the 2D renderer and Input v2.
- `Ion.Extensions.Physics2D` (Box2D v3 vs Aether decided by a 10k-body benchmark on x64 and arm64) and `Ion.Extensions.Physics3D` (BepuPhysics v2): `PhysicsWorld` per scope, ECS adapter systems, collision events on the typed bus, debug draw, deterministic fixed step.
- Breakout ECS moved onto the physics module; a `Ion.Examples.Menu` sample exercises the UI on keyboard, mouse and gamepad.
- Acceptance: UI sample driven end-to-end by an agent through the remote protocol without screenshots; physics replay test produces identical state on x64 and arm64 after 10k fixed steps.
- Status (UI half, see "As implemented (Stage 5b, UI)" below and [../design/ion-ui.md](../design/ion-ui.md)): `Ion.Extensions.UI` and `Ion.Extensions.UI.Abstractions` are in, with the `Ion.Examples.Menu` sample on keyboard, mouse and gamepad. The sample is driven end to end through `IUiTree` only (paths, values, clicks; no screenshots) by its headless test, which is the surface the remote protocol exposes as `ui.tree`/`ui.click` (met for the tree; the protocol itself is the concurrent Stage 6 wave). One golden image of the options screen (lavapipe). Zero allocation per frame in steady state (test, met). NativeAOT publish of the menu sample: no `Ion.*` warnings (met; added to the CI AOT lane). Not verified: the R36S itself (the gamepad path is tested with scripted input).

**As implemented (Stage 5b, UI, September 2026).** Design, API, layout model, tree contract and theme in [../design/ion-ui.md](../design/ion-ui.md). Decisions and deviations:

- *Immediate API, retained tree.* Widgets are methods on a `Ui` context injected into systems and called from Update. The calls of a frame are recorded into a node array that is laid out once at the end of Update and kept for the next frame, where it is the hit-test tree (input is read against it at the start of Update), and the published `IUiTree`. A widget therefore reacts to input from the frame after it first appears. Containers return a disposable `UiScope` struct (or close with `Ui.End()`).
- *Schedule.* `UseUi()` adds a scope around Update at the new `StageOrder.UiFrame` (-550: after coroutines, before scenes, so scene and game steps both build UI) and the drawing step at the new `StageOrder.Ui` (700: inside the sprite batch scope, after the game's Render steps, before the metrics overlay at 800).
- *Identity and paths.* A widget's id is its explicit key, else its call site (`[CallerFilePath]`/`[CallerLineNumber]`) and parent, with repeats at one call site (loops) numbered in call order in constant time. Tree paths are key, else caption, else kind, joined with `/`, `#n` for repeated sibling segments; the path strings are cached per widget, so the tree is stable (same instances) and allocation-free across frames.
- *Layout.* A flex subset: direction, fixed/min/max sizes (min wins), grow with redistribution past maximums, padding, gap, the five justify modes, cross alignment with stretch, and wrapping (which needs a definite main size when measuring). No shrink; children overflow and scroll views clip. Two passes over the node array (reverse for measure, forward for placement), no recursion.
- *Rendering.* Through `ISpriteBatch` only: solid rectangles, nine-slices (`UiNineSlice`), text with the 2D renderer's FontStashSharp fonts (`IFont`, already cached per string, so nothing was added to `Ion.Extensions.Rendering2D`), and a nested sprite batch segment with `SpriteBatchOptions.Scissor` per scroll view; nodes outside their clip are not submitted.
- *Input.* Pointer from the left mouse button (touch arrives as the mouse), wheel scrolling, spatial arrow/D-pad/stick focus navigation, Tab order, activate (Enter, Space, A), back (Escape, B, reported as `Ui.BackPressed`), slider adjust with Left/Right, text editing (typed text, Backspace, Delete, Left, Right, Home, End), key repeat timed by the frame delta (deterministic under the fixed-step clock), and auto-focus of the first focusable widget for gamepad-only devices.
- *Tree commands.* `Click`, `SetValue`, `Focus`, `Type`, `Back`: validated against the published tree when called (false and nothing queued for an unknown path, a disabled node or a kind the command does not apply to), queued under a lock, applied at the start of the next frame's Update before input, and indistinguishable from the equivalent input for the game code.
- *Results.* `UiBenchmarks` (500 widgets: 125 cells of label, button and toggle or slider in a wrapping row, 1920x1080, `--job short` on the 2.1 GHz Xeon VM): build, layout and tree publication 53 us, plus the draw submission into the 2D renderer's `SpriteBatch` 89 us; 0 B per frame. The allocation test runs 200 frames of a screen with every widget kind, scrolling and focus movement and asserts 0 bytes.
- *Not done.* Text selection, clipboard, IME composition display, multi-line text, flex shrink, drag-to-scroll gestures, nested scroll views following the focus beyond the nearest one, the Dear ImGui debug overlay module.
- Acceptance status (Stage 6b web wave): the UI sample is driven end to end by an agent through the remote protocol without screenshots (met): `Ion.Extensions.UI.Remote` adds `ui.tree`, `ui.click`, `ui.set_value`, `ui.focus`, `ui.type` and `ui.back`, the menu sample registers it, and `Ion.Examples.Menu.Tests.MenuRemoteTests` runs the game with `--remote-allow-mutations` on its own thread and, with only the token file and JSON-RPC over HTTP, changes every option, plays and quits; the MCP server has `ion_ui_tree` and `ion_ui_click`. `Ion.Extensions.Physics2D.Remote` adds `physics2d.bodies` and `physics2d.raycast`. The physics replay acceptance is unchanged (above).
- Status (physics, see "As implemented (Stage 5b, physics)" in 4.12): `Ion.Extensions.Physics2D` (Box2D v3, picked by the 10k-body benchmark on x64 and arm64) and `Ion.Extensions.Physics3D` (BepuPhysics v2) are in with a world per scope, ECS adapter steps in FixedUpdate, collision and trigger events on `IEvents`, ray cast and overlap queries, debug drawing on both renderers and deterministic fixed stepping; Breakout ECS runs on the 2D module without Aether. Replay acceptance: identical state on repeated runs (met, 10k steps, 2D and 3D); identical on x64 and arm64 (met for 3D at equal vector width, and for 2D with Box2D built without FMA contraction, verified under QEMU; the packaged arm64 Box2D natives differ, so the 2D golden is linux-x64 until CI builds contraction-free natives). Not verified on arm64 hardware.

### Stage 6: Agentic toolchain (3-4 weeks)

- `Ion.Tools` (`ion` dotnet tool): `new`, `run --headless --frames --seed --screenshot --summary`, `schedule`, `bench`, `trace`.
- `Ion.Extensions.Remote`: JSON-RPC inspection protocol (4.10) plus MCP server; `input.send`, `screenshot`, `metrics`, `+watch`; loopback-only default, per-run bearer token, read versus mutate scopes, idempotent request ids, compiled out of release builds unless opted in.
- Snapshot testing helpers and templates with `CLAUDE.md`; documentation site generated from XML docs.
- Acceptance: an agent with only the `ion` CLI and the MCP server can create a game from the template, add a system, run 600 headless frames, take a screenshot, diff it against a golden image, inspect an entity and mutate a component, without reading engine source; a test proves that a client without the token, or with a read-only token, cannot call any mutate operation, and that the server refuses a non-loopback bind unless explicitly configured.
- Status (see "As implemented (Stage 6)" in 4.10, `docs/design/ion-remote.md` and `docs/agentic/`): `Ion.Tools` (`ion new`, `run`, `schedule`, `bench`, `trace`, plus `diff`, `remote` and `mcp`), `Ion.Extensions.Remote` with its abstractions and the ECS provider, the MCP server (`Ion.Tools.Mcp`), `Ion.Testing` additions (`IonTestHost.Run<TGame>`, `JsonSnapshot`, `WorldSnapshot`) and the three templates with `CLAUDE.md` (also as the `Ion.Templates` pack) are in. The acceptance scenario is `docs/agentic/acceptance.md` and runs as `TemplateTests.AcceptanceScenarioWithOnlyTheCliAndMcp` (met; slow, `ION_SLOW_TESTS=1`). The access-control acceptance is `Ion.Extensions.Remote.Tests.SecurityTests`: no token gets 401/-32001, the read token gets 403/-32002 on every mutate method, a non-loopback bind throws unless `AllowNonLoopback` (met). `ion run` against the Breakout sample headless, the exit code of a failing run, and the MCP server driving a live Breakout are in `Ion.Tools.Tests` (met). Not done: the documentation site generated from XML docs, hot-reload results on the protocol, NativeAOT publish of the `ion` tool itself (the tool is a framework-dependent dotnet tool; `Ion.Tools.Mcp` is marked AOT-compatible and builds without trim/AOT analyzer warnings).

### Stage 6b: Web server and multiplayer networking modules (P2, 6-8 weeks, after Stage 6)

- `Ion.Extensions.Web` (4.12): embedded HTTP/1.1 + WebSocket server on its own thread, lock-free queue into the game thread, generated routing for `[Http]` methods on systems, hosts the remote protocol; companion-app sample (a phone controller page).
- `Ion.Extensions.Networking` per `docs/design/ion-networking.md`: (1) abstractions, generator (`[Replicated]`, `[NetworkMessage]`, registry, delta serializers, ION2xx), snapshot ring, message bus, loopback transport, headless server mode; (2) LiteNetLib transport, handshake and security checks, metrics, Breakout over loopback and UDP; (3) interpolation, prediction and reconciliation, lag compensation, interest policy hook; (4) WebSocket transport when the browser target is picked up.
- Acceptance: two headless instances (server and client) run the Breakout sample over loopback with replicated balls and pass a state-convergence test; a browser page drives a running game through the web module.
- Status (web half, see "As implemented (Stage 6b, web)" below and [../design/ion-web.md](../design/ion-web.md)): `Ion.Extensions.Web` with its abstractions and routing generator, on the HTTP core now shared with the remote module (`Ion.Extensions.Http`), and the `Ion.Examples.Companion` phone-controller sample are in. "A browser page drives a running game through the web module": the companion page is served by the game and drives the paddle over a WebSocket endpoint; its headless test does the page's part (opens the WebSocket, sends stick positions, reads `/score`) with a .NET client, not a browser (met for the protocol; no browser automation in CI). Zero allocation on the request path in steady state (test, met). NativeAOT publish of the companion sample: no `Ion.*` warnings (met; added to the CI AOT lane).

**As implemented (Stage 6b, web, September 2026).** Design, configuration, routing rules, diagnostics and security in [../design/ion-web.md](../design/ion-web.md). Decisions and deviations:

- *One server core.* The remote module's socket code became `Ion.Extensions.Http` (blocking listener with a thread per connection, a request parser and response writer on reusable buffers, RFC 6455 framing with a writer thread per socket, the Host/Origin/token policy, token-bucket rate limits, a bounded lock-free multi-producer queue). The remote transport runs on it, and its request handling is an `IHttpEndpoint` that the web module mounts at `/rpc` when both modules run. The core gained chunked request bodies (a lone `chunked` coding on HTTP/1.1; both framings together stay 400) and pooled WebSocket frame buffers while the web tests were written.
- *Order.* `StageOrder.Web = 960` in Last: after the ECS command playback (950), before the remote step (970) and the event stepping (1000). Requests, WebSocket connections, messages and disconnections share one queue, handled in arrival order, at most `MaxRequestsPerFrame` per frame; a request waits for the end of the frame it arrives in.
- *Routing.* `[Http(method, route)]` (repeatable) and `[WebSocket(path)]` on instance methods of registered singleton systems; `Ion.Extensions.Web.Generators` emits a per-assembly `WebRouteTable` with typed invokers and registers it from a module initializer (`WebRoutes`), so the server needs no reflection and games need no registration call. Route values and query parameters bind through `IUtf8SpanParsable<T>` without allocating; bodies are text, bytes, or JSON through the class's or assembly's `[WebJson]` context (`GetTypeInfo(typeof(T))`, AOT-safe). Diagnostics `ION401` to `ION407` (the plan said ION4xx; `ION2xx` belongs to networking).
- *Security* is the remote policy plus browsers: off by default, loopback by default with a logged `AllowNonLoopback` opt-in (a LAN bind generates a per-run token for mutating endpoints unless `AllowAnonymousMutations`), the loopback `Host` check, an origin allow-list where the server's own origin is always allowed (its static pages) and listed origins get CORS, the bearer token for `WebAccess.Mutate` endpoints (non-GET methods and WebSocket endpoints by default; browsers offer it as a `bearer.<token>` subprotocol), per-address rate limits, bounded heads, bodies, messages, connections and queues. The token separates reading from changing the game on a trusted network; there is no TLS (use a reverse proxy).
- *Companion sample* (`Ion.Examples.Companion`, not a mode of Breakout ECS so its tests and goldens stay untouched): phones become virtual gamepads 1 to 3 through `ScriptedInput`, so the game reads ordinary gamepad input; static page without a build step; score pushed over the same socket.
- *Remote integrations of Stage 5b modules* are separate projects (`Ion.Extensions.UI.Remote`, `Ion.Extensions.Physics2D.Remote`) with `AddUiRemote()`/`AddPhysics2DRemote()` guarded by `Ion.Remote.IsSupported`, rather than code inside the modules, so the UI and physics modules keep no dependency on the protocol. `physics2d.*` covers the root physics world only.
- *Results* (`WebBenchmarks`, loopback, web step run continuously, `--job short` on the 4-core VM): keep-alive `GET` round trip about 34 us and 0 B; a broadcast to 1 and 4 WebSocket clients about 2.2 us and 8.3 us.
- *Not done:* TLS, HTTP/2, streamed request and response bodies (and server-sent events, so remote `+watch` still needs WebSocket or stdio), compression, route constraints, scene-scoped endpoint systems, `physics2d.*` on scene worlds, browser automation in CI for the companion page.
- Status (networking, delivery steps 1 to 3, see "As implemented" in [../design/ion-networking.md](../design/ion-networking.md) section 14 and [benchmarks/2026-09-26-stage6b-networking](benchmarks/2026-09-26-stage6b-networking/README.md)): `Ion.Extensions.Networking` with its abstractions, its own generator (`[Replicated]`, `[NetworkMessage]`, delta serializers, registry hash, ION201 to ION210), the snapshot ring captured by a FixedUpdate End scope at the new `StageOrder.Network` (-870; sends at `StageOrder.NetworkSend`, 870), the message bus over `IEvents`, `LoopbackTransport`, headless server mode, the LiteNetLib transport (`Ion.Extensions.Networking.LiteNetLib`, NativeAOT-clean), the handshake with protocol and registry hashes and a join secret, authority checks, rate limits, metrics, prediction with reconciliation, interpolation, lag compensation and the interest hook are in. Acceptance: a headless server and a headless client of `Ion.Examples.Breakout.Net` converge over loopback (also with latency, jitter, loss and reordering) and over UDP on 127.0.0.1, checked every frame for 900 frames (met). A steady-state frame over the loopback allocates 0 B (met). Measured: capture of 10,000 entities 156 us, a 10,000-entity snapshot round trip 0.30 ms (1 % moving), 100 messages 4.9 us, 0 B. Not done: the WebSocket transport (step 4, with the browser target), whole-schedule rollback and resimulation, clock-rate nudging.

### Stage 7: Native and multi-platform builds (4-6 weeks, can start after Stage 4)

- NativeAOT publishing profiles for win-x64/arm64, osx-arm64/x64, linux-x64/arm64 (R36S) in the templates and CI; size and startup tracked in the benchmark artifact; an `ion publish --target r36s` preset that produces the ArkOS launcher layout.
- Mobile heads: `net10.0-android` and `net10.0-ios` projects on the SDL windowing platform and the Vulkan backend (GLES fallback), NativeAOT, touch input mapped into Input v2.
- Browser (optional): `net10.0-browser` target for `Core`, `Rendering2D/3D`, `Ecs`; `Graphics.WebGPU` over `Silk.NET.WebGPU` or Emscripten `webgpu.h`; canvas windowing/input shim; `requestAnimationFrame` driven `Step`. Picked up only if a web build is wanted.
- Acceptance: Breakout ECS installs and runs on an R36S, an Android phone and an iPad from CI-produced artifacts; desktop AOT binaries under 30 MB start in under 300 ms; the handheld build starts in under one second.
- Status (see "As implemented (Stage 7)" in 4.11 and `docs/platforms/publishing.md`): publishing presets for all seven targets with `ion publish --target`, the r36s ArkOS layout, the size and startup measurement in the CI AOT lane, the Android and iOS heads with touch input, and view-only SDL windows are in. Desktop acceptance met on linux-x64 (Breakout ECS 12.6 MB, 46 ms to the end of the first headless frame); the handheld build starts in 0.6 s under QEMU emulation, not yet measured on the device; installing and running on an R36S, an Android phone and an iPad from CI artifacts is not done (no hardware, no Android SDK or macOS here).

---

## 7. Decisions taken and what is still open

Answered by the owner (recorded at the top of this document): Silk.NET as the rendering platform; desktop, tablet, mobile and the R36S as primary targets with web optional; .NET 10 now; generated dispatch is a goal in itself; the event bus gets fixed and source-generated; ECS chosen by measurement; audio P0, UI and physics P1, web server and networking P2.

Still open, with the assumption the plan currently makes:

1. **Breaking changes.** Stage 2 changes the recommended system signature (`Next` instead of `GameLoopDelegate next`) and Stage 3 replaces `IEventListener`. Assumed: a 0.3 release with a migration guide; 0.2 code keeps compiling through the reflection and adapter fallbacks for one release.
2. **R36S input and display details.** Assumed: SDL platform, fullscreen 640x480, gamepad only (no pointer). If a specific ArkOS launcher integration (port scripts, resolution switching) is required, it goes into Stage 7's `ion publish --target r36s`.
3. **2D physics library.** Decided (Stage 5b benchmark, [benchmarks/2026-09-physics2d](benchmarks/2026-09-physics2d/README.md)): **Box2D v3** through `Box2D.NET.Bindings.Release` 3.1.0, 5x faster than the managed Box2D v3 port and 23x faster than Aether at 10,000 bodies on x64 and arm64, allocation-free and AOT-clean. Cross-architecture bit-identity needs natives built with `-ffp-contract=off` (the packaged arm64 ones use FMA); until CI builds them the 2D replay golden is linux-x64. A browser target would use the managed port behind the same module.
4. **UI style.** Assumed immediate-mode engine-native UI (4.12). If a retained, markup-driven UI is preferred for designers, that is a different module and a larger effort.
5. **Remote protocol transport.** Assumed both HTTP and stdio behind one handler; the web server module hosts the HTTP side.
6. **Licensing of shipped assets** for templates and golden images (fonts, textures) must be CC0 or owner-provided.

## 8. Appendix: sources consulted

- Silk.NET releases and 3.0 roadmap: https://github.com/dotnet/Silk.NET/releases, https://github.com/dotnet/Silk.NET/milestone/9, https://github.com/dotnet/Silk.NET/issues/960, https://github.com/dotnet/Silk.NET/discussions/1160
- WebGPU: https://github.com/webgpu-native/webgpu-headers, https://github.com/gfx-rs/wgpu-native/releases, https://github.com/amerkoleci/Alimer.Bindings.WebGPU, https://github.com/PhilippeMonteil/WebGPUSharp
- Browser .NET: https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM, https://github.com/EvergineTeam/WebGPU.NET, https://github.com/Refsa/pollus
- ECS: https://github.com/genaray/Arch, https://github.com/genaray/Arch.Extended, https://github.com/friflo/Friflo.Engine.ECS, https://github.com/Doraku/Ecs.CSharp.Benchmark
- Rendering architecture: https://bevy.org/news/bevy-0-19/, https://github.com/stride3d/stride-docs (rendering pipeline), https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4, https://docs.godotengine.org/en/stable/classes/class_renderingserver.html
- Agentic tooling: https://github.com/bevyengine/bevy/blob/main/crates/bevy_remote/src/lib.rs, https://github.com/natepiano/bevy_brp, https://unity.com/blog/unity-ai-mcp-how-to-get-started, https://github.com/IvanMurzak/Unreal-MCP, https://github.com/Coding-Solo/godot-mcp
- Codegen and AOT: https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md, https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/aot/request-delegate-generator/rdg.md, https://github.com/dotnet/runtime/blob/main/src/coreclr/tools/aot/ILCompiler.Compiler/Compiler/StackTraceEmissionPolicy.cs, https://github.com/pakrym/jab, https://github.com/devteam/Pure.DI, https://github.com/dotnet/docs/blob/main/docs/core/extensions/configuration-generator.md
- Metrics: https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/test/Benchmarks/Metrics/MetricsBenchmarks.cs, https://github.com/clibequilibrium/Tracy-CSharp, https://github.com/bevyengine/bevy/blob/main/docs/profiling.md
