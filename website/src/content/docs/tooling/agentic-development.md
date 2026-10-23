---
title: Agentic development
description: Build Ion games with coding agents such as Claude Code, using the templates' CLAUDE.md, the ion CLI, the MCP server and headless tests in a closed loop.
sidebar:
  order: 7
---

Ion is designed so that a coding agent can create a game, change it, run it, look at it and check it without a human in
the loop and without reading the engine's source. Every piece of the loop gives a machine-readable answer: builds fail
with schedule diagnostics that state the rule, runs end with an exit code and a JSON summary, pictures are compared by a
tool, and a running game can be queried and changed over a protocol.

This page is the map. Each game made from a template carries the same workflow in its own `CLAUDE.md`.

## The toolchain

| Need | Tool | What it gives |
|---|---|---|
| Start a game | `ion new 2d\|3d\|ecs <Name>` (or `dotnet new ion-2d\|ion-3d\|ion-ecs`) | A game project, a test project with a headless test and a snapshot test, `CLAUDE.md`, `appsettings.json`. |
| Run it and get an answer | `ion run --headless --frames 600 --seed 1 --screenshot out/f.png --summary out/run.json` | An exit code (non-zero on an exception), a PNG of the last frame, a JSON summary. |
| Compare pictures | `ion diff out/f.png Golden/f.png` | Exit code 0 on a match, a diff PNG with mismatches in red. |
| See the order of things | `ion schedule` | Every stage's steps in run order, with orders and scopes. |
| Profile | `ion trace --frames 300 --out out/trace.json`, `ion bench <filter>` | A Chrome trace for Perfetto, BenchmarkDotNet results. |
| Inspect a running game | `ion mcp` (MCP server) or `ion remote <method> [params]` | Entities, components, resources, schedule, metrics, screenshots, input, pause and step. |
| Test | `dotnet test` with `Ion.Testing` | `IonTestHost.RunEntryPoint<Program>(frames)`, scripted input, JSON snapshots, golden images. |

Set it up once:

```bash
dotnet pack Ion/Ion.Tools -c Release -o out
dotnet tool install -g Ion.Tools --add-source out
claude mcp add ion -- ion mcp
```

See [The ion command line](/Ion/tooling/ion-cli/) and [MCP server](/Ion/tooling/mcp-server/).

## The loop

1. **Create.** `ion new ecs Arena` (add `--ion-source <path to an Ion checkout>` to build against the sources instead of
   the packages). Read `Arena/CLAUDE.md`.
2. **Change.** Systems are plain classes with stage attributes (`[Update] public void Move(GameTime dt)`), registered
   on the builder in `Program.cs` (`builder.AddSystem<T>()`) and added to the schedule there (`game.UseSystem<T>()`).
   ECS components are `record struct`s registered for serialization, which also makes them visible to snapshots and to
   the remote protocol.
3. **Build.** `dotnet build` must stay warning-free. Schedule mistakes are compile-time diagnostics (`ION001` to
   `ION014` for the schedule, `ION1xx` for events, `ION3xx` for queries) whose messages state the rule. See
   [Diagnostics](/Ion/reference/diagnostics/).
4. **Run.** `ion run --headless --frames 600 --seed 1 --summary out/run.json`. Headless runs use a fixed 60 Hz clock, so
   the same seed gives the same state and the same pixels. Read the summary: `status`, `exitCode`, `exception` (type,
   message and a stack trace naming the step), `errors`, `warnings`, `frameStats`, `counters`, `schedule`.
5. **Look.** Add `--screenshot out/frame.png` and compare with a golden image using `ion diff`. Headless rendering needs a
   Vulkan driver or EGL with OpenGL ES 3 (Mesa lavapipe or llvmpipe on Linux CI).
6. **Inspect and poke.** `ion_run` with `live=true` (MCP) starts the game with the remote protocol and pauses after N
   frames. Then `ion_query`, `ion_get`, `ion_mutate`, `ion_spawn`, `ion_input`, `ion_step`, `ion_screenshot`,
   `ion_metrics`, `ion_logs` and `ion_call` (any method; `rpc.discover` lists them). `ion_stop` ends it. Games with the UI
   module and `AddUiRemote()` are driven by path, without screenshots: `ion_ui_tree` lists the widgets and
   `ion_ui_click` clicks one.
7. **Lock it in.** Add a headless test (`IonTestHost.RunEntryPoint<Program>(frames)` runs `Program.cs` as it is; assert on
   state and counters) and a snapshot (`JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("world.json"))`).
   Accept intended changes with `ION_UPDATE_GOLDEN=1 dotnet test` and review the diff.

