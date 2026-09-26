using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

using Microsoft.Extensions.DependencyInjection;

namespace Ion;

/// <summary>
/// A schedule emitted by the Ion source generator: one method per stage that calls every step directly, in plan order,
/// with a <c>try/finally</c> per scope. <see cref="ScheduleModel.Build"/> runs it instead of the delegate-based
/// <see cref="Schedule"/> runners when the registrations made at run time are exactly the ones the generator saw (see
/// <see cref="GeneratedScheduleFactory"/>).
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class GeneratedSchedule
{
	/// <summary>Runs the Init stage.</summary>
	public abstract void Init(GameTime dt);

	/// <summary>Runs the First stage.</summary>
	public abstract void First(GameTime dt);

	/// <summary>Runs one FixedUpdate step.</summary>
	public abstract void FixedUpdate(GameTime dt);

	/// <summary>Runs the Update stage.</summary>
	public abstract void Update(GameTime dt);

	/// <summary>Runs the Render stage.</summary>
	public abstract void Render(GameTime dt);

	/// <summary>Runs the Last stage.</summary>
	public abstract void Last(GameTime dt);

	/// <summary>Runs the Destroy stage.</summary>
	public abstract void Destroy(GameTime dt);
}

/// <summary>
/// Creates a <see cref="GeneratedSchedule"/> for one schedule the generator saw (the registrations of an application
/// before its <c>Build()</c>/<c>Run()</c> call, or of a scene's configure callback), after checking that it matches what
/// was registered at run time.
/// </summary>
/// <remarks>
/// <para>
/// The generator lists every registration it can see, in the order they would run, each with the call site that makes it
/// (<see cref="ScheduleEntry.Site"/>) and whether it depends on a branch (an <c>if</c>, a loop, a callback). Conditional
/// registrations are guarded in the generated stage methods. At build time the runtime registrations are aligned with
/// that list: every runtime registration must come from a listed call site, in the same order, and every unconditional
/// listed registration must be present. The runtime plan (planned by the same rules the generator used) must then equal,
/// stage by stage, the generated order restricted to the present registrations.
/// </para>
/// <para>
/// When anything differs (a registration the generator could not see, such as a plugin or a <c>Type</c> only known at run
/// time, a helper the generator could not follow, a registration repeated in a loop) the generated schedule is not used
/// and the runtime binds the plan itself: correctness never depends on the generator seeing everything, only speed does.
/// </para>
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public abstract class GeneratedScheduleFactory
{
	private readonly string[] _sites;
	private readonly bool[] _conditional;
	private readonly string[][] _stages;

	/// <summary>Creates the factory from the generator's data.</summary>
	/// <param name="name">A name for logs (the method that builds the application, or the scene).</param>
	/// <param name="sites">The call site of every registration the generator saw, in registration order.</param>
	/// <param name="conditional">For every entry of <paramref name="sites"/>, whether it depends on a branch.</param>
	/// <param name="stages">For each of the seven stages, the items in run order, as <c>entry|kind|method|declaration</c> keys.</param>
	protected GeneratedScheduleFactory(string name, string[] sites, bool[] conditional, string[][] stages)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(sites);
		ArgumentNullException.ThrowIfNull(conditional);
		ArgumentNullException.ThrowIfNull(stages);
		if (sites.Length != conditional.Length) throw new ArgumentException("One conditional flag per site is required.", nameof(conditional));
		if (stages.Length != 7) throw new ArgumentException("Seven stages are required.", nameof(stages));

		Name = name;
		_sites = sites;
		_conditional = conditional;
		_stages = stages;
	}

	/// <summary>A name for logs.</summary>
	public string Name { get; }

	/// <summary>Creates the schedule; the context resolves systems, services and delegates of present registrations.</summary>
	protected abstract GeneratedSchedule Create(GeneratedScheduleContext context);

	/// <summary>
	/// Creates the generated schedule when <paramref name="plan"/> (the runtime plan of <paramref name="model"/>) is the
	/// one the generator emitted.
	/// </summary>
	/// <param name="model">The registrations made at run time.</param>
	/// <param name="plan">Their plan.</param>
	/// <param name="services">The services to resolve systems and injected parameters from.</param>
	/// <param name="schedule">The generated schedule, when it matches.</param>
	/// <param name="mismatch">Why it does not match, otherwise.</param>
	public bool TryCreate(ScheduleModel model, SchedulePlan plan, IServiceProvider services, [NotNullWhen(true)] out GeneratedSchedule? schedule, [NotNullWhen(false)] out string? mismatch)
	{
		ArgumentNullException.ThrowIfNull(model);
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(services);

		schedule = null;
		var entries = model.Entries;
		var byCandidate = new ScheduleEntry?[_sites.Length];
		var candidateOf = new Dictionary<ScheduleEntry, int>(entries.Count);
		var next = 0;

		foreach (var entry in entries)
		{
			if (entry.Site is null)
			{
				mismatch = $"'{entry}' was registered at run time without a generated call site (a Type only known at run time, a plugin, or a helper compiled without the generator).";
				return false;
			}

			while (next < _sites.Length && _sites[next] != entry.Site)
			{
				if (!_conditional[next])
				{
					mismatch = $"the registration at {_sites[next]} did not run before '{entry}' ({entry.Site}).";
					return false;
				}

				next++;
			}

			if (next == _sites.Length)
			{
				mismatch = $"'{entry}' ({entry.Site}) was registered out of the order the generator saw, or more often.";
				return false;
			}

			byCandidate[next] = entry;
			candidateOf[entry] = next;
			next++;
		}

		for (; next < _sites.Length; next++)
		{
			if (!_conditional[next])
			{
				mismatch = $"the registration at {_sites[next]} did not run.";
				return false;
			}
		}

		foreach (var stage in plan.Stages)
		{
			var expected = _stages[(int)stage.Stage - 1];
			var e = 0;

			foreach (var step in stage.Steps)
			{
				while (e < expected.Length && byCandidate[EntryOf(expected[e])] is null) e++;

				var key = Key(step, candidateOf);
				if (e == expected.Length || expected[e] != key)
				{
					mismatch = $"{stage.Stage} runs '{step.Name}' ({key}) where the generated schedule has {(e == expected.Length ? "nothing" : expected[e])}.";
					return false;
				}

				e++;
			}

			while (e < expected.Length && byCandidate[EntryOf(expected[e])] is null) e++;
			if (e < expected.Length)
			{
				mismatch = $"{stage.Stage} of the generated schedule has {expected[e]} which the runtime plan does not.";
				return false;
			}
		}

		schedule = Create(new GeneratedScheduleContext(services, byCandidate));
		mismatch = null;
		return true;
	}

	private static int EntryOf(string key) => int.Parse(key.AsSpan(0, key.IndexOf('|')), NumberStyles.None, CultureInfo.InvariantCulture);

	/// <summary>The key of a planned item: <c>entry|kind|method|declaration</c> (see the generator).</summary>
	private static string Key(StepPlan step, Dictionary<ScheduleEntry, int> candidateOf)
	{
		ScheduleEntry? entry = (ScheduleEntry?)step.System ?? (ScheduleEntry?)step.Function ?? step.Middleware;
		var c = entry is not null && candidateOf.TryGetValue(entry, out var index) ? index : -1;
		var kind = step.Kind switch
		{
			StepKind.Step => "S",
			StepKind.Scope => "B",
			StepKind.Function => "F",
			_ => step.System is null ? "D" : "M",
		};

		return string.Create(CultureInfo.InvariantCulture, $"{c}|{kind}|{step.MethodName}|{step.DeclarationIndex}");
	}
}

