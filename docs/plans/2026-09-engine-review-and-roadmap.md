# Ion Engine: Review and Multi-Stage Roadmap (September 2026)

This document is the output of a full review of the repository at commit `67d2dd4` (v0.2.5): the code, the samples, the build on a fresh Linux machine, a NativeAOT publish, and a new benchmark suite (`Ion/Ion.Benchmarks`). It ends with a staged plan for turning Ion into a code-first, high-performance engine that is also the best engine for agentic development.

The goals it is written against, as stated by the owner:

1. Code-first, high-performance game engine. Keep the middleware pattern and the "no async" design.
2. Best engine for agentic development (AI coding agents can build, run, verify and debug a game without a human in the loop).
3. Maximum performance via source generation and native compilation, with stack traces that show user code, not engine wrappers.
4. Replace the stale Veldrid backend with Silk.NET, keeping Ion's own game loop. Multiple platform builds including WebGPU.
5. Ready-to-use 3D rendering, and a story for how 2D/3D rendering integrates with an ECS (Arch).

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

**Where to go.** The plan below keeps the API shape that users write against and replaces the machinery under it in seven stages: a fixed, tested, headless-capable baseline (Stage 0-1); a source-generated, reflection-free schedule with clean stack traces (Stage 2); typed events, bitset input and a real metrics story (Stage 3); a WebGPU-first graphics layer on Silk.NET with a rewritten 2D renderer (Stage 4); built-in Arch ECS integration and the 3D SDK (Stage 5); the agentic toolchain (headless CLI, screenshots, remote inspection, MCP) (Stage 6); and native/multi-platform publishing including browser (Stage 7). Each stage has acceptance criteria and benchmark targets so an agent can drive it.

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

1. **The user-facing shape stays.** `IonApplication.CreateBuilder(args)`, `builder.Services.Add*`, `app.UseSystem<T>()`, plain classes with stage attributes, `next(dt)` for systems that need to wrap the rest of the stage, scenes as DI scopes, pull-based coroutines, frame events. Existing games should migrate with mechanical edits.
2. **Everything under it becomes compile-time.** Reflection is replaced by a Roslyn source generator plus C# interceptors (stable since the .NET 9.0.200 SDK). The reflection path stays as the fallback when the generator is not present (the same policy ASP.NET Core uses for its Request Delegate Generator).
3. **No async anywhere in the frame.** Long operations (asset decode, shader compile, network) are expressed as polled jobs on engine-owned worker threads with results consumed on the main thread in a stage. `Task` never appears in the public API.
4. **Determinism is a first-class feature.** The clock, the RNG seed, and input are injectable so a game can be run headless for N fixed steps and produce the same state and the same pixels every time. This is what makes the engine testable by agents.
5. **One thin graphics abstraction shaped like WebGPU**, with the WebGPU backend as the reference implementation. WebGPU already is the common subset of Vulkan/Metal/D3D12 and it is the only API that reaches the browser.
6. **ECS is a first-class extension, not the core.** Non-ECS games remain fully supported; the ECS module adds a `World` per scene scope, generated queries, a command buffer, and renderer extraction systems.
7. **Measure everything.** Every stage below has a benchmark or a test that gates it, and the engine ships a metrics/trace surface that agents and humans can read.

### 4.2 Package layout after the plan

```
Ion.Core.Abstractions      stage attributes, GameTime, IClock, events, input, storage interfaces (no deps beyond M.E.*)
Ion.Core                   builder, schedule runner, typed events, clock, storage, metrics ring buffer
Ion.Generators             schedule + query + config generators (netstandard2.0, Roslyn 4.4), shipped as analyzer
Ion.Extensions.Graphics.Abstractions   IGraphicsDevice/RHI (WebGPU-shaped), ISpriteBatch v2, camera/material/mesh types, IWindow, input enums
Ion.Extensions.Graphics.WebGPU         Silk.NET.WebGPU (wgpu-native) backend; swappable bindings layer
Ion.Extensions.Windowing.SilkNet       Silk.NET windowing + input driven by Ion's loop (GLFW platform registered explicitly for AOT)
Ion.Extensions.Graphics.Headless       offscreen render target + PNG readback; no window (agents, CI)
Ion.Extensions.Rendering2D             sprite batch v2, text, atlases, 2D camera, tilemaps (backend-agnostic, uses RHI)
Ion.Extensions.Rendering3D             meshes, materials (Unlit/PBR), lights, cameras, render graph, glTF importer
Ion.Extensions.Ecs                     Arch integration: World per scope, [Query] codegen, CommandBuffer stage, Transform hierarchy
Ion.Extensions.Ecs.Rendering           extraction systems: Sprite/MeshRenderer/Camera/Light components -> renderer submissions
Ion.Extensions.Scenes, Coroutines, Assets, Audio (rewritten on a cross-platform mixer), Debug (trace/metrics exporters)
Ion.Extensions.Remote                  JSON-RPC inspection protocol over HTTP/stdio + MCP server (Stage 6)
Ion.Tools (dotnet tool `ion`)          new/run/screenshot/bench/trace commands
Ion                                    meta-package with AddIon()/UseIon() defaults
```

