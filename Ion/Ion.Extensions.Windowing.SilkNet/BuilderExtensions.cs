using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Extensions.Graphics;

namespace Ion.Extensions.Windowing;

/// <summary>
/// Registration of the Silk.NET windowing and input module.
/// </summary>
public static class BuilderExtensions
{
	/// <summary>
	/// Registers <see cref="SilkWindow"/> (as <see cref="IWindow"/> and <see cref="IWindowSurface"/>) and
	/// <see cref="SilkInputState"/> (as <see cref="IInputState"/>, over the shared <see cref="InputTracker"/>), binding
	/// <see cref="WindowConfig"/> from <c>Ion:Window</c> and <see cref="GraphicsConfig"/> from <c>Ion:Graphics</c>.
	/// Pair it with an RHI graphics backend (for example <c>AddVulkanGraphics</c>) and call <see cref="UseSilkWindowing"/>.
	/// </summary>
	public static IServiceCollection AddSilkWindowing(this IServiceCollection services, IConfiguration config)
	{
		var ion = config.GetSection("Ion");
		services.AddOptions<GameConfig>();
		services.AddOptions<GraphicsConfig>();
		services.AddOptions<WindowConfig>();
		services.Configure<WindowConfig>(ion.GetSection("Window"));
		services.Configure<GraphicsConfig>(ion.GetSection("Graphics"));

		services
			.AddSingleton<SilkWindow>()
			.AddSingleton<IWindow>(static sp => sp.GetRequiredService<SilkWindow>())
			.AddSingleton<IWindowSurface>(static sp => sp.GetRequiredService<SilkWindow>())
			.AddSingleton(static sp => new SilkInputState(sp.GetRequiredService<SilkWindow>(), sp.GetRequiredService<IEvents>(), sp.GetRequiredService<InputTracker>()))
			.AddSingleton<IInputState>(static sp => sp.GetRequiredService<SilkInputState>())
			.AddSingleton<SilkWindowSystem>()
			.AddSingleton<SilkInputSystem>();

		services.AddInputTracker();
		return services;
	}

	/// <summary>
	/// Adds the window system (creates the window at Init and pumps it at the start of every frame, both at
	/// <see cref="StageOrder.Window"/>; turns a closed window into <see cref="ExitGameEvent"/>) and the input system
	/// (applies the frame's input at <see cref="StageOrder.Input"/>).
	/// </summary>
	public static IIonApplication UseSilkWindowing(this IIonApplication app)
	{
		return app
			.UseSystem<SilkWindowSystem>()
			.UseSystem<SilkInputSystem>();
	}
}

/// <summary>
/// Creates the Silk.NET window (Init), pumps its events at the start of every frame (First), both at
/// <see cref="StageOrder.Window"/>, and turns a closed window into an exit request at the end of Render
/// (<see cref="StageOrder.WindowClose"/>). Destroys the window at Destroy, after the graphics backend (<see cref="StageOrder.WindowClose"/>).
/// </summary>
internal sealed class SilkWindowSystem(SilkWindow window, SilkInputState input, IEvents events, ILogger<SilkWindowSystem> logger)
{
	private EventReader<WindowClosedEvent> _closed = events.Reader<WindowClosedEvent>();

	[Init(Order = StageOrder.Window)]
	public void Init(GameTime dt)
	{
		window.Initialize();
		if (window.View is { } view) input.Attach(view);
	}

	[First(Order = StageOrder.Window)]
	public void First(GameTime dt) => window.Step();

	[Render(Order = StageOrder.WindowClose)]
	public void CheckClosed(GameTime dt)
	{
		if (_closed.Read().Length == 0) return;
		logger.LogInformation("Window closed; exiting.");
		window.MarkClosed();
		events.Emit<ExitGameEvent>();
	}

	// Late in Destroy so graphics backends (StageOrder.Graphics) release their surface before the native window goes away.
	[Destroy(Order = StageOrder.WindowClose)]
	public void Destroy(GameTime dt)
	{
		input.Dispose();
		window.Dispose();
	}
}

/// <summary>
/// Applies the frame's queued input to the shared <see cref="InputTracker"/> at the start of every frame (First,
/// <see cref="StageOrder.Input"/>, after the window pumped its events).
/// </summary>
internal sealed class SilkInputSystem(SilkInputState input)
{
	[First(Order = StageOrder.Input)]
	public void First(GameTime dt) => input.Step();
}
