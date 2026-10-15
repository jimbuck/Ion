using Ion.Core;

using Microsoft.Extensions.Logging;

namespace Ion.Tests;

/// <summary>
/// Builds a headless application whose game loop is driven by a test-controlled clock.
/// </summary>
internal sealed class LoopTestHost : IDisposable
{
	public LoopTestHost(IClock clock, Action<GameConfig>? configure = null, Action<IServiceCollection>? services = null, Action<IIonApplication>? use = null, params Type[] systems)
	{
		Clock = clock;

		var builder = IonApplication.CreateBuilder();
		builder.Services.AddLogging(l => l.ClearProviders());
		builder.Services.AddSingleton(clock);
		builder.Services.Configure<GameConfig>(config =>
		{
			// Deterministic defaults: no pacing unless a test asks for it.
			config.MaxFPS = 0;
			configure?.Invoke(config);
		});
		foreach (var system in systems) builder.Services.AddSingleton(system);
		services?.Invoke(builder.Services);

		App = builder.Build();
		App.UseEvents();
		use?.Invoke(App);
		foreach (var system in systems) App.UseSystem(system);
	}

	public IClock Clock { get; }

	public IonApplication App { get; }

	public IServiceProvider Services => App.Services;

	public T Get<T>() where T : notnull => App.Services.GetRequiredService<T>();

	public GameLoop BuildLoop() => App.Build();

	public void Dispose() => App.Dispose();
}
