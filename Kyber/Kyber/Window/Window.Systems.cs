using Kyber.Graphics;

namespace Kyber;

public class WindowSystems : IInitializeSystem, IFirstSystem
{
	private readonly Window _window;
	private readonly InputState _input;
	private readonly ILogger _logger;

	public bool IsEnabled { get; set; } = true;

	public WindowSystems(IWindow window, IInputState input, ILogger<WindowSystems> logger)
	{
		_window = (Window)window;
		_input = (InputState)input;
		_logger = logger;
	}

	public void Initialize()
	{
		_window.Initialize();
	}

	public void First(float dt)
	{
		var snapshot = _window.Step();
		if (snapshot != null) _input.UpdateState(snapshot);
	}
}

internal class WindowResizeSystem : IPostUpdateSystem
{
	private readonly GraphicsDevice _graphicsDevice;
	private readonly IEventListener _events;

	public bool IsEnabled { get; set; } = true;

	public WindowResizeSystem(IGraphicsDevice graphicsDevice, IEventListener events)
	{
		_graphicsDevice = (GraphicsDevice)graphicsDevice;
		_events = events;
	}

	public void PostUpdate(float dt)
	{
		if (_events.OnLatest<WindowResizeEvent>(out var e)) _graphicsDevice.Internal?.ResizeMainWindow(e.Data.Width, e.Data.Height);
	}
}