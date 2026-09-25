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
| `Ion.Examples.Breakout.ECS` under Xvfb + Mesa lavapipe (Vulkan) | Runs (window, device, shaders, sprite buffer, resize). Reaching Vulkan required requesting `Direct3D12` in config because of the enum bug, and a `libdl.so` symlink because Veldrid's `vk` package loads `libdl` rather than `libdl.so.2`. |
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

### 4.5 Input v2 (Stage 3)

Fixed-size state: `ulong[]` bitsets for `Down`, `Pressed`, `Released` indexed by `Key` (max ~256), a 32-bit mask for mouse buttons, `Vector2` position and delta, wheel, text-input `ReadOnlySpan<char>` for the frame, and gamepad state (Silk.NET.Input exposes it). Press and release in the same frame set both bits. Focus loss clears `Down`. Input is captured from the windowing layer in `First` and is immutable for the rest of the frame. A `RecordedInput` implementation replays a stream for deterministic tests, and a `ScriptedInput` lets agents inject keys and clicks over the remote protocol.

### 4.6 Metrics v2 (Stage 3)

Three layers, all allocation-free on the hot path:

1. **Spans.** The generated schedule brackets every system call with `Stopwatch.GetTimestamp()` writes into a preallocated per-frame ring (`FrameProfile[]`, N frames deep). Enabled by a `static readonly bool` behind a feature switch so ILC removes it entirely from release builds that opt out, and toggleable at runtime in builds that keep it. No strings on the hot path: system and stage names are interned ids resolved at export time.
2. **Counters.** Engine counters (`draw_calls`, `sprites`, `triangles`, `entities`, `events_emitted`, `gc_gen0/1/2`, `allocated_bytes`, `frame_ms`, `fixed_steps`) live in a `FrameStats` struct written once per frame; games add their own with `Metrics.Counter("balls")`.
3. **Export.** Chrome trace JSON (Perfetto) and Tracy zones from the ring, `System.Diagnostics.Metrics` `Meter` for frame-level aggregates so `dotnet-counters` works, a JSONL "frame log" line per frame for agents, and an optional on-screen overlay drawn by the 2D renderer.

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

### 4.11 Native compilation and platforms (Stages 2, 4, 7)

- **.NET 10 everywhere.** Engine, generators' consumers, samples and templates target `net10.0` from Stage 0; generators stay `netstandard2.0` on Roslyn 4.4 so any SDK 8.0+ can still compile a game that references the packages. .NET 10 brings the stable interceptors, escape analysis for delegates, the newer NativeAOT (Android support), and `net10.0-browser`, `net10.0-android`, `net10.0-ios` TFMs for the platform heads.
- **NativeAOT on desktop** (Windows, macOS, Linux; x64 and arm64) is the primary "native compilation" story and is already almost there: the engine's own code produced two AOT warnings. After Stage 2 (no reflection binder, generated config binding) and Stage 4 (no Veldrid/DependencyModel/Newtonsoft, no NAudio COM) the engine and templates publish with `PublishAot=true` and zero warnings. `IlcGenerateStackTraceData` stays on so traces remain readable; `EventSourceSupport` on for `dotnet-trace`.
- **R36S and other Linux arm64 handhelds** are a NativeAOT `linux-arm64` publish with the GLES backend, SDL windowing, and a fullscreen 640x480 default. A cheap board on CI (or QEMU user-mode for smoke tests) keeps that lane honest; the benchmark suite runs there too so budgets are set against the slowest primary target rather than a desktop.
- **Mobile.** iOS NativeAOT (since .NET 9) and Android NativeAOT (.NET 10) through the SDL windowing platform and the Vulkan backend (GLES fallback on older Android). Not scheduled before Stage 7, but nothing in the plan blocks it: no async, no reflection, no JIT dependency.
- **Browser** stays optional: `net10.0-browser` with the `wasm-tools` workload (Mono interpreter/AOT, single-threaded), a `Graphics.WebGPU` RHI backend, a canvas windowing/input shim, and `requestAnimationFrame` driving `GameLoop.Step`. The "no async" design and the WebGPU-shaped RHI keep this feasible whenever it is picked up.

### 4.12 Modules beyond rendering

