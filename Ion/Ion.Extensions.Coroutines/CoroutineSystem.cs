namespace Ion.Extensions.Coroutines;

/// <summary>
/// Steps the application's <see cref="ICoroutineRunner"/> once per frame in the Update stage, at order
/// <see cref="StageOrder.Coroutines"/> (before scenes and user steps). Added with <see cref="BuilderExtensions.UseCoroutines"/>.
/// </summary>
public sealed class CoroutineSystem(ICoroutineRunner runner)
{
	[Update(Order = StageOrder.Coroutines)]
	public void Update(GameTime dt)
	{
		if (runner is CoroutineRunner coroutineRunner) coroutineRunner.SystemUpdate(dt);
		else runner.Update(dt);
	}
}
