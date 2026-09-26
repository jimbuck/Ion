using System.Numerics;

using Arch.Core;

using Ion.Extensions.Ecs;
using Ion.Testing;

namespace MyIonGame.Tests;

/// <summary>
/// Headless tests: the game runs without a window, GPU or audio device, with a fixed 60 Hz clock, so every run of the same
/// seed produces the same state.
/// </summary>
public sealed class GameTests
{
	[Fact]
	public void BallsMoveAndStayInsideTheWindow()
	{
		using var run = IonTestHost.Run<Game>(600);

		Assert.Equal(600, run.Frames);
		var world = run.Get<EcsWorlds>().Root;
		var size = run.Host.Window.Size;
		var balls = 0;
		world.Query(new QueryDescription().WithAll<Ball, Transform2D>(), (ref Transform2D t) =>
		{
			balls++;
			Assert.InRange(t.Position.X, BallSystem.Radius, size.X - BallSystem.Radius);
			Assert.InRange(t.Position.Y, BallSystem.Radius, size.Y - BallSystem.Radius);
		});

		Assert.Equal(run.Get<GameSettings>().Balls, balls);
		Assert.True(run.Counters["bounces"] > 0, "Ten seconds of play should bounce at least one ball.");
		Assert.Equal(balls, run.LastFrame.Sprites);
	}

	[Fact]
	public void TheSameSeedGivesTheSameWorld()
	{
		string Run()
		{
			using var run = IonTestHost.Run<Game>(120, host => host.WithConfiguration("Ion:Seed", "7"));
			return run.WorldJson()!;
		}

		Assert.Equal(Run(), Run());
	}
}

/// <summary>
/// Snapshot tests: the world after a number of frames, as normalized JSON, compared with a committed file under Golden/.
/// When the game changes on purpose, review the new state and update the snapshot: set ION_UPDATE_GOLDEN=1 and run the
/// tests once (a missing snapshot is written and fails the test, so CI never passes on a missing file).
/// </summary>
public sealed class SnapshotTests
{
	[Fact]
	public void WorldAfter120FramesMatchesTheSnapshot()
	{
		using var run = IonTestHost.Run<Game>(120, host => host.WithConfiguration("Ion:Seed", "1"));
		JsonSnapshot.AssertMatches(run.WorldJson()!, RenderingEnvironment.GoldenPath("world-120.json"));
	}
}
