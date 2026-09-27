using System.Reflection;

namespace Ion.Testing;

/// <summary>
/// Runs a program's own entry point (its <c>Main</c>, top-level statements included) up to its <c>Run()</c> call and hands
/// over the application it built, so a test exercises exactly what <c>Program.cs</c> does, the way ASP.NET Core's
/// <c>WebApplicationFactory</c> does for web apps. <see cref="IonTestHost.UseEntryPoint{TProgram}"/> builds on it; use it
/// directly to drive the application yourself (a windowed run, a real clock).
/// </summary>
/// <remarks>
/// <para>
/// The entry point runs on a dedicated thread with an <see cref="IonApplicationHook"/> installed for it (and only it, so
/// tests can run in parallel): <c>configure</c> runs on the builder when <c>IonApplication.CreateBuilder</c> creates it,
/// before the program registers anything, <c>beforeBuild</c> runs in <c>builder.Build()</c> after the program's
/// registrations (so its registrations win), and <c>game.Run()</c> hands the application, with every system the program
/// added, to <see cref="Application"/> and blocks until this object is disposed. <see cref="Dispose"/> lets <c>Run()</c>
/// return; the program then finishes (a <c>using var game</c> disposes the application), and the application is disposed
/// if it was not.
/// </para>
/// <para>
/// An exception the program throws before <c>Run()</c>, a program that returns without calling it, or one that does not
/// get there within the timeout fails <see cref="Start(Assembly, string[], Action{IonApplicationBuilder}, Action{IonApplicationBuilder}, TimeSpan?)"/>
/// with an exception that says so. The schedule generator's interceptors in the program run as in production: the
/// schedule compiled for <c>Program.cs</c> is used when nothing is added to it (<c>host.Loop.Schedule.IsGenerated</c>).
/// </para>
/// </remarks>
public sealed class IonEntryPoint : IDisposable
{
	/// <summary>How long <see cref="Start{TProgram}"/> waits for the program to call <c>Run()</c>, and <see cref="Dispose"/> for it to return, by default.</summary>
	public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

	private readonly Hook _hook;
	private readonly Thread _thread;
	private readonly TimeSpan _timeout;
	private bool _disposed;

	private IonEntryPoint(Assembly assembly, Hook hook, Thread thread, TimeSpan timeout, IonApplication application)
	{
		Assembly = assembly;
		_hook = hook;
		_thread = thread;
		_timeout = timeout;
		Application = application;
	}

	/// <summary>The program's assembly.</summary>
	public Assembly Assembly { get; }

	/// <summary>The application the program built and configured, as it was when the program called <c>Run()</c>.</summary>
	public IonApplication Application { get; }

	/// <summary>
	/// Runs the entry point of the assembly that declares <typeparamref name="TProgram"/> (any type of the program: its
	/// <c>Program</c> class, which the Ion generator makes public for top-level statements, or one of its systems).
	/// </summary>
	/// <param name="args">The command line arguments passed to <c>Main</c>.</param>
	/// <param name="configure">Runs on the builder as soon as it is created (add configuration here: the program reads it while it registers).</param>
	/// <param name="beforeBuild">Runs on the builder after the program's registrations (replace services here).</param>
	/// <param name="timeout">How long to wait for <c>Run()</c> (<see cref="DefaultTimeout"/> when omitted).</param>
	public static IonEntryPoint Start<TProgram>(string[]? args = null, Action<IonApplicationBuilder>? configure = null, Action<IonApplicationBuilder>? beforeBuild = null, TimeSpan? timeout = null) =>
		Start(typeof(TProgram).Assembly, args ?? [], configure, beforeBuild, timeout);

