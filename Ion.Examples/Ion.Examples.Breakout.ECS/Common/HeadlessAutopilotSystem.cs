using Arch.Core;
using Arch.Core.Extensions;

using Microsoft.Extensions.Logging;

using Ion.Extensions.Audio;
using Ion.Extensions.Graphics;

using World = Arch.Core.World;

namespace Ion.Examples.Breakout.ECS.Common;

/// <summary>
/// Plays the game when it runs headless (<c>--Ion:Headless=true</c>): scripts mouse input through
/// <see cref="NullInputState"/> to grab the mouse, launch balls and keep the paddle under the lowest ball, and logs what
/// the headless backends recorded about once a second.
/// </summary>
public class HeadlessAutopilotSystem(NullInputState input, NullSpriteBatch spriteBatch, NullAudioManager audio, IWindow window, World world, ILogger<HeadlessAutopilotSystem> logger)
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

		// Follow the lowest ball with the paddle.
		var target = window.Width / 2f;
		var lowest = float.MinValue;
		world.Query(in _ballQuery, (ref Transform2D transform) =>
		{
			if (transform.Position.Y > lowest)
			{
				lowest = transform.Position.Y;
				target = transform.Position.X;
			}
		});
		input.SetMousePosition(new Vector2(target, window.Height / 2f));
	}

	[Last]
	public void Last(GameTime dt)
	{
		if (_frame % ReportInterval != 0) return;

		var drawn = spriteBatch.LastFrame;
		logger.LogInformation(
			"Frame {Frame}: {Balls} balls, {Blocks} blocks left, last frame drew {Sprites} sprites and {Strings} strings in {DrawCalls} draw calls, {Sounds} sounds played so far, {Mixed} audio frames mixed ({Voices} voices playing).",
			_frame, world.CountEntities(in _ballQuery), world.CountEntities(in _blockQuery), drawn.Sprites, drawn.Strings, drawn.DrawCalls, audio.Plays.Count, audio.Mixer.FramesRendered, audio.Mixer.ActiveVoices);
	}
}
