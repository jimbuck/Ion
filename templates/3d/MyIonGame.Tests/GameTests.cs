using System.Text.Json;

using Ion.Extensions.Graphics;
using Ion.Testing;

namespace MyIonGame.Tests;

/// <summary>
/// Headless tests: the game runs without a window, GPU or audio device, with a fixed 60 Hz clock. The 3D renderer runs its
/// CPU pipeline without a GPU, so its statistics (submitted, visible, batches) can be asserted.
/// </summary>
public sealed class GameTests
{
	[Fact]
	public void CubesSpinAndAreSubmitted()
	{
		using var run = IonTestHost.Run<Game>(60);
		var spin = run.Get<SpinState>();
		Assert.Equal(60, spin.Steps);
		Assert.InRange(spin.Angle, 1.1f, 1.3f); // one second at 1.2 rad/s

		var stats = run.Get<IRenderer3D>().LastFrameStatistics;
		Assert.Equal(run.Get<GameSettings>().Cubes + 1, stats.Submitted);
		Assert.True(stats.Visible > 0);
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
	public void StateAfter120FramesMatchesTheSnapshot()
	{
		using var run = IonTestHost.Run<Game>(120);
		var state = JsonSerializer.SerializeToNode(run.Get<SpinState>(), GameJson.Default.SpinState)!.AsObject();
		var stats = run.Get<IRenderer3D>().LastFrameStatistics;
		state["Submitted"] = stats.Submitted;
		state["Batches"] = stats.Batches;
		JsonSnapshot.AssertMatches(state.ToJsonString(), RenderingEnvironment.GoldenPath("state-120.json"));
	}
}
