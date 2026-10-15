---
title: Puppet
description: Skinned meshes and skeletal animation on the ECS - a glTF character spawned three times, idling, cross-fading between clips and walking in a circle.
sidebar:
  order: 13
---

**Source:** [`Ion.Examples/Ion.Examples.Puppet`](https://github.com/jimbuck/Ion/tree/main/Ion.Examples/Ion.Examples.Puppet).

Three copies of one glTF character stand on a field. The left one idles. The one in the middle cross-fades from Idle to
Walk to Wave every three seconds. The right one walks in a circle. All three are skinned on the GPU, and their shadows
follow their poses.

![Three puppets: idling, waving and walking](./images/puppet_640x360.png)

## What it shows

- glTF skins and animations: `Load<IModel>` gives `model.Skins` (a `Skeleton` per skin) and `model.Animations` (an
  `AnimationClip` per animation).
- `SpawnModel` on a skinned model: an `Animator` on the root, and a `SkinnedMesh` on the skinned primitive's entity.
- `AnimationPlayer.Play` and `CrossFade`, through each root's `Animator`.
- The ECS module's `SkeletalAnimationSystem` (part of `UseEcs()`) and the 3D extraction, which draws each skinned mesh
  posed by its animator, at the root's world matrix.
- Moving a skinned character: the walker's root transform goes round a circle while its Walk clip plays in place.

## Run it

```bash
dotnet run --project Ion.Examples/Ion.Examples.Puppet
npm run example:puppet                                   # the same, in Release
```

## The character

`Assets/puppet.gltf` is generated, not modelled. `tools/make_puppet.py` (plain Python 3, no packages) builds a blocky
figure of tubes and boxes on 12 joints:

- Hips, spine, chest and head.
- Two arms of upper and lower joints.
- Two legs of upper and lower joints.

The script weights each part to its joints, with smooth blends at the waist, shoulders, elbows, hips and knees. It keys
three looping clips at 15 to 20 keys per second:

- Idle, 2 seconds.
- Walk, 1 second.
- Wave, 1.5 seconds.

It writes one glTF file with an embedded buffer. To change the character, edit the script and run
`python make_puppet.py` from the `tools` folder.

## Spawning and playing

Each puppet is the model spawned at its place, with a clip started on its root's player:

```csharp title="Program.cs"
private Entity Spawn(IModel model, Vector3 position, string clip, float startTime)
{
	var root = world.SpawnModel(model, new Transform(position));
	var player = world.Get<Animator>(root).Player;
	player.Play(model.FindAnimation(clip) ?? throw new InvalidOperationException($"The puppet has no '{clip}' clip."));
	player.Time = startTime;
	return root;
}
```

The middle puppet changes clips with a cross-fade. Both clips play during the 0.4 seconds of the fade, so the change has
no pop:

```csharp title="Program.cs"
world.Get<Animator>(Performer).Player.CrossFade(model.FindAnimation(Sequence[clip])!, FadeSeconds);
```

The walker's root transform is set every Update to a point on its circle, turned to face along it. The Walk clip moves
only the legs, arms and hips, and the root carries the puppet along. Its speed along the circle roughly matches the
clip's stride.

The HUD shows the middle puppet's state from its player: `Clip`, `Next` and `FadeWeight`.

## Tests

`Ion.Examples.Puppet.Tests` runs the sample's own `Program.cs`:

- Headless, without a GPU, the three puppets play their clips. After three seconds the middle one has started one fade,
  and once it has finished it plays Walk alone.
- The walker's knee joint moves between frames, and its root stays on its circle.
- Rendered headless on Vulkan and OpenGL ES: the ground and the three puppets are drawn, they cast shadows, and their
  blue shirts are in the picture.

## See also

- [Skinned meshes and animation](/Ion/rendering/3d/animation/)
- [Models and glTF](/Ion/rendering/3d/models-gltf/)
- [Model](/Ion/examples/model/): a rigid glTF model on the ECS
