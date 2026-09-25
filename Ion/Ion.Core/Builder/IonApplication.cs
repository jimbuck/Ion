using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Ion.Core;

namespace Ion;

public class IonApplication : IIonApplication, IDisposable
{
	private readonly IHost _host;

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

	internal IonApplication(IHost host)
	{
		_host = host;
	}

	public static IonApplicationBuilder CreateBuilder(string[] args)
	{
		return new IonApplicationBuilder(args);
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
		BuildForRun().Run(cancellationToken);
	}

	/// <inheritdoc/>
	[StackTraceHidden]
	public void RunFrames(int frames)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
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

	public void Dispose()
	{
		_host.Dispose();
	}
}
