---
title: Diagnostics
description: Every ION diagnostic the schedule, event, query, networking and web generators and the runtime report, plus the IONPUB and IONMOB build errors, with severity, message, cause and fix.
sidebar:
  order: 2
---

Ion reports problems with stable ids. Most are reported twice: at **compile time** by a source generator (as compiler
errors, warnings or info in your IDE and build output), and at **run time** when the schedule is built
(`IonApplication.Build()` throws `IonScheduleException` listing every error; warnings are logged under the
`Ion.Schedule` category and kept on `SchedulePlan.Diagnostics`). The messages are the same in both places.

| Range | Area | Reported by | Category |
|---|---|---|---|
| `ION001` to `ION014` | The schedule: stages, ordering, scopes, signatures, services | `Ion.Generators` and the runtime planner | `Ion.Schedule` |
| `ION101` to `ION106` | Events | `Ion.Generators` (event analyzer) | `Ion.Events` |
| `ION201` to `ION210` | Networking | `Ion.Extensions.Networking.Generators` | `Ion.Networking` |
| `ION301` to `ION307` | ECS `[Query]` methods | `Ion.Generators` (query analyzer) | `Ion.Ecs` |
| `ION401` to `ION407` | Web routing | `Ion.Extensions.Web.Generators` | `Ion.Web` |
| `IONPUB001` to `IONPUB004`, `IONMOB001` | Publishing and mobile builds | MSBuild (`build/*.props`, `build/*.targets`) | |

Each descriptor carries a help link to the design document for its area.

## Changing a severity

Diagnostics are ordinary Roslyn diagnostics, so the usual controls work:

```ini title=".editorconfig"
[*.cs]
# This project registers its systems by assembly scanning, so the generator cannot see the registrations.
dotnet_diagnostic.ION009.severity = none
# Treat unread events as errors.
dotnet_diagnostic.ION101.severity = error
```

```csharp
#pragma warning disable ION105 // intentionally read a frame late
```

Turning off a compile-time check does not turn off the runtime check: a real schedule error still throws at
`Build()`.

## Schedule (ION001 to ION015)

The generator reports `ION001` to `ION013` at the offending method (or at the registration call) with the runtime's
message. `ION006`, `ION008` and `ION009` are reported at compile time only for types declared in the project that no
service registration call in the project mentions. `ION015` is reported at run time only (it depends on how a service
was registered, which the generator cannot always see).

| Id | Severity | Title |
|---|---|---|
| `ION001` | Error | Unknown stage |
| `ION002` | Error | Ordering cycle |
| `ION003` | Error | Scope without a matching end |
| `ION004` | Error | Step can never run |
| `ION005` | Error | Async step |
| `ION006` | Error | Scoped service in the root schedule |
| `ION007` | Error | Unsupported step signature |
| `ION008` | Error | Unregistered step parameter |
| `ION009` | Error | Unregistered system |
| `ION010` | Warning | Legacy middleware step |
| `ION011` | Error | Ambiguous scope |
| `ION012` | Warning | Constraint on a system that is not in the schedule |
| `ION013` | Warning | System without steps |
| `ION014` | Warning | Ion schedule generator is disabled |
| `ION015` | Error | Singleton instance used by a scene |

### ION001: Unknown stage

**Message:** `'{step}' names stage {n}, which is not a Stage (Init, First, FixedUpdate, Update, Render, Last, Destroy).`

**Cause:** a stage or scope attribute was given a value that is not a `Stage`, for example `[Begin((Stage)42)]`.
**Fix:** use one of the seven stages.

### ION002: Ordering cycle

**Message:** `The [Before]/[After] constraints in {stage} form a cycle: A.Step -> B.Step -> A.Step.`

**Cause:** `[Before<T>]`/`[After<T>]` constraints in one stage contradict each other. **Fix:** remove one of the
constraints in the printed cycle, or replace it with an `Order` value.

```csharp
[After<PhysicsSystem>] public sealed class PlayerSystem { [FixedUpdate] public void Move(GameTime dt) { } }
[Before<PlayerSystem>] public sealed class PhysicsSystem { [FixedUpdate] public void Step(GameTime dt) { } }
// Fine: Physics before Player. Adding [After<PlayerSystem>] to PhysicsSystem would be ION002.
```

