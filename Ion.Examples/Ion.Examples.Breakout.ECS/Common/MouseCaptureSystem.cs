using Vector2 = System.Numerics.Vector2;

using Ion.Extensions.Graphics;

namespace Ion.Examples.Breakout.ECS.Common;

public class MouseCaptureSystem(IWindow window, IInputState input)
{
	[Init]
	public void Init(GameTime dt, GameLoopDelegate next)
	{
		window.Size = new Vector2((BreakoutConstants.COLS * BreakoutConstants.BLOCK_SIZE.X) + ((BreakoutConstants.COLS + 1) * BreakoutConstants.BLOCK_GAP), (BreakoutConstants.ROWS * BreakoutConstants.BLOCK_SIZE.Y) + ((BreakoutConstants.ROWS + 1) * BreakoutConstants.BLOCK_GAP) + BreakoutConstants.PLAYER_GAP + BreakoutConstants.PADDLE_SIZE.Y + BreakoutConstants.BOTTOM_GAP);
		window.IsResizable = false;

		next(dt);
	}

	[Last]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		var isMouseGrabbed = window.IsMouseGrabbed;

		if (input.Pressed(Key.Escape) && isMouseGrabbed)
		{
			window.IsMouseGrabbed = false;
			window.IsCursorVisible = true;
		}

		if (input.Pressed(MouseButton.Left) && !isMouseGrabbed)
		{
			window.IsMouseGrabbed = true;
			window.IsCursorVisible = false;
		}

		next(dt);
	}
}
