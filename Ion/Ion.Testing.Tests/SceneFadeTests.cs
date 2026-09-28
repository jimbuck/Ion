using Ion.Extensions.Graphics;
using Ion.Extensions.Scenes;
using Ion.Testing;

namespace Ion.Tests;

/// <summary>
/// The built-in fade scene transition on the engine (AddIon/UseIon) under the test host's fixed clock: the fade is a
/// full-window rectangle drawn over the scene at the transition's coverage, recorded by the headless sprite batch.
/// </summary>
public class SceneFadeTests
{
	// Frames of exactly 1/16 s (below the loop's 0.1 s frame clamp), so a 0.25 s phase is four frames.
	private static IonTestHost Host() => new IonTestHost(TimeSpan.FromSeconds(0.0625))
		.ConfigureApp(app =>
		{
			app.UseScene(1, scene => scene.Render((GameTime dt, ISpriteBatch sprites) => sprites.DrawRect(Color.ForestGreen, new RectangleF(10, 10, 90, 90))));
			app.UseScene(2, scene => scene.Render((GameTime dt, ISpriteBatch sprites) => sprites.DrawRect(Color.DarkRed, new RectangleF(10, 10, 90, 90))));
		});

	private static (int Scene, float Alpha, int Rects) Frame(IonTestHost host)
	{
		host.Step();
		var frame = host.SpriteBatch.LastFrame;
		var last = frame.Commands[^1];
		var window = host.Get<IWindow>();
		var fade = last.Size == window.Size && last.Position == Vector2.Zero ? last.Color.A : 0f;
		return (host.Get<SceneSystem>().CurrentSceneId, fade, frame.Rects);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AFadeCoversTheOldSceneThenUncoversTheNewOne()
	{
		using var host = Host();
		host.Step();

		host.Events.EmitChangeScene(2, SceneTransition.Fade(0.25f, 0.25f));
		var frames = Enumerable.Range(0, 10).Select(_ => Frame(host)).ToList();

		Assert.Equal(
			[
				(1, 0f, 1), // the out phase starts: nothing to cover yet
				(1, 0.25f, 2),
				(1, 0.5f, 2),
				(1, 0.75f, 2),
				(2, 1f, 2), // scene 2 loaded under a fully covered frame
				(2, 0.75f, 2),
				(2, 0.5f, 2),
				(2, 0.25f, 2),
				(2, 0f, 1),
				(2, 0f, 1),
			],
			frames);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TheFadeIsDrawnOverTheSceneInItsColor()
	{
		using var host = Host();
		host.Step();
		host.Get<SceneFadeSystem>().Color = Color.White;

		host.Events.EmitChangeScene(2, SceneTransition.Fade(0.25f, 0.25f));
		host.Step(3);

		var commands = host.SpriteBatch.LastFrame.Commands;
		Assert.Equal(Color.ForestGreen, commands[0].Color);
		var fade = commands[^1];
		Assert.Equal(SpriteBatchCommandKind.Rect, fade.Kind);
		Assert.Equal(new Color(Color.White, 0.5f), fade.Color);
		Assert.Equal(host.Get<IWindow>().Size, fade.Size);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void ACustomTransitionIsNotDrawnByTheFade()
	{
		using var host = Host();
		host.Step();

		host.Events.EmitChangeScene(2, SceneTransition.Custom(1, 0.25f, 0.25f));
		host.Step(3);

		Assert.True(host.Get<SceneSystem>().Transition.IsActive);
		Assert.Equal(1, host.SpriteBatch.LastFrame.Rects);
	}
}
