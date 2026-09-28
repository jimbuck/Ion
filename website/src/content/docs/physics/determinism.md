---
title: Determinism
description: What makes Ion's physics reproducible, how to check it with state hashes and replays, the cross-platform limits of Box2D and BepuPhysics, and remote inspection with Physics2DRemote.
sidebar:
  order: 6
---

Ion's physics is deterministic: the same scene, fed the same inputs, produces the same state bit for bit on every run on
the same platform. That is what makes input replays, golden tests, lockstep experiments and reliable bug reports possible.
Across processor architectures the guarantee depends on the engine and on how it was built; this page spells out exactly
where it holds.

## What the simulation depends on

Both physics worlds depend only on:

1. **The components**: `Collider*`, `RigidBody*`, `Joint*` and the transforms, as your code sets them.
2. **The fixed delta**: the physics step always uses `GameTime.Delta` of FixedUpdate (`1 / Ion:FixedUpdateRate`), never
   the frame's wall-clock time.
3. **The order entities are iterated in**: Arch's order, itself a function of the order entities were created and
   changed in.

Nothing reads the clock, a thread's timing or a random source. Box2D runs single-threaded. The 3D contact events are sorted
before they are emitted, and the 2D ones come out of Box2D in an order that depends only on the simulation.

The game loop feeds the fixed step from an accumulator: a slow frame runs several fixed steps, a fast one may run none,
but each step is the same length. So the simulation is identical whatever the frame rate, as long as the same inputs
reach the same fixed steps. See [time and determinism](/Ion/concepts/time-and-determinism/).

:::caution[What breaks determinism in your own code]
- Moving bodies with the frame delta (`Update`'s `dt.Delta`) instead of in FixedUpdate.
- Reading the wall clock, `Random.Shared`, or an unseeded `Random`. Seed your generators (the Breakout sample derives one
  per consumer from `Ion:Seed`).
- Creating entities in an order that depends on a `Dictionary` or `HashSet` enumeration, or on thread timing.
- Changing `Physics3DConfig.ThreadCount` between the recording and the replay.
:::

## State hashes

`IPhysicsWorld2D.ComputeStateHash()` and `IPhysicsWorld3D.ComputeStateHash()` return a 64-bit FNV-1a hash of every body's
position, rotation and velocities, read from the engine bit for bit, in body creation order. Two runs of the same scene
with the same inputs return the same hash; the smallest divergence changes it.

```csharp
public sealed class DesyncCheckSystem(IPhysicsWorld2D physics, ILogger<DesyncCheckSystem> logger)
{
	[FixedUpdate(Order = StageOrder.Physics + 1)]
	public void Check(GameTime dt)
	{
		// Log a checksum every second of simulation; compare the logs of two runs.
		if (physics.StepCount % 60 == 0) logger.LogInformation("step {Step}: {Hash:X16}", physics.StepCount, physics.ComputeStateHash());
	}
}
```

`StageOrder.Physics + 1` runs right after the physics step, before anything else changes the components.

## Replays

To replay a session, record what drives it (the input, or your own commands) per fixed step, and play it back into a
fresh run with the same seed and configuration. Ion's input recording does the first part; see
[recording and playback](/Ion/interaction/input/recording-and-playback/). The physics needs nothing special.

For a pure physics check, drive a `PhysicsWorld2D` or `PhysicsWorld3D` by hand without a game loop, the way the replay
tests do:

```csharp title="Replay2D.cs (simplified)"
public static ulong Run(int steps, int seed)
{
	using var world = World.Create();
	using var physics = new PhysicsWorld2D(world, events: null, new Physics2DConfig { GravityY = 9.81f });

	var rand = new Random(seed);
	for (var i = 0; i < 300; i++)
	{
		var position = new Vector2(rand.NextSingle() * 36 - 18, rand.NextSingle() * 20 - 13);
		world.Create(new Transform2D(position), Collider2D.Circle(0.3f), RigidBody2D.Dynamic());
	}

	for (var step = 0; step < steps; step++) physics.Step(1f / 60f);
	return physics.ComputeStateHash();
}
```

The repository's replay tests build a seeded scene (2D: 300 boxes, circles, capsules and polygons, a jointed chain, a
slider and a kinematic paddle; 3D: 200 boxes, spheres, capsules, cylinders and hulls, a chain on ball sockets, a hinged
door and a kinematic sweeper), run 10,000 fixed steps twice and assert identical hashes:

