---
title: NativeAOT and trimming
description: How Ion games publish as NativeAOT executables without trim or AOT warnings, the source generators and feature switches that make it possible, and the CI lane that keeps it that way.
sidebar:
  order: 6
---

Every Ion publishing preset is a **NativeAOT** publish: the IL compiler (ILC) compiles your game, the engine and their
dependencies ahead of time into one native executable, trimming everything that is never reached. NativeAOT gives you
a fast start (Breakout ECS reaches the end of its first headless frame in about 50 ms), a small binary (about 12 MB) and
no runtime to install, which matters most on small handhelds.

The catch with NativeAOT is reflection: code that discovers types or members at run time may find them trimmed away.
Ion is built so that a game's hot paths never need it, and its own code publishes with **zero trim and AOT warnings**.

## Publishing with NativeAOT

Use a preset (see [Publishing](/Ion/platforms/publishing/)):

```bash
dotnet publish MyGame -p:IonTarget=linux-x64
```

or set the properties yourself:

```xml title="MyGame.csproj"
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <!-- Keeps readable stack traces in NativeAOT builds. -->
  <IlcGenerateStackTraceData>true</IlcGenerateStackTraceData>
</PropertyGroup>
```

```bash
dotnet publish MyGame -c Release -r linux-x64
```

NativeAOT needs a native toolchain: `clang` and `zlib1g-dev` on Linux, the Visual Studio C++ build tools on Windows,
Xcode on macOS. It does not cross-compile between operating systems.

## Why Ion needs no reflection at run time

| Where reflection would be | What Ion does instead |
|---|---|
| Binding systems' `[Update]`, `[Render]`, ... methods | The **schedule generator** (`Ion.Generators`) describes every system at compile time with delegates that bind its methods directly, and emits a generated schedule that calls every step directly. |
| The event bus | The generator installs a generated bus: one typed channel field per event type, with a compile-time id. |
| ECS queries | Each `[Query]` method of a `partial` system becomes a generated chunk loop over Arch's chunks. |
| Arch's component arrays | Arch creates component arrays with `Array.CreateInstance` unless the array type is registered up front. The generator registers the components of every `[Query]`, the ECS module its built-ins, and you register the rest with `EcsComponents.Register<T>()`. |
| Web routes | The routing generator (`Ion.Extensions.Web.Generators`) turns `[Http]` and `[WebSocket]` methods into a static route table. |
| Network serialization | The networking generator writes a full and a delta serializer per `[Replicated]` component and `[NetworkMessage]` struct. |
| JSON | System.Text.Json source generation: `[WebJson(typeof(MyJsonContext))]` for endpoints, a `JsonTypeInfo<T>` per component for `AddEcsSerialization`. |
| Configuration binding | Engine libraries build with `EnableConfigurationBindingGenerator`, so `Ion:*` sections bind through generated code. |
| Windowing platform discovery | Silk.NET's platforms are registered explicitly (GLFW or SDL from `Ion:Window:Platform`), never discovered by reflection. |

The runtime path still exists and is the reference behaviour: without the generator, or for a registration the
generator could not follow (a `Type` known only at run time, a plugin), the runtime binds the schedule itself and logs
why at `Debug` level under `Ion.Schedule`. Correctness never depends on the generator; speed and reflection-freedom do.
See [Source generators](/Ion/concepts/source-generators/).

:::tip[Keep your registrations visible]
Keep `AddX`/`UseX`/`AddSystem`/`UseSystem` calls in `Program.cs` (or in methods it calls), between `CreateBuilder` and
`Run()`. The generator reads them there. A registration made in a loop, through a callback or from a `Type` variable
falls back to the runtime binder.
:::

### Registering component types

Components your game only creates (never names in a `[Query]` parameter) need registering for NativeAOT. Breakout ECS
does it at the start of `AddBreakout`:

```csharp title="BreakoutGame.cs"
public static void RegisterComponents()
{
	EcsComponents.Register<Block>();
	EcsComponents.Register<Paddle>();
	EcsComponents.Register<Ball>();
	EcsComponents.Register<Wall>();
}
```

`EcsComponents.Register<T>()` is safe to call more than once, from any thread. The networking generator registers
replicated components, and `AddPhysics2D` the physics ones.

## Engine libraries are AOT-compatible

The repository's `Directory.Build.props` classifies projects by name. Every engine library gets
`IsAotCompatible=true` (which turns on the trim, AOT and single-file analyzers while you build) and
`EnableConfigurationBindingGenerator=true`. Tests (`*.Tests`), benchmarks (`*.Benchmarks`), generators (`*Generators`)
and samples (`Ion.Examples*`) are excluded. A library can opt out by setting `IsAotCompatible` and
`EnableConfigurationBindingGenerator` to `false` in its project.

## Feature switches

Two engine features can be compiled out of a trimmed or NativeAOT build entirely:

| MSBuild property | Runtime switch | Default | Effect when `false` |
|---|---|---|---|
| `IonMetricsProfiling` | `Ion.Metrics.Profiling` | `true` | The schedule generator emits no profiler brackets, and ILC substitutes `FrameProfiler.IsProfilingEnabled` with `false` and removes every span recording site. Frame stats, the frame log, the meter and the overlay keep working. |
| `IonRemote` | `Ion.Remote.IsSupported` | `false` in Release, `true` otherwise | The remote inspection server, its transports and every remote method provider are removed. `AddRemote` registers nothing and says why on standard error. |

