
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class GraphicsSystem(IGraphicsContext graphics, ITraceTimer<GraphicsSystem> trace)
{
	private readonly GraphicsContext _graphics = (GraphicsContext)graphics;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		_graphics.Initialize();
		// ASSET MANAGER INIT
		timer.Stop();

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Render::Pre");
		_graphics.BeginFrame(dt);
		timer.Stop();
		next(dt);
		timer = trace.Start("Render::Post");
		_graphics.EndFrame(dt);
		timer.Stop();
	}
}
