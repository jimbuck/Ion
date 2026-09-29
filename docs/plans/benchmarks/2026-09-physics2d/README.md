# Stage 5b: 2D physics decision benchmark and physics module results (September 2026)

The roadmap (section 4.12, section 7 item 3) left the 2D physics library to a measurement: Box2D v3 through C bindings
against Aether.Physics2D, with 10,000 dynamic bodies, on x64 and arm64. This folder holds that benchmark, its results,
the replay runner used for the arm64 determinism check, and the module benchmarks of `Ion.Benchmarks`.

## Candidates found through the package proxy

| Package | What it is | arm64 natives | Notes |
|---|---|---|---|
| `Box2D.NET.Bindings.Release` 3.1.0 (BeanCheeseBurrito) | Generated P/Invoke bindings of the Box2D v3.1.0 C API (`DisableRuntimeMarshalling`, function pointers for callbacks) plus `Box2D.NET.Native.Release` | yes: win, osx, linux, android, ios for x64 and arm64 (and x86), shared and static libraries, a `Box2DStaticLink` MSBuild switch for NativeAOT | the picked one |
| `Box2dNet` 3.1.8.11 (thomasvt) | Thin wrapper over Box2D v3.1 | no: win-x64 and linux-x64 only | rejected: no arm64 |
| `Box2D.NET` 3.1.654 (ikpil) | A line-by-line C# port of Box2D v3.1 (pure managed) | not needed | measured as a third candidate |
| `Aether.Physics2D` 2.2.0 | Managed, Box2D 2.x lineage (Farseer), the sample's engine before this stage | not needed | measured |

