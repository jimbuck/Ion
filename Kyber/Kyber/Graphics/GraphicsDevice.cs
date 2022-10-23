namespace Kyber.Graphics;

public interface IGraphicsDevice : IDisposable
{
	Veldrid.GraphicsDevice? Internal { get; }

}

public class GraphicsDevice : IGraphicsDevice, IInitializeSystem
{
	private readonly IGameConfig _config;
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly ILogger _logger;

	private Veldrid.GraphicsDevice? _gd;

	public bool IsEnabled { get; set; } = true;

	// TODO: Remove this once the graphics API is implemented.
	public Veldrid.GraphicsDevice? Internal => _gd;

	public GraphicsDevice(IGameConfig config, Window window, IEventListener events, ILogger<GraphicsDevice> logger)
	{
		_config = config;
		_window = window;
		_events = events;
		_logger = logger;
	}

	public void Initialize()
	{
		if (_config.Output == GraphicsOutput.None) return;

		_gd = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new Veldrid.GraphicsDeviceOptions()
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = _config.VSync,
		});
	}

	public void Dispose()
	{
		_gd?.Dispose();
	}
}
