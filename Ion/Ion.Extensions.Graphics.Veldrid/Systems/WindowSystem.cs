
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class WindowSystem(IWindow window, IEventListener events, ITraceTimer<WindowSystem> trace)
{
	private readonly Window _window = (Window)window;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("Init");
		_window.Initialize();
		timer.Stop();
		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("First");
		_window.Step();
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
