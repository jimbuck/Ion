---
title: The ion command line
description: Every command of the ion tool (new, run, schedule, bench, trace, diff, remote, publish, mcp) with its options, defaults and exit codes.
sidebar:
  order: 1
---

The `ion` tool is the engine's command line. It creates games from the templates, builds and runs them headless for a
fixed number of frames with a screenshot and a JSON summary, prints the schedule, profiles, compares images, talks to a
running game over the [remote protocol](/Ion/tooling/remote-protocol/), publishes with the NativeAOT presets and serves the
[MCP server](/Ion/tooling/mcp-server/) that coding agents use.

```bash
ion new ecs Arena
cd Arena
ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png --summary out/run.json
ion diff out/frame600.png Golden/frame600.png
```

## Installing

The tool is the `Ion.Tools` project (`Ion/Ion.Tools`), packed as a .NET tool whose command name is `ion`. From an Ion
checkout:

```bash
dotnet pack Ion/Ion.Tools -c Release -o out
dotnet tool install -g Ion.Tools --add-source out
ion --version
```

You can also run it in place without installing: `dotnet Ion/Ion.Tools/bin/Release/net10.0/Ion.Tools.dll <command>`.

Inside the Ion repository, the root `package.json` wraps the same steps as npm tasks (run `npm install` once):

```bash
npm run ion -- run --headless --frames 600   # dotnet run -c Release --project Ion/Ion.Tools -- <args>
npm run mcp                                  # the MCP server from source
npm run install:tools                        # pack Ion.Tools into artifacts/packages and dotnet tool update -g
```

The game templates (`templates/2d`, `templates/3d`, `templates/ecs`) are embedded in the tool, so `ion new` works offline.

## Commands at a glance

| Command | What it does |
|---|---|
| `ion new <2d\|3d\|ecs> [name]` | Creates a game, a test project and a `CLAUDE.md` from a template. |
| `ion run [project]` | Builds and runs the game, optionally headless, for N frames, with a screenshot, a summary and the remote protocol. |
| `ion schedule [project]` | Prints the game's schedule: every stage's steps in run order. |
| `ion bench [filter]` | Runs BenchmarkDotNet benchmarks in Release. |
| `ion trace [project]` | Runs N frames with profiling on and writes a Chrome trace. |
| `ion diff <actual.png> <expected.png>` | Compares two PNGs and writes a diff image. |
| `ion remote <method> [params-json]` | Calls a remote protocol method of a running game. |
| `ion publish [project] --target <preset>` | Publishes with a NativeAOT preset. |
| `ion mcp` | Serves the Model Context Protocol on stdio. |

`ion`, `ion help`, `ion -h` and `ion --help` print the usage. `ion --version` prints the tool version.

### Argument syntax

The parser is deliberately small:

- Options take a value either as the next argument (`--frames 600`) or after `=` (`--frames=600`).
- Flags (`--headless`, `--render`, `--remote`, `--remote-allow-mutations`, `--force`, `--windowed`) take no value.
- Everything after a bare `--` is passed through unchanged (to the game for `run`, `schedule` and `trace`, to
  BenchmarkDotNet for `bench`, to MSBuild for `publish`).
- An option a command does not know is an error: `ion: Unknown option(s): --foo. Pass game arguments after '--'.`

### Exit codes

| Code | Meaning |
|---|---|
| `0` | Success (for `diff`, the images match). |
| `1` | `ion` with no arguments (usage printed), or `ion diff` found a mismatch. |
| `2` | A usage or tool error: unknown command, unknown option, a bad value, a missing file, a remote call that returned a JSON-RPC error. The message is printed to standard error as `ion: ...`. |
| other | `ion run`, `ion schedule` and `ion trace` return the game's exit code (non-zero when it threw) or the build's exit code when the build failed. `ion bench` and `ion publish` return the exit code of the `dotnet` command they start. |

## Which project is run

`run`, `schedule`, `trace`, `publish` and `remote --project` take an optional project path, resolved like this:

1. A path to a `.csproj` is used as is.
2. A directory with exactly one `.csproj` whose name does not end in `.Tests` uses that project.
3. Otherwise the tool looks one level down (skipping directories that start with `.`), which is how it finds the game
   inside a template's solution directory.

No candidate, or more than one, is an error that lists what it found. The default is the current directory.

The game runs with `dotnet run --no-build` from the **project directory**, after a quiet `dotnet build` of the same
configuration. The tool disables MSBuild node reuse and the build server for those child processes so that a caller
waiting on the output never hangs on a leftover build node.

## ion new

```bash
ion new <2d|3d|ecs> [name] [--output <dir>] [--ion-source <repo>] [--force]
```

