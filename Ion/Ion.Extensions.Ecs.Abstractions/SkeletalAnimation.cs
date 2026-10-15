using Arch.Core;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Ecs;

/// <summary>
/// Plays skeletal animation on an entity: the skeletal animation system calls <see cref="AnimationPlayer.Update"/> on
/// <see cref="Player"/> every Update (at <see cref="StageOrder.SpriteAnimation"/>, after the game's steps), and the 3D
/// extraction draws every <see cref="SkinnedMesh"/> that points at this entity posed by it, at this entity's world matrix.
/// <see cref="ModelSpawnExtensions.SpawnModel(World, IModel, in Transform, ModelSpawnOptions?)"/> puts one on a skinned
/// model's root. Not saved by the world serializers (it holds a player).
/// </summary>
/// <param name="Player">The player (start clips with <see cref="AnimationPlayer.Play"/> and <see cref="AnimationPlayer.CrossFade"/>).</param>
public record struct Animator(AnimationPlayer Player);

/// <summary>
/// Marks the entity's <see cref="MeshRenderer"/> as a skinned mesh posed by the <see cref="Animator"/> of
/// <paramref name="Animator"/> (often the model's root, possibly the entity itself). The mesh is drawn at the animator
/// entity's world matrix: the palette places the vertices in model space. Hidden with its own entity (<see cref="Hidden"/>
/// or a hidden ancestor). While the animator entity is gone (or has no <see cref="Ecs.Animator"/>), the mesh is drawn
/// unskinned, in its bind pose, at its own entity's world matrix.
/// </summary>
/// <param name="Animator">The entity with the <see cref="Ecs.Animator"/>.</param>
public record struct SkinnedMesh(Entity Animator);