### ION003: Scope without a matching end

**Message:** `{System}: [End] method 'X' has no matching [Begin(Render)].` (or the reverse, with the `ScopeName` when
there is one)

**Cause:** a system has a `[Begin(stage)]` without an `[End(stage)]` in the same stage and scope name, or the reverse.
**Fix:** add the missing half on the same system.

### ION004: Step can never run

**Messages:** `'{step}' has a stage or scope attribute but is not public, so it can never run. Make it public.`, or
`'{step}' was registered on schedule '{name}' after it was built, so it can never run. Register steps inside the configure callback.`

**Cause:** a stage attribute on a private or internal method (outside a generated `[Query]`), or a step added to a
scene's schedule after the scene loaded. **Fix:** make the method public; register scene steps inside the `UseScene`
callback.

### ION005: Async step

**Message:** `'{step}' in {stage} is async or returns Task. Steps are synchronous: return void, and start background work from the step instead.`

**Cause:** a step or scope method is `async` or returns `Task`/`ValueTask`. **Fix:** return `void`. Start background
work from the step and poll or read an event when it completes, or use a [coroutine](/Ion/ecs/coroutines/).

### ION006: Scoped service in the root schedule

**Messages:** `System 'X' is registered as scoped but is used by the root schedule, which resolves from the root provider. Register it as a singleton, or use it inside a scene (UseScene).`,
or `'{step}' injects scoped service 'Y' into a step of the root schedule, ...`

**Cause:** the root schedule resolves from the root provider, where scoped services are not available. **Fix:**
register the system as a singleton (`builder.AddSystem<T>()` does), or move it into a scene, whose systems resolve from
the scene's scope.

### ION007: Unsupported step signature

**Message:** `'{step}' in {stage} has an unsupported signature: {reason}. Use void M(GameTime dt) (extra parameters are injected services).`

**Cause:** the method's parameters or return type do not fit a step: the first parameter must be `GameTime`, further
parameters are services, and the return type is `void`. Scope methods take only `GameTime`. **Fix:** change the
signature as the message says.

### ION008: Unregistered step parameter

**Message:** `'{step}' injects 'IFoo', which is not registered in the service collection.`

**Cause:** a step parameter after `GameTime` names a service nobody registered. **Fix:** register the service, or
remove the parameter.

### ION009: Unregistered system

**Message:** `System 'X' is not registered in the service collection. Register it (for example services.AddSingleton<X>()) before building.`

**Cause:** `UseSystem<X>()` without a registration. **Fix:** `builder.AddSystem<X>()`. If your project registers
systems by scanning, set `dotnet_diagnostic.ION009.severity = none`; the runtime check still applies.

### ION010: Legacy middleware step

**Messages:** `'{step}' in {stage} uses the legacy middleware form (GameLoopDelegate next). Rewrite it as a leaf step ...`,
or `'{name}' in {stage} is a legacy middleware delegate (next => dt => ...). Rewrite it as a function step ...`

**Cause:** a method of the form `void M(GameTime dt, GameLoopDelegate next)` or `GameLoopDelegate M(GameLoopDelegate next)`,
or an `app.UseUpdate(next => dt => ...)` delegate. They still work for one release as opaque middleware. **Fix:** make
it a plain step without `next`, and move code that ran after `next(dt)` into a later step or a `[Begin]`/`[End]` scope.

```csharp
// Before (ION010)
public void Update(GameTime dt, GameLoopDelegate next) { Before(); next(dt); After(); }

// After
[Begin(Stage.Update)] public void Before(GameTime dt) { }
[End(Stage.Update)] public void After(GameTime dt) { }
```

### ION011: Ambiguous scope

**Messages:** `{System} has 2 [Begin] and 2 [End] methods (...). Give each pair a ScopeName.`, or
`{System}: [End] 'X' has Order 5 but [Begin] 'Y' has Order 0. A scope has one order; set the same value or omit it on [End].`

**Fix:** set `ScopeName` on each `[Begin]`/`[End]` pair when a system has several scopes in one stage, and give a pair
one `Order` (or omit it on `[End]`).

