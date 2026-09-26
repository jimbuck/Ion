using System.Diagnostics;
using System.Reflection;

using Microsoft.Extensions.DependencyInjection;

namespace Ion;

/// <summary>
/// A runnable schedule: a <see cref="SchedulePlan"/> bound to instances. Each stage is a flat array of step delegates
/// (direct delegate calls, no closure chain for leaf steps); a scope splits the stage into the steps before it and a
/// nested runner it wraps with <c>Begin</c>, <c>try</c>, <c>finally End</c>.
/// </summary>
public sealed class Schedule
{
	private readonly StageRunner[] _stages = new StageRunner[7];

	/// <summary>
	/// Binds <paramref name="plan"/>: resolves every system once (as its service type) and every injected service from
	/// <paramref name="services"/>, and creates the stage runners. Normally called by <see cref="ScheduleModel.Build"/>.
	/// </summary>
	public Schedule(SchedulePlan plan, IServiceProvider services)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(services);

		Plan = plan;
		var binder = new Binder(services, FrameProfiler.IsProfilingEnabled && services.GetService<IStepProfiler>() is { CanRecord: true } profiler ? profiler : null);

		foreach (var stage in plan.Stages)
		{
			_stages[(int)stage.Stage - 1] = binder.BuildRunner(stage.Steps, 0);
		}

