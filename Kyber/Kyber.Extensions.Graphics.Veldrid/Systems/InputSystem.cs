
using Kyber.Extensions.Debug;

namespace Kyber.Extensions.Graphics;

public class InputSystem
{
	private readonly InputState _input;
	private readonly ITraceTimer _trace;

	public InputSystem(IInputState input, ITraceTimer<InputSystem> trace)
	{
		_input = (InputState)input;
		_trace = trace;
	}

	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		var timer = _trace.Start("First");
		_input.Step();
		timer.Stop();
		next(dt);
	}
}
