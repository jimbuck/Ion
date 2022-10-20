using Kyber.Events;

namespace Kyber.Examples.Veldrid;

public class TestLoggerSystem : IStartupSystem, IShutdownSystem, IUpdateSystem
{
	private readonly ILogger _logger;
	private readonly InputState _input;
	private readonly IEventListener _events;

    private bool _wasDown = false;
    private float _totalDt = 0;
    private int _frameCount = 0;

    public bool IsEnabled { get; set; } = true;

    public TestLoggerSystem(ILogger<TestLoggerSystem> logger, InputState input, IEventListener events)
	{
		_logger = logger;
		_input = input;
		_events = events;
	}

	public void Startup()
	{
		_logger.LogInformation("Simple Example Started");
	}

	public void Update(float dt)
	{
        _totalDt += dt;
        _frameCount++;

        if (_totalDt > 1f)
        {
            _logger.LogInformation($"FPS: {_frameCount / _totalDt:###.0}");
            _totalDt = 0f;
            _frameCount = 0;
        }

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

	public void Shutdown()
	{
		_logger.LogInformation("Simple Example Stopped!");
	}
}
