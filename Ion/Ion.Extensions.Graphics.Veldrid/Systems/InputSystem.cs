
using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

internal class InputSystem(InputState input, ITraceTimer<InputSystem> trace)
{

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = trace.Start("First");
		input.Step();
		timer.Stop();
		next(dt);
	}
}
