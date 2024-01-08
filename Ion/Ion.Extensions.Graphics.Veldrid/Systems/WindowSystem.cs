
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class WindowSystem
{
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly ITraceTimer _trace;

	public WindowSystem(IWindow window, IEventListener events, ITraceTimer<WindowSystem> trace)
	{
		_window = (Window)window;
		_events = events;
		_trace = trace;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("Init");
		_window.Initialize();
		timer.Stop();
		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("First");
		_window.Step();
		timer.Stop();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		var timer = _trace.Start("Render");
		if (_events.On<WindowClosedEvent>()) _events.Emit<ExitGameEvent>();
		timer.Stop();
	}
}
