namespace Kyber.Examples.SpriteRenderer;

public class TestLoggerSystem : IInitializeSystem, IDestroySystem, IUpdateSystem, IFirstSystem
{
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IEventListener _events;

	private bool _wasDown = false;
	private float _totalDt = 0;
	private int _frameCount = 0;

	public bool IsEnabled { get; set; } = true;

	public TestLoggerSystem(ILogger<TestLoggerSystem> logger, IInputState input, IEventListener events)
	{
		_logger = logger;
		_input = input;
		_events = events;
	}

	public void Initialize()
	{
		_logger.LogInformation("Simple Example Started");
	}

	public void First(GameTime dt)
	{
		_totalDt += dt;
		_frameCount++;

		if (_totalDt > 1f)
		{
			_logger.LogInformation($"FPS: {_frameCount / _totalDt:###.0}");
			_totalDt = 0f;
			_frameCount = 0;
		}
	}

	public void Update(GameTime dt)
	{
		if (_events.On<WindowResizeEvent>()) _logger.LogInformation("Window Resized!");
		if (_events.On<WindowFocusGainedEvent>()) _logger.LogInformation("Window Focus Gained!");
		if (_events.On<WindowFocusLostEvent>()) _logger.LogInformation("Window Focus Lost!");
		if (_events.On<WindowClosedEvent>()) _logger.LogInformation("Window Closed!");

		var spaceDown = _input.Down(Key.Space);

		if (_input.Pressed(Key.Space))
		{
			_logger.LogInformation("SPACE PRESSED!");
			_wasDown = true;
		}
		if (spaceDown)
		{
			_logger.LogInformation("SPACE DOWN!");
		}
		if (_input.Released(Key.Space))
		{
			_wasDown = false;
			_logger.LogInformation("SPACE UP!");
		}

		if (!spaceDown && _wasDown)
		{
			_logger.LogWarning($"MISSED DOWN!");
		}

		if (_input.Pressed(MouseButton.Left)) _logger.LogInformation("LEFT MOUSE DOWN!");
		if (_input.Released(MouseButton.Left)) _logger.LogInformation("LEFT MOUSE UP!");
	}

	public void Destroy()
	{
		_logger.LogInformation("Simple Example Stopped!");
	}
}
