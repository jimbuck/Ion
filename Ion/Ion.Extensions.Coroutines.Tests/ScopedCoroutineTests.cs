using System.Collections;

using Ion.Extensions.Coroutines;

namespace Ion.Tests;

/// <summary>
/// A coroutine runner resolved from a service scope (a scene's) starts its coroutines on the application's runner and
/// stops them when the scope is disposed (the scene unloads); the root runner's own coroutines keep running.
/// </summary>
public class ScopedCoroutineTests : IDisposable
{
	private readonly IonApplication _app;
	private readonly ICoroutineRunner _root;
	private readonly GameTime _dt = new() { Frame = 0, Delta = 0.1f, Alpha = 1f, Elapsed = TimeSpan.Zero };

	public ScopedCoroutineTests()
	{
		var builder = IonApplication.CreateBuilder();
		builder.Services.AddCoroutines();
		_app = builder.Build();
		_app.UseEvents();
		_root = _app.Services.GetRequiredService<ICoroutineRunner>();
	}

	public void Dispose() => _app.Dispose();

	private void NextFrame()
	{
		_root.Update(_dt);
		_dt.Frame++;
	}

	private static IEnumerator<Wait> Forever(List<string> log, string name)
	{
		while (true)
		{
			log.Add(name);
			yield return Wait.None;
		}
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void TheRootProviderResolvesTheApplicationRunner()
	{
		Assert.IsType<CoroutineRunner>(_root);
		Assert.Same(_root, _app.Services.GetRequiredService<ICoroutineRunner>());
		Assert.Same(_root, _app.Services.GetRequiredService<CoroutineRunner>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AScopeResolvesOneScopedRunnerOverTheApplicationRunner()
	{
		using var scope = _app.Services.CreateScope();
		var scoped = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();

		var scopedRunner = Assert.IsType<ScopedCoroutineRunner>(scoped);
		Assert.Same(scoped, scope.ServiceProvider.GetRequiredService<ICoroutineRunner>());
		Assert.Same(_root, scopedRunner.Parent);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DisposingTheScopeStopsItsCoroutinesAndKeepsTheApplicationOnes()
	{
		var log = new List<string>();
		var app = Forever(log, "app");
		_root.Start(app);

		IEnumerator scene;
		using (var scope = _app.Services.CreateScope())
		{
			var runner = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();
			scene = Forever(log, "scene");
			runner.Start(scene);

			Assert.Equal(1, runner.Count);
			Assert.True(runner.IsActive(scene));
			Assert.False(runner.IsActive(app));
			Assert.Equal(2, _root.Count);
			Assert.True(_root.IsActive(scene));

			NextFrame();
			Assert.Equal(["app", "scene"], log);
		}

		Assert.False(_root.IsActive(scene));
		Assert.True(_root.IsActive(app));
		Assert.Equal(1, _root.Count);

		log.Clear();
		NextFrame();
		Assert.Equal(["app"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void StopAllOnAScopedRunnerStopsOnlyItsOwnCoroutines()
	{
		var log = new List<string>();
		_root.Start(Forever(log, "app"));

		using var scope = _app.Services.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();
		runner.Start(Forever(log, "scene a"));
		runner.Start(Forever(log, "scene b"));

		runner.StopAll();
		NextFrame();

		Assert.Equal(0, runner.Count);
		Assert.Equal(1, _root.Count);
		Assert.Equal(["app"], log);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AScopedRunnerCannotStopAnotherScopesCoroutine()
	{
		var log = new List<string>();
		var app = Forever(log, "app");
		_root.Start(app);

		using var scope = _app.Services.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();
		runner.Stop(app);

		Assert.True(_root.IsActive(app));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void AFinishedScopedCoroutineIsNoLongerCounted()
	{
		static IEnumerator Once()
		{
			yield return null;
		}

		using var scope = _app.Services.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();
		var routine = Once();
		runner.Start(routine);

		NextFrame();
		NextFrame();

		Assert.False(runner.IsActive(routine));
		Assert.Equal(0, runner.Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void SteppingScopedTypedRoutinesAllocatesNothing()
	{
		static IEnumerator<Wait> Waits()
		{
			while (true)
			{
				yield return Wait.For(0.001f);
				yield return Wait.None;
			}
		}

		using var scope = _app.Services.CreateScope();
		var runner = scope.ServiceProvider.GetRequiredService<ICoroutineRunner>();
		for (var i = 0; i < 100; i++) runner.Start(Waits());

		for (var i = 0; i < 10; i++) NextFrame();
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 100; i++) NextFrame();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

		Assert.Equal(0, allocated);
		Assert.Equal(100, runner.Count);
	}
}
