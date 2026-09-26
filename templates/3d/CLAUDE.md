# MyIonGame

An [Ion](https://github.com/jwbuck/Ion) 3D game on the immediate-mode 3D renderer (PBR materials, directional light with
shadows, glTF models). This file tells a coding agent how to work on it without reading the engine's source.

## Layout

- `MyIonGame/Program.cs`: the entry point. Keep its calls (`CreateBuilder`, `Game.Configure`, `Build`, `Game.Use`, `Run`)
  as they are: the Ion schedule generator reads them and compiles the schedule into direct calls.
- `MyIonGame/Game.cs`: services and schedule (`Game : IIonGame`), settings, `SpinState` and its JSON metadata (`GameJson`).
- `MyIonGame/SceneSystem3D.cs`: creates meshes and materials, simulates, submits the scene.
- `MyIonGame/appsettings.json`: configuration (`Ion:*` for the engine, `Game:*` for the game).
- `MyIonGame.Tests/`: a headless test (`GameTests`) and a state snapshot test (`SnapshotTests`, snapshots in `Golden/`).

## The loop and the 3D renderer

Each frame runs the stages `First`, `FixedUpdate` (zero or more times, 60 Hz by default), `Update`, `Render`, `Last`;
`Init` runs once before the first frame and `Destroy` once after the last. A system is a plain class; each public method
with a stage attribute (`[Init]`, `[Update]`, `[FixedUpdate]`, `[Render]`, ...) is a step taking `GameTime dt`.
Constructor parameters are injected (`IRenderer3D`, `ISpriteBatch` for a 2D HUD on top, `IInputState`, `IWindow`,
`IAssetManager`, `IEvents`, `IMetrics`, your own services).

- `IRenderer3D` is immediate mode. In `[Init]`: `CreateMesh(MeshPrimitives.Cube(1f))`, `CreateMaterial(new PbrMaterial(color,
  metallic, roughness))` (or `UnlitMaterial`), glTF models through `IAssetManager`. In `[Render]`, every frame:
  `SetCamera(camera, Transform.LookAt(eye, target))`, `AddLight(...)`, then `Submit(new MeshRenderer(mesh, material), world)`
  or `Draw(mesh, material, world)`. Nothing persists between frames except meshes and materials.
- Simulation in `[FixedUpdate]` (fixed `dt`), submission in `[Render]`.
- Headless without a GPU the renderer still runs its CPU pipeline: `IRenderer3D.LastFrameStatistics` (submitted, visible,
  culled, batches, draw calls) is meaningful in tests.
- Ordering: `[Update(Order = 10)]`, `[After<OtherSystem>]`, `[Before<OtherSystem>]`; check with `ion schedule`.
- New system: write the class, register it (`builder.Services.AddSingleton<MySystem>()` in `Configure`) and add it to the
  schedule (`.UseSystem<MySystem>()` in `Use`).
- No `async`/`Task` in steps (the build reports ION005).

## Commands

```sh
dotnet build                                   # zero warnings expected
dotnet test                                    # headless + snapshot tests (no GPU needed)
ion run --headless --frames 600 --screenshot out/frame600.png --summary out/run.json   # needs a Vulkan or EGL driver for the PNG
ion diff out/frame600.png Golden/frame600.png  # exit 0 when the images match (tolerance 2 per channel)
ion schedule
ion trace --frames 300 --out out/trace.json    # Chrome trace, open in https://ui.perfetto.dev
```

`ion run` exits non-zero when the game throws. `out/run.json` has `status`, `exitCode`, `frames`, `frameStats`,
`lastFrame`, `counters`, `warnings`, `errors`, `exception` and `schedule`. Read it first when something is wrong.
3D rendering differs slightly between drivers: compare screenshots with a tolerance (`ion diff --tolerance 4 --max-ratio 0.001`).

## Inspecting a running game

`ion mcp` is an MCP server (`claude mcp add ion -- ion mcp`). `ion_run` with `live=true` starts the game with the remote
protocol and pauses after `frames`; then `ion_call {"method": "resources.get", "params": {"name": "Game.Spin"}}` reads the
state, `resources.set` writes it, `ion_step {"frames": 60}` advances, `ion_screenshot {"path": "out/live.png"}` captures,
`ion_metrics` shows draw calls and frame times. `ion_call {"method": "rpc.discover"}` lists every remote method.

## Tests

- `IonTestHost.Run<Game>(frames, host => ...)` builds the game headless with a fixed clock, runs the frames and returns
  state (`run.Get<SpinState>()`, `run.Get<IRenderer3D>()`), `run.Counters`, `run.LastFrame` and, with
  `host.WithRendering()`, `run.Image`.
- `JsonSnapshot.AssertMatches(json, RenderingEnvironment.GoldenPath("name.json"))` compares with `Golden/`. Update
  snapshots on purpose with `ION_UPDATE_GOLDEN=1 dotnet test`, then review the diff.
- Golden images: `GoldenImage.AssertMatches(run.Image!, RenderingEnvironment.GoldenPath("frame.png"), tolerance: 4, maxMismatchRatio: 0.001)`
  (needs `host.WithRendering()` and a Vulkan or EGL driver).
