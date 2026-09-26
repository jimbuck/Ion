# MyIonGame

An [Ion](https://github.com/jwbuck/Ion) 2D game (no ECS: plain systems and a state class, drawn with the sprite batch).
This file tells a coding agent how to work on it without reading the engine's source.

## Layout

- `MyIonGame/Program.cs`: the entry point. Keep its calls (`CreateBuilder`, `Game.Configure`, `Build`, `Game.Use`, `Run`)
  as they are: the Ion schedule generator reads them and compiles the schedule into direct calls.
- `MyIonGame/Game.cs`: services and schedule (`Game : IIonGame`), settings, `PlayState` and its JSON metadata (`GameJson`).
- `MyIonGame/PaddleSystem.cs`: the game's system. Add new systems in new files.
- `MyIonGame/appsettings.json`: configuration (`Ion:*` for the engine, `Game:*` for the game).
- `MyIonGame.Tests/`: headless tests (`GameTests`) and a state snapshot test (`SnapshotTests`, snapshots in `Golden/`).

## The loop

Each frame runs the stages `First`, `FixedUpdate` (zero or more times, 60 Hz by default), `Update`, `Render`, `Last`;
`Init` runs once before the first frame and `Destroy` once after the last. A system is a plain class; each public method
with a stage attribute (`[Init]`, `[Update]`, `[FixedUpdate]`, `[Render]`, ...) is a step taking `GameTime dt`
(`dt.Delta` in seconds, `dt.Frame`). Constructor parameters are injected: `IInputState` (keys, mouse, gamepads, text),
`ISpriteBatch` (`DrawRect`, `Draw(texture, ...)`, `DrawString(font, ...)`), `IWindow` (`Size`), `IAssetManager`
(`Load<ITexture2D>("file.png")` from `Assets/`), `IAudioManager`, `IEvents`, `IMetrics`, and your own services.

- Simulation in `[FixedUpdate]` (fixed `dt`), input and presentation in `[Update]` and `[Render]`. Drawing only in `Render`.
- Ordering: steps run by `Order` (default 0) then registration order: `[Update(Order = 10)]`, `[After<OtherSystem>]`,
  `[Before<OtherSystem>]`. Engine steps use the bands below -500 and above 500. Check with `ion schedule`.
- New system: write the class, register it (`builder.Services.AddSingleton<MySystem>()` in `Configure`) and add it to the
  schedule (`.UseSystem<MySystem>()` in `Use`).
- Events: `events.Emit(new Scored(1))` with `record struct Scored(int Points)`; read with a reader created once in the
  constructor: `private EventReader<Scored> _scored = events.Reader<Scored>();` then `foreach (var e in _scored.Read())`.
- No `async`/`Task` in steps (the build reports ION005). Randomness from `GameSettings.Seed` (`Ion:Seed`).

## Commands

```sh
dotnet build                                   # zero warnings expected
dotnet test                                    # headless + snapshot tests (no GPU needed)
ion run --headless --frames 600 --seed 1 --screenshot out/frame600.png --summary out/run.json
ion diff out/frame600.png Golden/frame600.png  # exit 0 when the images match (tolerance 2 per channel)
ion schedule                                   # the schedule, stage by stage
ion trace --frames 300 --out out/trace.json    # Chrome trace, open in https://ui.perfetto.dev
```

`ion run` exits non-zero when the game throws. `out/run.json` has `status`, `exitCode`, `frames`, `frameStats`,
`lastFrame` (draw calls, sprites), `counters` (every `IMetrics.Counter`, such as `hits`), `warnings`, `errors`,
`exception` and `schedule`. Read it first when something is wrong.

## Inspecting a running game

`ion mcp` is an MCP server (`claude mcp add ion -- ion mcp`). `ion_run` with `live=true` starts the game with the remote
protocol and pauses after `frames`; then `ion_call {"method": "resources.get", "params": {"name": "Game.State"}}` reads
the state, `ion_call {"method": "resources.set", "params": {"name": "Game.State", "value": {...}}}` writes it,
`ion_input {"events": [{"type": "key", "key": "Right", "action": "hold", "frames": 30}]}` plays, `ion_step {"frames": 30}`
advances, `ion_screenshot` captures. `ion_call {"method": "rpc.discover"}` lists every remote method.

Expose more state the same way: `builder.Services.AddRemoteResource(name, description, GameJson.Default.X, get, set)`.

## Tests

- `IonTestHost.Run<Game>(frames, host => ...)` builds the game headless with a fixed clock, runs the frames and returns
  state (`run.Get<PlayState>()`, `run.Host`), `run.Counters`, `run.LastFrame` and, with `host.WithRendering()`, `run.Image`.
- Script input with `run.Host.Input.Press(Key.Right)`, `Release`, `Tap`, `Click(position)`; advance with `run.Host.Step(n)`.
- `JsonSnapshot.AssertMatches(json, RenderingEnvironment.GoldenPath("name.json"))` compares with `Golden/`. Update
  snapshots on purpose with `ION_UPDATE_GOLDEN=1 dotnet test`, then review the diff.
- Golden images: `GoldenImage.AssertMatches(run.Image!, RenderingEnvironment.GoldenPath("frame.png"), tolerance: 2)`
  (needs `host.WithRendering()` and a Vulkan or EGL driver); on a mismatch `*.actual.png` and `*.diff.png` are written.
