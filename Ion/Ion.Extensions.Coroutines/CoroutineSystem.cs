namespace Ion.Extensions.Coroutines;

/// <summary>
/// Steps the application's <see cref="ICoroutineRunner"/> once per frame in the Update stage, before the systems
/// registered after it. Added with <see cref="BuilderExtensions.UseCoroutines"/>.
/// </summary>
public sealed class CoroutineSystem(ICoroutineRunner runner)
{
	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		if (runner is CoroutineRunner coroutineRunner) coroutineRunner.SystemUpdate(dt);
		else runner.Update(dt);

		next(dt);
	}
}
