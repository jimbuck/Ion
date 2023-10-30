
namespace Kyber.Extensions.Graphics;

public class WindowSystem
{
	private readonly Window _window;

	public WindowSystem(IWindow window)
	{
		_window = (Window)window;
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
}
