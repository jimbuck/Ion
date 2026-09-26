# Stage 6 acceptance scenario

Roadmap Stage 6: "an agent with only the `ion` CLI and the MCP server can create a game from the template, add a system,
run 600 headless frames, take a screenshot, diff it against a golden image, inspect an entity and mutate a component,
without reading engine source".

The same steps run as a test: `Ion.Tools.Tests.TemplateTests.AcceptanceScenarioWithOnlyTheCliAndMcp`
(`ION_SLOW_TESTS=1 dotnet test Ion/Ion.Tools.Tests -c Release --filter AcceptanceScenario`; it builds the engine from
source into the new game, so it is marked slow). The screenshot and diff steps need a Vulkan or EGL driver (Mesa lavapipe
on Linux) and are skipped without one.

## 1. Create a game from the template

```sh
ion new ecs Arena --ion-source /path/to/Ion     # without --ion-source the game uses the Ion packages
cd Arena
```

`Arena/` has `CLAUDE.md` (the workflow), `Arena/` (the game: `Program.cs`, `Game.cs`, `Systems.cs`, `appsettings.json`),
`Arena.Tests/` (a headless test and a world snapshot test) and `Arena.slnx`.

## 2. Add a system

Following `CLAUDE.md` ("New system"): write the class, register it, add it to the schedule.

```csharp
// Arena/GravitySystem.cs
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

In `Game.cs`: `builder.Services.AddSingleton<GravitySystem>();` in `Configure` and `.UseSystem<GravitySystem>()` in `Use`.

## 3. Run 600 headless frames with a screenshot

```sh
ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png --summary out/run.json
echo $?                                          # 0; non-zero if the game threw
```

`out/run.json`: `"status": "ok"`, `"exitCode": 0`, `"frames": 600`, and `"counters": {"bounces": ..., "gravity": 4800}`
(8 balls times 600 fixed steps: the new system ran).

## 4. Diff against a golden image

The first accepted run becomes the golden image (reviewed, then committed); later runs must match it.

```sh
mkdir -p Golden && cp out/frame600.png Golden/frame600.png
ion run --headless --frames 600 --seed 1 --screenshot out/again.png
ion diff out/again.png Golden/frame600.png       # "match: 0 of 518400 pixels differ ...", exit 0
```

On a mismatch `ion diff` exits 1 and writes `out/again.diff.png` with the differing pixels in red.

## 5. Inspect an entity and mutate a component (MCP)

With `ion mcp` registered (`claude mcp add ion -- ion mcp`), from the game's directory:

1. `ion_run {"live": true, "frames": 600, "seed": 1}`: builds, starts the game headless with the remote protocol (mutations
   allowed, loopback, a per-run token), and answers once it has paused after 600 frames (`info.frame` is 599).
2. `ion_query {"with": ["Ball"], "components": ["Transform2D", "Velocity"]}`: the 8 balls with their components.
3. `ion_get {"entity": "Ball0"}`: one entity by name.
4. `ion_mutate {"entity": "Ball0", "component": "Transform2D", "path": "Position", "value": [100, 100]}` and
   `ion_mutate {"entity": "Ball0", "components": {"Velocity": {"X": 0, "Y": 0}}}`.
5. `ion_get {"entity": "Ball0", "components": ["Transform2D"]}`: `Position` is `[100, 100]`.
6. `ion_step {"frames": 1}`, then `ion_get` again: gravity pulled the ball down by `200 * (1/60)^2` pixels.
7. `ion_screenshot {"path": "out/live.png"}` (optional), then `ion_stop`.

The same from a shell, without MCP (from the project directory, where the game writes `.ion/run/remote.json` and where
`ion remote` looks for it):

```sh
cd Arena
dotnet run -- --headless-render --remote-allow-mutations --Ion:Run:FixedStep=true --Ion:Remote:PauseAtFrame=600 &
ion remote world.get_components '{"entity":"Ball0"}'
ion remote world.mutate_components '{"entity":"Ball0","component":"Transform2D","path":"Position","value":[100,100]}'
ion remote game.step '{"frames":1}'
ion remote game.exit
```
