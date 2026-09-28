---
title: Scenes
description: Split a game into scenes with Ion.Extensions.Scenes, each with its own schedule, service scope and ECS world, switch between them with ChangeSceneEvent, and handle transitions yourself.
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
// Scene systems are resolved from the scene's scope: register them scoped (one instance per load) or transient.
builder.Services.AddScoped<MenuSystem>().AddScoped<LevelSystem>();

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
        if (input.Pressed(Key.Enter)) events.EmitChangeScene(Scene.Level);
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

:::caution[Scene ids start at 1]
Id `0` means "no scene" (`CurrentScene.Root`, and `SceneSystem.CurrentSceneId` when nothing is loaded), so a scene
registered with id 0 never loads. Start your enum at 1, as the samples do.
:::

### The configure callback

`configure` runs **every time the scene loads**, building the scene's schedule in the new scope, and once more when the
application's schedule is built, to validate the scene and include it in `--Ion:PrintSchedule=true`. Keep it
declarative: add systems and steps, do not create entities or start work there (do that in an `[Init]` step).

`ISceneBuilder` is an `IScheduleBuilder`, so everything you can add to the application you can add to a scene:

| On `ISceneBuilder` | Adds |
|---|---|
| `scene.UseSystem<T>()`, `scene.UseSystem(type)` | A system, resolved from the scene's scope at load |
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
- **Singletons** are shared with the whole application and survive scene changes.

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

:::danger[Do not register ECS scene systems as singletons]
`builder.AddSystem<T>()` registers a singleton. A singleton is built from the root provider, so its `World` is the
**root** world, not the scene's: its entities outlive the scene and its queries see the wrong entities. Register scene
systems with `builder.Services.AddScoped<T>()` (or `AddTransient<T>()`). The reverse also fails: a scoped system in the
root schedule is error ION006.
:::

Systems without scene state (a frame timer, say) can stay singletons and be used by several scenes; the Scenes sample's
`TestMiddleware` is registered with `AddSystem` and used by one scene.

## Changing scenes

Emit a `ChangeSceneEvent`, most conveniently with the `IEvents` extensions:

| Call | |
|---|---|
| `events.EmitChangeScene(int nextSceneId)` | |
| `events.EmitChangeScene<TScene>(TScene nextSceneId)` | enum overload |
| `events.Emit(new ChangeSceneEvent(nextSceneId))` | the raw event |

The switch happens at the start of the **next** frame, in the scene system's First step:

1. If several changes were emitted, the latest wins. An unknown id logs an error and the current scene stays.
2. The current scene's Destroy stage runs, then its scope is disposed: scoped services are disposed and its ECS world is
   released.
3. A new scope is created and the new scene's schedule is built (its `configure` runs).
4. The new scene's Init stage runs, then its First stage, in the same frame.

When the application exits, the active scene's Destroy stage runs once (also on dispose, if the Destroy stage did not
run).

| To read | Use |
|---|---|
| The active scene's id (0 when none) | `ICurrentScene.SceneId` (`IsRoot` when none), or `SceneSystem.CurrentSceneId` |
| The active scene's schedule | `SceneSystem.ActiveScene` (`SceneInstance`: `Id`, `Name`, `Schedule`) |
| Whether a change is under way or pending | `SceneSystem.IsLoading` |

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

Ion does not run scene transitions for you. A `Transition` base class (with `Duration`, `State`, `Value`, `Update` and
`Render`) ships in `Ion.Extensions.Scenes.Abstractions`, but the scene system does not use it; scene changes are
immediate. Build a fade as an application-level system that covers the screen, switches the scene when it is fully
covered, and uncovers:

```csharp
public sealed class FadeSystem(IEvents events, ISpriteBatch sprites, IWindow window)
{
    private const float HalfDuration = 0.25f;
    private float _time;
    private int _next;
    private bool _fadingOut;
    private bool _active;

    public void FadeTo(Scene next)
    {
        _next = (int)next;
        _time = 0f;
        _fadingOut = true;
        _active = true;
    }

    [Update]
    public void Step(GameTime dt)
    {
        if (!_active) return;
        if (_fadingOut)
        {
            _time += dt.Delta;
            if (_time >= HalfDuration)
            {
                _fadingOut = false;
                events.EmitChangeScene(_next);   // switches at the start of the next frame, under a black screen
            }
        }
        else
        {
            _time -= dt.Delta;
            if (_time <= 0f) _active = false;
        }
    }

    // Order 0 draws over the extracted sprites; raise it (below StageOrder.Ui) to cover the UI too.
    [Render]
    public void Draw(GameTime dt)
    {
        if (!_active) return;
        var alpha = Math.Clamp(_time / HalfDuration, 0f, 1f);
        sprites.DrawRect(new Color(Color.Black, alpha), Vector2.Zero, window.Size);
    }
}
```

Register it as a singleton (`builder.AddSystem<FadeSystem>()`, `game.UseSystem<FadeSystem>()`) so it survives the change,
and inject it into scene systems that call `fade.FadeTo(Scene.Level)`.

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

## See also

- [Scenes example](/Ion/examples/scenes/)
- [Services and configuration](/Ion/concepts/services-and-configuration/)
- [Events](/Ion/concepts/events/)
- [Coroutines](/Ion/ecs/coroutines/)
- [ECS rendering](/Ion/ecs/ecs-rendering/)