| Argument | Default | Meaning |
|---|---|---|
| template | required | `2d` (sprite batch, no ECS), `3d` (the 3D renderer) or `ecs` (the built-in ECS). |
| `name` | the `--output` directory's name, else `MyGame` | The game name. Letters, digits and `_`, starting with a letter. Replaces `MyIonGame` in file names and contents. |
| `--output <dir>` | `name` | Where to write. Must be empty unless `--force` is given. |
| `--ion-source <repo>` | none | Build against an Ion source checkout instead of the Ion packages (sets `IonSource` in `Directory.Build.props`). |
| `--force` | off | Write into a non-empty directory. |

The result is the same as `dotnet new ion-2d|ion-3d|ion-ecs` from the `Ion.Templates` pack: a game project, a test
project with a headless test and a snapshot test, `CLAUDE.md`, `appsettings.json` and a `.slnx` solution. The command
prints the next steps:

```text
Created the ecs game 'Arena' in /work/Arena (13 files).
  cd Arena
  ion run --headless --frames 600 --screenshot out/frame600.png --summary out/run.json
  dotnet test
Read CLAUDE.md for the engine workflow.
```

See [Templates](/Ion/getting-started/templates/) for what each template contains and
[Agentic development](/Ion/tooling/agentic-development/) for the `CLAUDE.md` workflow.

## ion run

```bash
ion run [project] [--headless] [--render] [--frames N] [--seed S] [--screenshot file.png] [--summary file.json]
        [--remote] [--remote-allow-mutations] [--pause-at N] [-c Debug|Release] [--run-dir <dir>] [-- game args]
```

`ion run` builds the game and runs it to completion. Each option becomes a configuration key passed on the game's
command line, so every game built with `AddIon`/`UseIon` understands it without any code:

| Option | Passed to the game | Meaning |
|---|---|---|
| `--headless` | `--Ion:Headless=true` | Null window, input and audio backends. No GPU or display needed. |
| `--render` | `--Ion:Headless:Render=true` (only with `--headless`) | Render headless into an offscreen target. Implied by `--screenshot` when headless. |
| `--frames N` | `--Ion:Run:Frames=N` | Run N frames, then run Destroy and exit. Without it the game runs until it exits (or its window is closed). |
| `--seed S` | `--Ion:Seed=S` | The random seed. Games read it with `IonRun.Seed(config)`; the summary records it. |
| `--screenshot f.png` | `--Ion:Run:Screenshot=<full path>` | Write the last rendered frame as PNG at the end of the run. Windowed runs also get `--Ion:Graphics:RetainLastFrame=true`. |
| `--summary f.json` | `--Ion:Run:Summary=<full path>` | Write the run summary (below). |
| `--remote` | `--Ion:Remote:Enabled=true --Ion:Remote:Port=0 --Ion:Remote:RunDirectory=<run dir>` | Start the remote server on a free loopback port. |
| `--remote-allow-mutations` | the above plus `--Ion:Remote:AllowMutations=true` | Also create the mutate scope. |
| `--pause-at N` | the remote options plus `--Ion:Remote:PauseAtFrame=N` | Pause after N frames and keep serving remote requests. Headless runs also get `--Ion:Run:FixedStep=true`. |
| `-c`, `--configuration` | | Build configuration. Default `Debug`. |
| `--run-dir <dir>` | | Where the token file goes. Default `<project dir>/.ion/run`. |
| `-- args` | as is | Any other game argument, for example `-- --Ion:Window:Width=640`. |

:::note[Headless runs are deterministic]
A headless run with `--frames` uses a fixed-step clock (`Ion:Run:FixedStep` defaults to on when headless and
`Ion:Run:Frames` is set): every frame lasts exactly one 60 Hz step, however long it really took. The same seed then gives
the same state and the same pixels on every run. See [Time and determinism](/Ion/concepts/time-and-determinism/).
:::

:::caution[Release builds leave the remote protocol out]
`--remote` with `-c Release` starts nothing unless the game sets `<IonRemote>true</IonRemote>` (or you build with
`-p:IonRemote=true`). The game prints why on standard error. See [Remote protocol](/Ion/tooling/remote-protocol/).
:::

### The run summary

With `--summary`, the game's `RunReportSystem` writes a JSON document when the game shuts down, and also from the
unhandled exception handler when an exception ends the run. After the process exits, `ion run` adds the `exitCode`. If
the game crashed before writing anything, the tool writes a minimal summary with `"status": "crashed"` and the last lines
of output; if the build failed, `"status": "build-failed"` with the build output.

