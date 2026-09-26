using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Core;
using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;
using Ion.Extensions.Metrics;

namespace Ion.Testing;

/// <summary>
/// Runs an Ion game headless and deterministically for tests: no GPU, window or audio device, and a
/// <see cref="FixedStepClock"/> so every frame lasts exactly <see cref="FrameTime"/>.
/// </summary>
/// <remarks>
/// <para>
/// Configure the host with the <c>With*</c> and <c>Configure*</c> methods, then drive it with <see cref="Step"/> or
/// <see cref="RunUntil"/>. The application is built, and the Init stage run, on the first <see cref="Step"/>,
/// <see cref="RunUntil"/> or access to <see cref="Services"/> (or an explicit <see cref="Start"/>); configuring after that
/// throws <see cref="InvalidOperationException"/>. <see cref="Dispose"/> runs the Destroy stage and disposes the application.
/// </para>
/// <para>
/// By default the host registers every extension with <c>AddIon</c> (headless) and wires <c>UseIon</c>, then the systems
/// added with <see cref="WithSystem{T}"/>, in order. To test a whole game whose setup already calls <c>AddIon</c> and
/// <c>UseIon</c>, pass its setup to <see cref="UseGame"/> instead. Console logging is off; add providers in
/// <see cref="Configure"/> to see logs.
/// </para>
/// <para>
/// Exceptions thrown by systems propagate out of <see cref="Step"/>. A frame in which the game asks to exit
/// (<see cref="ExitGameEvent"/>) ends the current <see cref="Step"/> call early; <see cref="IsExitRequested"/> tells.
/// </para>
/// </remarks>
public sealed class IonTestHost : IDisposable
{
	/// <summary>
	/// The default frame time: one 60 Hz step, rounded up to a whole tick so that every frame runs exactly one fixed step
	/// at the default <see cref="GameConfig.FixedUpdateRate"/>.
	/// </summary>
	public static readonly TimeSpan DefaultFrameTime = TimeSpan.FromTicks((TimeSpan.TicksPerSecond + 59) / 60);

	private readonly List<Action<IServiceCollection>> _services = [];
	private readonly List<Action<IIonApplication>> _app = [];
	private readonly List<(Action<IServiceCollection> Register, Action<IIonApplication> Use)> _systems = [];
	private const string HeadlessKey = "Ion:Headless";

	private const string HotReloadKey = "Ion:Assets:HotReload";

	// Asset hot reload is off by default so no file system watcher runs; tests may turn it on with WithConfiguration.
	private readonly Dictionary<string, string?> _settings = new() { [HeadlessKey] = "true", [HotReloadKey] = "false" };
	private readonly List<IEventCollector> _collectors = [];

	private Action<IonApplicationBuilder>? _gameBuilder;
	private Action<IIonApplication>? _gameApp;
	private string[] _args = [];

	private IonApplication? _application;
	private GameLoop? _loop;
	private bool _disposed;

	/// <summary>
	/// Creates a host whose frames last <paramref name="frameTime"/> (<see cref="DefaultFrameTime"/> when omitted).
	/// </summary>
	public IonTestHost(TimeSpan? frameTime = null)
	{
		FrameTime = frameTime ?? DefaultFrameTime;
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(FrameTime, TimeSpan.Zero, nameof(frameTime));
		Clock = new FixedStepClock(FrameTime);
	}

	/// <summary>The duration of every frame.</summary>
	public TimeSpan FrameTime { get; }

	/// <summary>The deterministic clock driving the game loop.</summary>
	public FixedStepClock Clock { get; }

	/// <summary>Whether the application has been built and initialized.</summary>
	public bool IsStarted => _loop is not null;

	/// <summary>The application. Starts the host.</summary>
	public IonApplication Application => _started().Application;

	/// <summary>The application's services. Starts the host.</summary>
	public IServiceProvider Services => Application.Services;

	/// <summary>The game loop. Starts the host.</summary>
	public GameLoop Loop => _started().Loop;

	/// <summary>The number of frames run so far.</summary>
	public long Frame => _loop?.GameTime.Frame ?? 0;

	/// <summary>The scripted input. Input queued now is applied at the start of the next frame. Starts the host.</summary>
	public NullInputState Input => Get<NullInputState>();

	/// <summary>The headless window. Starts the host.</summary>
	public NullWindow Window => Get<NullWindow>();

