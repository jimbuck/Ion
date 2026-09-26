using Ion.Extensions.Assets;
using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;
using Ion.Testing;

namespace Ion.Tests;

public record struct ScoredEvent(int Points);
public record struct PingEvent(int Value);

/// <summary>
/// A small game system written only against the abstractions.
/// </summary>
public sealed class ProbeSystem(IInputState input, ISpriteBatch spriteBatch, IAudioManager audio, IAssetManager assets, IEvents events)
{
	private ISoundEffect _sound = default!;

	public int InitCount, DestroyCount, FixedSteps, Frames, FixedClicks, UpdateClicks;
	public bool ThrowInUpdate;
	public int ExitOnFrame = -1;

	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		InitCount++;
		_sound = assets.Load<ISoundEffect>("bonk.wav");
		next(dt);
	}

	[FixedUpdate]
	public void FixedUpdate(GameTime dt, GameLoopDelegate next)
	{
		FixedSteps++;
		if (input.Pressed(MouseButton.Left))
		{
			FixedClicks++;
			events.Emit(new ScoredEvent(10));
		}

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		Frames++;
		if (ThrowInUpdate) throw new InvalidOperationException("boom");
		if (input.Pressed(MouseButton.Left))
		{
			UpdateClicks++;
			audio.Play(_sound);
		}

		if (dt.Frame == ExitOnFrame) events.Emit<ExitGameEvent>();
		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		spriteBatch.DrawRect(Color.Red, new Vector2(1, 2), new Vector2(3, 4));
		spriteBatch.DrawRect(Color.Blue, new Vector2(1, 2), new Vector2(3, 4));
		next(dt);
	}

	[Destroy]
	public void Destroy(GameTime dt, GameLoopDelegate next)
	{
		DestroyCount++;
		next(dt);
	}
}

public sealed class OrderSystemA(List<string> log)
{
	[Update] public void Update(GameTime dt, GameLoopDelegate next) { log.Add("A"); next(dt); }
}

public sealed class OrderSystemB(List<string> log)
{
	[Update] public void Update(GameTime dt, GameLoopDelegate next) { log.Add("B"); next(dt); }
}

public class IonTestHostTests
{
	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void StepRunsFramesWithOneFixedStepEachOnADeterministicClock()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();

		Assert.False(host.IsStarted);
		Assert.Equal(5, host.Step(5));