```json title="out/run.json"
{
  "version": 1,
  "status": "ok",
  "title": "Arena",
  "frames": 600,
  "requestedFrames": 600,
  "seed": 1,
  "headless": true,
  "fixedStep": true,
  "wallMs": 812.4,
  "frameStats": {
    "count": 600,
    "avgFrameMs": 0.41,
    "minFrameMs": 0.21,
    "p95FrameMs": 0.66,
    "maxFrameMs": 12.3,
    "avgWorkMs": 0.41,
    "totalDrawCalls": 5400,
    "totalSprites": 5400
  },
  "lastFrame": { "frame": 599, "frame_ms": 0.39, "draw_calls": 9, "sprites": 9 },
  "counters": { "bounces": 37, "gravity": 4800 },
  "warnings": [],
  "errors": [],
  "exception": null,
  "screenshot": "/work/Arena/out/frame600.png",
  "schedule": "Init\n  ...",
  "exitCode": 0
}
```

(The numbers are illustrative; `lastFrame` has every field of the [frame log](/Ion/tooling/metrics-and-tracing/).)

| Field | Meaning |
|---|---|
| `status` | `ok`, `exception` (an unhandled exception ended the run), `failed` (written as `ok` but the process exited non-zero), `crashed` (no summary was written), `build-failed`. |
| `frames`, `requestedFrames` | Frames run, and `Ion:Run:Frames` when set. |
| `seed`, `headless`, `fixedStep` | The run settings as the game saw them. |
| `wallMs` | Wall-clock time of the run. |
| `frameStats` | Frame count, average, min, p95 and max frame time, average work time, total draw calls and sprites. |
| `lastFrame` | The last frame's stats. |
| `counters` | Every game counter (value), gauge (value) and histogram (total count) registered with `IMetrics`. |
| `warnings`, `errors` | Up to 200 log entries of level Warning and Error or above: `{level, category, message}`. A screenshot that could not be written adds a warning. |
| `exception` | `{type, message, stackTrace}` or `null`. The stack trace names the failing step. |
| `screenshot` | The full path written, or `null`. |
| `schedule` | The schedule as text, as `ion schedule` prints it. |
| `exitCode` | The process exit code (added by the tool). |

:::tip
Read the summary first when something is wrong. `exception.stackTrace` and `errors` usually point straight at the
system and step that failed.
:::

### Running without the tool

Because the options are plain configuration keys, `dotnet run` does the same thing:

```bash
# From the solution directory; Arena/Arena.csproj is the game project.
dotnet run --project Arena -- --headless --Ion:Run:Frames=600 --Ion:Seed=1 --Ion:Run:Summary=out/run.json
dotnet run --project Arena -- --headless-render --Ion:Run:Frames=600 --Ion:Run:Screenshot=out/frame.png
```

`IonApplication.CreateBuilder(args)` rewrites a few short switches before binding: `--headless`
(`Ion:Headless=true`), `--headless-render` (`Ion:Headless=true` and `Ion:Headless:Render=true`), `--remote`,
`--remote-allow-mutations` and `--remote-stdio`. See [Configuration](/Ion/reference/configuration/) for every key.

## ion schedule

```bash
ion schedule [project] [-c config] [-- game args]
```

Builds the game and runs it headless for zero frames with `--Ion:PrintSchedule=true` and log level Warning, so the only
output is the schedule: every stage's steps in run order with their orders and scopes. Use it to check where a new system
landed relative to the engine's steps. See [Stages](/Ion/concepts/stages/) and
[Stage order](/Ion/reference/stage-order/).

## ion trace

```bash
ion trace [project] [--frames N] [--out trace.json] [--windowed] [-c config] [-- game args]
```

| Option | Default | Meaning |
|---|---|---|
| `--frames N` | `300` | Frames to run and keep. |
| `--out file` | `trace.json` | The Chrome trace to write (made absolute). |
| `--windowed` | off | Run in a window instead of headless. |