	/// <summary>The headless recording sprite batch: <see cref="NullSpriteBatch.LastFrame"/> has the last frame's draw counts (empty with <see cref="WithRendering"/>, where the 2D renderer draws). Starts the host.</summary>
	public NullSpriteBatch SpriteBatch => Get<NullSpriteBatch>();

	/// <summary>The headless audio manager: <see cref="NullAudioManager.Plays"/> has every sound played. Starts the host.</summary>
	public NullAudioManager Audio => Get<NullAudioManager>();

	/// <summary>
	/// The application's metrics: the frame profiler (set <see cref="IMetrics.IsProfiling"/> to record spans), game
	/// counters and trace capture. Starts the host.
	/// </summary>
	public IMetrics Metrics => Get<IMetrics>();

	/// <summary>
	/// The stats of the last frame run: draw calls, sprites and triangles from the sprite batch, fixed steps, events,
	/// allocations and wall-clock timings. Default before the first frame. Starts the host.
	/// </summary>
	public FrameStats LastFrame => Loop.Profiler.LastFrame;

	/// <summary>The application's event bus, for emitting events into the game. Starts the host.</summary>
	public IEvents Events => Get<IEvents>();

	/// <summary>Whether the game asked to exit (<see cref="ExitGameEvent"/> or <see cref="GameLoop.Stop"/>).</summary>
	public bool IsExitRequested => _loop?.IsExitRequested ?? false;

	/// <summary>
	/// Resolves a required service. Starts the host.
	/// </summary>
	public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

	/// <summary>
	/// Registers <typeparamref name="T"/> as a singleton and adds it to the schedule. Its steps run by their order (the
	/// default order 0 is after the engine's setup steps and before its teardown steps), then in the order systems were added.
	/// </summary>
	public IonTestHost WithSystem<[DynamicallyAccessedMembers(SystemMembers)] T>() where T : class => WithSystem(typeof(T));

	/// <summary>
	/// Registers <paramref name="system"/> as a singleton and adds it to the schedule, like <see cref="WithSystem{T}"/>.
	/// </summary>
	public IonTestHost WithSystem([DynamicallyAccessedMembers(SystemMembers)] Type system)
	{
		ArgumentNullException.ThrowIfNull(system);
		_ensureNotStarted();
		_systems.Add((services => services.AddSingleton(system), app => app.UseSystem(system)));
		return this;
	}

	private const DynamicallyAccessedMemberTypes SystemMembers = SystemMiddlewareBinder.MiddlewareAccessibility;

	/// <summary>The order of the Last step that polls the event collectors (see <see cref="Collect{T}"/>).</summary>
	public const int CollectorOrder = StageOrder.Events - 10;

	/// <summary>
	/// Adds service registrations. Runs after the engine's registrations, so it can replace them.
	/// </summary>
	public IonTestHost Configure(Action<IServiceCollection> services)
	{
		ArgumentNullException.ThrowIfNull(services);
		_ensureNotStarted();
		_services.Add(services);
		return this;
	}

	/// <summary>
	/// Adds schedule setup (for example <c>app.UseSystem&lt;T&gt;()</c> or <c>app.Update(...)</c>). Runs after
	/// <c>UseIon</c> (or the game's own setup) and before the systems added with <see cref="WithSystem{T}"/>.
	/// </summary>
	public IonTestHost ConfigureApp(Action<IIonApplication> app)
	{
		ArgumentNullException.ThrowIfNull(app);
		_ensureNotStarted();
		_app.Add(app);
		return this;
	}

	/// <summary>
	/// Adds configuration values (for example <c>["Ion:Seed"] = "42"</c>). Later values win. <c>Ion:Headless</c> is always
	/// <c>true</c>. Asset hot reload (<c>Ion:Assets:HotReload</c>) defaults to <c>false</c> in the test host.
	/// </summary>
	public IonTestHost WithConfiguration(IEnumerable<KeyValuePair<string, string?>> settings)
	{
		ArgumentNullException.ThrowIfNull(settings);
		_ensureNotStarted();
		foreach (var (key, value) in settings) _settings[key] = value;
		_settings[HeadlessKey] = "true";
		return this;
	}

	/// <summary>
	/// Adds one configuration value, like <see cref="WithConfiguration(IEnumerable{KeyValuePair{string, string}})"/>.
	/// </summary>
	public IonTestHost WithConfiguration(string key, string? value) => WithConfiguration([new(key, value)]);