Priorities from the owner: audio is P0 (it must work on every primary target before anything else ships), UI and physics are P1, a web server for companion apps/integrations and multiplayer networking are P2. All of them follow the same rules as the rest of the engine: no async in the frame, systems scheduled through the generator, state inspectable over the remote protocol, and a headless mode for tests.

**Audio (P0, `Ion.Extensions.Audio`, rewritten in Stage 3).** Today's module is NAudio over DirectSound, which exists only on Windows and cannot ship on the R36S, mobile or macOS. The replacement is a small engine-owned mixer (float32 interleaved, resampling on load, master/bus/voice gains, pitch, pan, looping, fade) feeding a platform output through `Silk.NET.OpenAL` (OpenAL Soft ships for every primary target including linux-arm64, iOS and Android, and Silk.NET already packages it). The mixer runs on the audio thread and is fed from a lock-free command queue written on the game thread in the `Last` stage, so playing a sound never blocks a frame. Decoding (WAV, OGG Vorbis via a managed decoder, MP3 optional) happens at load time through the asset pipeline; streaming music is a polled job. A `NullAudioOutput` keeps headless runs silent and deterministic. Acceptance: the Breakout samples play on all three desktops and on the R36S; the audio thread never allocates after warm-up; `pitchShift`, `MasterVolume` and per-voice volume verified by tests against the mixed buffer.

**UI (P1, `Ion.Extensions.UI`, Stage 5b).** An immediate-mode UI (the kind of API Dear ImGui popularised, but engine-native and retained where it matters for hit testing) is the best fit for an agentic, code-first engine: no separate markup, layout is C# and therefore generated-schedule friendly, and a screen can be described and diffed as data. It sits on the 2D renderer (sprites, nine-slices, text, clip rects), takes input from Input v2 (pointer, keyboard, gamepad focus navigation for the R36S), and exposes a tree that the remote protocol can query (`ui.tree`, `ui.click(path)`), which is what lets an agent drive menus without pixel-hunting. Flex-style layout with a fixed set of widgets (panel, label, button, toggle, slider, text input, list, scroll view) and a theme struct. Dear ImGui through `Silk.NET`-bound `ImGui.NET` remains available as a separate debug-overlay module, not the game UI.

**Physics (P1, `Ion.Extensions.Physics2D` and `Physics3D`, Stage 5b).** 2D on Box2D v3 (through its C bindings, deterministic, SIMD, arm64-friendly) or Aether.Physics2D (pure managed, already used by the sample, but its XML serializer is the largest source of AOT warnings today); 3D on BepuPhysics v2 (pure managed, SIMD, no native dependency, well suited to NativeAOT) with Jolt as the alternative if a native library is acceptable. Both modules provide: a `PhysicsWorld` per scene scope, `RigidBody`/`Collider`/`Joint` ECS components with adapter systems (`FixedUpdate(Order = -100)` push transforms, step, pull transforms), collision and trigger events on the typed event bus, a fixed step decoupled from render rate, a debug-draw system on the 2D/3D renderers, and deterministic stepping for replay tests. Decision between the 2D candidates is a benchmark in Stage 5b (10k dynamic bodies, arm64 included).

**Web server (P2, `Ion.Extensions.Web`, Stage 6b).** A minimal HTTP/1.1 and WebSocket server embedded in the game process, running on its own thread with a lock-free queue into the game thread, so companion apps (second-screen controllers, level editors, dashboards) and integrations (Twitch, Discord, webhooks) can talk to a running game. It reuses the remote protocol from Stage 6 (JSON-RPC over HTTP and WebSocket) and adds routing for game-defined endpoints declared as ordinary methods on systems (`[Http("GET", "/score")]`), which the generator turns into a table (no reflection, AOT-clean). Kestrel is deliberately not used: it is async-first, large under AOT, and not needed for a handful of endpoints.

**Multiplayer networking (P2, `Ion.Extensions.Networking`, Stage 6b).** Transport first (UDP with reliability/ordering channels, plus WebSocket for browser and relay scenarios; LiteNetLib-style, managed, AOT-clean), then a replication layer built on the ECS: components marked `[Replicated]` are snapshotted by a generated serializer (the same generator, no reflection), delta-compressed against the last acknowledged state, and applied on clients with interpolation; inputs are sent client-to-server and the fixed-step simulation is deterministic enough (Stage 1) for client-side prediction and rollback to be an option. A `LoopbackTransport` runs client and server in one process for tests, and the headless CLI can run a dedicated server.

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

