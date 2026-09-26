using Microsoft.Extensions.Configuration;

namespace Ion;

/// <summary>
/// The game application used to build the game loop. Systems (<c>UseSystem</c>) and function steps (<c>Update(...)</c>,
/// <c>Render(...)</c>, ...) are added to its root <see cref="IScheduleBuilder.Schedule"/>.
/// </summary>
public interface IIonApplication : IScheduleBuilder
{
	/// <summary>
	/// The application's configured <see cref="IConfiguration"/>.
	/// </summary>
	IConfiguration Configuration { get; }

	/// <summary>
	/// The application's configured services.
	/// </summary>
	new IServiceProvider Services { get; }

	/// <summary>
	/// Adds a legacy middleware to the application's Init stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.Init((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseInit(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's First stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.First((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseFirst(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's FixedUpdate stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.FixedUpdate((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseFixedUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's Update stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.Update((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseUpdate(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's Render stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.Render((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseRender(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's Last stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.Last((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseLast(Func<GameLoopDelegate, GameLoopDelegate> middleware);
	/// <summary>
	/// Adds a legacy middleware to the application's Destroy stage, at order 0. It wraps every step that sorts after it, and
	/// building logs warning ION010: prefer a function step (<c>app.Destroy((GameTime dt, ...) =&gt; ...)</c>) or a system.
	/// </summary>
	/// <param name="middleware">The game loop middleware function.</param>
	/// <returns>The <see cref="IIonApplication"/> instance to chain `Use` calls.</returns>
	IIonApplication UseDestroy(Func<GameLoopDelegate, GameLoopDelegate> middleware);

	/// <summary>
	/// Plans the schedule (validating it) and returns it as text: every stage with its steps in run order, their orders and
	/// the scopes that wrap them, then each scene's schedule. The same text is printed at startup with
	/// <c>--Ion:PrintSchedule=true</c>.
	/// </summary>
	/// <exception cref="IonScheduleException">The schedule has errors.</exception>
	string PrintSchedule();

	/// <summary>
	/// Builds and runs the game until it exits (see <see cref="ExitGameEvent"/>).
	/// </summary>
	void Run();

	/// <summary>
	/// Builds and runs the game until it exits or <paramref name="cancellationToken"/> is cancelled.
	/// </summary>
	/// <param name="cancellationToken">Stops the game loop (after the current frame) when cancelled.</param>
	void Run(CancellationToken cancellationToken);

	/// <summary>
	/// Builds the game and runs Init, at most <paramref name="frames"/> frames, then Destroy. Useful for headless tests
	/// and deterministic runs.
	/// </summary>
	/// <param name="frames">The number of frames to run. Must not be negative.</param>
	void RunFrames(int frames);
}