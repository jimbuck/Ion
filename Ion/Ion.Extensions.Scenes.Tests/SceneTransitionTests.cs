using Ion.Extensions.Scenes;

using Microsoft.Extensions.DependencyInjection;

using static Ion.Tests.TestConstants;

namespace Ion.Tests;

/// <summary>
/// Scene transitions run across frames on game time: the out phase covers the old scene (which keeps running), the new
/// scene loads when it ends, and the in phase uncovers it. Every frame here lasts 0.125 s, so the phases are exact.
/// </summary>
public class SceneTransitionTests
{
	private const float Delta = 0.125f;

	private sealed class Game : IDisposable
	{
		private uint _frame;

		public Game(int scenes = 3)
		{
			var builder = IonApplication.CreateBuilder();
			builder.Services.AddScenes();
			builder.Services.AddSingleton<CallLog>();
			App = builder.Build();
			App.UseEvents();
			for (var id = 1; id <= scenes; id++)
			{
				var name = "s" + id;
				App.UseScene(id, scene =>
				{
					scene.Update((GameTime _, CallLog log) => log.Entries.Add(name));
					scene.Destroy((GameTime _, CallLog log) => log.Entries.Add(name + " destroy"));
				});
			}

			Loop = App.Build();
			Scenes = App.Services.GetRequiredService<SceneSystem>();
			Events = App.Services.GetRequiredService<IEvents>();
			Log = App.Services.GetRequiredService<CallLog>().Entries;
			Loop.Init(Time());
		}

		public IonApplication App { get; }
		public Core.GameLoop Loop { get; }
		public SceneSystem Scenes { get; }
		public IEvents Events { get; }
		public List<string> Log { get; }

		private GameTime Time() => new() { Frame = _frame++, Delta = Delta, Alpha = 1f, Elapsed = TimeSpan.Zero };

		/// <summary>Runs a frame and returns what it saw: the scene whose Update ran, the phase and the coverage.</summary>
		public (string Scene, TransitionPhase Phase, float Coverage) Step()
		{
			Log.Clear();
			Loop.Step(Time());
			var t = Scenes.Transition;
			return (string.Join(",", Log), t.Phase, t.Coverage);
		}

