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

		// Declaration order within a type is the run order.
		Assert.Equal(["init:void", "init:auto", "init:injected", "destroy:auto"], system.Log.Where(e => !e.StartsWith("update")).ToArray());
		Assert.Equal(2, system.Log.Count(e => e == "update:void"));
		Assert.Equal(2, system.Log.Count(e => e == "update:auto"));
		Assert.Equal(2, system.Log.Count(e => e == "update:injected"));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void UnsupportedSignaturesFailTheBuild()
	{
		// Before 0.3 unsupported signatures were logged and skipped; now they are schedule errors.
		using var host = new LoopTestHost(new ManualClock(), systems: typeof(BadSignatureSystem));

		var ex = Assert.Throws<IonScheduleException>(() => host.BuildLoop());

		// The pre-0.3 middleware forms (taking a GameLoopDelegate next) are unsupported signatures like any other.
		Assert.Equal([ScheduleDiagnosticCodes.InvalidSignature, ScheduleDiagnosticCodes.InvalidSignature, ScheduleDiagnosticCodes.InvalidSignature, ScheduleDiagnosticCodes.UnresolvableParameter], ex.Codes.ToArray());
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
	public void InitVoid(GameTime dt) => Log.Add("init:void");

	[Init]
	public void InitAuto() => Log.Add("init:auto");

	[Init]
	public void InitInjected(GameTime dt, IClock clock) => Log.Add("init:injected");

	[Update]
	public void UpdateVoid(GameTime dt) => Log.Add("update:void");

	[Update]
	public void UpdateAuto() => Log.Add("update:auto");

	[Update]
	public void UpdateInjected(GameTime dt, IClock clock) => Log.Add("update:injected");

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

	[Update]
	public void WithNext(GameTime dt, GameLoopDelegate next) { Calls++; next(dt); }

	[Update]
	public GameLoopDelegate Factory(GameLoopDelegate next) => dt => { Calls++; next(dt); };
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
