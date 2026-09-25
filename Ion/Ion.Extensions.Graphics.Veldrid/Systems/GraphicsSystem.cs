
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class GraphicsSystem(GraphicsContext graphics, ITraceTimer<GraphicsSystem> trace)
{

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		graphics.Initialize();
		// ASSET MANAGER INIT
		timer.Stop();

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Render::Pre");
		graphics.BeginFrame(dt);
		timer.Stop();
		next(dt);
		timer = trace.Start("Render::Post");
		graphics.EndFrame(dt);
		timer.Stop();
	}
}
