---
title: Components and serialization
description: Design components as small structs, register them for NativeAOT, save and load worlds as JSON or binary with AddEcsSerialization, and expose components to the remote protocol.
sidebar:
  order: 4
---

Components are the data of your entities. In Ion they are plain C# structs stored by Arch; this page covers how to design
them, the built-in ones, how to register them so NativeAOT builds work, and how the same registration drives world
snapshots and remote inspection.

## Designing components

A component is any struct. `record struct` is the idiomatic choice: value equality, a readable `ToString()` in logs and
test failures, and `with` expressions.

```csharp
using System.Numerics;

public record struct Velocity(Vector2 Value);   // data
public record struct Health(int Current, int Max);
public record struct Ball;                       // a tag: no data, used only in filters
public record struct Block(int Row, int Column);
```

Guidelines that follow from how Arch stores components (one array per component type per chunk):

| Guideline | Why |
|---|---|
| Keep components small and focused (`Velocity`, not `PhysicsState` with twenty fields) | Queries read only the arrays they ask for; small components mean more entities per cache line. |
| Prefer unmanaged fields (numbers, vectors, `Entity`) | Unmanaged components serialize with `AddUnmanaged` and need no GC scanning. |
| Use tags (empty structs) for categories and states | `All<Ball>`, `None<Frozen>` filter at the archetype level, for free per entity. |
| Reference other entities with an `Entity` field | The serializers can remap it (see below). Check `world.IsAlive` before use. |
| Put behaviour in systems, not in components | Components with methods are fine for small helpers (`Aabb2D.Intersects`), but logic belongs in steps. |
| Avoid adding and removing components every frame | Each add or remove moves the entity to another archetype. A `bool` field or a tag changed rarely is cheaper than churn. |

:::note[Class components]
Arch can store a class as a component (Ion does this for `Camera2D`), but `[Query]` parameters must be structs
(ION303). Read a class component through the `Entity` (`world.Get<Camera2D>(entity)`) or with Arch's own queries.
:::

## Built-in components

The ECS module (`Ion.Extensions.Ecs`) registers these with Arch on startup:

| Component | Kind | Used by |
|---|---|---|
| `Transform2D` | local 2D position, rotation (radians), scale | propagation, sprites, 2D physics |
| `GlobalTransform2D` | computed world transform (read only) | extraction, bounds |
| `Transform` (from `Ion.Extensions.Graphics`) | local 3D position, rotation (quaternion), scale | propagation, 3D extraction, 3D physics |
| `GlobalTransform` | computed world matrix (read only) | 3D extraction |
| `Parent`, `Children` | hierarchy links | propagation, `DestroyRecursive` |
| `Sprite` | texture, source rectangle, size, color, origin, depth, flip | 2D extraction |
| `SpriteAnimation` | flip-book frames, rate, loop, pause | `SpriteAnimationSystem` |
| `Aabb2D` | computed world bounds of a sprite | culling, your own hit tests |
| `Hidden`, `Visible` | tags | extraction visibility |
| `MainCamera` + `Camera2D` | tag + the engine's 2D camera class | 2D extraction |
| `EntityName` | a string name | `NameRegistry`, remote protocol |
| `MeshRenderer`, `Camera`, `DirectionalLight`, `PointLight`, `SpotLight`, `SceneEnvironment` | 3D types from `Ion.Extensions.Graphics` | 3D extraction |

See [Transforms](/Ion/ecs/transforms/) and [ECS rendering](/Ion/ecs/ecs-rendering/) for how each is used.

:::caution[Use the constructors]
`default(Transform2D)` has a zero scale and `default(Sprite)` has no texture and a top-left origin. Create them with
`new Transform2D(position)` and `new Sprite(texture, size)`, which set scale 1 and a centered origin.
:::

## Registering components with Arch (NativeAOT)

Arch creates a component's chunk arrays with `Array.CreateInstance` unless the array type was registered up front, which
NativeAOT cannot do for a type it has not seen. Every component a game stores must therefore be registered before the
first entity with it is created. Most of this is automatic:

| Components | Registered by |
|---|---|
| The built-in ones | `AddEcs()` (and `new Commands(world)`, `new EcsWorlds(...)`) |
| Every type used in a `[Query]` method: parameters and `All`/`Any`/`None` filters | A module initializer emitted by `Ion.Generators` |
| Components your game only creates and reads through `World` | You: `EcsComponents.Register<T>()` |