```xml title="MyGame.csproj"
<PropertyGroup>
  <IonMetricsProfiling>false</IonMetricsProfiling>
  <!-- Keep the remote protocol in Release for agents and end-to-end tests (it still only runs when asked). -->
  <IonRemote>true</IonRemote>
</PropertyGroup>
```

The Breakout sample sets `IonRemote=true` so agents can drive its Release build; the server still only runs with
`Ion:Remote:Enabled` (or `--remote`).

## Silk.NET substitutions

Silk.NET contains code paths Ion never runs: reflection-based platform discovery and deps.json native library probing.
On a NativeAOT publish, `Ion.Extensions.Windowing.SilkNet`'s `buildTransitive` targets pass ILLink substitution files to
ILC that stub those paths out, so they neither warn nor get compiled:

- `SilkNet.Core.ILLink.Substitutions.xml` when `Silk.NET.Core` is referenced (every Silk.NET module).
- `SilkNet.ILLink.Substitutions.xml` when the windowing module is referenced.

They ship in the package's `buildTransitive/` folder, so games that reference Ion through NuGet get them too. Opt out
with `<IonSilkNetSubstitutions>false</IonSilkNetSubstitutions>`.

## The warnings you will still see

After the substitutions, a publish shows exactly two third-party warnings, both from Silk.NET.Core's native library
loader:

| Warning | Where | Why it is harmless |
|---|---|---|
| `IL3000` | `Silk.NET.Core.Loader.DefaultPathResolver` | Reads `Assembly.Location` inside a try/catch as a fallback probe. Under NativeAOT the native libraries sit next to the executable and are found through `AppContext.BaseDirectory` first. |
| `IL3002` | `Silk.NET.Core.Loader.DefaultPathResolver` | Same lambda, `Assembly.CodeBase`. |

They live in a static resolver lambda that cannot be stubbed by signature safely, and it does nothing under NativeAOT.
The Menu sample, which does not pull in that path, publishes with none. **No warning comes from Ion.**

:::caution[Your own code]
Warnings from your game are real. The usual causes are reflection-based JSON (`JsonSerializer.Serialize(obj)` without
a `JsonTypeInfo`), `Activator.CreateInstance(type)`, `Type.GetType(name)`, `Enum.GetValues(Type)` and
`MakeGenericType`. Use source-generated JSON contexts, generic methods and compile-time registrations instead.
:::

## The CI AOT lane

The `aot` job of `.github/workflows/check-pr.yml` runs on every pull request, on `ubuntu-latest`:

1. Installs `clang lld llvm zlib1g-dev qemu-user-static` and builds the arm64 glibc 2.27 sysroot
   (`build/arm64-sysroot.py`).
2. Publishes Breakout ECS with the `linux-x64` and `r36s` presets through `measure.sh`, records sizes and startup
   (`r36s` under QEMU), and fails if a desktop executable passes 30 MB. The results table goes to the job summary and
   the `Stage7PublishResults` artifact (with the R36S ArkOS layout).
3. Publishes Quad, Menu, Companion and Breakout Net with `-p:IonTarget=linux-x64 -p:TrimmerSingleWarn=false`.
4. Collects every publish log into `aot-publish.log`, prints the warnings by source, and **fails if any warning's
   subject is Ion's own code**:

   ```bash
   if grep -E "warning IL[0-9]+: (Ion\.|Assembly 'Ion\.)" aot-publish.log; then
     echo "::error::NativeAOT publish produced trim/AOT warnings in Ion code."
     exit 1
   fi
   ```

5. Uploads the log as the `AotPublishLog` artifact.

`TrimmerSingleWarn=false` matters: in the default single-warning mode each assembly's warnings collapse to one
`IL2104` line, which would hide which member warned.

### Checking your own game the same way

```bash
dotnet publish MyGame -p:IonTarget=linux-x64 -p:TrimmerSingleWarn=false 2>&1 | tee publish.log
grep -E "warning IL[0-9]+:" publish.log | sort -u
```

Then run the published executable headless for a frame, which is a quick smoke test that nothing needed at startup was
trimmed:

```bash
./bin/Release/net10.0/linux-x64/publish/MyGame --Ion:Headless=true --Ion:Run:Frames=1
```

## Debugging a NativeAOT build

- **Stack traces** stay readable: the presets keep `IlcGenerateStackTraceData` and `StackTraceSupport` on (about 1 MB),
  and the schedule's generated types and dispatch methods are `[StackTraceHidden]`, so a trace through the game loop
  shows only your frames.
- **Symbols** are stripped into a `.dbg` (Linux) or `.dSYM` (macOS) file next to the executable. Keep it with the
  build; a native debugger or `llvm-symbolizer` uses it.
- **Traces and counters** on desktop: the presets keep `EventSourceSupport` on, so `dotnet-trace` and
  `dotnet-counters` (the `Ion` meter) attach to a NativeAOT desktop game. The handheld preset turns it off.
- **Reproduce first under JIT.** Most problems (a missing asset, a configuration mistake) reproduce with `dotnet run`,
  which is faster to iterate on.

## See also

- [Publishing](/Ion/platforms/publishing/): presets and the publish layout.
- [Source generators](/Ion/concepts/source-generators/): what the generators emit.
- [Diagnostics reference](/Ion/reference/diagnostics/): the `ION` diagnostics they report.
- [Metrics and tracing](/Ion/tooling/metrics-and-tracing/): profiling and the `Ion` meter.
