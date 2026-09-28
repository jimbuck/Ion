---
title: Source generators
description: What Ion's Roslyn source generators produce (the schedule, the event bus, ECS queries, network serializers, web routes), how interceptors wire them in, and how to keep your code on the generated path.
sidebar:
  order: 8
---

Ion does at compile time what engines usually do with reflection at startup. Roslyn source generators read your systems
and your `Program.cs`, emit plain C# that calls your methods directly, and report mistakes as compiler diagnostics. The
result is faster dispatch, no reflection on the hot path, NativeAOT-safe binaries, and stack traces that show your code.

The runtime path still exists and is the reference behavior: correctness never depends on the generator seeing
everything, only speed does.

## The generators

| Generator | Ships in | Emits | Diagnostics |
|---|---|---|---|
| Schedule generator (`Ion.Generators`) | `Ion`, `Ion.Core` packages (as an analyzer) | `IonSchedule.g.cs`: pre-bound system descriptions, a generated schedule per application and scene, registration summaries, interceptors | `ION001` to `ION014` |
| Event bus (same generator) | as above | `IonEvents.g.cs`: a closed, typed event bus for the application, compile-time event ids, `EventUsage` summaries, interceptors for `Emit` and `Reader` | `ION101` to `ION106` |
| ECS queries (same generator) | as above | `IonQueries.g.cs`: a chunk loop for every `[Query]` method, plus component registrations for NativeAOT | `ION301` to `ION307` |
| Public `Program` (same generator) | as above | `IonProgram.g.cs`: `public partial class Program`, so tests can name it | none |
| Networking (`Ion.Extensions.Networking.Generators`) | referenced as an analyzer | `IonNetworking.g.cs`: a `NetSerializer<T>` (full, delta, comparison, and a blend for interpolated components) for every `[Replicated]` component and `[NetworkMessage]` struct, and a registry whose hash both peers compare in the handshake | `ION201` to `ION210` |
| Web routes (`Ion.Extensions.Web.Generators`) | referenced as an analyzer | `IonWebRoutes.g.cs`: a static route table with a typed invoker per `[Http]` and `[WebSocket]` method | `ION401` to `ION407` |
| Scenes (`Ion.Extensions.Scenes.Generators`) | `Ion` | A marker attribute only (`ScenesEnumAttribute`). The scene enum overloads it used to generate are ordinary generic methods now (`UseScene<TScene>`, `EmitChangeScene<TScene>`), so the schedule generator can bind them. | none |

All generators target `netstandard2.0` on Roslyn 4.4, so they load in any compiler from the .NET 8 SDK onwards.

## Interceptors

The schedule and event generators work through **C# interceptors**: generated methods that replace specific calls in
your code at compile time. The generator intercepts `UseSystem`, function steps (`app.Update(...)` and the other
stages), legacy middleware (`app.UseUpdate(...)`), `UseScene`, `IonApplication.CreateBuilder` and `Build()`/`Run()`/
`RunFrames()`, as well as the game's own `IEvents.Emit` and `Reader` calls.

Interceptors need two things:

1. **The .NET SDK 9.0.200 or later** (Roslyn 4.12). Any .NET 10 SDK qualifies.
2. **The `Ion.Generated` namespace in `InterceptorsNamespaces`.** The `Ion` and `Ion.Core` packages add it through their
   build props. If you reference Ion by `ProjectReference`, add it yourself:

```xml title="MyGame.csproj"
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);Ion.Generated</InterceptorsNamespaces>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="path/to/Ion.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
</ItemGroup>
```

Without either, the generator reports **`ION014`** (warning, "Ion schedule generator is disabled") and the game runs on
the runtime path.

## The generated schedule

For each application (the registrations made on it before `Build()`/`Run()` in the same method) and each scene (its
configure callback), the generator emits one method per stage that calls every step directly, in plan order, with a
`try/finally` per scope. Systems and injected services are resolved once, in its constructor:

```csharp
// Generated for the Render stage of a small game (abridged):
public override void Render(global::Ion.GameTime dt)
{
    _b1(dt);                                // -850 NullSpriteBatchSystem.Begin
    try
    {
        _d7(dt);                            // -300 SpriteExtractionSystem.Extract
        _s9.RenderScore(dt);                // 0 ScoreSystem.RenderScore
        if (_g3) _d4(dt);                   // 900 NullWindowSystem.CheckClosed (registered under an if)
    }
    finally { _e1(dt); }
}
```

It is sorted by the same code as the runtime planner (`ScheduleSorter`, shared), so `PrintSchedule()` prints the same
thing either way.

### When the generated schedule is used

At `Build()`, the registrations actually made are aligned with the ones the generator saw (each carries its call site),
and the runtime plan is compared with the generated order. If they match, the generated schedule runs. If anything
differs, the runtime binds the plan itself (from pre-bound registrations where it has them) and logs why at `Debug`
level under `Ion.Schedule`. With `Debug` logging you see which path runs:

```text
[03:01:22] dbug: Ion.Schedule[0] Schedule 'root' runs the generated schedule of the application built at Program.cs:20.
```

In code, `loop.Schedule.IsGenerated` (or `host.Loop.Schedule.IsGenerated` in a test) tells you.

### What the generator can follow