`Ion_old`, `Ion.Extensions.Graphics.WGPU` (Alimer experiment), `Ion.Generators` (old) and `Ion.Extensions.Graphics.Veldrid` are removed once Stage 4 lands (Veldrid stays until the WebGPU backend passes the same sample and snapshot tests).

### 4.3 System registration and the generated schedule (Stage 2)

**User code does not change:**

```csharp
public sealed class PaddleSystem(IWindow window, World world, IInputState input, IEvents events)
{
    [FixedUpdate]
    public void Move(GameTime dt)               // "leaf" form: no next, runs in order
    {
        ...
    }
}

public sealed class PhysicsSystem(PhysicsWorld physics)
{
    [FixedUpdate(Order = -100)]                 // explicit ordering, default 0, stable by registration order
    public void Step(GameTime dt, Next next)    // "middleware" form: wraps everything after it in the stage
    {
        PushKinematics();
        next(dt);
        PullTransforms();
    }
}

app.UseSystem<PhysicsSystem>().UseSystem<PaddleSystem>();
```

`Next` is a small `readonly ref struct` (not a delegate) that the generator understands; in the reflection fallback it is backed by a delegate.

**What the generator emits** for every `UseSystem<T>()` call site (via `[InterceptsLocation]`) is a registration into a generated `Schedule` partial class that calls systems directly, in order, with `next` semantics preserved by splitting middleware methods at the single top-level `next(dt);` statement:

```csharp
// <auto-generated/> Ion.Generated.Schedule.g.cs
[StackTraceHidden]
partial class Schedule
{
    private readonly PhysicsSystem _physics; private readonly PaddleSystem _paddle; private readonly SpriteRenderSystem _sprites;

    public void FixedUpdate(GameTime dt)
    {
        _physics.Step_Before(dt);            // generated halves of PhysicsSystem.Step (or the whole method if it has no next)
        try { _paddle.Move(dt); }
        finally { _physics.Step_After(dt); } // only wrapped in try/finally when the original body has next() inside try
    }
    ...
}
```

Rules the generator enforces with diagnostics (so agents get precise errors): exactly one top-level `next(dt)` per middleware method; no `next` inside loops or conditionals (use `Order` and leaf methods instead); no `async`; stage attributes only on public instance methods of registered types; `Order` ties broken by registration order.

Systems whose bodies cannot be split (the `GameLoopDelegate(GameLoopDelegate next)` factory form used by `TraceTimerSystem` and the Scenes sample) are supported through a second generated shape: a struct-generic chain `Chain<TSystem, Chain<..., Terminal>>` where each hop is a constrained call on a value type, which both RyuJIT and NativeAOT's ILC inline. The `InliningBenchmarks` class in `Ion.Benchmarks` is the prototype of this chain; it is about 20 percent cheaper than the delegate chain under the JIT (section 5.1) and is expected to gain more under NativeAOT, but the flat direct-call schedule is the primary shape.

**DI.** Systems are resolved once, so the generator also emits the composition root (constructor calls in dependency order, scoped instances per scene) and only falls back to `Microsoft.Extensions.DependencyInjection` for types it cannot see (plugins). `IOptions<T>` binding goes through the configuration-binding generator (`EnableConfigurationBindingGenerator` on every project) so NativeAOT publishes with zero Ion warnings.

