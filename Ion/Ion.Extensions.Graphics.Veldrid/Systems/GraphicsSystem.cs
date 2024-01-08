
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class GraphicsSystem
{
	private readonly GraphicsContext _graphics;
	private readonly ITraceTimer _trace;

	public GraphicsSystem(IGraphicsContext graphics, ITraceTimer<GraphicsSystem> trace)
	{
		_graphics = (GraphicsContext)graphics;
		_trace = trace;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Init");
		_graphics.Initialize();
		// ASSET MANAGER INIT
		timer.Stop();

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Render::Pre");
		_graphics.BeginFrame(dt);
		timer.Stop();
		next(dt);
		timer = _trace.Start("Render::Post");
		_graphics.EndFrame(dt);
		timer.Stop();
	}
}