| Pattern | Generated? |
|---|---|
| Registrations in `Program.cs` between `CreateBuilder` and `Run()` | Yes |
| Registrations in methods that take a builder or an application (`AddBreakout(builder)`, `UseBreakout(app)`), in this or another assembly compiled with the generator | Yes, through the registration summaries (`[assembly: ScheduleRegistrations]`) |
| Registrations under an `if` | Yes, included and guarded by the condition |
| Engine modules (`UseIon()`, `UseEcsRendering3D()`) | Yes, they are compiled with the generator |
| A system `Type` known only at run time (`UseSystem(type)` from a list, a plugin) | No: runtime path |
| Registrations in a loop, or through a callback the generator cannot follow | No: runtime path |
| Registrations whose arguments depend on another generator's output | No: generators cannot see each other's output |
| A helper assembly compiled without the generator | No: runtime path |

:::tip[Keep it generator-friendly]
Keep registrations in `Program.cs`, or in `AddX(IonApplicationBuilder)`/`UseX(IIonApplication)` methods it calls. Use
`UseSystem<T>()` with a type argument, not `UseSystem(typeof(T))` in a loop. Make `[Query]` systems `partial`. Keep event
readers in mutable fields. These are also the conventions the templates' `CLAUDE.md` teaches agents.
:::

### What changes for you

- **Stack traces.** Every generated type and the engine's dispatch methods are `[StackTraceHidden]`, so an exception
  thrown in a step shows your frames and `Program.<Main>$`, not the scheduler.
- **Speed.** Per-frame dispatch is direct calls. The README's measurement: 32 systems in a stage cost 15.7 ns, against
  13.3 ns for a hand-written loop of direct calls and 67 ns for the runtime-bound schedule.
- **NativeAOT.** Pre-bound registrations bind methods through generated delegates, so publishing needs no reflection
  over your systems.
- **Profiling.** The generated stage methods bracket every call with profiler timestamps behind the
  `Ion.Metrics.Profiling` feature switch. Set `<IonMetricsProfiling>false</IonMetricsProfiling>` in your project to emit
  no brackets at all. See [Metrics and tracing](/Ion/tooling/metrics-and-tracing/).

```text
System.InvalidOperationException: Thrown by a user step.
   at MyGame.ThrowingSystem.Render(GameTime dt) in .../Systems.cs:line 12
   at Program.<Main>$(String[] args) in .../Program.cs:line 30
```

## The generated event bus

The application's `CreateBuilder` call is intercepted to install a generated bus with one typed channel field per event
type the game uses, compile-time ids (`EventId<T>.Value`) and capacities chosen from how each type is emitted. The
game's own `Emit` and `Reader` calls go straight to those fields. Types the generator cannot see still work on the same
bus through the runtime channels. See [Events](/Ion/concepts/events/#the-generated-bus).

## Generated ECS queries

Every `[Query]` method of a `partial` system becomes a public hidden `__IonQuery_{Method}` method that loops over Arch's
chunks with no delegate and no boxing, and carries the original method's stage and ordering attributes so it prints and
orders as `System.Method`. Without the generator, a reflection binder runs the same method correctly but boxes every
component (28 times slower in the engine's benchmark). The generator also registers every query component with Arch for
NativeAOT. See [Queries](/Ion/ecs/queries/).

## Networking and web generators

These two are separate analyzers. The samples reference them by project:

```xml
<ProjectReference Include="path/to/Ion.Extensions.Networking.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
<ProjectReference Include="path/to/Ion.Extensions.Web.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

- The **networking generator** writes a serializer per replicated component and network message and registers them from
  a module initializer. Nothing is reflected, and both peers compare the registry hash in the handshake. See
  [Messages](/Ion/networking/multiplayer/messages/).
- The **web routes generator** turns `[Http("GET", "/score")]` and `[WebSocket("/events")]` methods into a static route
  table with typed parameter binding, registered from a module initializer. See
  [Web endpoints](/Ion/networking/web-endpoints/).

## Diagnostics overview

| Range | Category | Examples |
|---|---|---|
| `ION001` to `ION014` | Schedule | `ION002` ordering cycle, `ION005` async step, `ION009` unregistered system, `ION014` generator disabled |
| `ION101` to `ION106` | Events | `ION101` emitted but never read, `ION106` reader in a `readonly` field |
| `ION201` to `ION210` | Networking | `ION201` network type not unmanaged, `ION209` `[Predicted]` without `[Replicated]` |
| `ION301` to `ION307` | ECS queries | `ION301` query on a class that is not `partial`, `ION305` structural change without `Commands` |
| `ION401` to `ION407` | Web | `ION401` invalid route, `ION407` async endpoint |

The schedule diagnostics carry the same messages at compile time as at run time, at the offending method or registration
call. The [diagnostics reference](/Ion/reference/diagnostics/) lists every id with its message and fix.

`ION006`, `ION008` and `ION009` are reported at compile time only for types declared in the project that no service
registration call in the project mentions. If you register systems by assembly scanning, silence the compile-time
check in `.editorconfig` (the runtime check still applies):

```ini title=".editorconfig"
[*.cs]
dotnet_diagnostic.ION009.severity = none
```

## Seeing the generated code

Generated files are normal Roslyn output. In an IDE, look under the project's analyzers node; on disk, ask the compiler
to write them:

```xml
<PropertyGroup>
  <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
</PropertyGroup>
```

They appear under `obj/<configuration>/<tfm>/generated/`.

## See also

- [Systems](/Ion/concepts/systems/) and [Events](/Ion/concepts/events/): what the generators read.
- [Native AOT](/Ion/platforms/native-aot/): why reflection-free binding matters.
- [Diagnostics](/Ion/reference/diagnostics/): every id.
- [Benchmarks](/Ion/tooling/benchmarks/).
