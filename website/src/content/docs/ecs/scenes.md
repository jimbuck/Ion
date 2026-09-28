---
title: Scenes
description: Split a game into scenes with Ion.Extensions.Scenes, each with its own schedule, service scope and ECS world, and switch between them with ChangeSceneEvent, instantly or with a fade or a transition of your own.
sidebar:
  order: 7
---

A scene is a schedule of its own (systems and function steps) that runs only while it is active, inside a fresh service
scope created when it loads and disposed when it unloads. With the ECS module, each scene also gets its own `World`, so
unloading a level throws its entities away. `Ion.Extensions.Scenes` is part of the engine core: `AddIon()` registers it.

```csharp title="Program.cs"
using Microsoft.Extensions.DependencyInjection;
using Ion;
using Ion.Extensions.Ecs;
using Ion.Extensions.Ecs.Rendering;
using Ion.Extensions.Scenes;

var builder = IonApplication.CreateBuilder(args);
builder.AddEcsRendering();
// Scene systems are created from the scene's scope, once per load, whatever their registration lifetime.
builder.AddSystem<MenuSystem>().AddSystem<LevelSystem>();

using var game = builder.Build();
game.UseIon();
game.UseScene(Scene.Menu, scene => scene.UseEcsRendering().UseSystem<MenuSystem>());
game.UseScene(Scene.Level, scene => scene.UseEcsRendering().UseSystem<LevelSystem>());
game.Run();

public enum Scene
{
    Menu = 1,
    Level,
}
```

The first registered scene loads when the game starts. Any system switches scenes by emitting an event:

```csharp
public sealed class MenuSystem(IInputState input, IEvents events)
{
    [Update]
    public void Update(GameTime dt)
    {
        if (input.Pressed(Key.Enter)) events.EmitChangeScene(Scene.Level, SceneTransition.Fade(0.5f));
    }
}
```

## Registering scenes

| Call | Does |
|---|---|
| `builder.AddIon()` (or `builder.AddScenes()`, which is `AddIon()`) | Registers the scene services: `SceneSystem` and `ICurrentScene`. |
| `services.AddScenes()` | The same on an `IServiceCollection`, for games that compose the engine from parts. |
| `game.UseScene(int sceneId, Action<ISceneBuilder> configure)` | Registers a scene. The first call also adds `SceneSystem` to the schedule. |
| `game.UseScene<TScene>(TScene sceneId, Action<ISceneBuilder> configure)` | The same with an enum value as the id. |

