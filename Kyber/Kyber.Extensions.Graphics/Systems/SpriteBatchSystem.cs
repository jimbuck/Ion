
using Kyber.Graphics;

namespace Kyber.Builder;

public class SpriteBatchSystem
{
	private readonly SpriteBatch _spriteBatch;

	public SpriteBatchSystem(ISpriteBatch spriteBatch)
	{
		_spriteBatch = (SpriteBatch)spriteBatch;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_spriteBatch.Initialize();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		_spriteBatch.Begin(dt);
		next(dt);
		_spriteBatch.End();
	}
}