| Test | Asserts |
|---|---|
| `Replay2DTests.TenThousandStepsOfASeededSceneAreIdenticalOnEveryRun` | Same hash on repeated runs; on linux-x64 also equal to the golden `0x7DF39E42CA6D46A6`. |
| `Replay2DTests.DifferentSeedsGiveDifferentStates` | The hash actually depends on the scene. |
| `Replay3DTests.TenThousandStepsOfASeededSceneAreIdenticalOnEveryRun` | Same hash on repeated runs; on Linux also equal to the golden of the current vector width. |
| `Replay3DTests.AMultithreadedStepIsDeterministicToo` | Two 4-thread runs agree. |

The sources are
[Replay2D.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Physics2D.Tests/Replay2D.cs) and
[Replay3D.cs](https://github.com/jimbuck/Ion/blob/main/Ion/Ion.Extensions.Physics3D.Tests/Replay3D.cs). In a game test,
`IonTestHost` gives you the same control with a deterministic clock: `host.Step(n)` runs n frames of exactly one fixed
step each. See [testing](/Ion/tooling/testing/).

## Across platforms: what is guaranteed

| Scenario | 2D (Box2D) | 3D (BepuPhysics) |
|---|---|---|
| Repeated runs, same machine and build | Identical | Identical |
| JIT vs NativeAOT, same architecture | Identical (the linux-x64 golden is the same for both) | Identical at equal vector width (see below) |
| x64 vs arm64, packaged natives | **Differs** | Identical at equal vector width |
| x64 vs arm64, contraction-free Box2D build | Identical (measured) | n/a |
| 1 thread vs several threads | n/a (always one thread) | **Differs**; each thread count is reproducible |

The cross-architecture results were measured under QEMU user-mode emulation of linux-arm64, not on arm64 hardware.

### Box2D and fused multiply-add

The Box2D natives packaged with `Box2D.NET.Bindings.Release` 3.1.0 are **not** bit-identical between x64 and arm64: the
arm64 library is compiled with fused multiply-add contraction (1,562 FMA instructions: `a * b + c` rounded once instead
of twice), which Box2D's own CMake build disables with `-ffp-contract=off`. The x64 library contains none. After a few
steps the two diverge.

The same Box2D 3.1.0 built with `-ffp-contract=off` gives the x64 results on arm64 bit for bit, both at 10,000 bodies and
on the 10,000-step replay scene:

| 2D replay, 10,000 steps | linux-x64 (JIT and NativeAOT) | linux-arm64 NativeAOT |
|---|---|---|
| Packaged natives | `7DF39E42CA6D46A6` | `5780ABC152A90013` |
| Built with `-ffp-contract=off` | `7DF39E42CA6D46A6` | `7DF39E42CA6D46A6` |

That is why the 2D golden is linux-x64. To get cross-architecture lockstep today, build the contraction-free static
libraries with the repository's script and link them into your NativeAOT game:

```bash
# A Box2D checkout at tag v3.1.0; writes <out>/linux-x64/libbox2d.a and <out>/linux-arm64/libbox2d.a.
# The arm64 build needs clang, llvm-ar and the arm64 cross headers (libc6-dev-arm64-cross).
docs/plans/benchmarks/2026-09-physics2d/native/build.sh ~/src/box2d ~/box2d-noffc
```

```xml title="YourGame.csproj"
<ItemGroup Condition="'$(PublishAot)' == 'true'">
  <DirectPInvoke Include="box2d" />
  <NativeLibrary Include="$(HOME)/box2d-noffc/$(RuntimeIdentifier)/libbox2d.a" />
</ItemGroup>
```

The benchmark projects that do this set `<Box2DStaticLink>` only when they are not linking their own build, since that
switch links the packaged static library.

:::note[Planned]
Building contraction-free Box2D natives on CI for every runtime identifier, so cross-architecture lockstep works out of
the box, is an open item. The script covers linux-x64 and linux-arm64 only.
:::

### BepuPhysics and vector width

BepuPhysics has no native code, and neither RyuJIT nor NativeAOT fuses separate multiplies and adds. But Bepu solves
bodies in bundles of `Vector<float>.Count` lanes, so the vector width is part of the result:

| Vector width | Where you get it | 3D replay hash (10,000 steps) |
|---|---|---|
| 4 lanes (128-bit) | arm64 (NEON), NativeAOT on x64 (its default instruction set has no AVX), or the JIT with `DOTNET_MaxVectorTBitWidth=128` | `0x0987708E02C11079` |
| 8 lanes (256-bit) | the JIT on an AVX2 machine | `0x0A315BD5EFFEF90B` |

At equal width, x64 and arm64 agree bit for bit. A game that needs replays shared between x64 JIT builds and arm64 pins
the width to 128 bits:

```bash
DOTNET_MaxVectorTBitWidth=128 dotnet run
```

### Threads in 3D

`Physics3DConfig.ThreadCount` above 1 runs the Bepu step on worker threads in Bepu's deterministic mode, and the adapter
sorts the contact pairs before diffing them, so a multithreaded run is reproducible. It still differs from a
single-threaded run: a replay must use the same thread count as the recording. The frame never goes async: the step
blocks until the workers finish.

## Remote inspection (Physics2DRemote)

`Ion.Extensions.Physics2D.Remote` exposes the root 2D physics world on Ion's [remote protocol](/Ion/tooling/remote-protocol/),
so tools and coding agents can watch a running simulation without screenshots:

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddPhysics2DRemote()      // also registers AddPhysics2D, the ECS module and the engine core
	.AddSystem<GameSystem>();

using var game = builder.Build();
game.UsePhysics2D().UseSystem<GameSystem>();
game.Run();
```

`AddPhysics2DRemote()` registers the module without a configure callback; if you configure it in code, also call
`AddPhysics2D(configure)` (the registrations are idempotent and the callbacks add up). On an `IServiceCollection`, `services.AddPhysics2DRemote()` adds only the
methods. The methods answer when the game runs with `--remote`, and do nothing when the remote module is compiled out.

| Method | Parameters | Returns |
|---|---|---|
| `physics2d.bodies` (read, watchable) | `name?` (an `EntityName`, exact or a prefix ending in `*`), `type?` (`static`, `kinematic` or `dynamic`), `limit?` (default 1000) | `bodies` (entity, name, type, position, rotation, velocity, angularVelocity, shape, sensor, layer, simulated), `total`, `truncated`, `bodyCount`, `stepCount`, `gravity` |
| `physics2d.raycast` (read) | `origin` `[x, y]`, and `translation` `[dx, dy]` or `to` `[x, y]`, `mask?` | `hit`, and when it hits: entity, name, point, normal, fraction |

Both are read methods, so the read token is enough. They run on the game thread at the end of a frame and see the state of
the last fixed step. Scene physics worlds are not exposed, only the root one.

```bash
ion run --remote &
ion remote physics2d.bodies '{"type": "dynamic", "limit": 10}'
ion remote physics2d.raycast '{"origin": [0, 300], "to": [800, 300], "mask": 1}'
```

Give entities an `EntityName` component to make the output readable. The `physics2d.raycast` method goes through
`IPhysicsWorld2D.RayCast`, so it shares its [known issue with sensors](/Ion/physics/queries-and-events/). There is no 3D
remote module yet. See also the [ion CLI](/Ion/tooling/ion-cli/) and the [MCP server](/Ion/tooling/mcp-server/).

## Networking

Ion's multiplayer is server-authoritative: the server simulates physics and replicates the results, so clients do not
need bit-identical physics. The Breakout network sample runs the 2D module on the server and drives kinematic paddles
from predicted input before the physics step. See [prediction](/Ion/networking/multiplayer/prediction/) and the
[Breakout network example](/Ion/examples/breakout-net/).

## See also

- [Time and determinism](/Ion/concepts/time-and-determinism/).
- [Physics overview](/Ion/physics/overview/): the fixed step.
- [3D physics](/Ion/physics/physics-3d/): `ThreadCount`.
- [NativeAOT](/Ion/platforms/native-aot/) and [the R36S handheld](/Ion/platforms/r36s/) (linux-arm64).
- Measurements: [the physics benchmark report](https://github.com/jimbuck/Ion/blob/main/docs/plans/benchmarks/2026-09-physics2d/README.md)
  and the [design document](https://github.com/jimbuck/Ion/blob/main/docs/design/ion-physics.md).