```csharp
public static class GameComponents
{
    // Call once at startup, before creating entities (the Breakout ECS sample calls it from AddBreakout).
    public static void Register()
    {
        EcsComponents.Register<Block>();
        EcsComponents.Register<Paddle>();
        EcsComponents.Register<Ball>();
        EcsComponents.Register<Wall>();
    }
}
```

`Register<T>()` is thread safe and idempotent; `EcsComponents.IsRegistered<T>()` tells you whether it ran. With JIT
(Debug, `dotnet run`) an unregistered type still works, so the mistake only shows in a NativeAOT build as a
`NotSupportedException` naming the component. See [Native AOT](/Ion/platforms/native-aot/).

## World serialization

`AddEcsSerialization` adds Ion's world serializers and a component registry. It is opt-in so a game that never saves
worlds does not carry System.Text.Json's serializer into its NativeAOT image.

```csharp title="Program.cs"
var builder = IonApplication.CreateBuilder(args);
builder.AddIon()
    .AddEcs()
    // Components registered here are saved by the world serializers and are readable and writable over the remote
    // protocol (world.query, world.mutate_components). Register every component you add.
    .AddEcsSerialization(components => components
        .AddUnmanaged("Velocity", GameJson.Default.Velocity)
        .AddTag<Ball>("Ball"))
    .AddSystem<BallSystem>();
```

```csharp title="Game.cs"
using System.Text.Json.Serialization;

public record struct Velocity(float X, float Y);
public record struct Ball;

/// <summary>Source-generated JSON metadata of the game's components (works under NativeAOT).</summary>
[JsonSourceGenerationOptions(IncludeFields = true)]
[JsonSerializable(typeof(Velocity))]
internal sealed partial class GameJson : JsonSerializerContext
{
}
```

What it registers (on the service collection, or through the builder overload which also calls `AddEcs()`):

| Service | Lifetime | Notes |
|---|---|---|
| `ComponentSerializerRegistry` | singleton | The built-in components, then yours. Calling `AddEcsSerialization` again adds to the same registry. |
| `JsonWorldSerializer` | singleton | Human-readable, used for snapshot tests. |
| `BinaryWorldSerializer` | singleton | Compact. |
| `IWorldSerializer` | singleton | The binary one. |

### Registering your components

`ComponentSerializerRegistry` methods return the registry, so calls chain. The name is what files store, so keep it
stable.

| Method | For | Binary form |
|---|---|---|
| `AddUnmanaged<T>(name, JsonTypeInfo<T>)` | Unmanaged structs without `Entity` fields | The struct's memory (little-endian) |
| `AddTag<T>(name)` | Empty tag structs | Nothing (presence only) |
| `Add<T>(name, JsonTypeInfo<T>, write, read)` | Anything else: managed fields, `Entity` references, versioned formats | Your `BinaryComponentWriter<T>` and `BinaryComponentReader<T>` |

Registering the same name or the same type twice throws `InvalidOperationException`.

A component that references entities or holds managed data needs codecs. The `EntityWriteMap` and `EntityReadMap`
passed to them turn entity references into positions in the saved list and back:

```csharp
public record struct Target(Entity Value, float Weight);

builder.AddEcsSerialization(components => components
    .Add("Target", GameJson.Default.Target,
        static (BinaryWriter writer, in Target value, EntityWriteMap entities) =>
        {
            writer.Write(entities.IndexOf(value.Value));   // -1 when not saved
            writer.Write(value.Weight);
        },
        static (reader, entities) => new Target(entities.EntityAt(reader.ReadInt32()), reader.ReadSingle())));
```

`GameJson.Default.Target` needs `[JsonSerializable(typeof(Target))]` on the context. For the JSON side of a component
with `Entity` fields, also add Ion's public `EntityJsonConverter` to the context's converters
(`[JsonSourceGenerationOptions(IncludeFields = true, Converters = [typeof(EntityJsonConverter)])]`) so the entity is
written as its saved position and resolved on load.

### What is and is not saved

`ComponentSerializerRegistry.CreateDefault()` registers `Transform2D`, `Transform`, `Parent`, `Children`, `EntityName`,
`SpriteAnimation`, `Camera2D` and the tags `Hidden`, `Visible` and `MainCamera`. Not saved:

