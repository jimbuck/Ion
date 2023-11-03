using Kyber.Extensions.Debug;

namespace Kyber.Extensions.Graphics;

public class SpriteBatchSystem
{
	private readonly SpriteBatch _spriteBatch;
	private readonly ITraceTimer _trace;

	public SpriteBatchSystem(ISpriteBatch spriteBatch, ITraceTimer<SpriteBatchSystem> trace)
	{
		_spriteBatch = (SpriteBatch)spriteBatch;
		_trace = trace;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Init");
		_spriteBatch.Initialize();
		timer.Stop();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Render::Pre");
		_spriteBatch.Begin(dt);
		timer.Stop();
		next(dt);
		timer = _trace.Start("Render::Post");
		_spriteBatch.End();
		timer.Stop();
	}
}