This is the most important number in the suite. Each `Emit` boxes (36 B per event) and, worse, each `On<T>(out)` call rescans both frame buffers from the start and probes two `HashSet`s per entry, so draining N events of a type is O(N^2) and multiplies by the number of listeners and event types. Eight systems each reading four event types with 100 events in flight costs 1.3 ms, 8 percent of a 60 fps frame, for the event bus alone. The unboxed per-type channel with a cursor per reader (`TypedChannel<T>` in the benchmark project) does the same work in 0.6 us with no allocation, a 2,000x difference. Events v2 in section 4.4 is this design.

### 5.4 Trace timers (`TraceBenchmarks`)

| Timer | `Start` + `Stop` | Allocated |
|---|---|---|
| Core default `NullTraceTimer<T>` | 7.4 ns | 24 B |
| Debug package `TraceTimer<T>` (Release: disabled path) | ~0 ns (devirtualized and inlined by PGO) | 0 B |

Under NativeAOT the Debug path remains two interface calls per bracket; Metrics v2 replaces both with a generated timestamp write behind a static feature switch.

### 5.5 Sprite batching CPU (`SpriteBatchBenchmarks`, 10,000 sprites)

| Textures | Without scissor transform | With the renderer's scissor transform |
|---|---|---|
| 1 | 269 us (27 ns/sprite) | 281 us |
| 16 | 267 us | 295 us |

Zero allocations; grouping by texture is free at this scale. 27 ns per sprite for the CPU write is acceptable but the 80-byte instance (16 B color, 16 B unused scissor) is what limits the GPU upload; the scissor transform adds 4 to 10 percent on the CPU and a per-pixel `discard` on the GPU. Renderer v2 targets 10 ns and 40 bytes per sprite.

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
- Leaf steps have no `next`; wrapping is expressed with `[Stage.Begin]`/`[Stage.End]` scope pairs; engine steps move to the reserved order bands. The old `(GameTime, GameLoopDelegate next)` form keeps working through generated closures for one release with an `ION010` rewrite hint.
- `Order` on stage attributes; `[After<T>]`/`[Before<T>]` constraints; `--print-schedule`.
- Options binding through the configuration-binding generator; `[LoggerMessage]` logging; `[OptionsValidator]`.
- Delete `Ion.Core.InternalGenerators` and `Scenes.Generators` (their overloads are generated by the new generator on demand).
- Acceptance: `PipelineBenchmarks.Ion_*` within 1.2x of `DirectCalls_FlatLoop` for 32 systems and zero bytes allocated per frame; the section 2 stack trace contains only user frames plus `Program.Main`; `ion schedule` prints the nesting for every sample; a scope whose step throws still runs its `End` (test); NativeAOT publish of all samples with zero warnings from `Ion.*`; hot reload rebuilds the generated schedule.

### Stage 3: Events, input and metrics (2 weeks, parallel with Stage 4)

- Events v2 (4.4): typed channels, readers, zero allocation on emit and read; `IEventListener`/`IEventEmitter` kept as thin adapters for one release, then removed.
- Input v2 (4.5): bitsets, edge semantics fixed, text input, mouse delta, gamepads, `RecordedInput`.
- Metrics v2 (4.6): frame ring, counters, Chrome trace and Tracy export, `Meter` aggregates, JSONL frame log, overlay hook. Release builds keep tracing available behind a runtime toggle.
- Coroutines: singleton runner driven by the engine in `Update`, unboxed waits (`Wait` becomes a struct union), `Stop` safe, proper namespace.
- Assets: cache by (loader, path) with reference counting, scoped release on scene unload, hot reload on file change, polled background decode for images.
- Audio (P0, section 4.12): engine mixer on the audio thread fed by a lock-free command queue, `Silk.NET.OpenAL` output, WAV/OGG decoding at load, `NullAudioOutput` for headless; fixes pitch, master volume and resampling.
- Acceptance: `EventBenchmarks` at or below the `Prototype_TypedChannels` numbers with zero allocation and the generated bus emitting `ION10x` diagnostics for the misuse cases in 4.4; `FullFrameBenchmarks.Step_8Systems` allocates 0 bytes; trace export of a 10-minute run bounded in memory; audio plays on all three desktops and on the R36S with an allocation-free audio thread.