- Computed components (`GlobalTransform2D`, `GlobalTransform`, `Aabb2D`): the propagation recomputes them after a load.
- `Sprite`: it holds a texture. Register your own codec that saves the asset path if you need it.
- The 3D renderer components (`MeshRenderer`, cameras, lights): they hold renderer handles. Serializing them is planned.
- Any component without a registration: skipped silently on save.

### Saving and loading

```csharp
public sealed class SaveSystem(World world, IWorldSerializer serializer, IInputState input)
{
    [Update]
    public void Update(GameTime dt)
    {
        if (input.Pressed(Key.F5))
        {
            using var file = File.Create("save.ionw");
            serializer.Serialize(world, file);
        }

        if (input.Pressed(Key.F9) && File.Exists("save.ionw"))
        {
            world.Clear();                                  // start from an empty world
            using var file = File.OpenRead("save.ionw");
            IReadOnlyList<Entity> loaded = serializer.Deserialize(file, world);
        }
    }
}
```

`Deserialize` creates the saved entities in the target world (after any entities already there) and returns them in
saved order. Loading is a structural change: do it outside queries. `Parent`/`Children` links are remapped to the new
entities, so a hierarchy survives the round trip into any world.

The JSON format is `{"format":"ion-world","version":1,"entities":[{...}, ...]}`, one object per entity with its
registered components by name. Vectors are arrays and entity references are positions:

```json
{
  "format": "ion-world",
  "version": 1,
  "entities": [
    { "Transform2D": { "Position": [1, 2], "Rotation": 0, "Scale": [1, 1] }, "EntityName": { "Value": "a" }, "Children": [1] },
    { "Transform2D": { "Position": [0, 0], "Rotation": 0, "Scale": [1, 1] }, "EntityName": { "Value": "b" }, "Parent": { "Value": 0 } }
  ]
}
```

`JsonWorldSerializer` also has `ToJson(world)`, `FromJson(json, world)` and an `Indented` init property;
`BinaryWorldSerializer` has `ToBytes(world)` and `FromBytes(bytes, world)`. The binary format stores a table of component
names and a byte length per component, so a reader skips components it does not know and keeps the rest.

### Snapshot tests

The test host's `run.WorldJson()` serializes the most recent world (the active scene's, or the root one) with the
registry, normalized for stable diffs. The ECS template's tests compare it with a committed file:

```csharp title="GameTests.cs"
[Fact]
public void WorldAfter120FramesMatchesTheSnapshot()
{
    using var run = IonTestHost.RunEntryPoint<Program>(120, host => host.WithConfiguration("Ion:Seed", "1"));
    JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("world-120.json"));
}
```

See [Snapshots and goldens](/Ion/tooling/snapshots-and-goldens/).

## Remote inspection

The ECS module registers the remote protocol's `world.*` methods and `registry.schema`. The components the protocol can
read and write are exactly the registry's: the built-in ones plus what you add with `AddEcsSerialization`. Other
components are listed by type name (`world.list_components`) but cannot be read or written. This is why the template's
comment says to register every component you add.

| Method | Kind | Does |
|---|---|---|
| `registry.schema` | read | The remote-visible components with their JSON Schema. |
| `world.list` | read | The live worlds (root and one per loaded scene) with entity counts. |
| `world.query` | read | Entities by `with`/`without` components and `name` (exact, or a prefix ending in `*`), with their components. |
| `world.get_components`, `world.list_components` | read | One entity's components. |
| `world.insert_components`, `world.mutate_components`, `world.remove_components` | mutate | Change one entity. `mutate_components` sets a field by path (`Position.0`). |
| `world.spawn`, `world.despawn` | mutate | Create or destroy an entity. |

Entities are addressed by id or by `EntityName`, and entity references are entity ids on the wire. Methods take an
optional `world` index and default to the most recently created live world. Mutations are applied on the game thread at
the end of the frame (`StageOrder.Remote`, after the ECS command playback) and are rejected while a scene is loading.

```bash
ion remote world.query '{"with":["Ball"],"components":["Transform2D","Velocity"]}'
```

See [Remote protocol](/Ion/tooling/remote-protocol/) and [MCP server](/Ion/tooling/mcp-server/).

## See also

- [Entities and commands](/Ion/ecs/entities-and-commands/)
- [Transforms](/Ion/ecs/transforms/)
- [Storage](/Ion/concepts/storage/)
- [Testing](/Ion/tooling/testing/)
- [Native AOT](/Ion/platforms/native-aot/)