:::tip[Counters make runs self-describing]
Register an `IMetrics` counter for every behavior you care about (bounces, hits, spawns). It then shows up in the run
summary, in `ion_metrics`, and in `IonRunResult.Counters`, so the agent can check "did the new system run?" by reading a
number instead of a picture.
:::

## What CLAUDE.md contains

Each template (`2d`, `3d`, `ecs`) ships a `CLAUDE.md` at the solution root, written for an agent that has never seen
the engine. Its sections:

| Section | Content |
|---|---|
| Layout | What each file is for: `Program.cs` (the setup, kept between `CreateBuilder` and `game.Run()` so the schedule generator and the tests see it), `Game.cs` (settings, components, JSON metadata), the systems, `appsettings.json`, the test project. |
| The loop | The stages, how a system and a step look, which services can be injected, ordering (`Order`, `[After<T>]`, `[Before<T>]`, the engine bands below -500 and above 500), ECS queries and `Commands`, how to add a component and a system, no `async` in steps (`ION005`), seeding randomness from `Ion:Seed`. The 3D template covers the 3D renderer instead of ECS queries. |
| Commands | `dotnet build`, `dotnet test`, `ion run ...`, `ion diff`, `ion schedule`, `ion trace`, what the summary contains, and the same run without the tool. |
| Inspecting a running game | `ion mcp` and the main tools with example arguments; `ion remote` from a shell; where the token lives. |
| Tests | `RunEntryPoint`, `JsonSnapshot`, scripted input, golden images, `ION_UPDATE_GOLDEN`. |

Keep it up to date as the game grows: add the game's own conventions (its components, resources, counters and test
names) so the next session starts with them.

## A worked example: adding a system

From the acceptance scenario below, in an `ecs` game named `Arena`. The agent writes the system:

```csharp title="Arena/GravitySystem.cs"
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Metrics;

namespace Arena;

public sealed partial class GravitySystem(IMetrics metrics)
{
	private readonly MetricsCounter _pulls = metrics.Counter("gravity");

	[FixedUpdate(Order = -10), Query, All<Ball>]
	private void Pull(ref Velocity velocity, [Data] in float dt)
	{
		velocity.Y += 200f * dt;
		_pulls.Increment();
	}
}
```

registers it and adds it to the schedule:

```csharp title="Arena/Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon(graphics => graphics.ClearColor = new Color(0x1B, 0x26, 0x3B))
	.AddEcs()
	.AddEcsSerialization(components => components
		.AddUnmanaged("Velocity", GameJson.Default.Velocity)
		.AddTag<Ball>("Ball"))
	.AddSystem<BallSystem>()
	.AddSystem<GravitySystem>()
	.AddSystem<DrawSystem>();
builder.Services.AddSingleton(GameSettings.From(builder.Configuration));

using var game = builder.Build();
game.UseIon()
	.UseEcs()
	.UseSystem<BallSystem>()
	.UseSystem<GravitySystem>()
	.UseSystem<DrawSystem>();
game.Run();
```

then verifies it without a window:

```bash
ion run --headless --frames 600 --seed 1 --summary out/run.json
```

`out/run.json` has `"status": "ok"`, `"exitCode": 0` and `"counters": {"bounces": ..., "gravity": 4800}`: 8 balls times
600 fixed steps, so the new system ran on every ball every step.

## The run settings every game understands

`AddIon`/`UseIon` honour these in every game, so `dotnet run -- <settings>` works without the tool:

| Setting | Effect |
|---|---|
| `--headless` (`Ion:Headless=true`) | Null window, input and audio; no GPU needed. |
| `--headless-render` (`Ion:Headless:Render=true`) | Also render into an offscreen target (screenshots). |
| `Ion:Run:Frames=N` | Run N frames, then Destroy and exit. |
| `Ion:Run:FixedStep=true` | Deterministic clock (on by default when headless with `Ion:Run:Frames`). |
| `Ion:Run:Screenshot=path.png` | Write the last frame at the end of the run. |
| `Ion:Run:Summary=path.json` | Write the run summary at the end, or when an exception ends the run. |
| `Ion:Seed=N` | The seed; games read it with `IonRun.Seed(config)`. |
| `Ion:PrintSchedule=true` | Print the schedule when the loop is built. |
| `Ion:Metrics:FrameLog=path.jsonl` | One JSON line per frame (stats and counters). |
| `Ion:Metrics:Profiling=true`, `Ion:Metrics:TraceOutput=path.json` | A Chrome trace at shutdown. |
| `--remote`, `--remote-allow-mutations`, `--remote-stdio` | The [remote protocol](/Ion/tooling/remote-protocol/). |

