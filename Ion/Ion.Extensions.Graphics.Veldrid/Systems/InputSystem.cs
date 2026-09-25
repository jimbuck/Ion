using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Takes the frame's input snapshot at the start of every frame (First, order <see cref="StageOrder.Input"/>, after the
/// window pumped its events).
/// </summary>
internal class InputSystem(InputState input, ITraceTimer<InputSystem> trace)
{
	[First(Order = StageOrder.Input)]
	public void First(GameTime dt)
	{
		var timer = trace.Start("First");
		input.Step();
		timer.Stop();
	}
}
