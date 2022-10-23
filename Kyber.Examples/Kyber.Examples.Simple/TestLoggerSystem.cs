using Kyber.Scenes;

namespace Kyber.Examples.Simple;

public class TestLoggerSystem : IInitializeSystem, IDestroySystem, IUpdateSystem
{
    private readonly ILogger _logger;
    private readonly ICurrentScene _currentScene;
    private readonly IInputState _input;
    private readonly IEventListener _events;

    public bool IsEnabled { get; set; } = true;

    private bool _wasDown = false;

    public TestLoggerSystem(ILogger<TestLoggerSystem> logger, ICurrentScene currentScene, IInputState input, IEventListener events)
    {
        _logger = logger;
        _currentScene = currentScene;
        _input = input;
        _events = events;
    }

    public void Initialize()
    {
        _logger.LogInformation("Simple Example Started ({CurrentScene} scene)", _currentScene);
    }

    public void Update(float dt)
    {
        if (_currentScene.IsRoot)
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

        //_logger.LogInformation("Simple Example Update ({CurrentScene} scene)", _currentScene);
    }

    public void Destroy()
    {
        _logger.LogInformation("Simple Example Stopped ({CurrentScene} scene)", _currentScene);
    }
}
