using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Initializes the graphics device (Init) and brackets every Render stage with the frame begin and end (a scope at order
/// <see cref="StageOrder.Graphics"/>).
/// </summary>
internal class GraphicsSystem(GraphicsContext graphics, ITraceTimer<GraphicsSystem> trace)
{
	[Init(Order = StageOrder.Graphics)]
	public void Init(GameTime dt)
	{
		var timer = trace.Start("Init");
		graphics.Initialize();
		timer.Stop();
	}

	[Begin(Stage.Render, Order = StageOrder.Graphics)]
	public void BeginFrame(GameTime dt)
	{
		var timer = trace.Start("Render::Pre");
		graphics.BeginFrame(dt);
		timer.Stop();
	}

	[End(Stage.Render, Order = StageOrder.Graphics)]
	public void EndFrame(GameTime dt)
	{
		var timer = trace.Start("Render::Post");
		graphics.EndFrame(dt);
		timer.Stop();
	}
}
