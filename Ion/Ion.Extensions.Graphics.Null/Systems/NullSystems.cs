using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Initializes the <see cref="NullWindow"/> (emitting the initial <see cref="WindowResizeEvent"/>) and turns
/// <see cref="WindowClosedEvent"/> into <see cref="ExitGameEvent"/>, like the Veldrid window system.
/// </summary>
internal sealed class NullWindowSystem(NullWindow window, IEvents events, ILogger<NullWindowSystem> logger)
{
	private EventReader<WindowClosedEvent> _closed = events.Reader<WindowClosedEvent>();

	[Init(Order = StageOrder.Window)]
	public void Init(GameTime dt)
	{
		window.Initialize();
		logger.LogInformation("Headless graphics: null window {Width}x{Height}, nothing is rendered.", window.Width, window.Height);
	}

	[Render(Order = StageOrder.WindowClose)]
	public void CheckClosed(GameTime dt)
	{
		if (_closed.Read().Length > 0)
		{
			window.MarkClosed();
			events.Emit<ExitGameEvent>();
		}
	}
}

/// <summary>
/// Applies the scripted input of <see cref="NullInputState"/> at the start of every frame.
/// </summary>
internal sealed class NullInputSystem(NullInputState input)
{
	[First(Order = StageOrder.Input)]
	public void First(GameTime dt)
	{
		input.Step();
	}
}

/// <summary>
/// Wraps every Render stage in <see cref="NullSpriteBatch.Begin"/> and <see cref="NullSpriteBatch.End"/> (a scope at
/// order <see cref="StageOrder.SpriteBatch"/>).
/// </summary>
internal sealed class NullSpriteBatchSystem(NullSpriteBatch spriteBatch)
{
	[Begin(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void Begin(GameTime dt) => spriteBatch.Begin();

	[End(Stage.Render, Order = StageOrder.SpriteBatch)]
	public void End(GameTime dt) => spriteBatch.End();
}
