using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class SpriteBatchSystem(ISpriteBatch spriteBatch, ITraceTimer<SpriteBatchSystem> trace)
{
	private readonly SpriteBatch _spriteBatch = (SpriteBatch)spriteBatch;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		_spriteBatch.Initialize();
		timer.Stop();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Render::Pre");
		_spriteBatch.Begin(dt);
		timer.Stop();
		next(dt);
		timer = trace.Start("Render::Post");
		_spriteBatch.End();
		timer.Stop();
	}
}
