namespace Kyber.Graphics;

public interface IGraphicsDevice
{

}

public class GraphicsDevice : IGraphicsDevice, IDisposable
{
	private readonly IGameConfig _config;
	private readonly Window _window;
	private readonly IEventListener _events;
	private readonly ILogger _logger;

	private Veldrid.GraphicsDevice? _gd;

	// TODO: Remove this once the graphics API is implemented.
	public Veldrid.GraphicsDevice? Internal => _gd;

	public GraphicsDevice(IGameConfig config, IWindow window, IEventListener events, ILogger<GraphicsDevice> logger)
	{
		_config = config;
		_window = (Window)window;
		_events = events;
		_logger = logger;
	}

	public void Initialize()
	{
		if (_config.Output == GraphicsOutput.None) return;

		_logger.LogInformation("Creating graphics device...");

		_gd = Veldrid.StartupUtilities.VeldridStartup.CreateGraphicsDevice(_window.Sdl2Window, new Veldrid.GraphicsDeviceOptions()
		{
			PreferStandardClipSpaceYDirection = true,
			PreferDepthRangeZeroToOne = true,
			SyncToVerticalBlank = _config.VSync,
		});

		_logger.LogInformation("Graphics device created!");
	}

	public void Dispose()
	{
		_gd?.Dispose();
	}
}