	/// <summary>
	/// Passes command line arguments to <see cref="IonApplication.CreateBuilder(string[])"/>.
	/// </summary>
	public IonTestHost WithArgs(params string[] args)
	{
		ArgumentNullException.ThrowIfNull(args);
		_ensureNotStarted();
		_args = args;
		return this;
	}

	/// <summary>
	/// Builds a whole game with its own setup instead of the default <c>AddIon</c>/<c>UseIon</c>: <paramref name="configure"/>
	/// registers its services (and is expected to call <c>AddIon</c>) and <paramref name="use"/> wires its schedule (and is
	/// expected to call <c>UseIon</c>). The host still forces headless mode and the deterministic clock.
	/// </summary>
	public IonTestHost UseGame(Action<IonApplicationBuilder> configure, Action<IIonApplication> use)
	{
		ArgumentNullException.ThrowIfNull(configure);
		ArgumentNullException.ThrowIfNull(use);
		_ensureNotStarted();
		_gameBuilder = configure;
		_gameApp = use;
		return this;
	}

	/// <summary>
	/// Records every event of type <typeparamref name="T"/> the game emits from now on (polled by a Last step at
	/// <see cref="CollectorOrder"/>, so events emitted by Last steps with a higher order are recorded the next frame).
	/// Starts the host.
	/// </summary>
	[ReadsEvent]
	public EventCollector<T> Collect<T>() where T : unmanaged
	{
		var collector = new EventCollector<T>(Get<IEvents>().Reader<T>());
		_collectors.Add(collector);
		return collector;
	}

	/// <summary>
	/// Builds the application and runs the Init stage. Called implicitly by the members that need a running game.
	/// </summary>
	public IonTestHost Start()
	{
		_started();
		return this;
	}