### Stage 4: Silk.NET graphics stack and 2D renderer v2 (5-7 weeks)

- Spike (one week, go/no-go on details, not on Silk.NET): `Silk.NET.Windowing` + `Silk.NET.Vulkan` textured quad on Windows, macOS (MoltenVK) and Linux driven by `GameLoop` and published with NativeAOT; the same quad on `Silk.NET.OpenGLES` under the SDL platform on an R36S (or a Mali/Panfrost board) as a `linux-arm64` NativeAOT publish. Verify HiDPI framebuffer sizing, Wayland, gamepad input, and startup time on the handheld.
- RHI (`Graphics.Abstractions`): the ~15 WebGPU-shaped interfaces; `Graphics.Vulkan` and `Graphics.GLES` implementations; `Graphics.Headless` (offscreen target + PNG readback, no window); windowing/input module with explicit platform registration.
- Shader build task: GLSL 4.5 source, Shaderc to SPIR-V, SPIRV-Cross to GLSL ES 3.10, reflection-generated C# layouts; validated on every platform dialect in CI.
- `Rendering2D` on the RHI (4.7): instance ring, sort modes, packed color, camera, blend/sampler presets, render targets, text with cached layout and atlas, `MeasureString`, debug lines/shapes, screenshot.
- Port the three samples; keep Veldrid selectable until the snapshot tests match; then delete it.
- Acceptance: all samples run on all three desktops, on the R36S at a steady 60 fps, and headless in CI with golden-image tests on both backends; `SpriteBatchBenchmarks` per-sprite CPU cost halved (no scissor transform, packed color); a 100k-sprite stress sample holds 60 fps on an integrated desktop GPU and 10k sprites hold 60 fps on the R36S, one draw call per texture; zero AOT warnings from `Ion.*`.

### Stage 5: Built-in ECS and the 3D SDK (6-8 weeks)

- `Ion.Extensions.Ecs` (4.9) on the library picked in 5.7: `World` per scope, `[Query]` generation to chunk-span loops, `Commands` flush per stage, transform hierarchy, `Name`, world serialization; NativeAOT publish verified with zero warnings on linux-x64 and linux-arm64 (Arch needs `Arch.AOT.SourceGenerator` for this; Friflo works as is).
- `Ion.Extensions.Ecs.Rendering`: 2D extraction (Sprite, Camera2D, SpriteAnimation, Tilemap) first, then 3D.
- `Rendering3D` (4.8): mesh/material/camera/light types, transform propagation, bounds, frustum culling, extract/prepare/queue/sort, render graph with shadow, opaque, skybox, transparent, post and overlay passes, Unlit and PBR materials in WGSL, glTF import, instancing.
- Samples: `Ion.Examples.Cubes` (immediate-mode 3D, no ECS), `Ion.Examples.Sponza` (glTF, PBR, lights, ECS), Breakout ECS moved onto the built-in components.
- Acceptance: 10k `MeshRenderer` entities with 3 materials render in under 2 ms CPU on the extraction+queue path (benchmark added); glTF sample matches reference screenshots on all backends; ECS query benchmark on the built-in `[Query]` path matches `ChunkSpans` numbers.

### Stage 5b: UI and physics modules (P1, 4-6 weeks, parallel with Stage 6)

- `Ion.Extensions.UI` (4.12): immediate-mode widgets, flex layout, theme, gamepad focus navigation, remote-inspectable tree (`ui.tree`, `ui.click`), on the 2D renderer and Input v2.
- `Ion.Extensions.Physics2D` (Box2D v3 vs Aether decided by a 10k-body benchmark on x64 and arm64) and `Ion.Extensions.Physics3D` (BepuPhysics v2): `PhysicsWorld` per scope, ECS adapter systems, collision events on the typed bus, debug draw, deterministic fixed step.
- Breakout ECS moved onto the physics module; a `Ion.Examples.Menu` sample exercises the UI on keyboard, mouse and gamepad.
- Acceptance: UI sample driven end-to-end by an agent through the remote protocol without screenshots; physics replay test produces identical state on x64 and arm64 after 10k fixed steps.

