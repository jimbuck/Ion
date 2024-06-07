
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

public class InputSystem(IInputState input, ITraceTimer<InputSystem> trace)
{
	private readonly InputState _input = (InputState)input;

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("First");
		_input.Step();
		timer.Stop();
		next(dt);
	}
}