It runs the game with `--Ion:Metrics:Profiling=true`, `--Ion:Metrics:TraceOutput=<out>` and
`--Ion:Metrics:HistoryFrames=<frames>`; the metrics module writes the trace at shutdown. Open it in
[ui.perfetto.dev](https://ui.perfetto.dev) or `chrome://tracing`. If the game exits with 0 but no file appears, the tool
says so (the metrics module is missing; `AddIon` installs it). See
[Metrics and tracing](/Ion/tooling/metrics-and-tracing/).

## ion bench

```bash
ion bench [filter] [--project <benchmarks project>] [-- BenchmarkDotNet args]
```

Runs `dotnet run -c Release --project <project> -- --filter <filter> [args]`. The filter defaults to `*`; a filter
without `*` is wrapped as `*filter*`. Without `--project`, the tool searches the current directory and its parents (up
to the repository root) for a `*.Benchmarks.csproj`. See [Benchmarks](/Ion/tooling/benchmarks/).

```bash
ion bench FullFrame -- --job short
ion bench '*Network*'
```

## ion diff

```bash
ion diff <actual.png> <expected.png> [--tolerance N] [--max-ratio R] [--out diff.png]
```

| Option | Default | Meaning |
|---|---|---|
| `--tolerance N` | `2` | A pixel mismatches when any RGBA channel differs by more than N (0 to 255). |
| `--max-ratio R` | `0` | Fraction of pixels allowed to mismatch. |
| `--out file` | `<actual>.diff.png` | The diff image: mismatches in red over a dimmed copy of the expected image. |

It prints one line and exits `0` on a match, `1` otherwise:

```text
match: 0 of 518400 pixels differ by more than 2 (max channel difference 0); diff: /work/Arena/out/again.diff.png
MISMATCH: sizes differ (actual 640x480).
```

The rule is the same as `GoldenImage` in `Ion.Testing`. See
[Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/).

## ion remote

```bash
ion remote <method> [params-json] [--project <dir> | --run-dir <dir> | --token-file <file>]
```

Calls one method of a game running with `--remote` and prints the result as indented JSON. The token file is found in
this order: `--token-file`, then `<--run-dir>/remote.json`, then `<project dir>/.ion/run/remote.json` for `--project` (or
the current directory). The client uses the mutate token when the file has one. A JSON-RPC error prints
`ion: error <code>: <message>` and exits `2`.

```bash
ion remote rpc.discover
ion remote game.info
ion remote world.query '{"name":"Ball*","components":["Transform2D"]}'
ion remote world.mutate_components '{"entity":"Ball0","component":"Transform2D","path":"Position","value":[100,100]}'
ion remote game.step '{"frames":1}'
ion remote game.exit
```

:::caution[Run it from the right directory]
A game started with `dotnet run -- --remote` writes its token file to `.ion/run/remote.json` under its **current
directory**. `ion remote` looks under the **project directory**. Start the game from the project directory (as `ion run`
does), or point `ion remote` at the file with `--token-file`.
:::

## ion publish

```bash
ion publish [project] --target <preset> [-c Release] [--output <dir>] [--sysroot <dir>] [-- msbuild args]
```

Runs `dotnet publish <project> -c Release -p:IonTarget=<preset>`. The presets are `win-x64`, `win-arm64`, `osx-arm64`,
`osx-x64`, `linux-x64`, `linux-arm64` and `r36s` (the R36S handheld: linux-arm64, OpenGL ES, SDL, fullscreen 640x480,
plus the ArkOS ports layout in `<output>-arkos`). `--output` (or `-o`) sets the output directory and `--sysroot` sets
`IonArm64SysRoot` for cross-compiling to arm64. See [Publishing](/Ion/platforms/publishing/),
[NativeAOT](/Ion/platforms/native-aot/) and [R36S](/Ion/platforms/r36s/).

## ion mcp

```bash
ion mcp
```

Serves the Model Context Protocol on standard input and output until the input ends. Register it with your agent, for
example `claude mcp add ion -- ion mcp`. See [MCP server](/Ion/tooling/mcp-server/).

## Common problems

| Message or symptom | Cause and fix |
|---|---|
| `ion: Unknown option(s): --foo. Pass game arguments after '--'.` | Only the options listed above are known. Game settings go after `--`: `ion run -- --Ion:Window:Width=640`. |
| `ion: Several projects in '...': A.csproj, B.csproj. Pass the one to run.` | The directory holds more than one non-test project. Pass the `.csproj` path. |
| `ion: No token file at .../.ion/run/remote.json; is the game running with --remote?` | Start the game with `ion run --remote` (or `--remote-allow-mutations`, `--pause-at`), or point `ion remote` at the file with `--token-file`. A game started with `dotnet run` writes the file under its current directory. |
| `ion: error -32002: ...` from `ion remote` | The game runs without the mutate scope. Restart it with `--remote-allow-mutations`. |
| `--remote` prints `Ion remote: --remote was requested but the remote module is compiled out of this build` | A Release build without `IonRemote=true`. Use `-c Debug` (the default) or set `<IonRemote>true</IonRemote>`. |
| `"status": "crashed"` in the summary with no `exception` | The process died before the game's report ran (a build-time or startup failure). Read `output`, the tail of the process output. |
| `ion trace` says `the game did not write trace.json` | The game does not install the metrics module. `AddIon` does; without it call `builder.AddMetrics()`. |
| `ion run --screenshot` writes a warning `Screenshot not written: No screenshot source` | Headless rendering was not available. On Linux install Mesa (lavapipe or llvmpipe); see [Graphics backends](/Ion/rendering/graphics-backends/). |

## See also

- [Remote protocol](/Ion/tooling/remote-protocol/): what `--remote` and `ion remote` talk to.
- [MCP server](/Ion/tooling/mcp-server/): the same operations as tools for coding agents.
- [Testing](/Ion/tooling/testing/): the in-process equivalent of `ion run --headless`.
- [Agentic development](/Ion/tooling/agentic-development/): the full create, run, look, inspect, lock-in loop.
- [Configuration](/Ion/reference/configuration/): every `Ion:*` key.