**Stack traces.** Generated types carry `[StackTraceHidden]` (honoured by NativeAOT's ILCompiler as well as CoreCLR). The trace from section 2 becomes:

```
at Ion.Extensions.Graphics.GraphicsSystem.Init(GameTime dt) in .../Systems/GraphicsSystem.cs:line 13
at Ion.Extensions.Graphics.WindowSystem.Init(GameTime dt, Next next) in .../Systems/WindowSystem.cs:line 16
at Program.<Main>$(String[] args) in .../Program.cs:line 40
```

**Ordering and validation.** `app.Build()` (generated) validates the schedule and prints it on request (`ion run --print-schedule`), which is the single most useful thing an agent can read when a system "does not run".

### 4.4 Events v2 (Stage 3)

Replace the boxed `RingBuffer<IEvent>` with one unboxed double-buffered channel per event type and a read cursor per reader:

```csharp
public interface IEvents
{
    void Emit<T>(in T e) where T : unmanaged;            // appends to Channel<T>.Current
    EventReader<T> Reader<T>() where T : unmanaged;      // stable per-system reader (created once, in the constructor)
}

public ref struct EventReader<T> { public bool TryRead(out T e); public ReadOnlySpan<T> Read(); public bool Any(); }
```

Semantics preserved from today: an event is visible for the frame it was emitted in and the next one (so a system earlier in the schedule still sees it), each reader sees each event once, `Emit` from any stage. Removed: `Handled` (it was never set), `EventId`, the type-hash `EventType`. The `EventBenchmarks` prototype in the benchmark project (`TypedChannel<T>`) is the reference for the data layout; the measured difference is in section 5.

### 4.5 Input v2 (Stage 3)

Fixed-size state: `ulong[]` bitsets for `Down`, `Pressed`, `Released` indexed by `Key` (max ~256), a 32-bit mask for mouse buttons, `Vector2` position and delta, wheel, text-input `ReadOnlySpan<char>` for the frame, and gamepad state (Silk.NET.Input exposes it). Press and release in the same frame set both bits. Focus loss clears `Down`. Input is captured from the windowing layer in `First` and is immutable for the rest of the frame. A `RecordedInput` implementation replays a stream for deterministic tests, and a `ScriptedInput` lets agents inject keys and clicks over the remote protocol.

### 4.6 Metrics v2 (Stage 3)

Three layers, all allocation-free on the hot path:

1. **Spans.** The generated schedule brackets every system call with `Stopwatch.GetTimestamp()` writes into a preallocated per-frame ring (`FrameProfile[]`, N frames deep). Enabled by a `static readonly bool` behind a feature switch so ILC removes it entirely from release builds that opt out, and toggleable at runtime in builds that keep it. No strings on the hot path: system and stage names are interned ids resolved at export time.
2. **Counters.** Engine counters (`draw_calls`, `sprites`, `triangles`, `entities`, `events_emitted`, `gc_gen0/1/2`, `allocated_bytes`, `frame_ms`, `fixed_steps`) live in a `FrameStats` struct written once per frame; games add their own with `Metrics.Counter("balls")`.
3. **Export.** Chrome trace JSON (Perfetto) and Tracy zones from the ring, `System.Diagnostics.Metrics` `Meter` for frame-level aggregates so `dotnet-counters` works, a JSONL "frame log" line per frame for agents, and an optional on-screen overlay drawn by the 2D renderer.

### 4.7 Graphics on Silk.NET (Stage 4)

**Findings that shape the choice.** Silk.NET 2.23.0 (January 2026) is the current stable line; 3.0 has no public NuGet preview yet (CI builds only; the maintainers call Preview 4 the first production-ready build). `Silk.NET.WebGPU` binds the pre-"futures" `webgpu.h` (callback-style `RequestAdapter`, no `WGPUStringView`/`CallbackInfo`) and ships wgpu-native binaries; there has been binary/bindings drift before (fixed in 2.22). `Silk.NET.Windowing` and `Silk.NET.Input` use reflection for platform discovery (issue #960) but work under NativeAOT when the platform is registered explicitly (`Window.Add(new GlfwPlatform())`). `Silk.NET.SDL` is SDL2 only. Nothing in Silk.NET is marked `IsAotCompatible`.

**Decision.** Use Silk.NET as the owner asked, but keep the native binding surface behind a thin, WebGPU-shaped RHI (`IGraphicsDevice`, `IBuffer`, `ITexture`, `ISampler`, `IShaderModule`, `IBindGroup`, `IRenderPipeline`, `ICommandEncoder`, `IRenderPass`, `ISurface`) so that the binding package is a one-project swap. Candidate swaps if `Silk.NET.WebGPU` blocks: `Alimer.Bindings.WebGPU` 1.6 (wgpu-native v27, current header, AOT-clean) or `WebGPUSharp` (Dawn). The RHI is deliberately small (about 15 interfaces) and mirrors WebGPU verbatim so a future browser backend over Emscripten's `webgpu.h` is a mechanical port.

**Windowing with Ion's loop.** `Silk.NET.Windowing` exposes exactly the primitives needed: `Initialize()`, `DoEvents()` (main thread), `FramebufferSize`, `Native` handles, `Reset()`. The engine's `WindowSystem` calls `DoEvents()` in `First` and never uses `IWindow.Run`. The surface is created from `window.Native` through `WebGPUSurface.CreateWebGPUSurface`. `DevicePoll(false)` runs once per frame in `Last` to pump wgpu callbacks; `DevicePoll(true)` on shutdown.

**Shaders.** Author in WGSL (the only language browsers accept) and keep them as embedded resources; add a build step later (Stage 7) that runs `naga` for validation and generates C# bind-group/uniform structs from reflection so shader/CPU layout mismatches are compile errors.

**2D renderer v2** (`Ion.Extensions.Rendering2D`): single instance buffer per frame with a 3-deep ring (no `WaitForIdle`), draws issued as ranges into that buffer per texture/material, packed `RGBA8` color, no per-sprite scissor (scissor becomes a batch-level render state), explicit `SpriteSortMode` (`Deferred`, `Texture`, `FrontToBack`, `BackToFront`), a `Camera2D` matrix per batch, blend/sampler presets (`AlphaBlend`, `Additive`, `Opaque`; `Point`, `Linear`), render-to-texture, `ReadOnlySpan<char>` text with cached layouts and a glyph atlas owned by the renderer, and an `ISpriteBatch` that keeps today's `Draw*` signatures. `MeasureString` implemented.

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

### 4.9 ECS integration with Arch (Stage 5)

**Choice.** Arch (owner preference, already used by the sample, active master, archetype/chunk layout, `CommandBuffer`, `ParallelQuery`). Upgrade from 1.2.8 to 2.1 (versioned `Entity` replaces `EntityReference`, cached archetype sets). Friflo.Engine.ECS 3.6 is the documented alternative (explicit NativeAOT/WASM support, no `unsafe`, `[Query]` generator, per-system timing) and the integration layer is written so that swapping is a module change, not a user-code change. Decide at the Stage 5 spike based on NativeAOT results (Arch's `ArrayRegistry`/`ComponentRegistry` currently emit IL3050 and need `Arch.AOT.SourceGenerator`).

**Integration surface (`Ion.Extensions.Ecs`):**

- `World` registered per scene scope (`AddEcs()`), disposed with the scope; a root `World` when there are no scenes.
- Systems inject `World` and use Arch queries directly, or the Ion `[Query]` attribute (generated with `Arch.System.SourceGenerator` semantics, plain named methods, no lambdas):

```csharp
public sealed partial class SpriteExtractSystem(World world, ISpriteBatch sprites)
{
    [Render(Order = -300), Query, All<Sprite, GlobalTransform2D>, None<Hidden>]
    private void Extract(ref Sprite sprite, ref GlobalTransform2D transform)
        => sprites.Draw(sprite.Texture, transform.Matrix, sprite.Size, sprite.Color, sprite.Depth);
}
```

- A `Commands` service (Arch `CommandBuffer`) injected into systems for structural changes; flushed automatically at the end of each stage by a generated step, so iteration is never invalidated mid-query. Structural changes inside a query throw a clear `StructuralChangeException`-style error.
- Built-in component modules: `Transform2D`/`Transform` hierarchy with propagation systems, `Sprite`, `SpriteAnimation`, `Camera2D`/`Camera`, `MeshRenderer`, lights, `Aabb`/visibility, `Name` (for the remote protocol).
- `Ion.Extensions.Ecs.Rendering` contains the extraction systems for 2D and 3D described in 4.8. Entities are never touched by the renderer; extraction copies into flat arrays every frame (Bevy's model), which also makes the renderer usable from non-ECS code.
- Physics stays a plugin (Aether 2D, later a 3D option) with adapter systems, following the Breakout ECS sample.

### 4.10 Agentic development (Stage 6)

Concrete capabilities, in priority order, each with the engine feature that delivers it:

1. **One CLI entry that exits.** `ion run --headless --frames 600 --seed 42 --screenshot out/frame600.png --summary out/run.json` (also `dotnet run -- --headless ...`). Exit code reflects exceptions; the summary JSON has frame stats, counters, warnings, and the schedule.
2. **Deterministic stepping.** `IClock` with a `FixedStepClock`; `GameLoop.Step()` public and used by tests; `RecordedInput`/`ScriptedInput`.
3. **Headless rendering + screenshots.** `Graphics.Headless` renders to an offscreen texture and reads back PNG; also available in windowed mode via `window.Screenshot(path)`.
4. **Snapshot tests.** `Ion.Testing` helpers: `IonTestHost.Run<TGame>(frames)` returns state, counters and an image; golden-image comparison with tolerance and diff output; world state serialized via `Arch.Persistence`.
5. **Machine-readable output.** JSONL frame log, Chrome trace export, `--print-schedule`, structured exceptions that name stage/system/entity.
6. **Remote inspection.** `Ion.Extensions.Remote`: JSON-RPC over HTTP or stdio modelled on the Bevy Remote Protocol (`world.query`, `get/insert/mutate/remove_components`, `spawn/despawn`, `resources`, `+watch` streaming, `registry.schema`, `rpc.discover`, `input.send`, `screenshot`, `metrics`), and a small MCP server on top so Claude Code can drive a running game.
7. **Precise errors.** Generator diagnostics for schedule mistakes, DI errors that name the system and missing service, "system registered after UseScene" warnings, no swallowed exceptions.
8. **Small, typed, discoverable API.** Few namespaces, `Ion` meta-package, XML docs, analyzers for misuse (missing `next`, `async` in a stage, `Task` in a system).
9. **Hot reload.** Keep the metadata-update handler; rebuild the generated schedule on reload; reload shaders and assets on file change with results reported on the protocol.
10. **Templates.** `ion new 2d|3d|ecs` scaffolds a game with `CLAUDE.md`, `appsettings.json`, a headless test and a snapshot test.

### 4.11 Native compilation and platforms (Stages 2, 4, 7)

- **NativeAOT on desktop** (Windows, macOS, Linux; x64 and arm64) is the primary "native compilation" story and is already almost there: the engine's own code produced two AOT warnings. After Stage 2 (no reflection binder, generated config binding) and Stage 4 (no Veldrid/DependencyModel/Newtonsoft, no NAudio COM) the engine and templates publish with `PublishAot=true` and zero warnings. `IlcGenerateStackTraceData` stays on so traces remain readable; `EventSourceSupport` on for `dotnet-trace`.
- **Browser** via `net10.0-browser` with the `wasm-tools` workload (Mono interpreter/AOT, single-threaded). The RHI gets an `Ion.Extensions.Graphics.WebGPU.Browser` backend over Emscripten's `webgpu.h` (the Evergine/Pollus pattern); the windowing layer becomes a canvas + JS input shim. WGSL-only shaders and the "no async" design (`requestAnimationFrame` drives `GameLoop.Step`) make this feasible; NativeAOT-LLVM for wasm stays an experiment.
- **Mobile** (iOS NativeAOT since .NET 9, Android in .NET 10) follows the same RHI; not scheduled.
- Generators target Roslyn 4.4 so any 8.0+ SDK works; engine targets `net8.0;net10.0`.

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

### 5.7 ECS iteration baseline (`ArchQueryBenchmarks`, Arch 1.2.8, 10,000 entities)

| Query style | Time | Allocated |
|---|---|---|
| Delegate query (what the sample uses) | 215 us | 88 B |
| `InlineQuery` with a struct `IForEach` | 206 us | 0 B |
| Raw chunk spans | 84 us | 0 B |

Chunk-span iteration is 2.5x faster than the delegate form in this Arch version; the `[Query]` code generation in Stage 5 emits the chunk-span form.

### 5.8 Startup (`PipelineBuildBenchmarks`)

Building the host, binding by reflection and building the seven pipelines: 1.28 ms and 296 KB for 8 systems, 2.0 ms and 451 KB for 32 (dominated by `Host.CreateApplicationBuilder`). Not a problem for startup, but it is the cost paid on every hot reload and every scene switch (each scene rebuilds its pipelines the same way). Note that every `IonApplication` that is built and not disposed leaks a `FileSystemWatcher` (the host's `appsettings.json` reload watcher); the benchmark hit the Linux inotify limit of 128 instances until it disposed the application. Tests that build many apps must dispose them.

---

## 6. Staged roadmap

Each stage is sized so a single agent session (or a small PR series) can deliver it, and each has acceptance criteria that a machine can check. Stages 0-3 are pure engine work and do not touch graphics; Stage 4 is the Silk.NET migration; 5 adds ECS and 3D; 6 and 7 make the engine agent-friendly and native/multi-platform. Stages 3 and 4 can proceed in parallel; 5 depends on 4; 6 depends on 2, 3 and the headless backend from 4; 7 depends on 4 and 5.

### Stage 0: Make it build, run and measure (1 week)

- Fix the confirmed bugs in section 3.8 (enum mapping, `WindowConfig` binding, `Bonk.wav`, generator Roslyn pin to 4.4, `NullTraceTimer` boxing, static `_scenesAdded`, `SceneSystem.next`, `DrawString` color, `MeasureString`, `Color(uint)`, input edge cases, coroutine `Stop`, storage root/truncate, texture loader leaks, stale test project).
- Remove `Ion_old`, the old `Ion.Generators`, the unused `Assimp`/`Veldrid.ImageSharp` references; move the WGPU experiment to a `spikes/` folder or delete it (its reusable parts are the backend-agnostic sprite/font code, which already exists in the Veldrid project).
- Add `global.json` (`rollForward: latestFeature`), `Directory.Build.props` with shared TFM/analyzers/`IsAotCompatible`/`EnableConfigurationBindingGenerator`, and `dotnet format` in CI.
- CI: build on all three OSes with the pinned SDK, run tests, run `Ion.Benchmarks --job short` and publish the results as an artifact, publish the ECS sample with `PublishAot` on Linux and fail on new Ion warnings.
- Acceptance: `dotnet build` clean on SDK 8.0.1xx and 10.x; all samples start under Xvfb+lavapipe with the backend they asked for; NativeAOT publish has no `Ion.*` warnings; benchmark artifact produced.

### Stage 1: Testable, deterministic core (1-2 weeks)

- `IClock` (`StopwatchClock`, `FixedStepClock`, `ManualClock`) injected into `GameLoop`; separate `FixedUpdate` rate from `MaxFPS`; frame pacing via a high-resolution wait (spin-then-sleep with timer resolution raised on Windows) and a `VSync` option that defers pacing to the swapchain.
- `GameLoop.Run(CancellationToken)` and `RunFrames(int)`; `Step()` is the unit of testing.
- Remove all interface-to-concrete downcasts; `Ion.Extensions.Graphics.Null` becomes a complete headless backend (window, context, sprite batch, input, loaders) so any game runs without a GPU.
- `Ion.Testing` package: `IonTestHost` that builds a headless app, steps N frames, exposes services, counters and emitted events.
- Tests for: fixed-step accumulation, `MaxFPS` pacing with a manual clock, all three binder signatures, scene load/unload/`Destroy` counts and scope disposal, coroutine waits, assets cache/dispose, storage, events across frame boundaries.
- Acceptance: coverage above 70 percent on `Core`, `Scenes`, `Coroutines`, `Assets`; a Breakout ECS headless test that runs 600 frames and asserts score and entity counts.

### Stage 2: Source-generated schedule, DI and clean stack traces (2-3 weeks)

- New `Ion.Generators` (Roslyn 4.4, incremental): intercept `UseSystem<T>()`, `UseInit/First/...` delegate registrations and `UseScene(...)` builders; emit `Schedule` partial classes per app and per scene, the composition root, `[StackTraceHidden]` on all generated code, and diagnostics `ION001..ION0xx` for schedule mistakes.
- `Next` ref struct replaces `GameLoopDelegate next` in the recommended signature; the delegate form keeps working (fallback path and struct-generic chain for factory-style middleware).
- `Order` on stage attributes; `[After<T>]`/`[Before<T>]` as optional constraints; `--print-schedule`.
- Options binding through the configuration-binding generator; `[LoggerMessage]` logging; `[OptionsValidator]`.
- Delete `Ion.Core.InternalGenerators` and `Scenes.Generators` (their overloads are generated by the new generator on demand).
- Acceptance: `PipelineBenchmarks.Ion_*` within 1.2x of `DirectCalls_FlatLoop` for 32 systems and zero bytes allocated per frame; the section 2 stack trace contains only user frames plus `Program.Main`; NativeAOT publish of all samples with zero warnings from `Ion.*`; hot reload rebuilds the generated schedule.

### Stage 3: Events, input and metrics (2 weeks, parallel with Stage 4)

- Events v2 (4.4): typed channels, readers, zero allocation on emit and read; `IEventListener`/`IEventEmitter` kept as thin adapters for one release, then removed.
- Input v2 (4.5): bitsets, edge semantics fixed, text input, mouse delta, gamepads, `RecordedInput`.
- Metrics v2 (4.6): frame ring, counters, Chrome trace and Tracy export, `Meter` aggregates, JSONL frame log, overlay hook. Release builds keep tracing available behind a runtime toggle.
- Coroutines: singleton runner driven by the engine in `Update`, unboxed waits (`Wait` becomes a struct union), `Stop` safe, proper namespace.
- Assets: cache by (loader, path) with reference counting, scoped release on scene unload, hot reload on file change, polled background decode for images.
- Audio: replace `DirectSoundOut` with a cross-platform output (Silk.NET.OpenAL or SDL audio through the windowing layer) and a resampling mixer; fix pitch/volume.
- Acceptance: `EventBenchmarks` at or below the `Prototype_TypedChannels` numbers with zero allocation; `FullFrameBenchmarks.Step_8Systems` allocates 0 bytes; trace export of a 10-minute run bounded in memory; audio plays on all three OSes.

### Stage 4: Silk.NET graphics stack and 2D renderer v2 (4-6 weeks)

- Spike (3 days, go/no-go): `Silk.NET.Windowing` + `Silk.NET.WebGPU` triangle and textured quad on Windows, macOS and Linux, driven by `GameLoop`, published with NativeAOT. Verify the shipped wgpu-native binary matches the bindings, HiDPI framebuffer sizing, Wayland, and `DevicePoll`. If it fails, the same spike on `Alimer.Bindings.WebGPU` decides the binding package; the RHI stays the same either way.
- RHI (`Graphics.Abstractions`): the ~15 WebGPU-shaped interfaces; `Graphics.WebGPU` implementation; `Graphics.Headless` (offscreen target + PNG readback, no window); windowing/input module.
- `Rendering2D` on the RHI (4.7): instance ring, sort modes, packed color, camera, blend/sampler presets, render targets, text with cached layout and atlas, `MeasureString`, debug lines/shapes, screenshot.
- Port the three samples; keep Veldrid selectable until the snapshot tests match; then delete it.
- Acceptance: all samples run on all three OSes and headless in CI with golden-image tests; `SpriteBatchBenchmarks` per-sprite CPU cost halved (no scissor transform, packed color); a 100k-sprite stress sample holds 60 fps on an integrated GPU with one draw call per texture; zero AOT warnings from `Ion.*`.

### Stage 5: Built-in ECS and the 3D SDK (6-8 weeks)

- `Ion.Extensions.Ecs` (4.9): Arch 2.1, `World` per scope, `[Query]` generation, `Commands` flush per stage, transform hierarchy, `Name`, serialization via `Arch.Persistence`; NativeAOT verified with `Arch.AOT.SourceGenerator` (or Friflo if Arch cannot be made warning-free).
- `Ion.Extensions.Ecs.Rendering`: 2D extraction (Sprite, Camera2D, SpriteAnimation, Tilemap) first, then 3D.
- `Rendering3D` (4.8): mesh/material/camera/light types, transform propagation, bounds, frustum culling, extract/prepare/queue/sort, render graph with shadow, opaque, skybox, transparent, post and overlay passes, Unlit and PBR materials in WGSL, glTF import, instancing.
- Samples: `Ion.Examples.Cubes` (immediate-mode 3D, no ECS), `Ion.Examples.Sponza` (glTF, PBR, lights, ECS), Breakout ECS moved onto the built-in components.
- Acceptance: 10k `MeshRenderer` entities with 3 materials render in under 2 ms CPU on the extraction+queue path (benchmark added); glTF sample matches reference screenshots on all backends; ECS query benchmark on the built-in `[Query]` path matches `ChunkSpans` numbers.

### Stage 6: Agentic toolchain (3-4 weeks)

- `Ion.Tools` (`ion` dotnet tool): `new`, `run --headless --frames --seed --screenshot --summary`, `schedule`, `bench`, `trace`.
- `Ion.Extensions.Remote`: JSON-RPC inspection protocol (4.10) plus MCP server; `input.send`, `screenshot`, `metrics`, `+watch`.
- Snapshot testing helpers and templates with `CLAUDE.md`; documentation site generated from XML docs.
- Acceptance: an agent with only the `ion` CLI and the MCP server can create a game from the template, add a system, run 600 headless frames, take a screenshot, diff it against a golden image, inspect an entity and mutate a component, without reading engine source.

### Stage 7: Native and multi-platform builds (4-6 weeks, can start after Stage 4)

- NativeAOT publishing profiles for win-x64/arm64, osx-arm64/x64, linux-x64/arm64 in the templates and CI; size and startup tracked in the benchmark artifact.
- Browser: `net10.0-browser` target for `Core`, `Rendering2D/3D`, `Ecs`; `Graphics.WebGPU.Browser` over Emscripten `webgpu.h`; canvas windowing/input shim; `requestAnimationFrame` driven `Step`; a hosted demo of Breakout ECS.
- Shader build step: `naga` validation and generated bind-group structs.
- Acceptance: Breakout ECS runs in Chrome from a static host; desktop AOT binaries under 30 MB start in under 300 ms.

---

## 7. Open questions for the owner

Assumptions were made where needed so that this plan is complete; the answers change scope, not direction.

1. **Silk.NET scope.** Is Silk.NET a hard requirement for the WebGPU binding itself, or for "a maintained binding set"? The plan uses `Silk.NET.Windowing`/`Input`/`WebGPU` first and keeps the binding swappable because `Silk.NET.WebGPU` 2.23 is on an older `webgpu.h` and 3.0 has no NuGet preview. If Silk.NET is the only acceptable vendor, the Stage 4 spike should also test raw `Silk.NET.Vulkan` as a second native backend for safety.
2. **Minimum platform set for v1.** The plan assumes desktop (Windows/macOS/Linux) first, browser second, mobile unscheduled.
3. **ECS: Arch or Friflo?** Arch is assumed (your preference, sample already uses it). Friflo has the better NativeAOT/WASM story today; the Stage 5 spike decides. Should the engine ship ECS components (Transform, Sprite, Camera) as the primary way to build games, or keep immediate-mode (`ISpriteBatch`) as the primary and ECS as optional? The plan keeps both with one renderer.
4. **Breaking changes.** The schedule generator changes the recommended system signature (`Next` instead of `GameLoopDelegate next`) and Events v2 replaces `IEventListener`. Is a 0.3 release with a migration guide acceptable, or must 0.2 code compile unchanged?
5. **Target frameworks.** `net8.0` only today. The plan proposes `net8.0;net10.0` multi-target with generators on Roslyn 4.4. Dropping `net8.0` would simplify (interceptors are stable from the 9.0.200 SDK, which also builds `net8.0` targets, so this is about runtime, not compiler).
6. **Audio backend.** OpenAL (Silk.NET.OpenAL) vs SDL audio vs miniaudio bindings; the plan assumes whichever comes with the windowing choice.
7. **Physics.** Keep physics out of the engine (adapters only) as today, or ship a 2D physics module on Aether/Box2D v3 and a 3D one on Jolt/BepuPhysics later?
8. **Remote protocol transport.** HTTP (matches Bevy BRP and is easy from any tool) vs stdio (simpler for MCP). The plan does both behind one handler.
9. **Licensing of shipped assets** for templates and golden images (fonts, textures) must be CC0 or owner-provided.

---

## 8. Appendix: sources consulted

- Silk.NET releases and 3.0 roadmap: https://github.com/dotnet/Silk.NET/releases, https://github.com/dotnet/Silk.NET/milestone/9, https://github.com/dotnet/Silk.NET/issues/960, https://github.com/dotnet/Silk.NET/discussions/1160
- WebGPU: https://github.com/webgpu-native/webgpu-headers, https://github.com/gfx-rs/wgpu-native/releases, https://github.com/amerkoleci/Alimer.Bindings.WebGPU, https://github.com/PhilippeMonteil/WebGPUSharp
- Browser .NET: https://github.com/dotnet/runtimelab/tree/feature/NativeAOT-LLVM, https://github.com/EvergineTeam/WebGPU.NET, https://github.com/Refsa/pollus
- ECS: https://github.com/genaray/Arch, https://github.com/genaray/Arch.Extended, https://github.com/friflo/Friflo.Engine.ECS, https://github.com/Doraku/Ecs.CSharp.Benchmark
- Rendering architecture: https://bevy.org/news/bevy-0-19/, https://github.com/stride3d/stride-docs (rendering pipeline), https://docs.unity3d.com/Packages/com.unity.entities.graphics@1.4, https://docs.godotengine.org/en/stable/classes/class_renderingserver.html
- Agentic tooling: https://github.com/bevyengine/bevy/blob/main/crates/bevy_remote/src/lib.rs, https://github.com/natepiano/bevy_brp, https://unity.com/blog/unity-ai-mcp-how-to-get-started, https://github.com/IvanMurzak/Unreal-MCP, https://github.com/Coding-Solo/godot-mcp
- Codegen and AOT: https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md, https://github.com/dotnet/AspNetCore.Docs/blob/main/aspnetcore/fundamentals/aot/request-delegate-generator/rdg.md, https://github.com/dotnet/runtime/blob/main/src/coreclr/tools/aot/ILCompiler.Compiler/Compiler/StackTraceEmissionPolicy.cs, https://github.com/pakrym/jab, https://github.com/devteam/Pure.DI, https://github.com/dotnet/docs/blob/main/docs/core/extensions/configuration-generator.md
- Metrics: https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/test/Benchmarks/Metrics/MetricsBenchmarks.cs, https://github.com/clibequilibrium/Tracy-CSharp, https://github.com/bevyengine/bevy/blob/main/docs/profiling.md