		public void Dispose() => App.Dispose();
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AFadeKeepsTheOldSceneUntilTheOutPhaseEndsThenUncoversTheNewOne()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.5f, 0.5f));

		var frames = Enumerable.Range(0, 10).Select(_ => game.Step()).ToList();

		Assert.Equal(
			[
				("s1", TransitionPhase.Out, 0f),
				("s1", TransitionPhase.Out, 0.25f),
				("s1", TransitionPhase.Out, 0.5f),
				("s1", TransitionPhase.Out, 0.75f),
				("s1 destroy,s2", TransitionPhase.In, 1f),
				("s2", TransitionPhase.In, 0.75f),
				("s2", TransitionPhase.In, 0.5f),
				("s2", TransitionPhase.In, 0.25f),
				("s2", TransitionPhase.None, 0f),
				("s2", TransitionPhase.None, 0f),
			],
			frames);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheStateExposesTheTransitionAndItsProgress()
	{
		using var game = new Game();
		var fade = SceneTransition.Fade(1f);
		Assert.Equal(new SceneTransition(TransitionKind.Fade, 0.5f, 0.5f), fade);
		Assert.False(game.Scenes.Transition.IsActive);

		game.Events.EmitChangeScene(2, fade);
		game.Step();
		game.Step();

		var state = game.Scenes.Transition;
		Assert.True(state.IsActive);
		Assert.Equal(fade, state.Transition);
		Assert.Equal(TransitionPhase.Out, state.Phase);
		Assert.Equal(0.25f, state.Progress);
		Assert.True(game.Scenes.IsLoading);
		Assert.Equal(1, game.Scenes.CurrentSceneId);

		for (var i = 0; i < 4; i++) game.Step();
		state = game.Scenes.Transition;
		Assert.Equal(TransitionPhase.In, state.Phase);
		Assert.Equal(0.25f, state.Progress);
		Assert.False(game.Scenes.IsLoading);
		Assert.Equal(2, game.Scenes.CurrentSceneId);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void WithoutATransitionTheSceneChangesOnTheNextFrame()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2);

		Assert.Equal(("s1 destroy,s2", TransitionPhase.None, 0f), game.Step());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnInOnlyTransitionSwapsAtOnceAndUncovers()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0f, 0.25f));

		Assert.Equal(("s1 destroy,s2", TransitionPhase.In, 1f), game.Step());
		Assert.Equal(("s2", TransitionPhase.In, 0.5f), game.Step());
		Assert.Equal(("s2", TransitionPhase.None, 0f), game.Step());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnOutOnlyTransitionShowsTheNewSceneAsSoonAsItLoads()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.25f, 0f));

		Assert.Equal(("s1", TransitionPhase.Out, 0f), game.Step());
		Assert.Equal(("s1", TransitionPhase.Out, 0.5f), game.Step());
		Assert.Equal(("s1 destroy,s2", TransitionPhase.None, 0f), game.Step());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheLatestRequestDuringTheOutPhaseWins()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.25f, 0.25f));
		game.Step();
		game.Events.EmitChangeScene(3, SceneTransition.Fade(0.25f, 0.25f));

		// The out phase goes on from where it was (the coverage does not jump), then scene 3 loads; scene 2 never does.
		Assert.Equal(("s1", TransitionPhase.Out, 0.5f), game.Step());
		Assert.Equal(("s1 destroy,s3", TransitionPhase.In, 1f), game.Step());
		Assert.Equal(3, game.Scenes.CurrentSceneId);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void RequestingTheShownSceneDuringTheOutPhaseUncoversItAgain()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.5f, 0.5f));
		game.Step();
		game.Step();
		game.Step(); // coverage 0.5

		game.Events.EmitChangeScene(1, SceneTransition.Fade(0.5f, 0.5f));

		// No reload: scene 1 is uncovered from the coverage it reached.
		Assert.Equal(("s1", TransitionPhase.In, 0.5f), game.Step());
		Assert.Equal(("s1", TransitionPhase.In, 0.25f), game.Step());
		Assert.Equal(("s1", TransitionPhase.None, 0f), game.Step());
		Assert.False(game.Scenes.IsLoading);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AChangeDuringTheInPhaseCoversAgainFromTheCurrentCoverage()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.25f, 0.5f));
		game.Step();
		game.Step();
		Assert.Equal(("s1 destroy,s2", TransitionPhase.In, 1f), game.Step());
		Assert.Equal(("s2", TransitionPhase.In, 0.75f), game.Step());

		game.Events.EmitChangeScene(3, SceneTransition.Fade(0.5f, 0.5f));

		Assert.Equal(("s2", TransitionPhase.Out, 0.75f), game.Step());
		Assert.Equal(("s2 destroy,s3", TransitionPhase.In, 1f), game.Step());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AChangeWithoutATransitionCancelsTheRunningOne()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Fade(0.5f, 0.5f));
		game.Step();
		game.Step();

		game.Events.EmitChangeScene(3);

		Assert.Equal(("s1 destroy,s3", TransitionPhase.None, 0f), game.Step());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AnUnknownSceneDoesNotStartATransition()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(99, SceneTransition.Fade(0.5f));

		Assert.Equal(("s1", TransitionPhase.None, 0f), game.Step());
		Assert.False(game.Scenes.IsLoading);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ACustomTransitionCarriesItsStyle()
	{
		using var game = new Game();
		game.Events.EmitChangeScene(2, SceneTransition.Custom(7, 0.25f, 0.25f));
		game.Step();

		var state = game.Scenes.Transition;
		Assert.Equal(TransitionKind.Custom, state.Transition.Kind);
		Assert.Equal(7, state.Transition.Style);
		Assert.Equal(TransitionPhase.Out, state.Phase);
	}

	[Theory, Trait(CATEGORY, UNIT)]
	[InlineData(TransitionKind.None, 1f, 1f, true)]
	[InlineData(TransitionKind.Fade, 0f, 0f, true)]
	[InlineData(TransitionKind.Fade, -1f, float.NaN, true)]
	[InlineData(TransitionKind.Fade, 0f, 0.5f, false)]
	[InlineData(TransitionKind.Custom, 0.5f, 0f, false)]
	public void IsNoneWhenThereIsNothingToAnimate(TransitionKind kind, float outDuration, float inDuration, bool none)
	{
		Assert.Equal(none, new SceneTransition(kind, outDuration, inDuration).IsNone);
	}

	[Fact, Trait(CATEGORY, UNIT)]
	public void CoverageRisesDuringTheOutPhaseAndFallsDuringTheInPhase()
	{
		var fade = SceneTransition.Fade(1f);
		Assert.Equal(0f, new SceneTransitionState(fade, TransitionPhase.None, 0.3f).Coverage);
		Assert.Equal(0.3f, new SceneTransitionState(fade, TransitionPhase.Out, 0.3f).Coverage);
		Assert.Equal(0.7f, new SceneTransitionState(fade, TransitionPhase.In, 0.3f).Coverage);
	}
}
