using Microsoft.Extensions.Hosting;

namespace Kyber.Hosting;

internal class HostedKyberService : IHostedService
{
	private readonly GameLoop _gameLoop;
	private readonly ILogger _logger;
	private readonly IHostApplicationLifetime _hostAppLifetime;

	private readonly Thread _mainGameThread;
	private readonly CancellationTokenSource _mainGameThreadCancellationToken = new();

	public HostedKyberService(
			GameLoop gameLoop,
			IGameConfig config,
			ILogger<HostedKyberService> logger,
			IHostApplicationLifetime applicationLifetime)
	{
		_gameLoop = gameLoop;

		_logger = logger;
		_hostAppLifetime = applicationLifetime;

		_hostAppLifetime.ApplicationStopping.Register(_onAppHostExiting);

		_mainGameThread = new Thread(_runGame)
		{
			Name = config.Title ?? "Kyber",
		};
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Starting Kyber service...");
		_mainGameThread.Start(_mainGameThreadCancellationToken.Token);
		_logger.LogDebug("Kyber service started!");
		return Task.CompletedTask;
	}

	private void _runGame(object? data)
	{
		if (data == null) throw new ArgumentNullException(nameof(data));

		var cancellationToken = (CancellationToken)data;
		cancellationToken.Register(() => _gameLoop.Exit());
		_gameLoop.Exiting += _onGameExiting;
		_gameLoop.Run();
	}

	private void _onGameExiting(object? sender, EventArgs e)
	{
		_hostAppLifetime.StopApplication();
	}

	private void _onAppHostExiting()
	{
		_gameLoop?.Exit();
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		_logger.LogDebug("Stopping Kyber service...");
		_gameLoop.Exiting -= _onGameExiting;
		_mainGameThreadCancellationToken.Cancel();
		_logger.LogDebug("Kyber service stopped!");
		return Task.CompletedTask;
	}
}