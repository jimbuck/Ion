using System.ComponentModel;

namespace Ion;

/// <summary>
/// Lets a host run a program's own entry point and take over the application it builds, the way a test host runs
/// <c>Program.cs</c> (<c>IonTestHost.UseEntryPoint&lt;Program&gt;()</c> in <c>Ion.Testing</c>): the first
/// <see cref="IonApplication.CreateBuilder(string[])"/> call made where the hook is installed is handed to the hook when
/// the builder is created and again just before it builds, and that application's <see cref="IonApplication.Run()"/> (or
/// <see cref="IonApplication.RunFrames"/>) hands the built, fully configured application to the hook instead of running
/// the game loop.
/// </summary>
/// <remarks>
/// A hook is installed for the current execution context (<see cref="AsyncLocal{T}"/>), so it reaches a thread or task
/// started while it is installed and nothing else: tests running in parallel each see their own. A program is expected
/// to create one application; a second <c>CreateBuilder</c> call runs as usual.
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class IonApplicationHook
{
	private static readonly AsyncLocal<IonApplicationHook?> Current = new();
	private int _claimed;

	/// <summary>
	/// Installs <paramref name="hook"/> for the current execution context until the returned scope is disposed (which
	/// restores the hook installed before, if any). Threads started meanwhile keep it.
	/// </summary>
	public static IDisposable Install(IonApplicationHook hook)
	{
		ArgumentNullException.ThrowIfNull(hook);
		var previous = Current.Value;
		Current.Value = hook;
		return new Scope(previous);
	}

	/// <summary>The installed hook, once: the first builder created where it is installed claims it.</summary>
	internal static IonApplicationHook? Claim()
	{
		var hook = Current.Value;
		return hook is not null && Interlocked.Exchange(ref hook._claimed, 1) == 0 ? hook : null;
	}

	/// <summary>
	/// Called by <see cref="IonApplication.CreateBuilder(string[])"/> once the builder has its default configuration (the
	/// command line, <c>appsettings.json</c>, the environment), before the program registers anything.
	/// </summary>
	protected internal abstract void OnBuilderCreated(IonApplicationBuilder builder);

	/// <summary>
	/// Called by <see cref="IonApplicationBuilder.Build"/> after the program's registrations, before the services are built:
	/// registrations made here win over the program's.
	/// </summary>
	protected internal abstract void OnBuilding(IonApplicationBuilder builder);

	/// <summary>
	/// Called by <see cref="IonApplication.Run()"/> (and its overloads, and <see cref="IonApplication.RunFrames"/>) instead
	/// of running the game loop, on the program's thread, with the application as the program configured it. When it
	/// returns, <c>Run</c> returns and the program goes on (usually disposing the application).
	/// </summary>
	protected internal abstract void OnRun(IonApplication application);

	private sealed class Scope(IonApplicationHook? previous) : IDisposable
	{
		private bool _disposed;

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			Current.Value = previous;
		}
	}
}
