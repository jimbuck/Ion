namespace Ion.Tests;

public class BinderTests
{
	[Fact, Trait(CATEGORY, UNIT)]
	public void AllThreeSystemSignaturesBind()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(SignatureSystem));
		var system = host.Get<SignatureSystem>();
		var loop = host.BuildLoop();

		loop.RunFrames(2);

		// Declaration order within a type is the pipeline order.
		Assert.Equal(["init:wrapper", "init:void", "init:auto", "init:end", "destroy:auto"], system.Log.Where(e => !e.StartsWith("update")).ToArray());
		Assert.Equal(2, system.Log.Count(e => e == "update:wrapper"));
		Assert.Equal(2, system.Log.Count(e => e == "update:void"));
		Assert.Equal(2, system.Log.Count(e => e == "update:auto"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UnsupportedSignaturesFailTheBuild()
	{
		// Before 0.3 unsupported signatures were logged and skipped; now they are schedule errors.
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(BadSignatureSystem));

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		Assert.Equal([ScheduleDiagnosticCodes.InvalidSignature, ScheduleDiagnosticCodes.UnresolvableParameter], ex.Codes.ToArray());
		Assert.Equal(0, host.Get<BadSignatureSystem>().Calls);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UseSystemWithServiceAndImplementationTypes()
	{
		using var host = new LoopTestHost(new ManualClock(), services: s => s.AddSingleton<ICountingSystem, CountingSystem>(), use: app => app.UseSystem<ICountingSystem, CountingSystem>());
		var loop = host.BuildLoop();

		loop.RunFrames(3);

		Assert.Equal(3, host.Get<ICountingSystem>().Count);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void EveryStageAttributeBinds()
	{
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(TestSystem));
		var system = host.Get<TestSystem>();

		host.BuildLoop().RunFrames(1);

		Assert.Equal(1, system.InitializeCount);
		Assert.Equal(1, system.FirstCount);
		Assert.Equal(1, system.UpdateCount);
		Assert.Equal(1, system.RenderCount);
		Assert.Equal(1, system.LastCount);
		Assert.Equal(1, system.DestroyCount);

		system.Reset();
		Assert.Equal(0, system.InitializeCount);
	}
}

public sealed class SignatureSystem
{
	public List<string> Log { get; } = [];

	[Init]
	public GameLoopDelegate InitWrapper(GameLoopDelegate next) => dt => { Log.Add("init:wrapper"); next(dt); };

	[Init]
	public void InitVoid(GameTime dt, GameLoopDelegate next) { Log.Add("init:void"); next(dt); }

	[Init]
	public void InitAuto() => Log.Add("init:auto");

	[Init]
	public GameLoopDelegate InitEnd(GameLoopDelegate next) => dt => { next(dt); Log.Add("init:end"); };

	[Update]
	public GameLoopDelegate UpdateWrapper(GameLoopDelegate next) => dt => { Log.Add("update:wrapper"); next(dt); };

	[Update]
	public void UpdateVoid(GameTime dt, GameLoopDelegate next) { Log.Add("update:void"); next(dt); }

	[Update]
	public void UpdateAuto() => Log.Add("update:auto");

	[Destroy]
	public void DestroyAuto() => Log.Add("destroy:auto");
}

public sealed class BadSignatureSystem
{
	public int Calls { get; private set; }

	[Update]
	public int WrongReturn() => ++Calls;

	[Update]
	public void WrongParameters(int value) => Calls += value;
}

public interface ICountingSystem
{
	int Count { get; }
}

public sealed class CountingSystem : ICountingSystem
{
	public int Count { get; private set; }

	[Update]
	public void Update() => Count++;
}
