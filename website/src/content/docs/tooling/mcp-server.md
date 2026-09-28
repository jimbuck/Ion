---
title: MCP server
description: Connect Claude Code or any Model Context Protocol client to ion mcp and drive Ion games with its tools, from headless runs to live inspection.
sidebar:
  order: 3
---

`ion mcp` is a [Model Context Protocol](https://modelcontextprotocol.io) server on stdio. It gives a coding agent tools to
build and run an Ion game headless and read its summary, start it live with the
[remote protocol](/Ion/tooling/remote-protocol/), query and change entities, inject input, step frames, take
screenshots and compare them with golden images. It lives in `Ion.Tools.Mcp` and ships inside the
[`ion` tool](/Ion/tooling/ion-cli/).

## Setup

Install the `ion` tool first (see [Installing](/Ion/tooling/ion-cli/#installing)), then register the server with your
client. The server resolves relative paths and the default game project against its **working directory**, so start
the client from the game's directory (or pass `project` to the tools).

### Claude Code

```bash
claude mcp add ion -- ion mcp
```

To share the registration with everyone who works on the repository, add it at project scope, which writes a
`.mcp.json` next to your code:

```bash
claude mcp add --scope project ion -- ion mcp
```

### Other MCP clients

Any client that launches stdio servers works. The usual JSON configuration is:

```json title="mcp.json"
{
  "mcpServers": {
    "ion": {
      "command": "ion",
      "args": ["mcp"]
    }
  }
}
```

If the tool is not installed globally, point `command` at `dotnet` and `args` at the tool's DLL:
`["path/to/Ion/Ion.Tools/bin/Release/net10.0/Ion.Tools.dll", "mcp"]`.

### Protocol details

| Item | Value |
|---|---|
| Transport | stdio, newline-delimited JSON-RPC 2.0 |
| Protocol versions | `2025-06-18`, `2025-03-26`, `2024-11-05` (the requested one if supported, else the newest) |
| Server name | `ion` |
| Capabilities | `tools` (the tool list does not change) |
| Methods | `initialize`, `ping`, `tools/list`, `tools/call` |

The `initialize` result carries short instructions describing the workflow, which most clients show to the model.

## How the tools fit together

There are two modes.

**Headless run.** `ion_run` without `live` builds the game, runs it headless for `frames` frames (600 by default) and
returns the [run summary](/Ion/tooling/ion-cli/#the-run-summary): exit code, frame stats, counters, warnings, errors, the
exception and the schedule, plus the last 30 lines of output. Add `screenshot` to get the last frame as a PNG. This is
the fast "did it work" check.

**Live game.** `ion_run` with `live: true` builds the game and starts it with the remote protocol (mutations allowed by
default) and keeps it running. With `frames`, it pauses after that many frames and answers only once it has paused, so
the next tool sees frame N. Headless live runs pausing at a frame use the deterministic clock, so frame N is the same
state every time. The other tools then act on that game until `ion_stop`. `ion_connect` attaches to a game you started
yourself with `--remote`.

```text
ion_run        {"live": true, "frames": 600, "seed": 1}
ion_query      {"with": ["Ball"], "components": ["Transform2D", "Velocity"]}
ion_get        {"entity": "Ball0"}
ion_mutate     {"entity": "Ball0", "component": "Transform2D", "path": "Position", "value": [100, 100]}
ion_step       {"frames": 1}
ion_screenshot {"path": "out/live.png"}
ion_stop       {}
```

A tool that fails returns its message with `isError: true` (a JSON-RPC error from the game reads
`The game answered with error <code>: <message>`). Calling a live tool with no game connected says so: call `ion_run`
with `live=true` or `ion_connect` first.

## Tools

### Running and connecting

| Tool | Description | Arguments |
|---|---|---|
| `ion_run` | Runs an Ion game. By default headless for `frames` frames with a deterministic clock, then returns the run summary and writes `screenshot` if given. With `live=true`, starts it with the remote protocol, pauses after `frames` frames if given, and keeps it running for the other tools. | see below |
| `ion_connect` | Connects to a game already running with `--remote`, through its token file. | `tokenFile?` (default `<project dir>/.ion/run/remote.json`), `runDirectory?`, `project?` |
| `ion_stop` | Stops the live game: `game.exit`, then kills it if it does not exit within 15 seconds. Returns the exit code. | none |

`ion_run` arguments:

| Argument | Default | Meaning |
|---|---|---|
| `project` | the working directory | The game project (`.csproj` or its directory). |
| `frames` | `600` | Frames to run; with `live=true`, the frame to pause after (no pause when omitted). |
| `seed` | none | The random seed (`Ion:Seed`). |
| `headless` | `true` | Use the headless backends. |
| `render` | `true` | Render headless into an offscreen target so screenshots work. |
| `screenshot` | none | Write the last frame to this PNG. |
| `summary` | `.ion/run/summary.json` | Where to write the summary JSON. |
| `live` | `false` | Keep the game running with the remote protocol. |
| `allowMutations` | `true` | With `live=true`, grant the mutate scope. |
| `configuration` | `Debug` | Build configuration. |
| `args` | none | Extra game arguments, for example `["--Ion:Window:Width=640"]`. |

A live game's standard output and error go to `.ion/run/game.log` in the run directory; the `ion_run` result gives its
path.

### Entities (ECS games)

| Tool | Remote method | Description | Arguments |
|---|---|---|---|
| `ion_query` | `world.query` | Finds ECS entities by components and name and returns their components as JSON. | `components?`, `with?`, `without?`, `name?` (exact, or a prefix ending in `*`), `limit?` (default 1000), `world?` |
| `ion_get` | `world.get_components` | Reads the components of one entity. | `entity` (id or `EntityName`), `components?`, `world?` |
| `ion_mutate` | `world.mutate_components` or `world.insert_components` | Sets one component field by path (`component`, `path`, `value`; empty path replaces the component), or inserts/replaces whole components (`components` object). | `entity`, `component?`, `path?`, `value?`, `components?`, `world?` |
| `ion_spawn` | `world.spawn` | Creates an entity with components and an optional name. | `components?`, `name?`, `world?` |
| `ion_despawn` | `world.despawn` | Destroys an entity. | `entity`, `world?` |

Paths are dot separated with array indexes as numbers: `Position.0` is the X of a `Vector2` position.

### Frames, input and pictures

| Tool | Remote method | Description | Arguments |
|---|---|---|---|
| `ion_step` | `game.step` | Runs frames of the paused live game and pauses again; answers when they have run. | `frames?` (default 1) |
| `ion_pause` | `game.pause` | Pauses at the end of the current frame (the game keeps answering). | none |
| `ion_resume` | `game.resume` | Resumes the live game. | none |
| `ion_input` | `input.send` | Injects keys, pointer, wheel, text or gamepad input, applied at the start of the next frame. | `events` (see [Injecting input](/Ion/tooling/remote-protocol/#injecting-input)) |
| `ion_screenshot` | `screenshot` | Captures the last rendered frame; returns the image and saves it to `path` if given. | `path?`, `includeImage?` (default `true`) |
| `ion_diff` | none | Compares a PNG with a golden PNG and writes a diff image (mismatches in red). | `actual`, `expected`, `tolerance?` (default 2), `maxMismatchRatio?` (default 0), `diff?` (default `<actual>.diff.png`) |

`ion_screenshot` returns a text part with `width`, `height`, `frame` and `path`, and an `image/png` part the model can
look at. Pass `includeImage: false` to only save the file. `ion_diff` works on files and needs no live game; it returns
`match`, `sameSize`, `width`, `height`, `mismatchedPixels`, `mismatchRatio`, `maxChannelDifference` and `diff`.

### Diagnostics

| Tool | Remote method | Description | Arguments |
|---|---|---|---|
| `ion_schedule` | `schedule.get` | The schedule as text: every stage's steps in run order with orders and scopes. | none |
| `ion_metrics` | `metrics.get` | The last frame's stats and every game counter, gauge and histogram. | none |
| `ion_logs` | `log.tail` | Recent log entries. | `since?` (the previous call's `next`), `level?` (default `Information`), `limit?` |
| `ion_events` | `events.tail` | Event counts per type and recent payloads of remote-registered events. | `since?`, `limit?` |

### UI (games with `AddUiRemote`)

| Tool | Remote method | Description | Arguments |
|---|---|---|---|
| `ion_ui_tree` | `ui.tree` | The UI tree: every node's path, kind, text, value, rectangle, enabled and focused state, and the focused path. | `prefix?` (for example `options/`) |
| `ion_ui_click` | `ui.click` | Clicks a node by path, as the pointer would, at the start of the next frame. | `path` (for example `main/Options`) |

Use `ion_call` for `ui.set_value`, `ui.type`, `ui.focus` and `ui.back`. See
[UI over the remote protocol](/Ion/interaction/ui/ui-remote/).

### Anything else

| Tool | Description | Arguments |
|---|---|---|
| `ion_call` | Calls any remote method of the connected game. `rpc.discover` lists them with their parameters. | `method`, `params?` |

```text
ion_call {"method": "rpc.discover"}
ion_call {"method": "resources.get", "params": {"name": "Game.State"}}
ion_call {"method": "ui.set_value", "params": {"path": "options/Volume", "value": 0.5}}
ion_call {"method": "physics2d.raycast", "params": {"origin": [0, 0], "to": [100, 0]}}
```

## Tips for agents

- Start with a headless `ion_run` and read `exitCode`, `summary.status`, `summary.exception` and `summary.errors` before
  anything else.
- Use a fixed `seed` everywhere so runs, screenshots and snapshots are comparable.
- Prefer `ion_query`/`ion_get` and `ion_ui_tree` over screenshots to check state: they are exact and cheap.
- Only components registered with `AddEcsSerialization` are visible. If `ion_query` returns an entity without the
  component you expect, register it (see [Components and serialization](/Ion/ecs/components-and-serialization/)).
- Mutations land at the end of the frame and show from the next one: `ion_step {"frames": 1}` after `ion_mutate` before
  checking an effect.
- Always `ion_stop` when done so the game process and its port are released.

:::caution[Screenshots need a renderer]
Headless rendering (`render`, on by default) needs a Vulkan driver or EGL with OpenGL ES 3 (Mesa lavapipe or llvmpipe
on Linux CI). On a machine without either, pass `render: false` and check state with the query tools and the summary
instead of pictures. A game running headless without rendering answers `ion_screenshot` with error `-32005`.
:::

## See also

- [Remote protocol](/Ion/tooling/remote-protocol/): the methods behind the tools.
- [The ion command line](/Ion/tooling/ion-cli/): the same operations from a shell.
- [Agentic development](/Ion/tooling/agentic-development/): the loop the tools are designed for.
- [Snapshots and golden images](/Ion/tooling/snapshots-and-goldens/): locking in what the agent verified.