`UseScene` can be called before or after your other `UseSystem` calls: scene steps always run at the scene system's
fixed order (see [Ordering](#ordering)).

:::note[Any id works]
Every `int` is a valid scene id, 0 and negative ids included, so an enum that starts at 0 works. Whether a scene is
loaded is its own state (`SceneSystem.HasScene`, `ICurrentScene.HasScene`), not a reserved id: before the first scene
loads `CurrentSceneId` reads 0 and `HasScene` is false.
:::

### The configure callback

`configure` runs **every time the scene loads**, building the scene's schedule in the new scope, and once more when the
application's schedule is built, to validate the scene and include it in `--Ion:PrintSchedule=true`. Keep it
declarative: add systems and steps, do not create entities or start work there (do that in an `[Init]` step).

`ISceneBuilder` is an `IScheduleBuilder`, so everything you can add to the application you can add to a scene:

| On `ISceneBuilder` | Adds |
|---|---|
| `scene.UseSystem<T>()`, `scene.UseSystem(type)` | A system, created from the scene's scope at load (see below) |
| `scene.Init(...)`, `scene.First(...)`, `scene.Update(...)`, `scene.Render(...)`, ... | Function steps with up to four service parameters, resolved from the scene's scope |
| `scene.UseEcs()` | The ECS systems for the scene's world (commands, propagation, sprite animation) |
| `scene.UseEcsRendering()`, `scene.UseEcsRendering3D()` | The 2D or 3D extraction for the scene's world, with `UseEcs()` |
| `scene.UseInit(next => ...)`, `scene.UseUpdate(next => ...)`, ... | Legacy middleware (warning ION010) |
| `SceneId`, `Configuration`, `Services` | The id, the app configuration and the scene's scoped services |

Function steps are the quickest way to add small behaviour. The Scenes sample draws a colored square per scene:

```csharp
game.UseScene(Scene.Menu, scene =>
{
    scene.Render((GameTime dt, ISpriteBatch spriteBatch) => spriteBatch.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90)));
});
```

## Scene-scoped systems and the scene's world

Everything a scene resolves comes from its scope, created when the scene loads:

- Services registered **scoped** get one instance per load, disposed (with `IDisposable.Dispose`) when the scene unloads.
- Services registered **transient** get a new instance per resolution.
- **Singletons** are shared with the whole application and survive scene changes, with one exception: the scene's
  **systems** (below).
- `ICoroutineRunner` is the scene's own runner: coroutines started through it stop when the scene unloads (see
  [Coroutines and scenes](/Ion/ecs/coroutines/#coroutines-and-scenes)).

`World`, `Commands` and `NameRegistry` resolve to the scene's own instances when resolved from the scene's scope. The
scene's world is created on first use and released when the scene unloads, with every entity in it.

```csharp
public sealed partial class LevelSystem(World world, Commands commands, IAssetManager assets)
{
    [Init]
    public void Load(GameTime dt)
    {
        // Runs each time the Level scene loads, into a fresh, empty world.
        var block = assets.Load<ITexture2D>("block.png");
        for (var i = 0; i < 10; i++) world.Create(new Transform2D(new Vector2(100 + i * 110, 80)), new Sprite(block, new Vector2(100, 40)));
    }

    [Update, Query, All<Falling>]
    private void Fall(Entity entity, ref Transform2D transform, [Data] float dt)
    {
        transform.Position += new Vector2(0, 200 * dt);
        if (transform.Position.Y > 800) commands.Destroy(entity);
    }
}

public record struct Falling;
```

### Scene systems and registration lifetimes

A system added with `scene.UseSystem<T>()` is always created from the scene's scope, once per load, so its `World`,
`Commands` and other scoped services are the scene's, whatever lifetime it was registered with:

| Registered with | In the scene |
|---|---|
| `builder.AddSystem<T>()`, `services.AddSingleton<T>()`, or a singleton factory (`AddSingleton(sp => new T(...))`) | A new instance per load, built from the scene's scope (the factory gets the scene's provider), disposed when the scene unloads. The root schedule, if it also uses `T`, keeps the singleton. |
| `services.AddScoped<T>()` | The scope's instance: one per load, disposed with the scene. |
| `services.AddTransient<T>()` | A new instance per load. |
| `services.AddSingleton(new T(...))` (an instance) | Error `ION015` at build: the instance was built outside any scene and cannot be created again. Register the type or a factory instead. |

So `builder.AddSystem<T>()` is fine for scene systems. The reverse still fails: a scoped system in the root schedule is
error `ION006`. A singleton used by both the root schedule and a scene runs as two instances (the root's and the
scene's): keep state that must be shared in a separate singleton service that both inject.

:::note[Earlier releases]
In earlier releases a scene system registered as a singleton was built from the root provider, so its `World` was the
**root** world and its entities outlived the scene. The scene system now creates it from the scene's scope (see the
[changelog](/Ion/reference/changelog/)).
:::

## Changing scenes

Emit a `ChangeSceneEvent`, most conveniently with the `IEvents` extensions:

| Call | |
|---|---|
| `events.EmitChangeScene(int nextSceneId)` | |
| `events.EmitChangeScene<TScene>(TScene nextSceneId)` | enum overload |
| `events.EmitChangeScene(nextSceneId, SceneTransition.Fade(0.5f))` | with a [transition](#transitions) (enum overload too) |
| `events.Emit(new ChangeSceneEvent(nextSceneId, transition))` | the raw event (the transition is optional) |

Without a transition, the switch happens at the start of the **next** frame, in the scene system's First step:

1. If several changes were emitted, the latest wins. An unknown id logs an error and the current scene stays. Asking for
   the scene that is already active does nothing (it is not reloaded).
2. The current scene's Destroy stage runs, then its scope is disposed: scoped services are disposed and its ECS world is
   released.
3. A new scope is created and the new scene's schedule is built (its `configure` runs).
4. The new scene's Init stage runs, then its First stage, in the same frame.

With a [transition](#transitions), steps 2 to 4 run in the First step that ends the transition's out phase.

When the application exits, the active scene's Destroy stage runs once (also on dispose, if the Destroy stage did not
run).

| To read | Use |
|---|---|
| Whether a scene is loaded | `ICurrentScene.HasScene` (`IsRoot` is its opposite), or `SceneSystem.HasScene` |
| The active scene's id | `ICurrentScene.SceneId`, or `SceneSystem.CurrentSceneId` (0 when none is loaded: check `HasScene`) |
| The active scene's schedule and services | `SceneSystem.ActiveScene` (`SceneInstance`: `Id`, `Name`, `Schedule`, `Services`) |
| Whether a change is under way or pending | `SceneSystem.IsLoading` (true through a transition's out phase) |
| The running transition | `SceneSystem.Transition` (see [Transitions](#transitions)) |

The [remote protocol](/Ion/tooling/remote-protocol/) rejects mutations while `IsLoading` is true, because the world they
address is about to be replaced.

## Ordering

`SceneSystem` has one step per stage at `StageOrder.Scenes` (-500, the end of the engine setup band). The active
scene's whole schedule runs inside that step:

```text
Update:  -600 coroutines  |  -550 UI frame  |  -500 [active scene: its steps by their own orders]  |  0 your app steps  |  950 ECS commands
```

So scene steps run after the engine's setup steps (window, input, sprite batch scope) and before the application's own
steps at order 0, whatever the registration order. Use a lower order or `[Before<SceneSystem>]` on an application step
that must run before the scene. Inside the scene, steps are ordered with the usual rules, and the scene's own ECS steps
use the same constants (`TransformPropagation`, `Extract`, `Ecs`) relative to the scene's other steps. See
[Stage order](/Ion/reference/stage-order/).

## Transitions

A scene change can be animated. Pass a `SceneTransition` with the change:

```csharp
events.EmitChangeScene(Scene.Level, SceneTransition.Fade(0.5f));          // 0.25 s out, 0.25 s in
events.EmitChangeScene(Scene.Level, SceneTransition.Fade(0.4f, 0.2f));    // 0.4 s out, 0.2 s in
events.Emit(new ChangeSceneEvent((int)Scene.Level, SceneTransition.Fade(0.5f)));
```

A transition has two phases, timed in game time (`GameTime.Delta`, so a fixed clock makes it deterministic):

1. **Out.** It starts in the First step of the frame after the event. The old scene keeps running (its steps, systems,
   world and coroutines) while the transition covers it. `SceneSystem.IsLoading` is true.
2. When the out phase ends, in that frame's First step, the old scene's Destroy stage runs, its scope is disposed and the
   new scene loads (its Init, then First).
3. **In.** The new scene runs while the transition uncovers it.

`SceneSystem.Transition` is a `SceneTransitionState` for the code that draws the transition:

| Member | |
|---|---|
| `Transition` | The `SceneTransition`: `Kind` (`Fade`, `Custom`), `OutDuration`, `InDuration`, `Style` |
| `Phase` | `TransitionPhase.None`, `Out` or `In` |
| `Progress` | 0 to 1 through the current phase |
| `Coverage` | 0 (scene fully visible) to 1 (fully covered): rises through the out phase, falls through the in phase |
| `IsActive` | Whether a transition runs |

It is updated in the scene system's First step (`StageOrder.Scenes`), so every later step of the frame sees the same
value. With frames of 0.125 s, `Fade(0.5f)` reads (frame 1 is the frame after the event):

```text
frame          1     2     3     4     5
active scene   old   old   new   new   new
phase          Out   Out   In    In    None
coverage       0     0.5   1     0.5   0
```

A new request while a transition runs does not jump: a different scene during the out phase becomes the target and the
cover keeps going from where it got to; the scene still on screen, asked for during the out phase, turns the transition
around and uncovers it from the coverage it reached (no reload); a change during the in phase covers again from the
current coverage. A change without a transition cancels the running one and switches at once. `Fade(0f, 0.5f)` loads
the new scene at once and only fades it in.

### The built-in fade

`SceneTransition.Fade` is drawn by `SceneFadeSystem`: a rectangle over the whole window, in `Color` (black by default)
at `Coverage` opacity, drawn with the 2D renderer at `StageOrder.SceneTransition` (750), over the scene, your own
drawing and the UI, under the metrics overlay. `AddIon()`/`UseIon()` register and add it; a game composed from parts
calls `services.AddSceneFade()` and `game.UseSceneFade()` (the Scenes sample does). Headless, the rectangle goes to the
recording sprite batch like any other draw. To fade to another color:

```csharp
game.Services.GetRequiredService<SceneFadeSystem>().Color = Color.White;
```

### Custom transitions

`SceneTransition.Custom(style, outSeconds, inSeconds)` runs the same phases but draws nothing: draw it yourself from
`SceneSystem.Transition`, telling your transitions apart by `Style`:

```csharp
public sealed class WipeSystem(SceneSystem scenes, ISpriteBatch sprites, IWindow window)
{
    public const int Wipe = 1;

    [Render(Order = StageOrder.SceneTransition)]
    public void Draw(GameTime dt)
    {
        var t = scenes.Transition;
        if (t.Transition.Kind != TransitionKind.Custom || t.Transition.Style != Wipe) return;
        sprites.DrawRect(Color.Black, Vector2.Zero, new Vector2(window.Size.X * t.Coverage, window.Size.Y));
    }
}

events.EmitChangeScene(Scene.Level, SceneTransition.Custom(WipeSystem.Wipe, 0.3f, 0.3f));
```

Register it as a singleton and add it to the root schedule (`builder.AddSystem<WipeSystem>()`,
`game.UseSystem<WipeSystem>()`) so it draws across the change.

## Testing scenes

With `IonTestHost`, emit the event and step:

```csharp
using var run = IonTestHost.RunEntryPoint<Program>(1);
var scenes = run.Get<SceneSystem>();
Assert.Equal((int)Scene.Menu, scenes.CurrentSceneId);
```

```csharp
using var host = new IonTestHost()
    .Configure(services => services.AddEcs())
    .ConfigureApp(app =>
    {
        app.UseScene(1, scene => scene.UseEcs().Init((GameTime dt, World world) => world.Create(new Transform2D())));
        app.UseScene(2, scene => scene.UseEcs());
    });

host.Step();
var worlds = host.Get<EcsWorlds>();
Assert.Equal(1, worlds.EntityCount);           // the scene's world (the root world is empty)

host.Events.EmitChangeScene(2);
host.Step(2);
Assert.Equal(2, host.Get<SceneSystem>().CurrentSceneId);
Assert.Equal(0, worlds.EntityCount);           // scene 1's world was released with its scope
```

Transitions run on the host's fixed clock, so a test can step through one frame by frame and read
`SceneSystem.Transition` (or the fade in `host.SpriteBatch.LastFrame`). The loop clamps a frame to 0.1 s of game time,
so a host created with a longer frame time advances transitions by 0.1 s per frame.

`run.WorldJson()` serializes the most recent live world, which is the active scene's when one is loaded. See
[Testing](/Ion/tooling/testing/).

## The scene generator

`Ion.Extensions.Scenes.Generators` is a small source generator that some projects reference as an analyzer (the Scenes
and Breakout samples do). It adds:

- a `[ScenesEnum]` marker attribute (namespace `Ion.Extensions.Scenes`) for scene enums, and
- `UseInit<TService0, ...>` through `UseDestroy<...>` overloads on `ISceneBuilder` (one to eight services) that resolve
  services from the scene's scope for legacy middleware (`scene.UseUpdate<ISpriteBatch>((next, sprites) => dt => ...)`).
  These are middleware, so they get warning ION010; prefer systems and function steps.

The enum overloads it used to generate are now the generic `UseScene<TScene>` and `EmitChangeScene<TScene>` in the
library, so the main Ion generator can compile scene registrations into the generated schedule. You do not need the
scene generator for anything on this page.

## Common problems

| Symptom | Cause and fix |
|---|---|
| Build error ION015 | A scene uses a system registered as an instance (`AddSingleton(new T())`). Register the type (`builder.AddSystem<T>()`) or a factory instead. |
| Build error ION006 | A scoped system or scoped step parameter in the root schedule. Move it into a scene, or register it as a singleton or transient. |
| "Tried to load unknown scene" in the log | The id was never registered with `UseScene`. The current scene stays. |
| Entities created in a scene survive the scene change | They were created in the root world: the system that creates them runs in the root schedule, or holds a `World` resolved from the root provider. Add the system with `scene.UseSystem<T>()` and inject `World` in its constructor. |
| Nothing draws in a scene that creates sprites | The scene's schedule needs its own extraction: `scene.UseEcsRendering()` (the root's `UseEcsRendering()` draws the root world only). |
| A coroutine keeps running after the scene unloaded | It was started on the application's runner (a root system, or the concrete `CoroutineRunner`). Start it through the `ICoroutineRunner` resolved from the scene's scope. |
| `CurrentSceneId` is 0 | Either no scene is loaded or the scene with id 0 is active. Check `HasScene`. |

## See also

- [Scenes example](/Ion/examples/scenes/)
- [Services and configuration](/Ion/concepts/services-and-configuration/)
- [Events](/Ion/concepts/events/)
- [Coroutines](/Ion/ecs/coroutines/)
- [ECS rendering](/Ion/ecs/ecs-rendering/)
