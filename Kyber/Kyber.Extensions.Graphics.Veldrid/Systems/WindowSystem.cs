
namespace Kyber.Extensions.Graphics;

public class WindowSystem
{
	private readonly Window _window;
	private readonly IEventListener _events;

	public WindowSystem(IWindow window, IEventListener events)
	{
		_window = (Window)window;
		_events = events;
	}

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		_window.Initialize();
		next(dt);
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		_window.Step();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);
		if (_events.On<WindowClosedEvent>()) _events.Emit<ExitGameEvent>();
	}
}
