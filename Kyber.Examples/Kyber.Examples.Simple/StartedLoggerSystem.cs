namespace Kyber.Examples.Simple;

public class StartedLoggerSystem : IStartupSystem
{
    private readonly ILogger _logger;

    public StartedLoggerSystem(ILogger<StartedLoggerSystem> logger)
    {
        _logger = logger;
    }

    public void Startup()
    {
        _logger.LogInformation("Simple Example Started");
    }
}
