using System.Numerics;
using Microsoft.Extensions.DependencyInjection;

using Kyber;
using Kyber.Extensions.Graphics;
using System.Data.Common;

var builder = KyberApplication.CreateBuilder(args);

builder.Services.AddKyber(builder.Configuration, graphics =>
{
	graphics.Output = GraphicsOutput.Window;
	graphics.ClearColor = Color.DarkSlateGray;
});

builder.Services.AddSingleton<BreakoutSystems>();

var game = builder.Build();
game.UseKyber()
	.UseSystem<BreakoutSystems>()
	.Run();


public class BreakoutSystems
{
	private readonly IWindow _window;
	private readonly IInputState _input;
	private readonly ISpriteBatch _spriteBatch;
	private readonly IEventListener _events;

	private const int ROWS = 10;
	private const int COLS = 10;

	private readonly bool[] _blockStates = new bool[ROWS * COLS];
	private readonly Color[] _blockColors = new Color[ROWS * COLS];
	private readonly RectangleF[] _blockRects = new RectangleF[ROWS * COLS];

	private Vector2 _blockSize = new(50, 10f);
	private readonly float _blockGap = 5f;

	private RectangleF _playerRect = new(0, 0, 80, 10f);

	private RectangleF _ballRect = new(0, 0, 10f, 10f);
	private Vector2 _ballVelocity = Vector2.Zero;
	private float _ballSpeed = 200f;
	private bool _ballIsCaptured = true;

	public BreakoutSystems(IWindow window, IInputState input, ISpriteBatch spriteBatch, IEventListener events)
	{
		_window = window;
		_input = input;
		_spriteBatch = spriteBatch;
		_events = events;
	}

	[Init]
	public void SetupBlocks(GameTime dt, GameLoopDelegate next)
	{
		// Setup blocks in rows and columns across the window each with different colors:
		for (int row = 0; row < ROWS; row++)
		{
			for (int col = 0; col < COLS; col++)
			{
				var i = (row * COLS) + col;
				_blockStates[i] = true;
				_blockColors[i] = Color.Lerp(Color.LightSeaGreen, Color.DarkGreen, (float)row / ROWS);
				_blockRects[i] = new RectangleF(Vector2.Zero, _blockSize);
			}
		}

		_window.Size = new Vector2((COLS * _blockSize.X) + ((COLS + 1) * _blockGap), 300 + _blockSize.Y + 20);
		_window.IsResizable = false;

		_repositionBlocks();

		next(dt);
	}

	[First]
	public void HandleWindowResize(GameTime dt, GameLoopDelegate next)
	{
		if (_events.On<WindowResizeEvent>()) _repositionBlocks();

		next(dt);
	}

	[Update]
	public void Update(GameTime dt, GameLoopDelegate next)
	{
		_playerRect.Location = new Vector2(Math.Clamp(_input.MousePosition.X - (_playerRect.Height / 2f), 0, _window.Width - _playerRect.Width), 300);

		if (_ballIsCaptured)
		{
			_ballRect.Location = _playerRect.Location + new Vector2((_playerRect.Width - _ballRect.Width) / 2f, -(_ballRect.Height + 1));

			if (_input.Pressed(MouseButton.Left))
			{
				_ballIsCaptured = false;
				_ballVelocity = new Vector2(1, -1);
			}
		}

		// Ball movement
		_ballRect.Location += _ballVelocity * dt * _ballSpeed;

		// WIndow border collisions
		if (_ballRect.X <= 0)
		{
			_ballRect.X = 0;
			_ballVelocity.X *= -1;
		}

		if (_ballRect.X >= _window.Width - _ballRect.Width)
		{
			_ballRect.X = _window.Width - _ballRect.Width;
			_ballVelocity.X *= -1;
		}

		if (_ballRect.Top <= 0)
		{
			_ballRect.Y = 0;
			_ballVelocity.Y *= -1;
		}

		if (_ballRect.Y > _window.Height)
		{
			_ballVelocity = Vector2.Zero;
			_ballIsCaptured = true;
		}

		// Ball to player collisions
		if (_ballRect.Bottom > 250 && _ballVelocity.Y > 0 && _ballIsColliding(ref _playerRect))
		{
			_ballRect.Y = _playerRect.Y - (_ballRect.Height + 1);
			_ballVelocity.Y *= -1.05f;
		}

		// Ball to block collisions
		for (var i = 0; i < _blockRects.Length; i++)
		{
			if (_blockStates[i] && _ballIsColliding(ref _blockRects[i]))
			{
				_blockStates[i] = false;
				// TODO: Rebound the ball.
			}
		}

		if (!_blockStates.Any(s => s == true))
		{
			_ballIsCaptured = true;
			_ballVelocity = Vector2.Zero;
			for (int row = 0; row < ROWS; row++)
			{
				for (int col = 0; col < COLS; col++)
				{
					var i = (row * COLS) + col;
					_blockStates[i] = true;
				}
			}

			Console.WriteLine("You Win!");
		}

		next(dt);
	}

	[Render]
	public void Render(GameTime dt, GameLoopDelegate next)
	{
		for (var row = 0; row < ROWS; row++)
		{
			for (var col = 0; col < COLS; col++)
			{
				var i = (row * COLS) + col;
				if (_blockStates[i]) _spriteBatch.DrawRect(_blockColors[i], _blockRects[i]);
			}
		}

		_spriteBatch.DrawRect(Color.DarkBlue, _playerRect);
		_spriteBatch.DrawRect(Color.OrangeRed, _ballRect);

		next(dt);
	}

	private void _repositionBlocks()
	{
		var edgeOffset = new Vector2(_blockGap);
		for (int row = 0; row < ROWS; row++)
		{
			for (int col = 0; col < COLS; col++)
			{
				_blockRects[(row * COLS) + col].Location = edgeOffset + new Vector2(col * (_blockSize.X + _blockGap), row * (_blockSize.Y + _blockGap));
			}
		}
	}

	private bool _ballIsColliding(ref RectangleF target)
	{
		if (_ballIsCaptured) return false;

		return _ballRect.Intersects(target);
	}
}