	/// <summary>Runs the entry point of <paramref name="assembly"/> (see <see cref="Start{TProgram}"/>).</summary>
	/// <exception cref="ArgumentException">The assembly has no entry point.</exception>
	/// <exception cref="InvalidOperationException">The program threw before calling <c>Run()</c>, or returned without calling it.</exception>
	/// <exception cref="TimeoutException">The program did not call <c>Run()</c> within <paramref name="timeout"/>.</exception>
	public static IonEntryPoint Start(Assembly assembly, string[] args, Action<IonApplicationBuilder>? configure = null, Action<IonApplicationBuilder>? beforeBuild = null, TimeSpan? timeout = null)
	{
		ArgumentNullException.ThrowIfNull(assembly);
		ArgumentNullException.ThrowIfNull(args);
		var entryPoint = assembly.EntryPoint ?? throw new ArgumentException($"'{assembly.GetName().Name}' has no entry point: pass a type of the game's executable project (for example its Program class).", nameof(assembly));
		var wait = timeout ?? DefaultTimeout;
		var name = assembly.GetName().Name;

		var hook = new Hook(configure, beforeBuild);
		var thread = new Thread(() => hook.RunMain(entryPoint, args))
		{
			IsBackground = true,
			Name = $"{name} Main",
		};

		// The hook reaches the program's thread (it captures the execution context when it starts) and nothing else.
		using (IonApplicationHook.Install(hook))
		{
			thread.Start();
		}

		if (!hook.Ready.Wait(wait))
		{
			hook.Release();
			throw new TimeoutException($"The entry point of '{name}' did not call Run() on its application within {wait.TotalSeconds:0.#} s.");
		}

		if (hook.Application is not { } application)
		{
			thread.Join(wait);
			throw hook.MainException is { } exception
				? new InvalidOperationException($"The entry point of '{name}' threw before it called Run() on its application: {exception.Message}", exception)
				: new InvalidOperationException($"The entry point of '{name}' returned without calling Run() on an application created with IonApplication.CreateBuilder.");
		}

		return new IonEntryPoint(assembly, hook, thread, wait, application);
	}

	/// <summary>
	/// Lets the program's <c>Run()</c> return, waits for the entry point to finish and disposes the application if the
	/// program did not. Stop the game loop first (<see cref="Ion.Core.GameLoop.Shutdown"/>).
	/// </summary>
	/// <exception cref="InvalidOperationException">The program threw after <c>Run()</c> returned.</exception>
	/// <exception cref="TimeoutException">The program did not return in time.</exception>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		_hook.Release();
		var returned = _thread.Join(_timeout);
		Application.Dispose();

		if (!returned) throw new TimeoutException($"The entry point of '{Assembly.GetName().Name}' did not return within {_timeout.TotalSeconds:0.#} s after Run() returned.");
		if (_hook.MainException is { } exception) throw new InvalidOperationException($"The entry point of '{Assembly.GetName().Name}' threw after Run() returned: {exception.Message}", exception);
	}

	/// <summary>The hook installed for the program's thread.</summary>
	private sealed class Hook(Action<IonApplicationBuilder>? configure, Action<IonApplicationBuilder>? beforeBuild) : IonApplicationHook
	{
		private readonly ManualResetEventSlim _release = new();

		/// <summary>Set when the program called <c>Run()</c> (<see cref="Application"/>) or ended before (<see cref="MainException"/>).</summary>
		public ManualResetEventSlim Ready { get; } = new();

		public IonApplication? Application { get; private set; }

		public Exception? MainException { get; private set; }

		public void Release() => _release.Set();

		public void RunMain(MethodInfo entryPoint, string[] args)
		{
			try
			{
				var result = entryPoint.Invoke(null, entryPoint.GetParameters().Length == 0 ? null : [args]);
				if (result is Task task) task.GetAwaiter().GetResult();
			}
			catch (TargetInvocationException ex) when (ex.InnerException is not null)
			{
				MainException = ex.InnerException;
			}
			catch (Exception ex)
			{
				MainException = ex;
			}
			finally
			{
				Ready.Set();
			}
		}

		protected override void OnBuilderCreated(IonApplicationBuilder builder) => configure?.Invoke(builder);

		protected override void OnBuilding(IonApplicationBuilder builder) => beforeBuild?.Invoke(builder);

		protected override void OnRun(IonApplication application)
		{
			Application = application;
			Ready.Set();

			// The program's thread waits here, inside Run(), while the test drives the application.
			_release.Wait();
		}
	}
}
