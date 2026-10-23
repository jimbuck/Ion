---
title: Scene worlds
description: Replicate the ECS world of a scene besides the root world with scene.UseNetworking("name"), so each client gets a level only while its own level scene is loaded.
sidebar:
  order: 17
---

Each [scene](/Ion/ecs/scenes/) has its own ECS world, thrown away when the scene unloads. The root world is always
networked. A scene's world is networked too when the scene calls `UseNetworking` with a name:

```csharp title="Program.cs"
game.UseIon().UseNetworking();

game.UseScene(GameScene.Menu, scene => scene.UseEcs());
game.UseScene(GameScene.Level, scene => scene
	.UseNetworking("level")       // the scene's world is replicated as "level"
	.UsePhysics2D()
	.UseEcsRendering()
	.UseSystem<LevelSystem>());
```

Inside the level scene, `World` and `INetworkWorld` are the level's. The server networks every entity with a
replicated component in that world, the same way it does in the root world, and `network.Allocate(player)` gives an id
in that world. Each networked world is replicated on its own: it has its own ids, snapshots, acknowledgments and
interest policy. The session, the tick, the messages and the prediction steps are shared by all of them.

## Clients choose their worlds

The server does not follow the clients' scenes. Each process loads its own scenes, and each client gets the worlds it
has open:

- When a client loads a networked scene, it asks the server for that world. The server answers with a full snapshot of
  the world, then sends deltas against that client's acknowledgments.
- When the scene unloads, the world and its replicated entities go away with it, and the client tells the server to
  stop sending it.
- During a transition a client can have two worlds open. Each is replicated until its scene unloads.
- If a client opens a world the server does not have (its own level scene is not loaded yet, or has already unloaded),
  the server answers with `NetworkWorldUnavailable`. The client's world stays empty, and the server remembers the
  request: as soon as it loads that world, it sends it.
- When the server unloads a world, every client that has it open gets `NetworkWorldUnavailable` and loses that world's
  replicated entities.

Deciding which world a player should be in is game logic. A simple approach is to replicate a value in the root world
and have every peer follow it with its own scene change. That is what [Breakout Net](/Ion/examples/breakout-net/) does:

```csharp title="Systems.cs"
// Every peer, the server included: go where the replicated Stage says.
[First(Order = StageOrder.Scenes - 10)]
public void Follow(GameTime dt)
{
	var scene = (int)Stage.Scene;
	if (scenes.IsLoading || (scenes.HasScene && scenes.CurrentSceneId == scene)) return;
	events.Emit(new ChangeSceneEvent(scene));
}
```

A message from the server works too. Because the server answers opens of worlds it does not have yet, the order in
which the server and the clients load their scenes does not matter.

## Names

Both sides must network the same set of names. The set is part of the handshake, and a client whose set differs is
refused with `RegistryMismatch`. Names are registered when the application's schedule is built (each scene's
configuration runs once then), so call `UseNetworking("name")` unconditionally. Registering a new name after the
session has started throws.

Worlds are numbered in ordinal name order: the root world is 0, and named worlds are 1 to 255. `NetworkId.World` holds
that number, so ids are unique across worlds and can be sent in messages unchanged. One world at a time can be live
under a given name.

| API | What it is |
|---|---|
| `scene.UseNetworking(string name)` | Networks the scene's world (with the scene's ECS systems). |
| `INetworkWorlds` | The networked worlds: `Root`, `Live` (the root, then the named worlds that are live), `Names`, `TryGet(name)`, `TryGet(index)`, and `TryGetEntity(NetworkId, out World, out Entity)`, which resolves the id in the world it names. |
| `INetworkWorld.Name`, `INetworkWorld.Index` | The world's name (empty for the root) and index. |
| `INetworkWorld` resolved in a scene | That scene's world (the root's in a scene that is not networked). |
| `NetworkWorldUnavailable(byte World)` | Raised on a client when the server does not have, or no longer has, a world it opened. |

Resolve `INetworkWorlds` from the root to reach a scene's world from a root system, as Breakout Net's headless
autopilot does to follow the balls:

```csharp
if (worlds.TryGet("level", out var level))
{
	foreach (ref var chunk in level.World.Query(in Balls)) { /* ... */ }
}
```

## Prediction and lag compensation

A player has one input per tick, whatever worlds are live. A registered prediction step runs on the player's own
entities in every live world, and each world is reconciled against its own snapshots, so a predicted paddle in the
level scene works like one in the root world. Register prediction steps once, from a root system.

Lag compensation (`TryGetRewindTick`, `WithWorldAtTick`) belongs to an `INetworkWorld`: call it on the world whose
entities you want to rewind.

## Wire format

These are the changes from a root-only session (protocol version 2):

- Snapshot parts carry the world index and the epoch of the client's open, so parts of an earlier open are ignored.
- Owner-authority updates carry the world index.
- Named worlds are acknowledged with a separate packet. The root world's acknowledgment is in every packet header,
  as before.
- `NetworkId` is 6 bytes on the wire (id, owner and world).
- Open, close and unavailable are small reliable packets.

## Not built yet

:::caution[Planned]
Hosting several worlds on a server outside the scene system, for example several rooms at once, or a lobby and a
match. The format already allows it, but there is no API yet to create a networked world that is not a scene's.
:::

## See also

- [Scenes](/Ion/ecs/scenes/)
- [Multiplayer overview](/Ion/networking/multiplayer/overview/)
- [Breakout Net](/Ion/examples/breakout-net/): menu, level, menu, with the level in a scene world
