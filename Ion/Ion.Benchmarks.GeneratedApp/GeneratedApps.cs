using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Ion.Core;

namespace Ion.Benchmarks.GeneratedApp;

/// <summary>The same shape as <c>Ion.Benchmarks.CounterSystem</c>: one trivial leaf step per stage.</summary>
public sealed class GeneratedCounterSystem
{
	public int Update;

	[Update] public void OnUpdate(GameTime dt) { Update++; }
}

/// <summary>The same shape as the full-frame benchmark's systems: one step in every per-frame stage.</summary>
public sealed class GeneratedStageSystem
{
	public int First, FixedUpdate, Update, Render, Last;

	[First] public void OnFirst(GameTime dt) { First++; }
	[FixedUpdate] public void OnFixedUpdate(GameTime dt) { FixedUpdate++; }
	[Update] public void OnUpdate(GameTime dt) { Update++; }
	[Render] public void OnRender(GameTime dt) { Render++; }
	[Last] public void OnLast(GameTime dt) { Last++; }
}

/// <summary>A benchmark application and its game loop.</summary>
public sealed record GeneratedBenchmarkApp(IonApplication Application, GameLoop Loop) : IDisposable
{
	/// <summary>Whether the loop runs the generated schedule (it must, or the benchmark measures the wrong thing).</summary>
	public bool IsGenerated => Loop.Schedule?.IsGenerated == true;

	public void Dispose() => Application.Dispose();
}

/// <summary>
/// Applications whose registrations and <c>Build()</c> call are in one method, so the generator emits their whole
/// schedule as direct calls. Each registration is written out: a loop would make the registrations conditional.
/// </summary>
public static class GeneratedApps
{
	/// <summary>The application with <paramref name="systemCount"/> (1, 8 or 32) counter systems.</summary>
	public static GeneratedBenchmarkApp Counters(int systemCount) => systemCount switch
	{
		1 => Counters1(),
		8 => Counters8(),
		32 => Counters32(),
		_ => throw new ArgumentOutOfRangeException(nameof(systemCount), systemCount, "1, 8 or 32."),
	};

	/// <summary>One counter system.</summary>
	public static GeneratedBenchmarkApp Counters1()
	{
		var app = CreateApplication();
		app.UseEvents();
		app.UseSystem<GeneratedCounterSystem>();
		return new GeneratedBenchmarkApp(app, app.Build());
	}

	/// <summary>Eight counter systems.</summary>
	public static GeneratedBenchmarkApp Counters8()
	{
		var app = CreateApplication();
		app.UseEvents();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		return new GeneratedBenchmarkApp(app, app.Build());
	}

	/// <summary>Thirty-two counter systems.</summary>
	public static GeneratedBenchmarkApp Counters32()
	{
		var app = CreateApplication();
		app.UseEvents();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		app.UseSystem<GeneratedCounterSystem>();
		return new GeneratedBenchmarkApp(app, app.Build());
	}

	/// <summary>Eight systems with a step in every per-frame stage (the full-frame benchmark's shape).</summary>
	public static GeneratedBenchmarkApp EightStageSystems()
	{
		var app = CreateApplication();
		app.UseEvents();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		return new GeneratedBenchmarkApp(app, app.Build());
	}

	/// <summary>
	/// <see cref="EightStageSystems"/> with a frame profiler (300 frames of history, so the loop writes frame stats every
	/// frame), recording a span per step, stage and scope when <paramref name="profiling"/> is true.
	/// </summary>
	public static GeneratedBenchmarkApp EightStageSystemsWithMetrics(bool profiling)
	{
		var app = CreateApplication(new FrameProfiler { IsActive = profiling });
		app.UseEvents();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		app.UseSystem<GeneratedStageSystem>();
		return new GeneratedBenchmarkApp(app, app.Build());
	}

	private static IonApplication CreateApplication(FrameProfiler? profiler = null)
	{
		var builder = IonApplication.CreateBuilder([]);
		builder.Services.AddLogging(logging => logging.ClearProviders());
		builder.Services.AddSingleton<GeneratedCounterSystem>().AddSingleton<GeneratedStageSystem>();
		if (profiler is not null) builder.Services.AddSingleton(profiler);
		return builder.Build();
	}
}