`Box2D.NET.Release` (the same author's wrapper package) ships an empty `Box2D.NET.dll` that collides with ikpil's
`Box2D.NET` assembly name; the bindings package alone has everything.

## Method

`decision/` (not in `Ion.sln`): one scene per engine, a static box (floor and two walls) and N dynamic bodies (half 0.5 x
0.5 boxes, half circles of radius 0.25) in a grid above it, gravity -10, 600 steps of 1/60 s, one thread everywhere (Box2D
v3: 4 sub-steps, its default; Aether: 8 velocity and 3 position iterations, its defaults, multithreading thresholds off).
Printed per engine: setup, the total and per-step times (mean, p50, p95, max, the first and last 60 steps), the awake
bodies, and a hash of every body's position and rotation bits after the last step.

```sh
cd docs/plans/benchmarks/2026-09-physics2d/decision
dotnet publish -c Release -r linux-x64 -p:PublishAot=true -o /tmp/p2d-x64
/tmp/p2d-x64/Physics2DDecision 10000 600 box2d-native,box2d-managed,aether 2
# linux-arm64 (docs/platforms/r36s.md: clang, lld, the arm64 cross packages), run under QEMU user mode
dotnet publish -c Release -r linux-arm64 -p:PublishAot=true -p:LinkerFlavor=lld -o /tmp/p2d-arm64
qemu-aarch64-static -L /usr/aarch64-linux-gnu /tmp/p2d-arm64/Physics2DDecision 10000 600 box2d-native 1
```

Machine: the benchmark VM (Intel Xeon 2.1 GHz, 4 cores) shared with other build jobs (load average 5 to 20 during the
runs), so treat the absolute numbers as order of magnitude; the ratios were stable across repeats. arm64 is QEMU user
mode emulation (TCG) on the same VM: its times measure an emulator, not a Cortex-A35, and are only useful as ratios
between engines. Raw outputs are in `results/`.

## Results: 10,000 bodies, 600 steps, NativeAOT, one thread

| Engine | x64 mean ms/step | x64 p50 | x64 last 60 steps | arm64 (QEMU) p50 | x64 vs Box2D v3 | managed allocations per step (1,000 bodies, events on) | NativeAOT warnings |
|---|---:|---:|---:|---:|---:|---:|---:|
| Box2D v3.1 (C, `Box2D.NET.Bindings.Release`) | **14.6** | **15.0** | **13.2** | **358** | 1.0x | 0 (native arena) | 0 |
| Box2D.NET 3.1 (managed port) | 72.7 | 80.9 | 80.8 | 623 | 5.0x | about 8 KB (a class per new contact) | 0 |
| Aether.Physics2D 2.2.0 | 339.6 | 344.4 | 347.7 | 1,243 | 23x | about 13.5 KB (contacts, constraints, delegates) | 2 summary warnings (IL2104, IL3053; 9 individual ones in the ECS sample: its XML serializer) |

Determinism (hash after the 600 steps):

| Engine | linux-x64 | linux-arm64 | Same? |
|---|---|---|---|
| Box2D v3.1, packaged natives | `E6EB68195CC5DCD4` | `68F836FF638AE348` | no |
| Box2D v3.1, built with `-ffp-contract=off` (`native/build.sh`) | `E6EB68195CC5DCD4` | `E6EB68195CC5DCD4` | **yes** |
| Box2D.NET 3.1 (managed) | `21A1A5507E79D0C3` | `21A1A5507E79D0C3` | yes |
| Aether.Physics2D | `20678D5DDB712FF9` | `20678D5DDB712FF9` | yes |

The packaged arm64 library contains 1,562 fused multiply-add instructions (`aarch64-linux-gnu-objdump -d libbox2d.so`),
the x64 one none: the natives are built with Zig's clang, whose default contracts `a * b + c` into one rounding on arm64.
Box2D's own CMake passes `-ffp-contract=off` for exactly this reason (Erin Catto, "Determinism", box2d.org, August 2024).
`native/build.sh` builds Box2D v3.1.0 that way for linux-x64 and linux-arm64 (plus the inline helpers the bindings
call); linked through `-p:Box2DNativeDir=...`, arm64 reproduces the x64 hash bit for bit, and x64 is unchanged.

## Decision

**Box2D v3 (the C library through `Box2D.NET.Bindings.Release` 3.1.0).** It is 5x faster than the managed port and 23x
faster than Aether at 10,000 bodies on both architectures, allocates nothing on the managed heap, ships natives for every
primary target (including the static libraries NativeAOT links into a single executable, and arm64 for the R36S), and
publishes without warnings. Aether is out on speed (23x), allocations and its AOT warnings; the managed Box2D port is
the fallback if a target without natives is ever needed (browser WebAssembly): its API is the same C API, so the module's
backend could switch without changing its public surface. The one weakness, cross-architecture determinism, is a build
flag, not the engine: the packaged arm64 natives use FMA, and a contraction-free build fixes it (measured above). Until
Ion builds its own natives on CI for every RID, the replay golden is linux-x64 (see the design document).

## Replay (10,000 fixed steps of the seeded test scenes)

The three runs of `results/replay.txt` (in its order: x64, arm64 with the packaged natives, arm64 with the
contraction-free build). `replay/` compiles `Replay2D.cs` and `Replay3D.cs` from the test projects into a console app for the arm64 run (the test
projects cannot run under QEMU):

| Scene | linux-x64 JIT | linux-x64 NativeAOT | linux-arm64 NativeAOT (QEMU) |
|---|---|---|---|
| 2D, packaged Box2D natives | `7DF39E42CA6D46A6` | `7DF39E42CA6D46A6` | `5780ABC152A90013` |
| 2D, Box2D built with `-ffp-contract=off` | | | **`7DF39E42CA6D46A6`** (the x64 hash) |
| 3D, BepuPhysics, 8-lane `Vector<float>` (JIT on AVX2) | `0A315BD5EFFEF90B` | | |
| 3D, BepuPhysics, 4-lane `Vector<float>` | `0987708E02C11079` (`DOTNET_MaxVectorTBitWidth=128`) | `0987708E02C11079` | `0987708E02C11079` |

BepuPhysics has no native code and no contraction (RyuJIT and ILC never fuse separate multiplies and adds), but it
solves bodies in bundles of `Vector<float>.Count`, so the vector width is part of the result: 4 lanes on arm64 (NEON) and
under NativeAOT for x64 (whose default instruction set has no AVX), 8 under the JIT on an AVX2 machine. At equal width,
x64 and arm64 agree bit for bit.

## Module benchmarks (`Ion.Benchmarks`, `--job short`, JIT)

| Benchmark | Mean | Allocated |
|---|---:|---:|
| `Physics2DStepBenchmarks.Step`, 1,000 bodies (pile, sleeping off, with the ECS sync) | 0.77 ms | 0 B |
| `Physics2DStepBenchmarks.Step`, 10,000 bodies | 14.3 ms | 0 B |
| `Physics3DStepBenchmarks.Step`, 1,000 bodies (BepuPhysics, one thread) | 1.73 ms | 0 B |
| `PhysicsSyncBenchmarks`: Box2D step alone, 10,000 moving bodies without contacts | 0.71 ms | 0 B |
| same, with the ECS push and pull (`PhysicsWorld2D.Step`) | 1.39 ms (+0.68 ms, 68 ns per moved body) | 0 B |
| `PhysicsSyncBenchmarks`: Bepu step alone, 10,000 moving bodies | 0.97 ms | 0 B |
| same, with the ECS push and pull (`PhysicsWorld3D.Step`) | 1.37 ms (+0.40 ms, 40 ns per moved body) | 0 B |

The reports are the `Ion.Benchmarks.Physics*-report-github.md` files. The 2D adapter's share is two P/Invokes per moved
body for its velocities on top of the transform from Box2D's move events; the 3D adapter reads Bepu's memory directly.