	/// <summary>
	/// Builds <typeparamref name="TGame"/> headless (its <see cref="IIonGame.Configure"/> and <see cref="IIonGame.Use"/>, with
	/// the deterministic clock), runs <paramref name="frames"/> frames and returns the outcome: state (the still running
	/// host and its services), counters, the last frame's stats and, with rendering, the image. Dispose the result.
	/// </summary>
	/// <param name="frames">The number of frames to run.</param>
	/// <param name="configure">
	/// Configures the host before it starts: <c>host =&gt; host.WithRendering()</c> for an image,
	/// <c>WithConfiguration("Ion:Seed", "42")</c>, <c>ConfigureApp</c> to add systems.
	/// </param>
	/// <param name="frameTime">The frame time (<see cref="DefaultFrameTime"/> when omitted).</param>
	public static IonRunResult Run<TGame>(int frames, Action<IonTestHost>? configure = null, TimeSpan? frameTime = null) where TGame : IIonGame
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		var host = new IonTestHost(frameTime).UseGame(TGame.Configure, TGame.Use);
		try
		{
			configure?.Invoke(host);
			var run = host.Step(frames);
			return new IonRunResult(host, run);
		}
		catch
		{
			host.Dispose();
			throw;
		}
	}

	/// <summary>
	/// Runs <paramref name="frames"/> frames (fewer if the game asks to exit). Returns the number of frames run.
	/// </summary>
	public int Step(int frames = 1)
	{
		ArgumentOutOfRangeException.ThrowIfNegative(frames);
		var loop = _started().Loop;

		for (var i = 0; i < frames; i++)
		{
			loop.Step();
			if (loop.IsExitRequested) return i + 1;
		}

		return frames;
	}

	/// <summary>
	/// Runs frames until <paramref name="condition"/> is true (checked before the first frame and after each one), the
	/// game asks to exit or <paramref name="maxFrames"/> frames have run. Returns whether the condition was met.
	/// </summary>
	public bool RunUntil(Func<bool> condition, int maxFrames = 10_000)
	{
		ArgumentNullException.ThrowIfNull(condition);
		ArgumentOutOfRangeException.ThrowIfNegative(maxFrames);
		var loop = _started().Loop;

		if (condition()) return true;

		for (var i = 0; i < maxFrames; i++)
		{
			loop.Step();
			if (condition()) return true;
			if (loop.IsExitRequested) return false;
		}

		return false;
	}

	/// <summary>
	/// Turns on headless rendering (<c>Ion:Headless:Render = true</c>): an RHI backend renders every frame into an offscreen
	/// target sized from <c>Ion:Window</c> (960x540 by default), systems can render through <see cref="IGraphicsFrame"/>, and
	/// <see cref="Screenshot"/> captures frames. The backend follows <c>Ion:Graphics:PreferredBackend</c>: <c>Auto</c> (the
	/// default) takes the first available (Vulkan on Mesa lavapipe, else OpenGL ES through EGL on Mesa llvmpipe, no display
	/// needed), or <c>Vulkan</c>/<c>OpenGLES</c> forces one. <see cref="ISpriteBatch"/> is then the 2D renderer's sprite batch,
	/// so sprites are rasterized; <see cref="SpriteBatch"/> (the recording null batch) no longer receives the game's draws.
	/// </summary>
	public IonTestHost WithRendering(uint? width = null, uint? height = null)
	{
		WithConfiguration(RenderKey, "true");
		if (width is { } w) WithConfiguration("Ion:Window:Width", w.ToString(System.Globalization.CultureInfo.InvariantCulture));
		if (height is { } h) WithConfiguration("Ion:Window:Height", h.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return this;
	}

	/// <summary>
	/// Captures the last rendered frame as RGBA8 pixels (compare it with <see cref="GoldenImage"/>). Requires headless
	/// rendering (<see cref="WithRendering"/> or <c>Ion:Headless:Render = true</c>) and at least one frame run.
	/// </summary>
	/// <exception cref="NotSupportedException">Headless rendering is off: the null backend records draw calls (see <see cref="SpriteBatch"/>) but does not rasterize them.</exception>
	/// <exception cref="InvalidOperationException">No frame has been rendered yet.</exception>
	public Screenshot Screenshot()
	{
		var source = Services.GetService<IScreenshotSource>()
			?? throw new NotSupportedException("IonTestHost captures frames only with headless rendering: call WithRendering() (Ion:Headless:Render = true). The default headless backend records draw calls (see SpriteBatch) but does not rasterize them.");
		return source.Capture();
	}

	/// <summary>Captures the last rendered frame (see <see cref="Screenshot"/>) and writes it to <paramref name="path"/> as PNG.</summary>
	public void SaveScreenshot(string path) => GoldenImage.Save(Screenshot(), path);

	private const string RenderKey = "Ion:Headless:Render";

	/// <summary>
	/// Runs the Destroy stage (when the game was started) and disposes the application and its services.
	/// </summary>
	public void Dispose()
	{
		if (_disposed) return;
		_disposed = true;

		try
		{
			_loop?.Shutdown();
		}
		finally
		{
			foreach (var collector in _collectors) collector.Dispose();
			_collectors.Clear();
			_application?.Dispose();
		}
	}

	private (IonApplication Application, GameLoop Loop) _started()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);

		if (_application is not null && _loop is not null) return (_application, _loop);
		if (_application is not null) throw new InvalidOperationException("The game failed to start; create a new IonTestHost.");

		var builder = IonApplication.CreateBuilder(_args);
		builder.Configuration.AddInMemoryCollection(_settings);
		builder.Services.AddLogging(logging => logging.ClearProviders());

		if (_gameBuilder is not null) _gameBuilder(builder);
		else builder.Services.AddIon(builder.Configuration);

		foreach (var system in _systems) system.Register(builder.Services);
		foreach (var configure in _services) configure(builder.Services);

		// Registered last so it wins over the default clock and anything the game registers.
		builder.Services.AddSingleton<IClock>(Clock);

		var application = builder.Build();
		_application = application;

		if (_gameApp is not null) _gameApp(application);
		else application.UseIon();

		foreach (var use in _app) use(application);
		foreach (var system in _systems) system.Use(application);

		// A Last step at the end of the teardown band: after every Last step at a lower order, and before the event system
		// steps the frame buffers (order StageOrder.Events).
		application.Last(dt =>
		{
			for (var i = 0; i < _collectors.Count; i++) _collectors[i].Poll(dt.Frame);
		}, order: CollectorOrder, name: "IonTestHost.PollCollectors");

		var loop = application.Build();
		loop.Initialize();
		_loop = loop;

		return (application, loop);
	}

	private void _ensureNotStarted()
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_application is not null) throw new InvalidOperationException("The host has already started; configure it before the first Step, RunUntil or service access.");
	}
}
