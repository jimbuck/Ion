using Kyber.Core.Scenes;

namespace Kyber.Examples.Simple;

public class ComprehnsiveLoggerSystem : IStartupSystem, IShutdownSystem, IUpdateSystem
{
    private readonly ILogger _logger;
    private readonly CurrentScene _currentScene;

    public bool IsEnabled { get; set; } = true;

    public ComprehnsiveLoggerSystem(ILogger<ComprehnsiveLoggerSystem> logger, CurrentScene currentScene)
    {
        _logger = logger;
        _currentScene = currentScene;
    }

    public void Startup()
    {
        _logger.LogInformation("Simple Example Started ({CurrentScene} scene)", _currentScene);
    }

    public void Update(float dt)
    {
        //_logger.LogInformation("Simple Example Update ({CurrentScene} scene)", _currentScene);
    }

    public void Shutdown()
    {
        _logger.LogInformation("Simple Example Stopped ({CurrentScene} scene)", _currentScene);
    }
}
