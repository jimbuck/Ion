# Working on Ion games as an agent

Ion is built so that a coding agent can create a game, change it, run it, look at it and check it without a human in the
loop and without reading the engine's source. This page is the map; each game made from a template also has a
`CLAUDE.md` with the same workflow in its own terms.

## The toolchain

| Need | Tool | What it gives |
|---|---|---|
| Start a game | `ion new 2d\|3d\|ecs <Name>` (or `dotnet new ion-2d\|ion-3d\|ion-ecs` with the `Ion.Templates` pack) | A game project, a test project with a headless test and a snapshot test, `CLAUDE.md`, `appsettings.json` |
| Run it and get an answer | `ion run --headless --frames 600 --seed 1 --screenshot out/f.png --summary out/run.json` | Exit code (non-zero on an exception), a PNG of the last frame, a JSON summary |
| Compare pictures | `ion diff out/f.png Golden/f.png [--tolerance 2] [--max-ratio 0]` | Exit code 0 when they match, a diff PNG (mismatches in red) |
| See the order of things | `ion schedule` | Every stage's steps in run order, orders and scopes |
| Profile | `ion trace --frames 300 --out out/trace.json`, `ion bench <filter>` | A Chrome trace (Perfetto), BenchmarkDotNet results |
| Inspect a running game | `ion mcp` (MCP server) or `ion remote <method> [params]` | The remote protocol: entities, components, resources, schedule, metrics, screenshots, input, pause and step |
| Test | `dotnet test`; `Ion.Testing` | `IonTestHost.Run<TGame>(frames)`, scripted input, golden images, JSON snapshots |

Install the tool from a checkout with `dotnet pack Ion/Ion.Tools -c Release -o out && dotnet tool install -g Ion.Tools --add-source out`
(or run it in place: `dotnet Ion/Ion.Tools/bin/Release/net10.0/Ion.Tools.dll`). Register the MCP server with Claude Code:
`claude mcp add ion -- ion mcp`.

## The loop of work

1. **Create**: `ion new ecs Arena` (add `--ion-source <path to an Ion checkout>` to build against the sources instead of
   the packages). Read `Arena/CLAUDE.md`.
2. **Change**: systems are plain classes with stage attributes (`[Update] public void Move(GameTime dt)`), registered in
   `Game.Configure` and added in `Game.Use`. ECS components are `record struct`s registered for serialization, which also
   makes them visible to snapshots and to the remote protocol.
3. **Build**: `dotnet build` must stay warning-free. Schedule mistakes are compile-time diagnostics (`ION001` to `ION013`,
   `ION3xx` for queries) with the rule in the message.
4. **Run**: `ion run --headless --frames 600 --seed 1 --summary out/run.json`. Headless runs use a fixed 60 Hz clock, so
   the same seed gives the same state and the same pixels. Read the summary: `status`, `exitCode`, `exception` (type,
   message, stack trace naming the step), `errors`, `warnings`, `frameStats`, `counters`, `schedule`.
5. **Look**: add `--screenshot out/frame.png` (headless rendering needs a Vulkan or EGL driver; Mesa lavapipe on Linux
   CI) and compare with a golden image using `ion diff`.
6. **Inspect and poke**: `ion_run` with `live=true` (MCP) starts the game with the remote protocol and pauses after N
   frames. Then `ion_query`, `ion_get`, `ion_mutate`, `ion_spawn`, `ion_input`, `ion_step`, `ion_screenshot`,
   `ion_metrics`, `ion_logs`, `ion_call` (any method; `rpc.discover` lists them). `ion_stop` ends it. Games with the UI
   module and `AddUiRemote()` are driven by path without screenshots: `ion_ui_tree` lists the widgets, `ion_ui_click`
   clicks one, and `ion_call` reaches `ui.set_value`, `ui.type`, `ui.focus` and `ui.back`.
7. **Lock it in**: add a headless test (`IonTestHost.Run<Game>(frames)`, assert on state and counters) and a snapshot
   (`JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("world.json"))`). Accept intended
   changes with `ION_UPDATE_GOLDEN=1 dotnet test` and review the diff.

## Run settings every game understands

`AddIon`/`UseIon` honour these in every game, so `dotnet run -- <settings>` works without the tool:

| Setting | Effect |
|---|---|
| `--headless` (`Ion:Headless=true`) | Null window, input and audio; no GPU needed. |
| `--headless-render` (`Ion:Headless:Render=true`) | Also render into an offscreen target (screenshots). |
| `Ion:Run:Frames=N` | Run N frames, then Destroy and exit. |
| `Ion:Run:FixedStep=true` | Deterministic clock (on by default when headless with `Ion:Run:Frames`). |
| `Ion:Run:Screenshot=path.png` | Write the last frame at the end of the run. |
| `Ion:Run:Summary=path.json` | Write the run summary at the end, or when an exception ends the run. |
| `Ion:Seed=N` | The seed; games read it (`IonRun.Seed(config)`). |
| `Ion:PrintSchedule=true` | Print the schedule at startup. |
| `Ion:Metrics:FrameLog=path.jsonl` | One JSON line per frame (stats and counters). |
| `Ion:Metrics:Profiling=true`, `Ion:Metrics:TraceOutput=path.json` | Chrome trace at shutdown. |
| `--remote`, `--remote-allow-mutations`, `--remote-stdio` | The remote protocol (see `docs/design/ion-remote.md`). |

## Remote protocol and safety

The remote protocol is off unless asked for, listens on loopback only, needs a per-run bearer token from an owner-only
file (`<project>/.ion/run/remote.json`), and grants writes only with `--remote-allow-mutations`. Mutations are applied on
the game thread at the end of a frame, idempotently per request id, and never while a scene loads. Release builds leave
it out unless published with `-p:IonRemote=true`. Details, the method table and the error codes: `docs/design/ion-remote.md`.

## Acceptance

`docs/agentic/acceptance.md` is the Stage 6 acceptance scenario (create from a template, add a system, run 600 headless
frames, screenshot, diff against a golden, inspect an entity, mutate a component, with only the CLI and MCP) as a
script; `Ion.Tools.Tests.TemplateTests.AcceptanceScenarioWithOnlyTheCliAndMcp` runs it (`ION_SLOW_TESTS=1`).
