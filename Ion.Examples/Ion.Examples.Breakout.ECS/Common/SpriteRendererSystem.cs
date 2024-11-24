using Arch.Core;

using World = Arch.Core.World;
using Vector2 = System.Numerics.Vector2;

using Ion.Extensions.Graphics;

namespace Ion.Examples.Breakout.ECS.Common;

public record struct Sprite(Texture2D Texture, Vector2 Size);

public class SpriteRendererSystem(ISpriteBatch spriteBatch, World world, IWindow window)
{
	private QueryDescription _spriteQuery = new QueryDescription().WithAll<Sprite, Transform2D>();

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		world.Query(in _spriteQuery, (Entity entity, ref Sprite sprite, ref Transform2D transform) => {
			var halfExtent = sprite.Size / 2f;
			var centerPosition = transform.Position;
			var rotation = transform.Rotation;

			var topLeft = new Vector2(
							-halfExtent.X * MathF.Cos(rotation) + halfExtent.Y * MathF.Sin(rotation),
							-halfExtent.X * MathF.Sin(rotation) - halfExtent.Y * MathF.Cos(rotation)
						) + centerPosition;

			spriteBatch.Draw(sprite.Texture, topLeft, sprite.Size, rotation: transform.Rotation);
		});

		next(dt);
	}
}