/// <summary>
/// What a <see cref="GeneratedSchedule"/> needs from the runtime registrations: which are present, the system
/// instances (resolved once, like the runtime binder does), services and delegates.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class GeneratedScheduleContext
{
	private readonly ScheduleEntry?[] _entries;
	private readonly object?[] _instances;

	internal GeneratedScheduleContext(IServiceProvider services, ScheduleEntry?[] entries)
	{
		Services = services;
		_entries = entries;
		_instances = new object?[entries.Length];
	}

	/// <summary>The services the schedule resolves from.</summary>
	public IServiceProvider Services { get; }

	/// <summary>
	/// The profiler the generated stage methods record a span per step and scope into (<see cref="FrameProfiler.Disabled"/>
	/// when none is registered).
	/// </summary>
	public FrameProfiler Profiler => _profiler ??= Services.GetService<FrameProfiler>() ?? FrameProfiler.Disabled;

	private FrameProfiler? _profiler;

	/// <summary>Whether registration <paramref name="entry"/> (an index into the generator's list) ran.</summary>
	public bool IsActive(int entry) => _entries[entry] is not null;

	/// <summary>The instance of system registration <paramref name="entry"/>, resolved once; null when it did not run.</summary>
	public T System<T>(int entry) where T : class => (T)Instance(entry)!;

	/// <summary>A service injected into a step of registration <paramref name="entry"/>; default when it did not run.</summary>
	public T Service<T>(int entry) where T : notnull => IsActive(entry) ? Services.GetRequiredService<T>() : default!;

	/// <summary>A step of system registration <paramref name="entry"/> bound through its <see cref="GeneratedSystem"/> (for systems the generated code cannot call directly).</summary>
	public GameLoopDelegate Step(int entry, Stage stage, string method, int declarationIndex)
	{
		if (Generated(entry) is not { } system) return null!;
		var step = system.GetStep(stage, GeneratedStepKind.Step, method, declarationIndex);
		return step.Bind!(step.IsStatic ? null : Instance(entry), Services);
	}

	/// <summary>The begin method of a scope of system registration <paramref name="entry"/>, bound through its <see cref="GeneratedSystem"/>.</summary>
	public GameLoopDelegate ScopeBegin(int entry, Stage stage, string method, int declarationIndex)
	{
		if (Generated(entry) is not { } system) return null!;
		var step = system.GetStep(stage, GeneratedStepKind.Scope, method, declarationIndex);
		return step.Bind!(step.IsStatic ? null : Instance(entry), Services);
	}

	/// <summary>The end method of a scope of system registration <paramref name="entry"/>, bound through its <see cref="GeneratedSystem"/>.</summary>
	public GameLoopDelegate ScopeEnd(int entry, Stage stage, string method, int declarationIndex)
	{
		if (Generated(entry) is not { } system) return null!;
		var step = system.GetStep(stage, GeneratedStepKind.Scope, method, declarationIndex);
		return step.BindEnd!(step.EndIsStatic ? null : Instance(entry), Services);
	}

	/// <summary>A legacy middleware method of system registration <paramref name="entry"/>, bound through its <see cref="GeneratedSystem"/>.</summary>
	public Func<GameLoopDelegate, GameLoopDelegate> StepMiddleware(int entry, Stage stage, string method, int declarationIndex)
	{
		if (Generated(entry) is not { } system) return null!;
		var step = system.GetStep(stage, GeneratedStepKind.Middleware, method, declarationIndex);
		return step.BindMiddleware!(step.IsStatic ? null : Instance(entry), Services);
	}

	/// <summary>The user delegate of function registration <paramref name="entry"/>; null when it did not run.</summary>
	public TDelegate Function<TDelegate>(int entry) where TDelegate : Delegate => _entries[entry] is FunctionEntry function ? (TDelegate)function.Function : null!;

	/// <summary>The bound per-frame delegate of function registration <paramref name="entry"/>; null when it did not run.</summary>
	public GameLoopDelegate BoundFunction(int entry) => _entries[entry] is FunctionEntry function ? function.Bind(Services) : null!;

	/// <summary>The factory of middleware registration <paramref name="entry"/>; null when it did not run.</summary>
	public Func<GameLoopDelegate, GameLoopDelegate> Middleware(int entry) => _entries[entry] is MiddlewareEntry middleware ? middleware.Middleware : null!;

	private GeneratedSystem? Generated(int entry) => _entries[entry] is SystemEntry system
		? system.Generated ?? throw new InvalidOperationException($"System '{system}' was not registered by generated code.")
		: null;

	private object? Instance(int entry)
	{
		if (_entries[entry] is not SystemEntry system) return null;
		return _instances[entry] ??= Services.GetRequiredService(system.ServiceType);
	}
}
