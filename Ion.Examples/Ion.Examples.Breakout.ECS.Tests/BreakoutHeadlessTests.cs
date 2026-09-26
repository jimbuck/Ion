using Arch.Core;

using Ion.Examples.Breakout.ECS.Common;
using Ion.Extensions.Ecs;
using Ion.Testing;

using Xunit;
using Xunit.Abstractions;

using static Ion.Tests.TestConstants;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.ECS.Tests;

/// <summary>
/// Plays the real Breakout ECS game headless, driven by its <see cref="HeadlessAutopilotSystem"/>, at the sample's
/// 120 fps with the default 60 Hz fixed step (so about every other frame runs no fixed step).
/// </summary>
public class BreakoutHeadlessTests(ITestOutputHelper output)
{
	private const int Frames = 600;
	private static readonly TimeSpan FrameTime = TimeSpan.FromSeconds(1.0 / 120);

	private static readonly QueryDescription BallQuery = new QueryDescription().WithAll<Ball>();
	private static readonly QueryDescription BlockQuery = new QueryDescription().WithAll<Block>();
	private static readonly QueryDescription BallTransformQuery = new QueryDescription().WithAll<Ball, Transform2D>();

	private readonly record struct FrameSnapshot(int Score, int Balls, int Blocks, int Sprites, int Sounds, long BallPositionHash);

	private sealed record RunResult(IReadOnlyList<FrameSnapshot> Frames, int Launches, int BlockHits, int InitialBlocks);

	private static IonTestHost CreateHost(int? seed)
	{
		var host = new IonTestHost(FrameTime).UseGame(b => BreakoutGame.Configure(b), a => BreakoutGame.Use(a));
		if (seed is int s) host.WithConfiguration(BreakoutGame.SeedKey, s.ToString(System.Globalization.CultureInfo.InvariantCulture));
		return host;
	}

	private static RunResult Play(int? seed)
	{
		using var host = CreateHost(seed);
		var world = host.Get<World>();
		var score = host.Get<ScoreSystem>();
		var launches = host.Collect<LaunchBallCommand>();
		var blockHits = host.Collect<BlockHitEvent>();
		var initialBlocks = world.CountEntities(in BlockQuery);

		var frames = new List<FrameSnapshot>(Frames);
		for (var i = 0; i < Frames; i++)
		{
			host.Step();

			long hash = 17;
			world.Query(in BallTransformQuery, (ref Transform2D transform) =>
			{
				hash = hash * 31 + BitConverter.SingleToInt32Bits(transform.Position.X);
				hash = hash * 31 + BitConverter.SingleToInt32Bits(transform.Position.Y);
			});

			frames.Add(new FrameSnapshot(
				score.Score,
				world.CountEntities(in BallQuery),
				world.CountEntities(in BlockQuery),
				host.SpriteBatch.LastFrame.Sprites,
				host.Audio.Plays.Count,
				hash));
		}

		return new RunResult(frames, launches.Count, blockHits.Count, initialBlocks);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void AutopilotPlays600FramesLaunchingBallsAndScoring()
	{
		var run = Play(seed: null);
		var last = run.Frames[^1];
		foreach (var frame in new[] { 119, 239, 359, 479, 599 })
		{
			var f = run.Frames[frame];
			output.WriteLine($"Frame {frame + 1}: score {f.Score}, {f.Balls} balls, {f.Blocks} blocks, {f.Sprites} sprites, {f.Sounds} sounds");
		}
		output.WriteLine($"{run.Launches} launches, {run.BlockHits} block hits");

		Assert.Equal(BreakoutConstants.ROWS * BreakoutConstants.COLS, run.InitialBlocks);

		// At least one ball was launched (the paddle reads the click in FixedUpdate).
		Assert.True(run.Launches >= 1, $"{run.Launches} launches");
		Assert.True(last.Balls >= 1, $"{last.Balls} balls");

		// Balls hit blocks: the score went up and blocks were destroyed.
		Assert.True(last.Score > 0, $"score {last.Score}");
		Assert.True(last.Blocks < run.InitialBlocks, $"{last.Blocks} blocks left");
		Assert.True(run.BlockHits > 0);
		Assert.True(last.Sounds > 0);

		// Something was drawn every frame: blocks, paddle and balls.
		Assert.All(run.Frames, f => Assert.True(f.Sprites > 0, "a frame drew no sprites"));
		Assert.Contains(run.Frames, f => f.Sprites > run.InitialBlocks);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TouchMovesThePaddleAndLiftingTheFingerLaunchesABall()
	{
		// The first frames, before the autopilot grabs the mouse (frame 5): only the scripted touches drive the paddle.
		using var host = CreateHost(seed: null);
		var world = host.Get<World>();
		var launches = host.Collect<LaunchBallCommand>();
		var paddleQuery = new QueryDescription().WithAll<Paddle, Transform2D>();
		float PaddleX()
		{
			var x = float.NaN;
			world.Query(in paddleQuery, (ref Transform2D transform) => x = transform.Position.X);
			return x;
		}

		host.Step();
		var start = PaddleX();

		host.Input.TouchDown(0, new System.Numerics.Vector2(300, 500));
		host.Step();
		Assert.Equal(300, PaddleX());
		Assert.Empty(launches);

		host.Input.TouchMove(0, new System.Numerics.Vector2(700, 480));
		host.Step();
		Assert.Equal(700, PaddleX());
		Assert.Empty(launches);

		host.Input.TouchUp(0, new System.Numerics.Vector2(700, 480));
		host.Step();
		Assert.Single(launches);
		Assert.NotEqual(start, PaddleX());

		// No finger, no movement.
		host.Step();
		Assert.Equal(700, PaddleX());
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void TwoRunsWithTheSameSeedAreIdentical()
	{
		var first = Play(seed: 42);
		var second = Play(seed: 42);

		Assert.Equal(first.Frames[^1].Score, second.Frames[^1].Score);
		Assert.Equal(first.Frames[^1].Balls, second.Frames[^1].Balls);
		Assert.Equal(first.Frames[^1].Blocks, second.Frames[^1].Blocks);
		Assert.Equal(first.Launches, second.Launches);

		// Frame by frame, down to the bits of every ball position (Box2D is deterministic).
		Assert.Equal(first.Frames, second.Frames);
	}

	[Fact, Trait(CATEGORY, INTEGRATION)]
	public void SeedIsReadFromConfiguration()
	{
		using (var host = CreateHost(seed: 7))
		{
			Assert.Equal(7, host.Get<BreakoutSettings>().Seed);
		}

		using (var host = CreateHost(seed: null))
		{
			Assert.Equal(BreakoutGame.DefaultSeed, host.Get<BreakoutSettings>().Seed);
		}
	}
}
