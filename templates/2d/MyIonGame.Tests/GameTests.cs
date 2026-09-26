using System.Text.Json;

using Ion;
using Ion.Testing;

namespace MyIonGame.Tests;

/// <summary>
/// Headless tests: the game runs without a window, GPU or audio device, with a fixed 60 Hz clock, so every run of the same
/// seed produces the same state. Input is scripted through <c>host.Input</c>.
/// </summary>
public sealed class GameTests
{
	[Fact]
	public void HoldingRightMovesThePaddleRight()
	{
		using var run = IonTestHost.Run<Game>(1);
		var state = run.Get<PlayState>();
		var start = state.PaddleX;

		run.Host.Input.Press(Key.Right);
		run.Host.Step(30);
		run.Host.Input.Release(Key.Right);
		run.Host.Step(1);

		// Half a second at the configured speed.
		Assert.InRange(state.PaddleX - start, 200, 260);
	}

	[Fact]
	public void TheBallStaysInPlayOrIsServedAgain()
	{
		using var run = IonTestHost.Run<Game>(600);
		var state = run.Get<PlayState>();
		Assert.Equal(600, run.Frames);
		Assert.True(state.Score + state.Misses > 0, "Ten seconds of play should end with a hit or a miss.");
		Assert.Equal(run.Counters["hits"], state.Score);
		Assert.Equal(2, run.LastFrame.Sprites);
	}
}

/// <summary>
/// Snapshot tests: the game state after a number of frames, as normalized JSON, compared with a committed file under
/// Golden/. When the game changes on purpose, review the new state and update the snapshot: set ION_UPDATE_GOLDEN=1 and run
/// the tests once (a missing snapshot is written and fails the test, so CI never passes on a missing file).
/// </summary>
public sealed class SnapshotTests
{
	[Fact]
	public void StateAfter300FramesMatchesTheSnapshot()
	{
		using var run = IonTestHost.Run<Game>(300, host => host.WithConfiguration("Ion:Seed", "1"));
		var json = JsonSerializer.Serialize(run.Get<PlayState>(), GameJson.Default.PlayState);
		JsonSnapshot.AssertMatches(json, RenderingEnvironment.GoldenPath("state-300.json"));
	}
}
