
using Kyber.Extensions.Graphics;

namespace Kyber.Builder;

public class GraphicsSystem
{
	private readonly GraphicsContext _graphics;
	private readonly SpriteBatch _spriteBatch;

	public GraphicsSystem(IGraphicsContext graphics, ISpriteBatch spriteBatch)
	{
		_graphics = (GraphicsContext)graphics;
		_spriteBatch = (SpriteBatch)spriteBatch;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_graphics.Initialize();
		// ASSET MANAGER INIT
		_spriteBatch.Initialize();

		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		_graphics.First();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		_graphics.BeginFrame(dt);
		next(dt);
		_graphics.EndFrame(dt);
	}
}