		Init = _stages[0].Entry;
		First = _stages[1].Entry;
		FixedUpdate = _stages[2].Entry;
		Update = _stages[3].Entry;
		Render = _stages[4].Entry;
		Last = _stages[5].Entry;
		Destroy = _stages[6].Entry;
	}

	/// <summary>
	/// Runs <paramref name="plan"/> with the stage methods of a <see cref="Ion.GeneratedSchedule"/> emitted by the source
	/// generator (normally created by <see cref="ScheduleModel.Build"/> once the generated schedule matched the plan).
	/// </summary>
	public Schedule(SchedulePlan plan, GeneratedSchedule generated)
	{
		ArgumentNullException.ThrowIfNull(plan);
		ArgumentNullException.ThrowIfNull(generated);

		Plan = plan;
		Generated = generated;

		Init = generated.Init;
		First = generated.First;
		FixedUpdate = generated.FixedUpdate;
		Update = generated.Update;
		Render = generated.Render;
		Last = generated.Last;
		Destroy = generated.Destroy;

		_stages[0] = new StageRunner([Init]);
		_stages[1] = new StageRunner([First]);
		_stages[2] = new StageRunner([FixedUpdate]);
		_stages[3] = new StageRunner([Update]);
		_stages[4] = new StageRunner([Render]);
		_stages[5] = new StageRunner([Last]);
		_stages[6] = new StageRunner([Destroy]);
	}

	/// <summary>The plan this schedule was bound from.</summary>
	public SchedulePlan Plan { get; }

	/// <summary>
	/// The generated schedule whose stage methods this schedule runs, or null when the runtime bound the plan itself
	/// (the generator is not referenced, or the registrations differ from what it saw).
	/// </summary>
	public GeneratedSchedule? Generated { get; }

	/// <summary>Whether the stages run the generated stage methods (see <see cref="Generated"/>).</summary>
	public bool IsGenerated => Generated is not null;

	/// <summary>Runs the Init stage.</summary>
	public GameLoopDelegate Init { get; }

	/// <summary>Runs the First stage.</summary>
	public GameLoopDelegate First { get; }

	/// <summary>Runs one FixedUpdate step.</summary>
	public GameLoopDelegate FixedUpdate { get; }

	/// <summary>Runs the Update stage.</summary>
	public GameLoopDelegate Update { get; }

	/// <summary>Runs the Render stage.</summary>
	public GameLoopDelegate Render { get; }

	/// <summary>Runs the Last stage.</summary>
	public GameLoopDelegate Last { get; }

	/// <summary>Runs the Destroy stage.</summary>
	public GameLoopDelegate Destroy { get; }

	/// <summary>The delegate that runs <paramref name="stage"/>.</summary>
	public GameLoopDelegate this[Stage stage] => stage switch
	{
		Stage.Init => Init,
		Stage.First => First,
		Stage.FixedUpdate => FixedUpdate,
		Stage.Update => Update,
		Stage.Render => Render,
		Stage.Last => Last,
		Stage.Destroy => Destroy,
		_ => throw new ArgumentOutOfRangeException(nameof(stage), stage, "Unknown stage."),
	};

	/// <summary>Runs <paramref name="stage"/> with <paramref name="dt"/>.</summary>
	[StackTraceHidden]
	public void Run(Stage stage, GameTime dt) => _stages[(int)stage - 1].Entry(dt);

	/// <summary>The schedule as text (see <see cref="SchedulePlan.Print"/>).</summary>
	public string Print() => Plan.Print();

	/// <inheritdoc/>
	public override string ToString() => Plan.Print();

	private sealed class Binder(IServiceProvider services, IStepProfiler? profiler)
	{
		private readonly Dictionary<SystemEntry, object> _instances = [];

		public StageRunner BuildRunner(IReadOnlyList<StepPlan> steps, int start)
		{
			var leaves = new List<GameLoopDelegate>();
			var ids = new List<SpanId>();
			var i = start;

			while (i < steps.Count && !steps[i].Wraps)
			{
				leaves.Add(BindLeaf(steps[i]));
				if (profiler is not null) ids.Add(MetricsIds.Register(steps[i].Name));
				i++;
			}

			var profiling = profiler is null ? null : new StageProfiling(profiler, [.. ids], i < steps.Count ? MetricsIds.Register(steps[i].Name) : default);
			if (i == steps.Count) return new StageRunner([.. leaves], profiling);

			var wrapper = steps[i];
			var inner = BuildRunner(steps, i + 1);

			if (wrapper.Kind == StepKind.Scope)
			{
				var instance = Instance(wrapper);
				if (wrapper.Generated is { } scope)
				{
					return new StageRunner([.. leaves], scope.Bind!(scope.IsStatic ? null : instance, services), scope.BindEnd!(scope.EndIsStatic ? null : instance, services), inner, profiling);
				}

				return new StageRunner([.. leaves], Bind(instance, wrapper.Method!), Bind(instance, wrapper.EndMethod!), inner, profiling);
			}

			// A middleware's next is the inner runner's entry: the next middleware or the single step directly when that is
			// all the inner runner does, so a chain of legacy middleware costs what it did before 0.3.
			var middleware = wrapper.Middleware?.Middleware
				?? (wrapper.Generated is { } legacy ? legacy.BindMiddleware!(legacy.IsStatic ? null : Instance(wrapper), services) : null)
				?? BindLegacy(Instance(wrapper), wrapper.Method!);
			return new StageRunner([.. leaves], middleware(inner.Entry), profiling);
		}

		private GameLoopDelegate BindLeaf(StepPlan step)
		{
			if (step.Function is { } function) return function.Bind(services);
			if (step.Generated is { } generated) return generated.Bind!(generated.IsStatic ? null : Instance(step), services);
			if (step.Binder is { } binder) return binder.Bind(step.Method!.IsStatic ? null : Instance(step), step.Method, services, step.Name);
			return Bind(Instance(step), step.Method!);
		}

		private object? Instance(StepPlan step)
		{
			if (step.System is not { } system) return null;
			if (!step.NeedsInstance) return null;

			if (!_instances.TryGetValue(system, out var instance))
			{
				instance = services.GetRequiredService(system.ServiceType);
				_instances[system] = instance;
			}

			return instance;
		}

		private GameLoopDelegate Bind(object? instance, MethodInfo method)
		{
			var target = method.IsStatic ? null : instance;

			switch (StepSignature.Classify(method, out _))
			{
				case SignatureKind.GameTime:
					return method.CreateDelegate<GameLoopDelegate>(target);

				case SignatureKind.NoArguments:
					return StepAdapters.FromAction(method.CreateDelegate<Action>(target));

				case SignatureKind.Injected:
					var parameters = method.GetParameters();
					var arguments = new object?[parameters.Length];
					var gameTimeIndex = -1;
					for (var p = 0; p < parameters.Length; p++)
					{
						if (parameters[p].ParameterType == typeof(GameTime)) gameTimeIndex = p;
						else arguments[p] = services.GetRequiredService(parameters[p].ParameterType);
					}

					return new InjectedStep(MethodInvoker.Create(method), target, arguments, gameTimeIndex).Invoke;

				default:
					throw new InvalidOperationException($"Cannot bind {method.DeclaringType?.Name}.{method.Name}: unsupported signature.");
			}
		}

		private static Func<GameLoopDelegate, GameLoopDelegate> BindLegacy(object? instance, MethodInfo method)
		{
			var target = method.IsStatic ? null : instance;

			if (StepSignature.Classify(method, out _) == SignatureKind.LegacyFactory)
			{
				return method.CreateDelegate<Func<GameLoopDelegate, GameLoopDelegate>>(target);
			}

			return StepAdapters.FromMiddleware(method.CreateDelegate<Action<GameTime, GameLoopDelegate>>(target));
		}
	}

	private sealed class InjectedStep(MethodInvoker invoker, object? target, object?[] arguments, int gameTimeIndex)
	{
		[StackTraceHidden]
		public void Invoke(GameTime dt)
		{
			if (gameTimeIndex >= 0) arguments[gameTimeIndex] = dt;
			invoker.Invoke(target, arguments.AsSpan());
		}
	}
}

