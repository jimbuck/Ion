My Changelog
<a name="unreleased"></a>
## Unreleased

### ⚠ Breaking and behaviour changes

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

### 🐛 Fixes

* Null trace timer no longer allocates per `Start`; scenes call `next` in every stage, register per application and ignore unknown scene ids; `DrawString` defaults to white; `IFont.MeasureString` implemented; `Color(uint)` 4-digit parsing; input press and release in one frame and focus loss; texture loader and sprite renderer release their GPU resources; audio pitch shift and master volume; `Bonk.wav` casing in the Breakout sample.
* Generators pin Roslyn 4.4 so they load in every SDK from 8.0 onwards.
* Publishing a sample with `-p:PublishAot=true` no longer fails with `NETSDK1207` (generator projects ignore `PublishAot`).
* Veldrid's transitive `Newtonsoft.Json` 9.0.1 (GHSA-5crp-9r3c-p9vr) is lifted to 13.0.4.

### Other

* The ECS sample uses Arch 2.1 (versioned `Entity` instead of `EntityReference`) and registers its component arrays so it runs under NativeAOT.
* CI builds and tests on Windows, macOS and Linux with the .NET 10 SDK, runs the benchmarks as a dry job and uploads the results, and publishes the ECS sample with NativeAOT, failing on trim/AOT warnings from Ion code.

<a name="0.2.5"></a>
## [0.2.5](https://www.github.com/jimbuck/Ion/releases/tag/v0.2.5) (2025-1-2)

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

