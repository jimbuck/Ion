using Microsoft.Extensions.Logging;

namespace Ion.Extensions.Graphics;

/// <summary>
/// Initializes the <see cref="NullWindow"/> (emitting the initial <see cref="WindowResizeEvent"/>) and turns
/// <see cref="WindowClosedEvent"/> into <see cref="ExitGameEvent"/>, like the Veldrid window system.
/// </summary>
internal sealed class NullWindowSystem(NullWindow window, IEventListener events, ILogger<NullWindowSystem> logger)
{
	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		window.Initialize();
		logger.LogInformation("Headless graphics: null window {Width}x{Height}, nothing is rendered.", window.Width, window.Height);
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		next(dt);

		if (events.On<WindowClosedEvent>())
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
	[First]
	public void First(GameTime dt, GameLoopDelegate next)
	{
		input.Step();
		next(dt);
	}
}

/// <summary>
/// Wraps every Render stage in <see cref="NullSpriteBatch.Begin"/> and <see cref="NullSpriteBatch.End"/>.
/// </summary>
internal sealed class NullSpriteBatchSystem(NullSpriteBatch spriteBatch)
{
	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		spriteBatch.Begin();
		next(dt);
		spriteBatch.End();
	}
}
