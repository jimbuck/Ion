using System.Diagnostics;

namespace Kyber.Examples.SpriteRenderer;

public class TestLoggerSystem : IInitializeSystem, IDestroySystem, IUpdateSystem
{
	private readonly ILogger _logger;
	private readonly IInputState _input;
	private readonly IEventListener _events;

	private bool _wasDown = false;
	private double _totalDt = 0;
	private int _frameCount = 0;
	private readonly Stopwatch _stopwatch = new();

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

	public void Update(GameTime dt)
	{
		_stopwatch.Restart();

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


		// next();

		_stopwatch.Stop();
		var duration = _stopwatch.Elapsed.TotalSeconds;
		_totalDt += duration;
		_frameCount++;

		if (_totalDt > 0.5f)
		{
			_logger.LogInformation($"Frame Time: {1000 * _totalDt / _frameCount: 00.00}, FPS: {_frameCount / _totalDt:###.0}");
			_totalDt = 0f;
			_frameCount = 0;
		}
	}

	public void Destroy()
	{
		_logger.LogInformation("Simple Example Stopped!");
	}
}
