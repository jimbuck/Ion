
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class WindowSystem(Window window, IEventListener events, ITraceTimer<WindowSystem> trace)
{

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		window.Initialize();
		timer.Stop();
		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("First");
		window.Step();
		timer.Stop();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		var timer = trace.Start("Render");
		if (events.On<WindowClosedEvent>()) events.Emit<ExitGameEvent>();
		timer.Stop();
	}
}
