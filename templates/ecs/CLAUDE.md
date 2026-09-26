# MyIonGame

An [Ion](https://github.com/jwbuck/Ion) game on the built-in ECS (Arch 2.1). This file tells a coding agent how to work
on it without reading the engine's source.

## Layout

- `MyIonGame/Program.cs`: the entry point. Keep its four calls (`CreateBuilder`, `Game.Configure`, `Build`, `Game.Use`,
  `Run`) as they are: the Ion schedule generator reads them and compiles the schedule into direct calls.
- `MyIonGame/Game.cs`: services and schedule (`Game : IIonGame`), settings, components and their JSON registration.
- `MyIonGame/Systems.cs`: the systems. Add new ones here or in new files.
- `MyIonGame/appsettings.json`: configuration (`Ion:*` for the engine, `Game:*` for the game).
- `MyIonGame.Tests/`: a headless test (`GameTests`) and a world snapshot test (`SnapshotTests`, snapshots in `Golden/`).

## The loop

Each frame runs the stages `First`, `FixedUpdate` (zero or more times, 60 Hz by default), `Update`, `Render`, `Last`;
`Init` runs once before the first frame and `Destroy` once after the last. A system is a plain class; each public method
with a stage attribute (`[Init]`, `[Update]`, `[FixedUpdate]`, `[Render]`, ...) is a step taking `GameTime dt`.
Constructor parameters are injected (`World`, `IInputState`, `ISpriteBatch`, `IWindow`, `IEvents`, `IMetrics`,
`IAssetManager`, `IAudioManager`, your own services).

- Ordering: steps run by `Order` (default 0) then registration order. `[Update(Order = 10)]`, `[After<OtherSystem>]`,
  `[Before<OtherSystem>]`. Engine steps use the bands below -500 and above 500, so default-order steps always run between
  engine setup and teardown. Print the schedule to check: `ion schedule` (or `dotnet run -- --Ion:PrintSchedule=true`).
- ECS queries: in a `partial` class, `[Update, Query] private void Move(ref Transform2D t, in Velocity v, [Data] in float dt)`
  runs once per entity with those components; filter with `[All<Ball>]`, `[None<Hidden>]`. Structural changes (create,
  destroy, add or remove components) inside a query go through an injected `Commands` (played back at the end of the stage).
- Built-in components: `Transform2D`, `GlobalTransform2D`, `Parent`/`Children`, `EntityName`, `Sprite`, `Hidden`.
- New component: a `record struct`, added to `GameJson` (`[JsonSerializable(typeof(MyComponent))]`) and registered in
  `Game.Configure` (`components.AddUnmanaged("MyComponent", GameJson.Default.MyComponent)`, or `.AddTag<T>(name)` for a
  tag). Registered components appear in world snapshots and over the remote protocol.
- New system: write the class, register it (`builder.Services.AddSingleton<MySystem>()` in `Configure`) and add it to the
  schedule (`.UseSystem<MySystem>()` in `Use`).
- No `async`/`Task` in steps (the build reports ION005). Long work goes to engine jobs; the frame never waits.
- Randomness: seed from `GameSettings.Seed` (`Ion:Seed`), never `Random.Shared`, so runs are reproducible.

## Commands

```sh
dotnet build                                   # zero warnings expected
dotnet test                                    # headless + snapshot tests (no GPU needed)
ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png --summary out/run.json
ion diff out/frame600.png Golden/frame600.png  # exit 0 when the images match (tolerance 2 per channel)
ion schedule                                   # the schedule, stage by stage
ion trace --frames 300 --out out/trace.json    # Chrome trace, open in https://ui.perfetto.dev
```

`ion run` exits non-zero when the game throws. `out/run.json` has `status`, `exitCode`, `frames`, `frameStats`
(avg/p95/max frame ms, draw calls), `lastFrame`, `counters` (every `IMetrics.Counter`), `warnings`, `errors`,
`exception` (type, message, stack trace) and `schedule`. Read it first when something is wrong.

Without the `ion` tool: `dotnet run --project MyIonGame -- --headless --Ion:Run:Frames=600 --Ion:Run:Summary=out/run.json`
(add `--Ion:Headless:Render=true --Ion:Run:Screenshot=out/frame.png` for a screenshot; needs a Vulkan or EGL driver,
Mesa lavapipe on Linux CI).

## Inspecting a running game

`ion mcp` is an MCP server (`claude mcp add ion -- ion mcp`). `ion_run` with `live=true` starts the game with the remote
protocol and pauses after `frames`; then:

- `ion_query {"with": ["Ball"], "components": ["Transform2D", "Velocity"]}`: entities and components as JSON.
- `ion_get {"entity": "Ball0"}`: one entity, by `EntityName` or id.
- `ion_mutate {"entity": "Ball0", "component": "Velocity", "path": "X", "value": 0}`: change a field.
- `ion_step {"frames": 60}`, `ion_screenshot {"path": "out/live.png"}`, `ion_input {"events": [{"type": "key", "key": "Space"}]}`.
- `ion_call {"method": "rpc.discover"}` lists every remote method.

From a shell, in `MyIonGame/`: `dotnet run -- --headless-render --remote-allow-mutations`, then (same directory)
`ion remote world.query '{"name":"Ball*"}'`. The server listens on 127.0.0.1 only, and the token is in
`.ion/run/remote.json` under the directory the game runs from (owner-only permissions).

## Tests

- `IonTestHost.Run<Game>(frames, host => ...)` builds the game headless with a fixed clock, runs the frames and returns
  state (`run.Get<T>()`, `run.Host`), `run.Counters`, `run.LastFrame` and, with `host.WithRendering()`, `run.Image`.
- `JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("name.json"))` compares with `Golden/`.
  Update snapshots on purpose with `ION_UPDATE_GOLDEN=1 dotnet test`, then review the diff.
- `host.Input.Tap(Key.Space)` / `host.Input.Click(position)` script input; `host.Step(n)` advances frames.
- Golden images: `GoldenImage.AssertMatches(run.Image!, RenderingEnvironment.GoldenPath("frame.png"), tolerance: 2)`
  (needs `host.WithRendering()` and a Vulkan or EGL driver); on a mismatch `*.actual.png` and `*.diff.png` are written.
