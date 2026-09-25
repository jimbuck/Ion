using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Creates the window (Init), pumps its events at the start of every frame (First), and turns a closed window into an
/// exit request at the end of Render (order <see cref="StageOrder.WindowClose"/>).
/// </summary>
internal class WindowSystem(Window window, IEventListener events, ITraceTimer<WindowSystem> trace)
{
	[Init(Order = StageOrder.Window)]
	public void Init(GameTime dt)
	{
		var timer = trace.Start("Init");
		window.Initialize();
		timer.Stop();
	}

	[First(Order = StageOrder.Window)]
	public void First(GameTime dt)
	{
		var timer = trace.Start("First");
		window.Step();
		timer.Stop();
	}

	[Render(Order = StageOrder.WindowClose)]
	public void CheckClosed(GameTime dt)
	{
		var timer = trace.Start("Render");
		if (events.On<WindowClosedEvent>()) events.Emit<ExitGameEvent>();
		timer.Stop();
	}
}
