using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class SpriteBatchSystem(SpriteBatch spriteBatch, ITraceTimer<SpriteBatchSystem> trace)
{

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		spriteBatch.Initialize();
		timer.Stop();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Render::Pre");
		spriteBatch.Begin(dt);
		timer.Stop();
		next(dt);
		timer = trace.Start("Render::Post");
		spriteBatch.End();
		timer.Stop();
	}
}