## Safety

The remote protocol is what lets an agent change a running game, so it is locked down by default:

- off unless asked for (`--remote`), and compiled out of Release builds unless `IonRemote=true`;
- loopback only, with a per-run bearer token in an owner-only file (`<project>/.ion/run/remote.json`);
- writes only with `--remote-allow-mutations` (the MCP server's live mode grants them unless `allowMutations=false`);
- mutations are applied on the game thread at the end of a frame, idempotently per request id, and never while a scene
  loads.

Agents do not need network access beyond loopback, and nothing an agent does through the protocol survives the process.

## The acceptance scenario

The roadmap's Stage 6 acceptance criterion is that an agent with only the `ion` CLI and the MCP server can create a game
from the template, add a system, run 600 headless frames, take a screenshot, diff it against a golden image, inspect an
entity and mutate a component, without reading engine source. It is written out step by step in
[docs/agentic/acceptance.md](https://github.com/jimbuck/Ion/blob/main/docs/agentic/acceptance.md) and runs as a test:

```bash
ION_SLOW_TESTS=1 dotnet test Ion/Ion.Tools.Tests -c Release --filter AcceptanceScenario
```

(`Ion.Tools.Tests.TemplateTests.AcceptanceScenarioWithOnlyTheCliAndMcp` builds the engine from source into the new game,
so it is marked slow; the screenshot and diff steps are skipped without a Vulkan or EGL driver. `npm run test:templates`
runs every template test the same way.) The steps:

1. `ion new ecs Arena --ion-source /path/to/Ion`, then `cd Arena`.
2. Add `GravitySystem` as above.
3. `ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png --summary out/run.json`; the exit code is 0 and
   `counters.gravity` is 4800.
4. Accept the first screenshot as `Golden/frame600.png`, run again, and `ion diff out/again.png Golden/frame600.png`
   reports a match.
5. With MCP: `ion_run {"live": true, "frames": 600, "seed": 1}`, `ion_query {"with": ["Ball"]}`,
   `ion_get {"entity": "Ball0"}`, `ion_mutate` to move `Ball0` to `[100, 100]` and zero its velocity, `ion_step {"frames": 1}`,
   `ion_get` again (gravity pulled it down by `200 * (1/60)^2` pixels), `ion_screenshot`, `ion_stop`.

The same inspection from a shell, without MCP, from the game project's directory:

```bash
cd Arena/Arena   # the game project inside the solution directory
dotnet run -- --headless-render --remote-allow-mutations --Ion:Run:FixedStep=true --Ion:Remote:PauseAtFrame=600 &
ion remote world.get_components '{"entity":"Ball0"}'
ion remote world.mutate_components '{"entity":"Ball0","component":"Transform2D","path":"Position","value":[100,100]}'
ion remote game.step '{"frames":1}'
ion remote game.exit
```

## Tips for working with agents

- **Keep setup in `Program.cs`.** The schedule generator and `IonTestHost.UseEntryPoint<Program>()` both read it; setup
  hidden behind reflection or runtime conditions is invisible to both.
- **Make every behavior observable.** A counter per behavior, a remote resource for important state
  (`builder.Services.AddRemoteResource(...)`), `EntityName`s on entities the agent will look for.
- **Register components for serialization.** Unregistered components are invisible to `ion_query` and world snapshots.
- **Seed everything.** Use `IonRun.Seed(config)`; never `Random.Shared` or wall-clock time in gameplay.
- **Prefer state over pixels.** Queries, the UI tree and snapshots are exact; screenshots are for layout and rendering
  changes.
- **Commit goldens on purpose.** An agent may update snapshots with `ION_UPDATE_GOLDEN=1`, but a human (or the agent,
  explicitly) should review the diff before it lands.

## See also

- [Templates](/Ion/getting-started/templates/): what `ion new` creates.
- [The ion command line](/Ion/tooling/ion-cli/), [MCP server](/Ion/tooling/mcp-server/) and
  [Remote protocol](/Ion/tooling/remote-protocol/).
- [Testing](/Ion/tooling/testing/) and [Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/).
- [Source generators](/Ion/concepts/source-generators/): why the setup belongs in `Program.cs`.
- The agent map in the repository: [docs/agentic/README.md](https://github.com/jimbuck/Ion/blob/main/docs/agentic/README.md).
