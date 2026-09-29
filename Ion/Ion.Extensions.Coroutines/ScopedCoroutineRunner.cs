using System.Collections;

using Microsoft.Extensions.DependencyInjection;

namespace Ion.Extensions.Coroutines;

/// <summary>
/// The <see cref="ICoroutineRunner"/> of a service scope (a scene): coroutines started through it run on the application's
/// <see cref="CoroutineRunner"/> (stepped by <see cref="CoroutineSystem"/> like every other), and are stopped when the
/// scope is disposed, that is when the scene unloads. Resolved as <see cref="ICoroutineRunner"/> from a scope, so scene
/// systems and scene steps get it without asking.
/// </summary>
/// <remarks>
/// <see cref="Count"/>, <see cref="IsActive"/>, <see cref="Stop"/> and <see cref="StopAll"/> see only the coroutines this
/// runner started. <see cref="Update"/> steps the application's runner (at most once per frame, see
/// <see cref="CoroutineRunner.Update"/>). Starting a coroutine allocates what <see cref="CoroutineRunner.Start"/> does and
/// nothing more; stepping allocates nothing for <c>IEnumerator&lt;Wait&gt;</c> coroutines.
/// </remarks>
public sealed class ScopedCoroutineRunner : ICoroutineRunner, IDisposable
{
	private readonly CoroutineRunner _runner;
	private bool _disposed;

	/// <summary>Creates a runner whose coroutines run on <paramref name="runner"/> until this runner is disposed.</summary>
	public ScopedCoroutineRunner(CoroutineRunner runner)
	{
		ArgumentNullException.ThrowIfNull(runner);
		_runner = runner;
	}

	/// <summary>The application's runner, which steps this runner's coroutines.</summary>
	public ICoroutineRunner Parent => _runner;

	/// <summary>Whether the scope ended: its coroutines were stopped and <see cref="Start"/> throws.</summary>
	public bool IsDisposed => _disposed;

	/// <inheritdoc/>
	public int Count => _runner.CountOwnedBy(this);

	/// <inheritdoc/>
	/// <exception cref="ObjectDisposedException">The scope ended (the scene unloaded).</exception>
	public void Start(IEnumerator routine)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		_runner.Start(routine, this);
	}

	/// <inheritdoc/>
	/// <remarks>A routine that this runner did not start is left running.</remarks>
	public void Stop(IEnumerator routine) => _runner.Stop(routine, this);

	/// <inheritdoc/>
	/// <remarks>Stops only the coroutines this runner started.</remarks>
	public void StopAll() => _runner.StopOwnedBy(this);

	/// <inheritdoc/>
	public bool IsActive(IEnumerator routine) => _runner.IsActive(routine, this);

	/// <inheritdoc/>
	/// <remarks>Steps the application's runner, so every coroutine, not only this runner's.</remarks>
	public void Update(GameTime dt) => _runner.Update(dt);

	/// <summary>Stops this runner's coroutines. Called when the scope is disposed.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_runner.StopOwnedBy(this);
	}
}

/// <summary>Picks the <see cref="ICoroutineRunner"/> of a provider: the application's for the root provider, the scope's otherwise.</summary>
internal sealed class CoroutineRunnerResolver(IServiceProvider root)
{
	public ICoroutineRunner For(IServiceProvider services) => ReferenceEquals(services, root)
		? root.GetRequiredService<CoroutineRunner>()
		: services.GetRequiredService<ScopedCoroutineRunner>();
}