### Stage 6: Agentic toolchain (3-4 weeks)

- `Ion.Tools` (`ion` dotnet tool): `new`, `run --headless --frames --seed --screenshot --summary`, `schedule`, `bench`, `trace`.
- `Ion.Extensions.Remote`: JSON-RPC inspection protocol (4.10) plus MCP server; `input.send`, `screenshot`, `metrics`, `+watch`; loopback-only default, per-run bearer token, read versus mutate scopes, idempotent request ids, compiled out of release builds unless opted in.
- Snapshot testing helpers and templates with `CLAUDE.md`; documentation site generated from XML docs.
- Acceptance: an agent with only the `ion` CLI and the MCP server can create a game from the template, add a system, run 600 headless frames, take a screenshot, diff it against a golden image, inspect an entity and mutate a component, without reading engine source; a test proves that a client without the token, or with a read-only token, cannot call any mutate operation, and that the server refuses a non-loopback bind unless explicitly configured.

### Stage 6b: Web server and multiplayer networking modules (P2, 6-8 weeks, after Stage 6)

- `Ion.Extensions.Web` (4.12): embedded HTTP/1.1 + WebSocket server on its own thread, lock-free queue into the game thread, generated routing for `[Http]` methods on systems, hosts the remote protocol; companion-app sample (a phone controller page).
- `Ion.Extensions.Networking` (4.12): UDP transport with reliability channels plus WebSocket transport, generated `[Replicated]` component serializers with delta compression, snapshot interpolation, input send/prediction hooks, `LoopbackTransport`, dedicated-server mode in the headless CLI.
- Acceptance: two headless instances (server and client) run the Breakout sample over loopback with replicated balls and pass a state-convergence test; a browser page drives a running game through the web module.

### Stage 7: Native and multi-platform builds (4-6 weeks, can start after Stage 4)

- NativeAOT publishing profiles for win-x64/arm64, osx-arm64/x64, linux-x64/arm64 (R36S) in the templates and CI; size and startup tracked in the benchmark artifact; an `ion publish --target r36s` preset that produces the ArkOS launcher layout.
- Mobile heads: `net10.0-android` and `net10.0-ios` projects on the SDL windowing platform and the Vulkan backend (GLES fallback), NativeAOT, touch input mapped into Input v2.
- Browser (optional): `net10.0-browser` target for `Core`, `Rendering2D/3D`, `Ecs`; `Graphics.WebGPU` over `Silk.NET.WebGPU` or Emscripten `webgpu.h`; canvas windowing/input shim; `requestAnimationFrame` driven `Step`. Picked up only if a web build is wanted.
- Acceptance: Breakout ECS installs and runs on an R36S, an Android phone and an iPad from CI-produced artifacts; desktop AOT binaries under 30 MB start in under 300 ms; the handheld build starts in under one second.

---

## 7. Decisions taken and what is still open

Answered by the owner (recorded at the top of this document): Silk.NET as the rendering platform; desktop, tablet, mobile and the R36S as primary targets with web optional; .NET 10 now; generated dispatch is a goal in itself; the event bus gets fixed and source-generated; ECS chosen by measurement; audio P0, UI and physics P1, web server and networking P2.

Still open, with the assumption the plan currently makes:

1. **Breaking changes.** Stage 2 changes the recommended system signature (`Next` instead of `GameLoopDelegate next`) and Stage 3 replaces `IEventListener`. Assumed: a 0.3 release with a migration guide; 0.2 code keeps compiling through the reflection and adapter fallbacks for one release.
2. **R36S input and display details.** Assumed: SDL platform, fullscreen 640x480, gamepad only (no pointer). If a specific ArkOS launcher integration (port scripts, resolution switching) is required, it goes into Stage 7's `ion publish --target r36s`.
3. **2D physics library.** Box2D v3 (native, fastest, deterministic) versus Aether (managed, already used). Decided by the Stage 5b benchmark on arm64; assumed Box2D v3 unless the native dependency is unwanted on some target.
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
