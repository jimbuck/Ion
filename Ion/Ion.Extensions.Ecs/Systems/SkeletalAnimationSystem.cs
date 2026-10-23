namespace Ion.Extensions.Ecs;

/// <summary>
/// Advances every <see cref="Animator"/>'s player by the frame's delta time and works out its pose and skinning palette
/// (Update, at <see cref="StageOrder.SpriteAnimation"/>: after the game's Update steps, so a clip started this frame is
/// already posed when the frame is drawn).
/// </summary>
public sealed partial class SkeletalAnimationSystem
{
	/// <summary>The query step: one entity with an <see cref="Animator"/>.</summary>
	[Update(Order = StageOrder.SpriteAnimation), Query]
	private static void Animate(ref Animator animator, [Data] in float dt) => animator.Player?.Update(dt);
}
