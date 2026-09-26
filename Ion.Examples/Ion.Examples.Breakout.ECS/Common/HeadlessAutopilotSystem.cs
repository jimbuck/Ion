using Arch.Core;
using Arch.Core.Extensions;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Audio;
using Ion.Extensions.Ecs;
using Ion.Extensions.Graphics;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.ECS.Common;

/// <summary>
/// Plays the game when it runs headless (<c>--Ion:Headless=true</c>): scripts mouse input through
/// <see cref="NullInputState"/> to grab the mouse, launch balls and keep the paddle under the lowest ball, and logs what
/// was drawn and played about once a second. The ball the paddle follows is found by a [Query] step.
/// </summary>
public partial class HeadlessAutopilotSystem(NullInputState input, ISpriteBatch spriteBatch, NullAudioManager audio, IWindow window, World world, ILogger<HeadlessAutopilotSystem> logger)
{
	private const int GrabFrame = 5;
	private const int FirstLaunchFrame = 10;
	private const int LaunchInterval = 60;
	private const int MaxLaunches = 10;
	private const int ReportInterval = 120;

	private readonly QueryDescription _ballQuery = new QueryDescription().WithAll<Ball, Transform2D>();
	private readonly QueryDescription _blockQuery = new QueryDescription().WithAll<Block>();

	private long _frame;
	private int _launches;
	private float _lowest;
	private float _target;

	[Init]
	public void Init(GameTime dt)
	{
		logger.LogInformation("Headless run: {Width}x{Height} null window, scripted input, null audio.", window.Width, window.Height);
	}

	[First]
	public void First(GameTime dt)
	{
		_frame++;

		if (_frame == GrabFrame) input.Click(MouseButton.Left);

		if (_frame >= FirstLaunchFrame && (_frame - FirstLaunchFrame) % LaunchInterval == 0 && _launches < MaxLaunches)
		{
			input.Click(MouseButton.Left);
			_launches++;
		}

		// Follow the lowest ball with the paddle (found by TrackLowestBall, then applied by Steer).
		_target = window.Width / 2f;
		_lowest = float.MinValue;
	}

	[First(Order = 1), Query, All<Ball>]
	private void TrackLowestBall(in Transform2D transform)
	{
		if (transform.Position.Y > _lowest)
		{
			_lowest = transform.Position.Y;
			_target = transform.Position.X;
		}
	}

	[First(Order = 2)]
	public void Steer(GameTime dt) => input.SetMousePosition(new Vector2(_target, window.Height / 2f));

	[Last]
	public void Last(GameTime dt)
	{
		if (_frame % ReportInterval != 0) return;

		// The recording batch (headless) or the 2D renderer (headless rendering): both report statistics.
		var drawn = (spriteBatch as ISpriteBatchStatistics)?.LastFrameStatistics ?? default;
		logger.LogInformation(
			"Frame {Frame}: {Balls} balls, {Blocks} blocks left, last frame drew {Sprites} quads in {DrawCalls} draw calls, {Sounds} sounds played so far, {Mixed} audio frames mixed ({Voices} voices playing).",
			_frame, world.CountEntities(in _ballQuery), world.CountEntities(in _blockQuery), drawn.Sprites, drawn.DrawCalls, audio.Plays.Count, audio.Mixer.FramesRendered, audio.Mixer.ActiveVoices);
	}
}
