My Changelog
<a name="unreleased"></a>
## Unreleased

### ⚠ Breaking and behaviour changes

* **Schedule of ordered steps instead of middleware chains.** Each stage is now an ordered list of steps built from a `ScheduleModel` (new, `IIonApplication.Schedule` / `ISceneBuilder.Schedule`) when the game loop is built: a step is a public `void M(GameTime dt)` (or `void M()`, or `void M(GameTime dt, TService s, ...)` with services resolved once) marked with a stage attribute, and runs and returns. Steps run by the new `Order` property of the stage attributes (default 0), then registration order, then declaration order; `[After<T>]`/`[Before<T>]` (new generic attributes, method or class level) take precedence. Wrapping is expressed with `[Begin(Stage.X)]`/`[End(Stage.X)]` scope pairs, whose end runs in a `finally`. New `Stage` enum (values match `GameLoopStage`) and `StageOrder` constants.
* **Registration order no longer matters.** Engine steps use reserved order bands (`-1000..-500` setup, `500..1000` teardown), so user steps at the default order run between them whether registered before or after `UseIon()`. Every engine system is now leaf steps and scopes: `EventSystem` (Last, 1000), `TraceTimerSystem` (a scope at -1000 on every stage but FixedUpdate; `UseDebugUtils()` adds nothing in Release builds of the package), `WindowSystem` and `NullWindowSystem` (Init/First -950, close check in Render at 900), `InputSystem`/`NullInputSystem` (First -940), `GraphicsSystem` (Init and a Render frame scope at -900), `AudioSystem` (Init and Last -880, Destroy 880), `SpriteBatchSystem`/`NullSpriteBatchSystem` (Init and a Render scope at -850), `CoroutineSystem` (Update -600) and `SceneSystem` (-500 in every stage).
* **Scenes.** `SceneSystem` is public and runs the active scene's own `Schedule` as one step per stage, so scene systems follow the same ordering rules; systems registered after `UseScene` are no longer affected by its position. `SceneInstance` exposes `Schedule` and its stage delegates are read-only. Scene schedules are planned (validated) when the application's schedule is built, with a temporary scope, so a scene's configure callback runs once more at build time. Registering on a scene builder after the scene loaded throws `ION004`.
* **Validation at build.** `IonApplication.Build()` throws `IonScheduleException` (with `Diagnostics` and `Codes`) listing every error: `ION001` unknown stage, `ION002` Before/After cycle, `ION003` unpaired scope, `ION004` stage attribute on a non-public method, `ION005` async/`Task` step, `ION006` scoped system or scoped step parameter in the root schedule, `ION007` unsupported signature, `ION008` unregistered parameter, `ION009` unregistered system, `ION011` ambiguous scope. Methods with unsupported signatures used to be logged and skipped. Warnings `ION010` (legacy middleware), `ION012` (constraint on a system not in the schedule) and `ION013` (system without steps) are logged once under `Ion.Schedule`. The Breakout ECS sample's systems are now singletons (they were scoped but resolved from the root).
* **Legacy middleware.** `(GameTime dt, GameLoopDelegate next)` and `GameLoopDelegate (GameLoopDelegate next)` methods, `UseInit(...)`..`UseDestroy(...)` delegates and the generated `Use{Stage}<TService...>` overloads keep working as opaque middleware placed by their order (order 0 for the delegates), wrapping the steps after them, with warning `ION010`.
* **Scene enum overloads are library methods.** `UseScene(Scene.X, ...)` and `EmitChangeScene(Scene.X)` are now the generic `UseScene<TScene>` and `EmitChangeScene<TScene>` (`where TScene : struct, Enum`) of `Ion.Extensions.Scenes`; the scenes generator only emits the `[ScenesEnum]` attribute. Call sites compile unchanged. (Generators cannot see each other's output, so the schedule generator could not bind scene registrations made through a generated overload.)
* **Clean stack traces.** `GameLoop.Run`/`RunFrames`/`Step`/`Initialize`/`Shutdown`, `IonApplication.Run`/`RunFrames` and the scene system's dispatch steps are `[StackTraceHidden]`, like the runtime schedule runners and the new step adapters, so an exception thrown by a step shows the step, then the caller of `Run` (for example `Program.<Main>$`).
* **Removed:** `MiddlewarePipelineBuilder`, `IMiddlewarePipelineBuilder`, `SystemMiddlewareBinder.TryGetSystemFunction` (the class keeps the `MiddlewareAccessibility` constant, which now includes non-public methods), and `GameLoop.InitBuilder`..`DestroyBuilder`; `GameLoop` gains `Schedule`, `ScheduleFactory` and `UseSchedule(Schedule)`, and `GameLoop.Build()` rebuilds from the factory (hot reload). The stage attributes are sealed and derive from the new `StageAttribute`. `IonTestHost` polls its event collectors from a Last step at order 990 (`IonTestHost.CollectorOrder`), so events emitted by Last steps at lower orders are collected the same frame.

* **.NET 10.** Every engine, test, benchmark and sample project targets `net10.0` and the repository builds with the .NET 10 SDK (`global.json`: `10.0.100`, `rollForward: latestFeature`, C# `latest`). Source generators stay `netstandard2.0` on Roslyn 4.4. `Microsoft.Extensions.*` packages move to 10.0.x.
* **User data folder.** `IPersistentStorage.User` (and `Saves` under it) now lives in a per-game folder named after `GameConfig.Title` under the user's local application data folder in every build configuration (it was the working directory in Debug builds). Game content and assets are resolved from `AppContext.BaseDirectory` instead of the working directory. All three can be overridden with the new `Ion:Storage` section (`StorageConfig.GamePath`, `AssetsPath`, `UserPath`). `OpenWrite` now truncates existing files.
* **Asset caching.** `IBaseAssetManager.GetOrLoad(path, load)` caches assets by (type, path) and returns the same instance on repeated loads; scene-scoped asset managers own and dispose what they load. `Load<SoundEffect>`, `Load<Texture2D>` and `Load<FontSet>` all go through it (font sets are keyed by their name). Reloading a texture that was disposed directly loads it again.
* **Coroutines.** `CoroutineRunner` moved from the `Ion` namespace to `Ion.Extensions.Coroutines`. `ICoroutineRunner` is a singleton (it was transient, so every consumer had its own runner), and `UseCoroutines()` adds a `CoroutineSystem` that steps it in the Update stage; `UseIon()` now calls `UseCoroutines()`. `Stop` of an unknown or finished routine is a no-op. `CoroutineRunner` takes an `IEventListenerFactory` instead of `IServiceProvider`, releases each coroutine's event listener when the coroutine ends, and is `IDisposable`.
* **Events.** New `IEventListenerFactory` (registered by default) creates listeners the caller owns and disposes. Resolving `IEventListener` from the container still works.
* **Graphics configuration.** `GraphicsConfig.ClearColorHex` binds the clear color from configuration (`Ion:Graphics:ClearColorHex`, `RGB`/`RGBA`/`RRGGBB`/`RRGGBBAA`). `WindowConfig` is now bound from `Ion:Window` (it was ignored).
* **Graphics backend mapping.** `GraphicsConfig.PreferredBackend` is mapped explicitly onto Veldrid's backends (the old enum cast was off by one: Vulkan selected OpenGL, OpenGL selected Metal). Unsupported backends fall back to the platform default with a warning; `Direct3D12` and `WebGPU` throw `NotSupportedException`.
* **Injectable clock.** `GameLoop` takes an `IClock` (new in `Ion.Core.Abstractions`: `Elapsed`, `Seconds`, `NextFrame()`, `Sleep(TimeSpan)`) instead of owning a `Stopwatch`. `Ion.Core` ships `StopwatchClock` (registered by default), `FixedStepClock(TimeSpan step)` (every frame lasts exactly one step, sleeps never block) and `ManualClock` (test-controlled `Advance(TimeSpan)`, records sleep requests). Register another `IClock` to replace the default.
* **Fixed step decoupled from MaxFPS.** `FixedGameTime.Delta` is now `1 / GameConfig.FixedUpdateRate` (new, Hz, default 60) instead of `1 / MaxFPS`. `MaxFPS` only paces rendering, and `0` (or any value below 1) now means uncapped (it used to mean 120). The 100 ms frame clamp is configurable through `GameConfig.MaxFrameTime`. `FixedGameTime.Elapsed` is the simulated time (the sum of fixed steps run) rather than the frame's wall time. New `GameConfig.VSync` turns off the loop's own pacing so the graphics layer can pace on present.
* **Frame pacing.** The end-of-frame wait sleeps for the bulk of the remaining time and spins the last millisecond, and raises the Windows timer resolution to 1 ms (`timeBeginPeriod`) while the loop runs. No pacing happens when `VSync` is set.
* **Run and step API.** `GameLoop.Run(CancellationToken = default)`, `GameLoop.RunFrames(int)` and a parameterless `GameLoop.Step()` that runs one complete timed frame (accumulator, fixed steps, pacing); `Step(GameTime)` is unchanged. `IIonApplication`/`IonApplication` gain `Run(CancellationToken)` and `RunFrames(int)`. `GameLoop.Run` throws `InvalidOperationException` if the loop is already running.
* **Substitutable events and tracing.** The container registers the concrete `EventEmitter` and forwards `IEventEmitter` to it, so a fake `IEventEmitter` can be registered without breaking the engine's listeners. `EventListener` takes an `EventEmitter` (the `IEventEmitter` overload remains for compatibility and throws `ArgumentException` for other emitters). `ITraceManager` gains `CreateTimer(string prefix)`; `TraceTimer` and `TraceTimerSystem` no longer downcast to the internal `TraceManager`, so a fake `ITraceManager` can be injected.
* **Asset types as abstractions.** Games depend on `ITexture2D`, `IFontSet`, `IFont` and `ISoundEffect` and load them with the new `IBaseAssetManager.Load<T>(path)`, which looks up the loader registered for `typeof(T)` through the new `IAssetLoader<T>` and caches like `GetOrLoad`. Loaders are now registered under the interface type (`IAssetLoader.AssetType == typeof(ITexture2D)` etc.). `IFontSet.CreateStyle(size)` returns `IFont`; `IFontSetLoader` and `LoadFontSet(name, params fonts)` combine several font files. `Load<Texture2D>`/`Load<SoundEffect>` written as instance calls now bind to `Load<T>` and return the same cached asset.
* **Obsolete for one release:** the concrete `Texture2D`, `BaseTexture`, `FontSet`, `Font` (Veldrid) and `SoundEffect` (NAudio) types and their `Load<T>` extension methods (`Texture2DAssetManagerExtensions`, `FontAssetManagerExtensions`, `SoundEffectAssetManagerExtensions`), which forward to the new API. `FontLoader`, `SoundEffectLoader`, `AudioManager`, `TextureFactoryExtensions` and the Veldrid and audio systems (`WindowSystem`, `InputSystem`, `GraphicsSystem`, `SpriteBatchSystem`, `AudioSystem`) are now internal.
* **No interface downcasts.** The Veldrid backend registers `Window`, `GraphicsContext`, `SpriteBatch` and `InputState` as themselves and forwards `IWindow`, `IGraphicsContext`, `ISpriteBatch` and `IInputState` to them; engine systems depend on the concrete types, so tests can replace the interfaces with fakes. `AddAudio()` does the same for `IAudioManager`.
* **Input and events in fixed steps.** `IInputState` edges and deltas now depend on the stage that reads them: from `FixedUpdate` they cover everything since the previous fixed step, so an edge is seen by exactly one fixed step whatever the ratio of `MaxFPS` to `FixedUpdateRate` (at 120 fps and 60 Hz about every other frame runs no fixed step, and clicks on those frames were lost, which stopped the Breakout ECS paddle from launching balls). Every other stage keeps the per-frame view. `IInputState` gains `MouseDelta`. Events that leave the two-frame window before any fixed step has run since they were emitted are kept in `EventEmitter.FixedStepBacklog` and delivered to `FixedUpdate` listeners (at most `EventEmitter.MaxBacklogFrames` frames); other stages see the same two-frame window as before.
* **Loop context.** New `ILoopContext` (`Stage`, `Frame`, `FixedStepCount`) and `GameLoopStage` in `Ion.Core.Abstractions`; `GameLoopContext` is registered by `IonApplication.CreateBuilder` and kept up to date by `GameLoop` (exposed as `GameLoop.Context`). `GameLoop` gains `Initialize()`, `Shutdown()` and `IsExitRequested` for hosts that drive frames with `Step()`.
* **Audio rewritten (no NAudio).** The DirectSound (NAudio) backend is gone; audio is an engine mixer with an OpenAL output (see Features). `IAudioManager.Play(sound, volume, pitchShift)` keeps its first three parameters and gains `pan`, `loop`, `bus` and `fadeIn`; it returns a `VoiceHandle` (was `void`). `pitchShift` is now in octaves (factor `2^shift`, clamped to [-1, 1]) and changes the playback rate, so a shifted sound is also shorter or longer; before, [-1, 0] and [0, 1] mapped linearly to [0.5, 1] and [1, 2] and the duration was kept. New members: `Stop`, `StopAll`, `SetVolume`, `SetPitch`, `SetPan`, `IsPlaying`, `GetBusVolume`, `SetBusVolume`. `ISoundEffect` gains `Channels` and `SampleRate`. `SoundEffect` is no longer obsolete: it is the decoded sound (float32 `Samples` at `MixRate`, `Frames`, `Channels`, `SampleRate`) and loses `WaveFormat` and `AudioData`; `SoundEffectAssetManagerExtensions` (the obsolete `Load<T>` where `T : SoundEffect`) is removed. `AudioManager`, `SoundEffectLoader` and `AudioMixer` are public. `AddAudio`/`AddNullAudio` gain overloads taking `IConfiguration` (bound from `Ion:Audio`), and `AddIon` passes its configuration. `UseNullAudio()` now adds the audio system (it added nothing). `NullAudioManager` derives from `AudioManager` and mixes on the null output as well as recording `Plays`; `SoundPlay` gains `Pan`, `Loop`, `Bus` and `Voice`. `NullSoundEffectLoader` now also decodes the samples (`NullSoundEffect.Decoded`) so headless runs mix real audio.
* **Input v2.** `InputTracker` moved from the Veldrid backend (where it was internal and linked into the null backend) to `Ion.Core.Abstractions` as the public shared implementation, and both backends' input states derive from the new `TrackedInputState`. `IInputState` gains `Text`, `Modifiers`, `Gamepads` and `Gamepad(int)`, so custom `IInputState` implementations must add them. The backends' input states take the application's `InputTracker` singleton (`AddInputTracker()`); `NullInputState` keeps its `ILoopContext` constructor and gains one taking a tracker.
* **Coroutine waits are a struct.** `Wait.For`, `Wait.Until`, `Wait.While` and `Wait.For<TEvent>` now return the `Wait` struct instead of `IWait`, and the `WaitFor`, `WaitUntil` and `WaitForEvent<T>` record structs are removed. `IWait` stays as the interface for custom waits (implement it as a class). Yielding `null` after a `Wait.Until`/`Wait.While` now resumes on the next frame instead of re-evaluating the old predicate.
* **Assets.** `UseIon()` now calls the new `UseAssets()`, which adds `AssetReloadSystem` (First, `StageOrder.AssetReload = -920`). Hot reload is on by default in Debug builds of `Ion.Extensions.Assets` (`Ion:Assets:HotReload`), where a `FileSystemWatcher` follows the assets folder; `IonTestHost` sets it to `false`.
* **Headless mode.** `AddIon(config)` registers the headless graphics and audio backends when `Ion:Headless` is `true` (`--Ion:Headless=true` on the command line) or the graphics output is `None`, and `UseIon()` adds their systems. `IsHeadless(config)` exposes the decision.

### ✨ Features

* **Compile-time schedule (`Ion.Generators`, new).** A Roslyn source generator, shipped as an analyzer of the `Ion` and `Ion.Core` packages (whose build props add `Ion.Generated` to `InterceptorsNamespaces`), intercepts `UseSystem`, function steps (`app.Update(...)`), legacy middleware (`app.UseUpdate(...)`), `UseScene` and `Build()`/`Run()`/`RunFrames()` with C# interceptors, and emits: a reflection-free description of every system (steps, scopes, constraints, diagnostics and direct-call binders, `GeneratedSystem`); for each application and scene a `GeneratedSchedule` with one method per stage that calls every step directly in plan order, with a `try/finally` per scope and the systems resolved once in its constructor; and a `ScheduleRegistrations` summary of every method that takes a builder, so an application sees what library helpers such as `UseIon()` register. The generated schedule runs only when the registrations made at run time and their plan match what the generator saw (conditional registrations are guarded); otherwise the runtime binds the pre-bound registrations, and anything the generator cannot see (a `Type` only known at run time, a plugin) stays on the reflection path. 32 systems in one stage: 15.7 ns against 13.3 ns for hand-written direct calls (runtime-bound: 67 ns); a full headless frame with 8 systems: 46 ns against 125 ns; no allocation per frame. The Breakout ECS sample publishes with NativeAOT with no Ion warnings and runs its generated schedule.
* **Schedule diagnostics at compile time.** `ION001` to `ION013` are reported by the generator with the runtime's ids, severities and messages (`ION008`, `ION009` and `ION006` for types declared in the project that no registration call mentions, `ION002` for cycles among unconditional registrations, `ION012` when the generator sees every registration), plus `ION014` when interceptors are not enabled.
* **Runtime support for generated code** (`Ion.Core.Abstractions`): `ScheduleModel.AddSystem(GeneratedSystem, site)`, `AddFunction`/`AddMiddleware` overloads with precomputed names, constraints and call sites, `ScheduleEntry.Site`, `ScheduleModel.UseGenerated(...)`, `Schedule.IsGenerated`/`Generated`, `GeneratedSchedule`, `GeneratedScheduleFactory` and `GeneratedScheduleContext`, and `StepAdapters`. The stage sort moved to `ScheduleSorter`, shared with the generator.
* **Function steps.** `app.Init(...)`, `app.First(...)`, ..., `app.Destroy(...)` (and the same on `ISceneBuilder`) register a delegate as a step: `Action<GameTime>`, or `Action<GameTime, T1..T4>` with the services resolved once. Optional `order` and `name`.
* **Printable schedule.** `IIonApplication.PrintSchedule()` (and `Schedule.Print()`/`SchedulePlan.Print()`) returns every stage in run order with orders, `System.Method`, constraints and scope braces, then each scene's schedule; `--Ion:PrintSchedule=true` (`IonApplication.PrintScheduleKey`) prints it at startup. `IonApplication.BuildSchedule()` returns the bound `Schedule`, and `ScheduleModel.Plan(services)` the `SchedulePlan` (stages, steps, scopes, orders, constraints and systems) that the source generator will consume.
* **Faster dispatch.** Leaf steps are bound once to `GameLoopDelegate`s in a flat array per stage (a stage with one step is that step's delegate). 32 systems in one stage: 47-54 ns instead of 111 ns; the legacy middleware path stays at 117 ns; no allocation per frame (`docs/plans/benchmarks/2026-09-25-stage2-schedule`).
* **`Ion.Testing`** (new package): `IonTestHost` builds a headless application on a `FixedStepClock`, steps it (`Step(frames)`, `RunUntil(condition, maxFrames)`) and exposes `Services`, `Input` (scripted `NullInputState`), `Window`, `SpriteBatch` statistics, `Audio` plays, `Events` and `Collect<T>()` for emitted events. Setup helpers: `WithSystem<T>()`, `Configure(services)`, `ConfigureApp(app)`, `WithConfiguration(settings)`, `WithArgs(args)` and `UseGame(configure, use)`. `Screenshot()` is a placeholder that throws `NotSupportedException` until the headless renderer exists.

* **Audio mixer** (`Ion.Extensions.Audio`, audio P0 of the roadmap): float32 stereo at a configurable rate (`AudioConfig.OutputRate`, default 48 kHz); voices with gain, pitch (32.32 fixed-point resampling, linear or cubic interpolation), pan, loop, fade in/out and stop; master, sfx and music buses; a fixed voice pool (`MaxVoices`, default 64) that steals the oldest voice or refuses (`VoiceStealing`); a lock-free SPSC command queue from the game thread (flushed in `Last` at `StageOrder.Audio`) and a return queue of finished voices; no allocation on the audio thread after warm-up; output clamped to [-1, 1]. Configuration: `Ion:Audio` (`OutputRate`, `MaxVoices`, `BufferFrames`, `BufferCount`, `Backend`, `Device`, `Interpolation`, `VoiceStealing`, `CommandCapacity`).
* **Decoding at load time**: `SoundDecoder` reads WAV (`WavDecoder`: PCM 8/16/24/32-bit, IEEE float 32/64-bit, `WAVE_FORMAT_EXTENSIBLE`, any channel count and rate), OGG Vorbis (NVorbis 0.10.5) and MP3 (NLayer 3.0.0), all managed and NativeAOT-clean, and resamples to the output rate with a windowed-sinc `Resampler` (anti-aliased when downsampling). Streaming music is not implemented yet: long tracks are decoded whole.
* **Audio outputs**: `IAudioOutput` (`Start(format, callback)`, `Stop`, `BufferSize`, `IsRunning`). `OpenAlAudioOutput`: one streaming source with a ring of queued buffers refilled on a dedicated thread, float32 when `AL_EXT_FLOAT32` is present (16-bit otherwise), underrun recovery and disconnect detection, OpenAL Soft from `Silk.NET.OpenAL.Soft.Native` (win x86/x64/arm64, osx x64/arm64, linux arm/arm64/x64) or the system OpenAL, bound through function pointers (no Silk.NET.OpenAL managed bindings, whose loader adds NativeAOT single-file warnings); `GetDeviceNames()` lists devices. `NullAudioOutput` pulls the mixer on the game thread from the game clock (`Advance(elapsed)`) or on demand (`Render(frames)`), bit-identical for the same commands, with optional capture. A missing device or library logs a warning and falls back to the null output.
* **`Ion.Extensions.Audio.Tests`** (new): WAV decoding of every encoding, OGG and MP3 decoding, resampling (a 440 Hz tone keeps its frequency across rates), gain, pan, pitch, loop and fade math against the mixed buffer, voice pool stealing and refusal, command ordering and overflow, a two-thread queue test, a zero-allocation test of the mix callback, null output determinism, OpenAL fallback, and an `IonTestHost` test that plays `bonk.wav` and compares the mixed output sample by sample.

* **Input v2** (`Ion.Core.Abstractions`): fixed-size input state in `ulong` bitsets indexed by `Key` (held, pressed and released, for both the per-frame and fixed-step views), 32-bit masks for mouse and gamepad buttons, mouse position and delta, wheel, per-frame text input (`IInputState.Text`, a `ReadOnlySpan<char>` fed from the Veldrid snapshot's character events), held `Modifiers`, and eight gamepad slots (`IGamepadState`: `Down`/`Up`/`Pressed`/`Released(GamepadButton)`, `Axis(GamepadAxis)`, `LeftStick`, `RightStick`) with a radial dead zone from `Ion:Input:GamepadDeadZone` (`InputConfig`, default 0.15). No allocation per frame (checked by a test). Modifier and focus-loss semantics are documented on `InputTracker`. The Veldrid backend exposes no gamepads (Veldrid's SDL2 snapshot has no controller events); the Silk.NET backend in Stage 4 will feed them. `NullInputState` scripts text (`Type`), key repeats (`Repeat`) and gamepads (`ConnectGamepad`, `DisconnectGamepad`, `Press`/`Release`/`Tap(pad, button)`, `SetAxis`, `SetLeftStick`, `SetRightStick`).
* **Recorded input.** `AddInputRecording(path)` records every frame's input events with `InputRecorder` into a compact binary stream (`InputRecordingFormat`: frame blocks of 7-bit encoded frame numbers and events, with an end marker); `AddInputPlayback(path)` replays it with `InputPlayer` at the recorded frame numbers into any `IInputEventSink`, replacing device input until the recording ends. `IInputEventSink`, `IInputRecorder`, `IInputPlayback`, `IInputTrackerHook` and the `InputEvent` value type are public. A test records 100 frames of random input in `IonTestHost` and replays it into another host with identical `Pressed`/`Down` sequences, per frame and per fixed step.
* **Unboxed coroutine waits.** `Wait` is a readonly struct union (`Kind`, `Seconds`, and one reference for the predicate, a cached per-type event check, a nested routine or a custom `IWait`) copied into the coroutine's handle, and `IEnumerator<Wait>` coroutines are read without boxing. `Wait.For(IEnumerator)` nests routines, `Wait.For(IWait)` wraps custom waits, `Wait.None` resumes next frame, and floats and `TimeSpan`s convert implicitly. `CoroutineBenchmarks.Update100Coroutines`: 1.90 us and 2.34 KB per frame before, 765 ns and 0 B after (the non-generic `IEnumerator` form, kept as `Update100LegacyCoroutines`, is 1.83 us and 3.2 KB because the routine boxes each yielded struct).
* **Asset hot reload.** `IAssetWatcher` (`AssetWatcher`: a `FileSystemWatcher` over the assets root with a 100 ms settle time, or manual `Enqueue`), `AssetReloadSystem` (reloads every cached asset of a changed path in the global and scene caches, emits `AssetReloadedEvent(AssetId, PreviousAssetId)`), and `IReloadableAssetLoader` for in-place updates, implemented by the null texture and font loaders and by the Veldrid texture loader (pixels re-uploaded into the same device texture when the size is unchanged). Other assets are loaded again and swapped into the cache.

### 🐛 Fixes

* Null trace timer no longer allocates per `Start`; scenes call `next` in every stage, register per application and ignore unknown scene ids; `DrawString` defaults to white; `IFont.MeasureString` implemented; `Color(uint)` 4-digit parsing; input press and release in one frame and focus loss; texture loader and sprite renderer release their GPU resources; audio pitch shift and master volume; `Bonk.wav` casing in the Breakout sample.
* Generators pin Roslyn 4.4 so they load in every SDK from 8.0 onwards.
* Publishing a sample with `-p:PublishAot=true` no longer fails with `NETSDK1207` (generator projects ignore `PublishAot`).
* Veldrid's transitive `Newtonsoft.Json` 9.0.1 (GHSA-5crp-9r3c-p9vr) is lifted to 13.0.4.

### Other

* `UseDelegateServicesGenerator` (Ion.Core.InternalGenerators) and `UseDelegateServicesSceneGenerator` stay: they emit the public `Use{Stage}<TService...>` overloads, which the schedule generator intercepts but does not replace.
* New `Ion.Generators.Tests` (golden output, every diagnostic with its message checked against the runtime's, and each scenario run with and without the generator) and `Ion.Benchmarks.GeneratedApp` (the benchmark applications compiled with the generator; rows `Ion_GeneratedSchedule` and `Step_8Systems_GeneratedSchedule`).
* The Breakout ECS sample's setup moved to `BreakoutGame.Configure(builder)` / `BreakoutGame.Use(app)`; all its randomness is seeded from `Ion:Seed` (default 6014; the block tilt used an unseeded `Random`). New `Ion.Examples.Breakout.ECS.Tests` plays 600 headless frames with the autopilot and asserts launches, score, blocks, sprites and a frame-by-frame deterministic replay.
* The ECS sample uses Arch 2.1 (versioned `Entity` instead of `EntityReference`) and registers its component arrays so it runs under NativeAOT.
* CI builds and tests on Windows, macOS and Linux with the .NET 10 SDK, runs the benchmarks as a dry job and uploads the results, and publishes the ECS sample with NativeAOT, failing on trim/AOT warnings from Ion code.
* **Complete headless graphics backend** (`Ion.Extensions.Graphics.Null`): `NullWindow` (size from `WindowConfig`, `WindowResizeEvent` at Init, `Close()` emits `WindowClosedEvent` and ends the game), `NullInputState` with a scripting API (`Press`, `Release`, `Tap`, `Click`, `SetMousePosition`, `Scroll`, `ReleaseAll`, applied at the next First stage), `NullSpriteBatch` with per-frame `ISpriteBatchStats` (`DrawCalls`, `Sprites`, `Strings`, `Rects`, `Points`, `Lines`, last N `SpriteBatchCommand`s), `NullTexture2DLoader` (size from the image header, no GPU) and `NullFontLoader` (fonts that measure with a fixed glyph width). `UseNullGraphics()` adds the window, input and sprite batch systems.
* **Headless audio**: `AddNullAudio()` registers `NullAudioManager` (records every play in `Plays` with sound, volume and pitch, and mixes it on the null output) and `NullSoundEffectLoader` (reads the WAV header and decodes the samples).
* The Breakout samples load assets through the interfaces, and all three samples run headless with `--Ion:Headless=true`; the ECS sample then plays itself with scripted input and logs what the null backends recorded.
* New `Ion.Extensions.Graphics.Null.Tests` covering scripted input, sprite batch statistics, the null loaders, `NullWindow.Close()`, null audio and a headless `AddIon` integration test.

<a name="0.2.5"></a>
## [0.2.5](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.5) (2025-1-2)

* **Input v2** (`Ion.Core.Abstractions`): fixed-size input state in `ulong` bitsets indexed by `Key` (held, pressed and released, for both the per-frame and fixed-step views), 32-bit masks for mouse and gamepad buttons, mouse position and delta, wheel, per-frame text input (`IInputState.Text`, a `ReadOnlySpan<char>` fed from the Veldrid snapshot's character events), held `Modifiers`, and eight gamepad slots (`IGamepadState`: `Down`/`Up`/`Pressed`/`Released(GamepadButton)`, `Axis(GamepadAxis)`, `LeftStick`, `RightStick`) with a radial dead zone from `Ion:Input:GamepadDeadZone` (`InputConfig`, default 0.15). No allocation per frame (checked by a test). Modifier and focus-loss semantics are documented on `InputTracker`. The Veldrid backend exposes no gamepads (Veldrid's SDL2 snapshot has no controller events); the Silk.NET backend in Stage 4 will feed them. `NullInputState` scripts text (`Type`), key repeats (`Repeat`) and gamepads (`ConnectGamepad`, `DisconnectGamepad`, `Press`/`Release`/`Tap(pad, button)`, `SetAxis`, `SetLeftStick`, `SetRightStick`).
* **Recorded input.** `AddInputRecording(path)` records every frame's input events with `InputRecorder` into a compact binary stream (`InputRecordingFormat`: frame blocks of 7-bit encoded frame numbers and events, with an end marker); `AddInputPlayback(path)` replays it with `InputPlayer` at the recorded frame numbers into any `IInputEventSink`, replacing device input until the recording ends. `IInputEventSink`, `IInputRecorder`, `IInputPlayback`, `IInputTrackerHook` and the `InputEvent` value type are public. A test records 100 frames of random input in `IonTestHost` and replays it into another host with identical `Pressed`/`Down` sequences, per frame and per fixed step.
* **Unboxed coroutine waits.** `Wait` is a readonly struct union (`Kind`, `Seconds`, and one reference for the predicate, a cached per-type event check, a nested routine or a custom `IWait`) copied into the coroutine's handle, and `IEnumerator<Wait>` coroutines are read without boxing. `Wait.For(IEnumerator)` nests routines, `Wait.For(IWait)` wraps custom waits, `Wait.None` resumes next frame, and floats and `TimeSpan`s convert implicitly. `CoroutineBenchmarks.Update100Coroutines`: 1.90 us and 2.34 KB per frame before, 765 ns and 0 B after (the non-generic `IEnumerator` form, kept as `Update100LegacyCoroutines`, is 1.83 us and 3.2 KB because the routine boxes each yielded struct).
* **Asset hot reload.** `IAssetWatcher` (`AssetWatcher`: a `FileSystemWatcher` over the assets root with a 100 ms settle time, or manual `Enqueue`), `AssetReloadSystem` (reloads every cached asset of a changed path in the global and scene caches, emits `AssetReloadedEvent(AssetId, PreviousAssetId)`), and `IReloadableAssetLoader` for in-place updates, implemented by the null texture and font loaders and by the Veldrid texture loader (pixels re-uploaded into the same device texture when the size is unchanged). Other assets are loaded again and swapped into the cache.

### 🐛 Fixes

* Fixed generator references. ([716bc9a](https://www.github.com/jimbuck/Ion/commit/716bc9ae2fa5ecbd2e9b98af7c21ac3ac26075d5))

<a name="0.2.4"></a>
## [0.2.4](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.4) (2024-11-25)

### Other

* Create FUNDING.yml ([76b274d](https://www.github.com/jimbuck/Ion/commit/76b274dec2a81270b8ccc8ba80a8063922a8be78))

<a name="0.2.3"></a>
## [0.2.3](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.3) (2024-11-24)

### ✨ Features

* Initial ECS Graphics components (#46) ([69f1500](https://www.github.com/jimbuck/Ion/commit/69f1500985ee598584c8ed78578a862e07790846))

<a name="0.2.2"></a>
## [0.2.2](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.2) (2024-10-8)

### ✨ Features

* Scene generators (#44) ([88c6d6a](https://www.github.com/jimbuck/Ion/commit/88c6d6a85d6664ba8b6a2e2b096d32065fe52fba))

<a name="0.2.1"></a>
## [0.2.1](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.1) (2024-6-7)

### Other

* Codebase cleanup. ([b509ef6](https://www.github.com/jimbuck/Ion/commit/b509ef6a876caf9d5f47d77a4e989e814bf2930e))

<a name="0.2.0"></a>
## [0.2.0](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.0) (2024-6-5)

### Other

* Initial SDL3 and WGPU integration (hardcoded triangle). (#43) ([ae2c4c5](https://www.github.com/jimbuck/Ion/commit/ae2c4c5e10f1ef53aff9899469dafdb5d6c90e1e))

<a name="0.1.8"></a>
## [0.1.8](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.8) (2024-1-24)

### ✨ Features

* Sprite Font Support (#25) ([1f51e78](https://www.github.com/jimbuck/Ion/commit/1f51e7873ae688bc2efdbd98aade9f80eb9fccb4))

<a name="0.1.7"></a>
## [0.1.7](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.7) (2024-1-17)

### ✨ Features

* .NET 8 and Audio API (#22) ([f6068f6](https://www.github.com/jimbuck/Ion/commit/f6068f6feef9abe69ab2bd63b79305c652e35d3e))

<a name="0.1.6"></a>
## [0.1.6](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.6) (2024-1-16)

### ✨ Features

* Added basic asset loading (Texture2D only for now). (#21) ([2397ded](https://www.github.com/jimbuck/Ion/commit/2397ded3afe9de798d0caa992551c6739864618c))

<a name="0.1.5"></a>
## [0.1.5](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.5) (2024-1-10)

### ✨ Features

* Basic Texture2D Loading (#20) ([91a897e](https://www.github.com/jimbuck/Ion/commit/91a897eb750c519754811e6343e4fcbd063bafe4))

<a name="0.1.4"></a>
## [0.1.4](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.4) (2024-1-8)

### Other

* Renamed project to Ion. (#19) ([1a98ad2](https://www.github.com/jimbuck/Ion/commit/1a98ad2a890c726e9367b17e61bc77fbe7d373d0))

<a name="0.1.3"></a>
## [0.1.3](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.3) (2024-1-8)

### ✨ Features

* Middleware Architecture (#18) ([8781b04](https://www.github.com/jimbuck/Ion/commit/8781b04943ec067e9dcab80f5d3ba7c1ec1f1ad8))

<a name="0.1.2"></a>
## [0.1.2](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.2) (2023-4-19)

### ✨ Features

* Core Engine (#17) ([d9f1f98](https://www.github.com/jimbuck/Ion/commit/d9f1f98838d8c83ac9d6b5ce4be9abb41cec3dd4))

<a name="0.1.1"></a>
## [0.1.1](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.1) (2022-10-10)

### ✨ Features

* Added Entity methods. (#10) ([f003315](https://www.github.com/jimbuck/Ion/commit/f003315a11ae7ee5a5286fded3a7dac9c150f66e))

<a name="0.1.0"></a>
## [0.1.0](https://www.github.com/jimbuck/Ion/releases/tag/v0.1.0) (2022-10-9)

### 🛠 Internal

* Added main build & version action. (#3) ([1032b9a](https://www.github.com/jimbuck/Ion/commit/1032b9ae58905ae7084f12c0187ce7355e8b89b1))
* Fixed main action config. (#9) ([2333fd1](https://www.github.com/jimbuck/Ion/commit/2333fd1bebdd140753291edd279b89772a01f05b))
* Update changelog generation. (#5) ([9347ac7](https://www.github.com/jimbuck/Ion/commit/9347ac7a390615e9adcd1b36f18df38e7db21f98))
* Update changelog generation. (#6) ([62176fc](https://www.github.com/jimbuck/Ion/commit/62176fc3ce5d3600f0fbcc3458905c5c5b490907))
* Update changelog generation. (#7) ([5c9abc7](https://www.github.com/jimbuck/Ion/commit/5c9abc71ba6e5c342f6b61a83e9793a0781f1db1))
* Update changelog generation. (#8) ([b2f7e70](https://www.github.com/jimbuck/Ion/commit/b2f7e70249f2c736ca5af654b491278d5f603578))
* Updated main action order. ([04a82ec](https://www.github.com/jimbuck/Ion/commit/04a82ec861a96fe428fa5312eb3716c464881444))
* Updated versionize config/usage. (#4) ([5bac9ea](https://www.github.com/jimbuck/Ion/commit/5bac9eae2952f17ff5ac1184f7222b4f18f086d7))

### Other

* Fixed ComponentId generation issue. ([0347130](https://www.github.com/jimbuck/Ion/commit/034713074429e238cd841a5b5e1578080993b7b5))
* Initial commit ([b293ddb](https://www.github.com/jimbuck/Ion/commit/b293ddb273c4b44e7a1b893f1d2246954870d30c))
* Initial ECS implementation. ([0327e46](https://www.github.com/jimbuck/Ion/commit/0327e4608b65c37c5d7c1cc529dafc8d6c4b4474))
* Merge pull request #2 from jimbuck/feature/test-pr-build ([38f078d](https://www.github.com/jimbuck/Ion/commit/38f078d187c61759b45743601b568be0105d650b))
* Setup initial PR action. ([ae1e6a8](https://www.github.com/jimbuck/Ion/commit/ae1e6a851641da0090017e2dae7b2e52ef053445))
* Update main.yml ([078665e](https://www.github.com/jimbuck/Ion/commit/078665e4134071cf211e93919c6eec65e91cb2b6))
* Update README.md ([eee6be9](https://www.github.com/jimbuck/Ion/commit/eee6be957164dbadc0b68eb6f5c4a9d913152627))
* Update README.md ([1bc8961](https://www.github.com/jimbuck/Ion/commit/1bc8961212588331580eb49e62a0cfcfb939408a))
* Updated folder names, added vscode settings file. ([3f02e33](https://www.github.com/jimbuck/Ion/commit/3f02e33690773ffc41247a36965c7f0961399917))
* **internal:** Test commit. ([7490f74](https://www.github.com/jimbuck/Ion/commit/7490f74661b79d06f9fbc53b72514a0d67affad7))

