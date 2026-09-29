namespace Ion.Extensions.Ecs;

/// <summary>
/// Advances every <see cref="SpriteAnimation"/> by the frame's delta time and shows its current frame on the entity's
/// <see cref="Sprite"/> (Update, at <see cref="StageOrder.SpriteAnimation"/>: after the game's Update steps).
/// </summary>
public sealed partial class SpriteAnimationSystem
{
	/// <summary>The query step: one entity with a <see cref="SpriteAnimation"/> and a <see cref="Sprite"/>.</summary>
	[Update(Order = StageOrder.SpriteAnimation), Query]
	private static void Animate(ref SpriteAnimation animation, ref Sprite sprite, [Data] in float dt)
	{
		if (animation.Advance(dt, out var source)) sprite.Source = source;
	}
}
