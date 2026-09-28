using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Ion.Core;

namespace Ion;

public class IonApplication : IIonApplication, IDisposable
{
	private readonly IHost _host;
	private readonly IonApplicationHook? _hook;
	private bool _disposed;

	/// <summary>
	/// The configuration key that prints the schedule (<see cref="PrintSchedule"/>) to standard output when the game loop
	/// is built: <c>Ion:PrintSchedule = true</c>, or <c>--Ion:PrintSchedule=true</c> on the command line.
	/// </summary>
	public const string PrintScheduleKey = "Ion:PrintSchedule";

	/// <summary>
	/// The root schedule's registrations: systems, function steps and legacy middleware.
	/// </summary>
	public ScheduleModel Schedule { get; } = new("root", isRoot: true);

	/// <summary>
	/// The application's configured services.
	/// </summary>
	public IServiceProvider Services => _host.Services;

	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	public IConfiguration Configuration => _host.Services.GetRequiredService<IConfiguration>();

	internal IonApplication(IHost host, IonApplicationHook? hook = null)
	{
		_host = host;
		_hook = hook;
	}

	/// <summary>
	/// Creates the application builder: the configuration from the command line (<paramref name="args"/>, with the short
	/// switches of <see cref="IonCommandLine"/>), <c>appsettings.json</c> and the environment, and the core services.
	/// <c>appsettings.json</c> (and <c>appsettings.{Environment}.json</c>) are read from the folder of the game's
	/// executable (<see cref="AppContext.BaseDirectory"/>), whatever the working directory; <c>--contentRoot=&lt;folder&gt;</c>
	/// (or the <c>DOTNET_CONTENTROOT</c> environment variable) reads them from another folder.
	/// </summary>
	public static IonApplicationBuilder CreateBuilder(string[] args)
	{
		var builder = new IonApplicationBuilder(args);

		// A test host running this program's entry point (see IonApplicationHook) configures the first builder.
		if (IonApplicationHook.Claim() is { } hook)
		{
			builder.Hook = hook;
			hook.OnBuilderCreated(builder);
		}

		return builder;
	}

	public static IonApplicationBuilder CreateBuilder()
	{
		return CreateBuilder([]);
	}

	/// <inheritdoc/>
	public IIonApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Init, middleware);

	/// <inheritdoc/>
	public IIonApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.First, middleware);

	/// <inheritdoc/>
	public IIonApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.FixedUpdate, middleware);

	/// <inheritdoc/>
	public IIonApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Update, middleware);

	/// <inheritdoc/>
	public IIonApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Render, middleware);

	/// <inheritdoc/>
	public IIonApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Last, middleware);

	/// <inheritdoc/>
	public IIonApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware) => UseMiddleware(Stage.Destroy, middleware);

	private IonApplication UseMiddleware(Stage stage, Func<GameLoopDelegate, GameLoopDelegate> middleware)
	{
		Schedule.AddMiddleware(stage, middleware);
		return this;
	}

	/// <summary>
	/// Plans, validates and binds the root schedule: resolves every system and injected service and creates the stage
	/// runners. Warnings are logged once (category <c>Ion.Schedule</c>).
	/// </summary>
	/// <exception cref="IonScheduleException">The schedule has errors (ION001 to ION013).</exception>
	public Ion.Schedule BuildSchedule() => Schedule.Build(Services);

	/// <inheritdoc/>
	public string PrintSchedule() => Schedule.Plan(Services).Print();

	/// <summary>
	/// Builds the root schedule (see <see cref="BuildSchedule"/>) and a game loop that runs it. When
	/// <see cref="PrintScheduleKey"/> is set, the schedule is printed to standard output first.
	/// </summary>
	/// <exception cref="IonScheduleException">The schedule has errors.</exception>
	public GameLoop Build()
	{
		var schedule = BuildSchedule();

		if (bool.TryParse(Configuration[PrintScheduleKey], out var print) && print)
		{
			Console.Out.Write(schedule.Print());
			Console.Out.Flush();
		}

		var gameLoop = ActivatorUtilities.CreateInstance<GameLoop>(Services);
		gameLoop.ScheduleFactory = BuildSchedule;
		gameLoop.UseSchedule(schedule);

		return gameLoop;
	}

	/// <inheritdoc/>
	[StackTraceHidden]
	public void Run() => Run(CancellationToken.None);

	/// <inheritdoc/>
	[StackTraceHidden]
	public void Run(CancellationToken cancellationToken)
	{
		if (_hook is { } hook)
		{
			hook.OnRun(this);
			return;
		}

		var loop = BuildForRun();
		if (int.TryParse(Configuration[RunFramesKey], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var frames) && frames >= 0)
		{
			loop.RunFrames(frames);
		}
		else
		{
			loop.Run(cancellationToken);
		}
	}

	/// <summary>
	/// The configuration key that makes <see cref="Run()"/> stop after a number of frames, as <see cref="RunFrames"/>:
	/// <c>Ion:Run:Frames = 600</c>, or <c>--Ion:Run:Frames=600</c> on the command line (what <c>ion run --frames</c>
	/// passes). The game still exits earlier if it asks to.
	/// </summary>
	public const string RunFramesKey = "Ion:Run:Frames";

	/// <inheritdoc/>
	[StackTraceHidden]
	public void RunFrames(int frames)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		if (_hook is { } hook)
		{
			hook.OnRun(this);
			return;
		}

		BuildForRun().RunFrames(frames);
	}

	private GameLoop BuildForRun()
	{
		var gameLoop = Build();

#if DEBUG
		HotReloadService.ActiveApplication = this;
		HotReloadService.ActiveGameLoop = gameLoop;
#endif
		return gameLoop;
	}

	/// <summary>Disposes the application's services. Calling it again does nothing.</summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;
		_host.Dispose();
	}
}