### ION012: Constraint on a system that is not in the schedule

**Message:** `'{step}' is ordered relative to 'X', which is not a system of schedule '{name}'; the constraint has no effect.`

**Cause:** `[After<X>]`/`[Before<X>]` names a system with no step in that stage of the schedule. **Fix:** add the
system, or remove the constraint.

### ION013: System without steps

**Message:** `System 'X' has no public method with a stage attribute ([Init], [Update], ...) or [Begin]/[End], so it never runs.`

**Fix:** add a stage attribute to a public method, or stop adding the class with `UseSystem`.

### ION014: Ion schedule generator is disabled

**Messages:** `Interceptors are not enabled for the Ion.Generated namespace; add <InterceptorsNamespaces>$(InterceptorsNamespaces);Ion.Generated</InterceptorsNamespaces> to the project (the Ion package does this). The Ion schedule is bound by reflection at run time.`,
or `The compiler does not support interceptor locations (Roslyn 4.12 or later, .NET SDK 9.0.200 or later, is required); ...`

**Cause:** the generator needs C# interceptors. **Fix:** reference Ion through the `Ion` or `Ion.Core` package (whose
build props enable the namespace), or add the property yourself when you reference the generator as a project; build
with the .NET SDK 9.0.200 or later. The game still runs, on the runtime path.

### ION015: Singleton instance used by a scene

**Message:** `System 'X' is registered as a singleton instance but is used by schedule 'Scene 1'. A scene creates its systems from its own scope (so they get the scene's World and scoped services), which an instance cannot be. Register the type instead (builder.AddSystem<X>(), AddScoped or AddTransient) or a factory.`

