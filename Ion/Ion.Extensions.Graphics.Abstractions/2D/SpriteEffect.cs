
namespace Ion.Extensions.Graphics;

[Flags]
public enum SpriteEffect
{
	/// <summary>
	/// Renders the sprite normally.
	/// </summary>
	None = 0,

	/// <summary>
	/// Horizontally flip the sprite.
	/// </summary>
	FlipHorizontally,

	/// <summary>
	/// Vertically flip the sprite.
	/// </summary>
	FlipVertically,
}