		var probe = host.Get<ProbeSystem>();
		Assert.True(host.IsStarted);
		Assert.Equal(1, probe.InitCount);
		Assert.Equal(5, probe.Frames);
		Assert.Equal(5, probe.FixedSteps);
		Assert.Equal(5, host.Frame);
		Assert.Equal(5 * IonTestHost.DefaultFrameTime, host.Clock.Elapsed);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void CustomFrameTimeControlsTheFixedStepRatio()
	{
		using var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 120)).WithSystem<ProbeSystem>();

		host.Step(40);

		var probe = host.Get<ProbeSystem>();
		Assert.Equal(40, probe.Frames);
		Assert.InRange(probe.FixedSteps, 19, 20);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void DisposeRunsDestroyAndDisposesTheApplication()
	{
		var host = new IonTestHost().WithSystem<ProbeSystem>();
		host.Step();
		var probe = host.Get<ProbeSystem>();
		var services = host.Services;

		host.Dispose();
		host.Dispose();

		Assert.Equal(1, probe.DestroyCount);
		Assert.Throws<ObjectDisposedException>(() => services.GetRequiredService<ProbeSystem>());
		Assert.Throws<ObjectDisposedException>(() => host.Step());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void DisposeBeforeStartDoesNothing()
	{
		var host = new IonTestHost();
		host.Dispose();
		Assert.False(host.IsStarted);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ScriptedClickIsSeenOnceByFixedUpdateAndUpdateAndPlaysASound()
	{
		using var host = new IonTestHost(TimeSpan.FromSeconds(1.0 / 120)).WithSystem<ProbeSystem>();
		var probe = host.Get<ProbeSystem>();

		host.Step(3);
		host.Input.Click();
		host.Step(6);

		Assert.Equal(1, probe.FixedClicks);
		Assert.Equal(1, probe.UpdateClicks);
		Assert.Single(host.Audio.Plays);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SpriteBatchStatsDescribeTheLastFrame()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();

		host.Step(2);

		Assert.Equal(2, host.SpriteBatch.LastFrame.Rects);
		Assert.Equal(2, host.SpriteBatch.FramesCompleted);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void CollectRecordsEventsFromSystemsAndFromTheHostEmitter()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();
		var scored = host.Collect<ScoredEvent>();
		var pings = host.Collect<PingEvent>();

		host.Input.Click();
		host.Step(2);
		host.Events.Emit(new PingEvent(7));
		host.Step();

		Assert.Equal([new ScoredEvent(10)], scored);
		Assert.Equal([0u], scored.Frames); // input queued before a frame is applied at its start
		Assert.Single(pings);
		Assert.Equal(7, pings[0].Value);
		Assert.Equal(new PingEvent(7), pings.Last);

		scored.Clear();
		Assert.Empty(scored);
		Assert.Null(scored.Last);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RunUntilStopsWhenTheConditionHoldsOrAfterMaxFrames()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();
		var probe = host.Get<ProbeSystem>();

		Assert.True(host.RunUntil(() => probe.Frames >= 7, maxFrames: 100));
		Assert.Equal(7, probe.Frames);

		Assert.True(host.RunUntil(() => true, maxFrames: 0));
		Assert.False(host.RunUntil(() => false, maxFrames: 3));
		Assert.Equal(10, probe.Frames);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ExitGameEventEndsStepAndRunUntilEarly()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();
		host.Get<ProbeSystem>().ExitOnFrame = 2;

		Assert.Equal(3, host.Step(10));
		Assert.True(host.IsExitRequested);
		Assert.False(host.RunUntil(() => false, maxFrames: 10));
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SystemExceptionsPropagateOutOfStep()
	{
		using var host = new IonTestHost().WithSystem<ProbeSystem>();
		host.Get<ProbeSystem>().ThrowInUpdate = true;

		var ex = Assert.Throws<InvalidOperationException>(() => host.Step());
		Assert.Equal("boom", ex.Message);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SystemsRunInRegistrationOrderAfterConfigureApp()
	{
		var log = new List<string>();
		using var host = new IonTestHost()
			.Configure(services => services.AddSingleton(log))
			.ConfigureApp(app => app.UseUpdate(next => dt => { log.Add("app"); next(dt); }))
			.WithSystem<OrderSystemB>()
			.WithSystem(typeof(OrderSystemA));

		host.Step();

		Assert.Equal(["app", "B", "A"], log);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ConfigurationIsBoundAndHeadlessIsForced()
	{
		using var host = new IonTestHost()
			.WithConfiguration(new Dictionary<string, string?> { ["Ion:FixedUpdateRate"] = "30", ["Ion:Headless"] = "false" })
			.WithConfiguration("Ion:Title", "Under test")
			.WithArgs("--Ion:Window:Width=320");

		Assert.Equal(30, host.Get<Microsoft.Extensions.Options.IOptions<GameConfig>>().Value.FixedUpdateRate);
		Assert.Equal("Under test", host.Application.Configuration["Ion:Title"]);
		Assert.Equal("true", host.Application.Configuration["Ion:Headless"]);
		Assert.Equal(320u, host.Window.Width);
		Assert.IsType<NullWindow>(host.Get<IWindow>());
		Assert.Same(host.Clock, host.Get<IClock>());
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ConfiguringAfterStartThrows()
	{
		using var host = new IonTestHost();
		host.Start();

		Assert.Throws<InvalidOperationException>(() => host.WithSystem<ProbeSystem>());
		Assert.Throws<InvalidOperationException>(() => host.Configure(_ => { }));
		Assert.Throws<InvalidOperationException>(() => host.ConfigureApp(_ => { }));
		Assert.Throws<InvalidOperationException>(() => host.WithConfiguration("a", "b"));
		Assert.Throws<InvalidOperationException>(() => host.WithArgs());
		Assert.Throws<InvalidOperationException>(() => host.UseGame(_ => { }, _ => { }));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ArgumentsAreValidated()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new IonTestHost(TimeSpan.Zero));

		using var host = new IonTestHost();
		Assert.Throws<ArgumentOutOfRangeException>(() => host.Step(-1));
		Assert.Throws<ArgumentOutOfRangeException>(() => host.RunUntil(() => true, -1));
		Assert.Throws<ArgumentNullException>(() => host.RunUntil(null!));
		Assert.Throws<ArgumentNullException>(() => host.WithSystem(null!));
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void ScreenshotIsNotSupportedWithoutRendering()
	{
		using var host = new IonTestHost();
		var ex = Assert.Throws<NotSupportedException>(() => host.Screenshot());
		Assert.Contains("WithRendering", ex.Message);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void UseGameReplacesTheDefaultSetup()
	{
		var log = new List<string>();
		using var host = new IonTestHost().UseGame(
			builder =>
			{
				builder.Services.AddIon(builder.Configuration);
				builder.Services.AddSingleton(log).AddSingleton<OrderSystemA>();
			},
			app => app.UseIon().UseSystem<OrderSystemA>());

		host.Step(2);

		Assert.Equal(["A", "A"], log);
		Assert.Equal(2, host.SpriteBatch.FramesCompleted);
	}
}
