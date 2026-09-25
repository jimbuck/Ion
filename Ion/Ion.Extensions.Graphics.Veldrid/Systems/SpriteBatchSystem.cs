using Ion.Extensions.Debug;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Initializes the sprite batch (Init) and brackets every Render stage with <c>Begin</c> and <c>End</c> (a scope at order
/// <see cref="StageOrder.SpriteBatch"/>, inside the graphics frame scope).
/// </summary>
internal class SpriteBatchSystem(SpriteBatch spriteBatch, ITraceTimer<SpriteBatchSystem> trace)
{
	[Init(Order = StageOrder.SpriteBatch)]
	public void Init(GameTime dt)
	{
		var timer = trace.Start("Init");
		spriteBatch.Initialize();
		timer.Stop();
	}

	[Begin(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void Begin(GameTime dt)
	{
		var timer = trace.Start("Render::Pre");
		spriteBatch.Begin(dt);
		timer.Stop();
	}

	[End(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void End(GameTime dt)
	{
		var timer = trace.Start("Render::Post");
		spriteBatch.End();
		timer.Stop();
	}
}