**Cause:** a scene uses a system registered with `services.AddSingleton(new X(...))`. Scenes create their systems from
the scene's scope, once per load, including systems registered as singletons by type or factory; an instance built
before the application started cannot be. Reported at run time, when `Build()` plans the scenes. **Fix:** register the
type (`builder.AddSystem<X>()`) or a factory (`AddSingleton(sp => new X(...))`, which the scene calls with its own
provider). See [Scenes](/Ion/ecs/scenes/#scene-systems-and-registration-lifetimes).

## Events (ION101 to ION106)

| Id | Severity | Title | Reported when |
|---|---|---|---|
| `ION101` | Warning | Event emitted but never read | An event type is emitted but nothing in the application or the Ion assemblies it references reads it. |
| `ION102` | Warning | Event read but never emitted | An event type is read but nothing emits it. |
| `ION103` | Warning | Event reader created in a stage method | A reader is created inside a per-frame stage method. |
| `ION104` | Error | Event payload is not unmanaged | An event type is not an unmanaged struct (the message names the offending field). |
| `ION105` | Info | Event read in an earlier stage than it is emitted | A reader reads in an earlier stage than the only stages that emit it, so it sees each event a frame late. |
| `ION106` | Warning | Event reader in a readonly field or a property | Reads advance a copy of the reader, so it never moves on. |

**ION101 fix:** read it (`events.Reader<T>()` created in a constructor), or stop emitting it. **ION102 fix:** emit it,
or remove the reader. Methods that emit or read for their caller (such as `EmitChangeScene`, `Wait.For<T>()` or
`IonTestHost.Collect<T>()`) are marked `[EmitsEvent]`/`[ReadsEvent]`, and libraries compiled with the generator publish
an `[assembly: EventUsage(...)]` summary, so their call sites count.

**ION103:** a new reader starts at the oldest visible event, so it sees the previous frame's events again every time.
Create it once and keep it:

```csharp
public sealed class ScoreSystem(IEvents events)
{
	private EventReader<BlockHitEvent> _hits = events.Reader<BlockHitEvent>();   // once, not readonly

	[Update] public void Tally(GameTime dt) => Score += _hits.Read().Length;
	public int Score { get; private set; }
}
```

**ION104:** events are stored unboxed in typed channels. Use ids, indices or handles (such as an `Entity`) instead of
references, strings or arrays.

**ION105:** informational. If the latency is not intended, read it in a later stage or emit it earlier.

**ION106:** remove `readonly` from the field, and never expose a reader as a property.

## Networking (ION201 to ION210)

| Id | Severity | Title | Message |
|---|---|---|---|
| `ION201` | Error | Network type is not unmanaged | `'{0}' is a {1} but is not unmanaged: replicated components and network messages cannot hold references (use FixedString32/64/128 for text)` |
| `ION202` | Error | Predicted component without owner authority | `'{0}' is [Predicted] but its authority is Server: a predicted component must be [Replicated(Authority = Authority.Owner)]` |
| `ION203` | Error | Replicated component too large | `'{0}' serializes to up to {1} bytes, more than the {2} bytes one entity's component may use in a snapshot part` (the limit is 1024 bytes) |
| `ION204` | Warning | Replicated type not used as an ECS component | `'{0}' is [Replicated] but is never used as an ECS component in this project (World.Create/Add/Set/Get, Commands, [Query] parameters): it will not be replicated` |
| `ION205` | Error | Member cannot be serialized | `Member '{1}' of '{0}' cannot be serialized: {2}` |
| `ION206` | Warning | Network reader created in a stage method | `NetworkReader<{0}> is created in the stage method '{1}': it starts from the oldest visible message on every call and reads messages again; create it once in the constructor or a field initializer` |
| `ION207` | Warning | Network message sent but never read | `Network message '{0}' is sent but never read (no NetworkReader<{0}> or prediction registration)` |
| `ION208` | Warning | Network message read but never sent | `Network message '{0}' is read but never sent` |
| `ION209` | Error | Predicted or Interpolated without Replicated | `'{0}' is [{1}] but not [Replicated]` |
| `ION210` | Warning | Entity handle in a network type | `Member '{1}' of '{0}' is an Entity, whose handle differs between processes: it is not sent (use NetworkId to refer to another networked entity)` |

`ION205` reasons include: generic structs; fixed-size buffers (use `FixedString32/64/128` or separate members); members
not writable from the assembly (make it writable or mark it `[NetworkIgnore]`); more than 64 serialized members (the
change mask is 64 bits: group members into nested structs); reference types; unsupported types (supported are
primitives, enums, `System.Numerics` vectors and quaternions, `FixedString32/64/128`, `NetworkId`, or structs of those);
and structs nested too deeply.

```csharp
[Replicated(Authority = Authority.Owner), Predicted]      // not ION202: owner authority
public record struct PaddleControl(float X);

[Replicated]
public record struct Nameplate(FixedString32 Name);        // not ION201: a fixed-size string, not a string
```

See [Multiplayer](/Ion/networking/multiplayer/overview/).

## ECS queries (ION301 to ION307)

| Id | Severity | Title | Example message |
|---|---|---|---|
| `ION301` | Error | Query on a class that is not partial | `'Move' has [Query] but its class 'MoveSystem' is not partial, so the generator cannot add the query loop. Declare 'MoveSystem' partial.` |
| `ION302` | Error | Query component parameter passed by value | `Component parameter 'velocity' (Velocity) of 'Move' is passed by value, so changes would be lost and the component copied; pass it by ref (or in, to read it).` |
| `ION303` | Error | Query component is not a struct | `Parameter 'x' of 'Move' is a Foo, which is not a struct; query components are structs (read a class component through the Entity).` Also for `All<T>`/`Any<T>`/`None<T>` naming a class. |
| `ION304` | Error | Query component both required and excluded | `Component Frozen of 'Move' is both required (a parameter or All) and excluded (None), so the query matches no entity.` |
| `ION305` | Error | Structural change inside a query without Commands | `'Move' calls World.Destroy inside its query, a structural change that invalidates the chunks being iterated. Add a Commands parameter and record it there (commands.Destroy); ...` |
| `ION306` | Error | Unsupported query method | Returns a value, is generic or async, has an `out` parameter, a `[Data]` parameter that is not `float` or `GameTime`, a parameter that is not a component or a supported type, or the same component twice. |
| `ION307` | Warning | Query without a stage | `'Move' has [Query] but no stage attribute ([Update], [FixedUpdate], ...), so it never runs.` |

Supported query parameters are components (`ref T` to write, `in T` to read), `Entity`, `[Data] in float` (the delta
time), `GameTime`, `Commands` and `World`. Services go in the system's constructor.

```csharp
public sealed partial class MoveSystem                                 // partial: not ION301
{
	[Update, Query, None<Frozen>]                                       // a stage: not ION307
	private void Move(Entity entity, ref Transform2D transform, in Velocity velocity, [Data] in float dt, Commands commands)
	{
		transform.Position += velocity.Value * dt;
		if (transform.Position.X > 2000) commands.Destroy(entity);    // through Commands: not ION305
	}
}
```

At run time, a structural change inside a query also throws `StructuralChangeException` naming the step and the entity
(`[Query(Unchecked = true)]` drops that check). See [Queries](/Ion/ecs/queries/).

## Web routing (ION401 to ION407)

| Id | Severity | Title | Reported when |
|---|---|---|---|
| `ION401` | Error | Invalid route | An unsupported HTTP method (supported: GET, HEAD, POST, PUT, PATCH, DELETE), an invalid route template or WebSocket path, or a route parameter with no method parameter of that name. |
| `ION402` | Error | Duplicate route | Two endpoints match the same method and path shape, or two WebSocket endpoints share a path (compared case-insensitively). |
| `ION403` | Error | Unsupported endpoint method | Static, generic, in a generic type, not public or internal (or in a type that is not), returns by reference, or a WebSocket method that is not `void M(in WebSocketMessage message)`. |
| `ION404` | Error | Unsupported endpoint parameter | A parameter that cannot be bound: `WebRequest` not by value or `in`, `WebResponse` not by value or `ref`, another parameter by reference, a second `[FromBody]`, or a route or query value of a type other than string, bool, a number type or `Guid`. |
| `ION405` | Error | Unsupported body or result type | A body or result that needs a `JsonSerializerContext` and has none, a type that cannot be read or written at all, or a JSON context that is not a `JsonSerializerContext`. |
| `ION406` | Warning | Type missing from the JSON context | The context has no `[JsonSerializable(typeof(T))]` for a type an endpoint needs; the endpoint fails at run time until it is added. |
| `ION407` | Error | Async endpoint | A handler returns a task: handlers run synchronously on the game thread at the end of a frame. |

```csharp
[JsonSerializable(typeof(ScoreInfo))]
public sealed partial class GameJson : JsonSerializerContext;         // not ION406

[WebJson(typeof(GameJson))]                                            // not ION405
public sealed class ScoreEndpoints
{
	[Http("GET", "/score")] public ScoreInfo Score() => new(10, 3);     // synchronous: not ION407
	[Http("POST", "/players/{id}/name")] public void Rename(int id, [FromBody] string name) { }   // {id} bound: not ION401
}

public readonly record struct ScoreInfo(int Score, int Lives);
```

See [Web endpoints](/Ion/networking/web-endpoints/).

## Build errors (IONPUB and IONMOB)

| Id | Severity | When | Fix |
|---|---|---|---|
| `IONPUB001` | Error | `Unknown IonTarget '{name}'.` | Use `win-x64`, `win-arm64`, `osx-arm64`, `osx-x64`, `linux-x64`, `linux-arm64` or `r36s`. |
| `IONPUB002` | Error | `IonTarget applies to executable projects; {project} has OutputType '{type}'.` | Publish the game project, not a library or a solution. |
| `IONPUB003` | Warning | Cross-compiling for `r36s` without `IonArm64SysRoot`: the executable needs the build machine's glibc and may not start on ArkOS (glibc 2.30). | Build the glibc 2.27 sysroot with `build/arm64-sysroot.py` and pass it. |
| `IONPUB004` | Error | The ArkOS layout needs the published executable. | Publish with `-p:IonTarget=r36s` first. |
| `IONMOB001` | Error | `IonMobileHeads=true, but the {android or ios} workload is not installed.` | `dotnet workload install android` (or `ios`), or build without `IonMobileHeads`. |

See [Publishing](/Ion/platforms/publishing/) and [Mobile](/Ion/platforms/mobile/).

## See also

- [Source generators](/Ion/concepts/source-generators/).
- [Systems](/Ion/concepts/systems/) and [Events](/Ion/concepts/events/).
- [Stage order](/Ion/reference/stage-order/).