/// <summary>
/// How a <see cref="StageRunner"/> times its items: the profiler, a span id per leaf step and the scope's span id.
/// </summary>
internal sealed class StageProfiling(IStepProfiler profiler, SpanId[] steps, SpanId scope)
{
	public IStepProfiler Profiler { get; } = profiler;

	public SpanId[] Steps { get; } = steps;

	public SpanId Scope { get; } = scope;
}

/// <summary>
/// Runs one stage: the leaf steps in order, then either nothing, a scope (<c>Begin</c>, the inner runner, <c>End</c> in a
/// <c>finally</c>), or a legacy middleware (whose <c>next</c> is the inner runner's <see cref="Entry"/>). With
/// <see cref="StageProfiling"/>, every leaf step and the scope (from its begin to its end) is recorded as a span while the
/// profiler is active.
/// </summary>
internal sealed class StageRunner
{
	private static readonly GameLoopDelegate Empty = static _ => { };

	private readonly GameLoopDelegate[] _steps;
	private readonly GameLoopDelegate? _begin;
	private readonly GameLoopDelegate? _end;
	private readonly GameLoopDelegate? _middleware;
	private readonly StageRunner? _inner;
	private readonly StageProfiling? _profiling;

	public StageRunner(GameLoopDelegate[] steps, StageProfiling? profiling = null)
	{
		_steps = steps;
		_profiling = FrameProfiler.IsProfilingEnabled && steps.Length > 0 ? profiling : null;
		Entry = steps.Length switch
		{
			0 => Empty,
			1 when _profiling is null => steps[0],
			_ => RunSteps,
		};
	}

	public StageRunner(GameLoopDelegate[] steps, GameLoopDelegate begin, GameLoopDelegate end, StageRunner inner, StageProfiling? profiling = null)
	{
		_steps = steps;
		_begin = begin;
		_end = end;
		_inner = inner;
		_profiling = FrameProfiler.IsProfilingEnabled ? profiling : null;
		Entry = RunScope;
	}

	public StageRunner(GameLoopDelegate[] steps, GameLoopDelegate middleware, StageProfiling? profiling = null)
	{
		_steps = steps;
		_middleware = middleware;
		_profiling = FrameProfiler.IsProfilingEnabled && steps.Length > 0 ? profiling : null;
		Entry = steps.Length == 0 ? middleware : RunMiddleware;
	}

	/// <summary>
	/// The cheapest delegate that runs this stage (the only step itself when there is one and nothing else to do).
	/// </summary>
	public GameLoopDelegate Entry { get; }

	[StackTraceHidden]
	private void RunSteps(GameTime dt)
	{
		var steps = _steps;
		if (FrameProfiler.IsProfilingEnabled && _profiling is { } profiling && profiling.Profiler.IsActive)
		{
			RunStepsProfiled(dt, profiling);
			return;
		}

		for (var i = 0; i < steps.Length; i++) steps[i](dt);
	}

	[StackTraceHidden]
	private void RunStepsProfiled(GameTime dt, StageProfiling profiling)
	{
		var steps = _steps;
		var profiler = profiling.Profiler;
		var ids = profiling.Steps;
		for (var i = 0; i < steps.Length; i++)
		{
			var start = profiler.Begin(ids[i]);
			steps[i](dt);
			profiler.End(ids[i], start);
		}
	}

	[StackTraceHidden]
	private void RunMiddleware(GameTime dt)
	{
		var steps = _steps;
		if (FrameProfiler.IsProfilingEnabled && _profiling is { } stepProfiling && stepProfiling.Profiler.IsActive) RunStepsProfiled(dt, stepProfiling);
		else for (var i = 0; i < steps.Length; i++) steps[i](dt);
		_middleware!(dt);
	}

	[StackTraceHidden]
	private void RunScope(GameTime dt)
	{
		var steps = _steps;
		if (FrameProfiler.IsProfilingEnabled && _profiling is { } stepProfiling && stepProfiling.Profiler.IsActive) RunStepsProfiled(dt, stepProfiling);
		else for (var i = 0; i < steps.Length; i++) steps[i](dt);

		var profiling = FrameProfiler.IsProfilingEnabled ? _profiling : null;
		var start = profiling is null ? 0 : profiling.Profiler.Begin(profiling.Scope);

		_begin!(dt);
		try
		{
			_inner!.Entry(dt);
		}
		finally
		{
			_end!(dt);
			if (profiling is not null) profiling.Profiler.End(profiling.Scope, start);
		}
	}
}
